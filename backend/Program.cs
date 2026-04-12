using System.Text;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using SmartCollect.Application.Services;
using SmartCollect.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "SmartCollect API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Example: 'Bearer {token}'"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("SmartCollect");

var dataProtectionKeysPath = Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH");
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    Directory.CreateDirectory(dataProtectionKeysPath);
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

var currentDir = Directory.GetCurrentDirectory();
var currentEnvPath = Path.GetFullPath(Path.Combine(currentDir, ".env"));
var parentEnvPath = Path.GetFullPath(Path.Combine(currentDir, "..", ".env"));
var runningInsideBackendFolder = string.Equals(
    new DirectoryInfo(currentDir).Name,
    "backend",
    StringComparison.OrdinalIgnoreCase);

if (File.Exists(parentEnvPath))
{
    Env.Load(parentEnvPath);
}
else if (!runningInsideBackendFolder && File.Exists(currentEnvPath))
{
    Env.Load(currentEnvPath);
}

// Build connection string from appsettings + environment variables
var configuredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var connectionBuilder = new NpgsqlConnectionStringBuilder(
    string.IsNullOrWhiteSpace(configuredConnectionString)
        ? "Host=localhost;Port=55432;Database=smartcollect;Username=smartcollect"
        : configuredConnectionString);

var dbHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? Environment.GetEnvironmentVariable("DB_HOST");
var dbPort = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? Environment.GetEnvironmentVariable("DB_PORT");
var dbName = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? Environment.GetEnvironmentVariable("DB_NAME");
var dbUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? Environment.GetEnvironmentVariable("DB_USER");
var dbPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? Environment.GetEnvironmentVariable("DB_PASSWORD");

if (!string.IsNullOrWhiteSpace(dbHost)) connectionBuilder.Host = dbHost;
if (int.TryParse(dbPort, out var parsedPort)) connectionBuilder.Port = parsedPort;
if (!string.IsNullOrWhiteSpace(dbName)) connectionBuilder.Database = dbName;
if (!string.IsNullOrWhiteSpace(dbUser)) connectionBuilder.Username = dbUser;
if (!string.IsNullOrWhiteSpace(dbPassword)) connectionBuilder.Password = dbPassword;

if (string.IsNullOrWhiteSpace(connectionBuilder.Password))
{
    throw new InvalidOperationException(
        "Database password is not configured. Set POSTGRES_PASSWORD (or DB_PASSWORD) in .env/environment.");
}

// Infrastructure layer (DbContext, repositories, services)
builder.Services.AddInfrastructure(connectionBuilder.ConnectionString, builder.Configuration);
builder.Services.AddHostedService<PendingDispatchBackgroundService>();

// JWT Authentication
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
    ?? builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT Key not configured. Set JWT_KEY in .env.");

var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? builder.Configuration["Jwt:Issuer"] ?? "SmartCollect";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? builder.Configuration["Jwt:Audience"] ?? "SmartCollect";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// CORS for frontend dev server
var corsOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS")
    ?? builder.Configuration["Cors:Origins"]
    ?? "http://localhost:5173";

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

var applyMigrationsOnStartup = app.Environment.IsDevelopment()
    || string.Equals(
        Environment.GetEnvironmentVariable("APPLY_MIGRATIONS_ON_STARTUP"),
        "true",
        StringComparison.OrdinalIgnoreCase);

if (applyMigrationsOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmartCollect.Infrastructure.Data.AppDbContext>();
    db.Database.Migrate();

    // Normalize legacy data: Master users must be cross-tenant (TenantId = null)
    var legacyMasters = db.Set<SmartCollect.Domain.Entities.User>()
        .Where(u => u.Role == SmartCollect.Domain.Enums.UserRole.Master && u.TenantId != null)
        .ToList();
    if (legacyMasters.Count > 0)
    {
        foreach (var user in legacyMasters)
            user.TenantId = null;
        db.SaveChanges();
    }

    // Seed: Master user (Atos Capital) if no Master exists yet
    if (!db.Set<SmartCollect.Domain.Entities.User>().Any(u => u.Role == SmartCollect.Domain.Enums.UserRole.Master))
    {
        var masterPassword = Environment.GetEnvironmentVariable("MASTER_PASSWORD") ?? "Master@2026!";
        var masterEmail = Environment.GetEnvironmentVariable("MASTER_EMAIL") ?? "master@atoscapital.com.br";
        var masterName = Environment.GetEnvironmentVariable("MASTER_NAME") ?? "Atos Capital Master";

        db.Set<SmartCollect.Domain.Entities.User>().Add(new SmartCollect.Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            TenantId = null, // Master has no tenant
            Name = masterName,
            Email = masterEmail.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(masterPassword),
            Role = SmartCollect.Domain.Enums.UserRole.Master,
            Active = true
        });

        db.SaveChanges();
    }

    // Seed: Default tenant + Admin if DB is empty
    if (!db.Set<SmartCollect.Domain.Entities.Tenant>().Any())
    {
        var tenantId = Guid.NewGuid();
        db.Set<SmartCollect.Domain.Entities.Tenant>().Add(new SmartCollect.Domain.Entities.Tenant
        {
            Id = tenantId,
            CompanyName = "Atos Capital",
            TaxId = "00000000000000",
            EmailDomain = "atoscapital.com.br"
        });

        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "Admin@2026!";
        var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@atoscapital.com.br";

        db.Set<SmartCollect.Domain.Entities.User>().Add(new SmartCollect.Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Administrador",
            Email = adminEmail.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            Role = SmartCollect.Domain.Enums.UserRole.Admin,
            Active = true
        });

        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

