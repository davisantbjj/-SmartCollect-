using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using Microsoft.Extensions.Configuration;
using SmartCollect.Application.DTOs.Auth;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class AuthServiceTests
{
    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestSuperSecretKeyAtLeast32CharsLong!!",
                ["Jwt:Issuer"] = "Test",
                ["Jwt:Audience"] = "Test",
                ["Jwt:ExpirationMinutes"] = "60"
            })
            .Build();

    private static async Task<(AuthService service, Tenant tenant)> SetupAsync()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test Co", TaxId = "12345" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return (new AuthService(db, CreateConfig()), tenant);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_ReturnsToken()
    {
        var db2 = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        db2.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "12345" });
        db2.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Test",
            Email = "test@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            Role = UserRole.Worker,   // Updated: Worker (was Operator)
            Active = true
        });
        await db2.SaveChangesAsync();

        var svc = new AuthService(db2, CreateConfig());
        var result = await svc.LoginAsync(new LoginRequest("test@test.com", "password123"));

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Token);
        Assert.Equal("test@test.com", result.Email);
        Assert.Equal("Worker", result.Role);  // Updated: Worker
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsNull()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        db.Tenants.Add(tenant);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Test",
            Email = "test@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct"),
            Active = true
        });
        await db.SaveChangesAsync();

        var svc = new AuthService(db, CreateConfig());
        var result = await svc.LoginAsync(new LoginRequest("test@test.com", "wrong"));

        Assert.Null(result);
    }

    [Fact]
    public async Task Login_InactiveUser_ThrowsBlockedAccessMessageForWorker()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        db.Tenants.Add(tenant);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Inactive",
            Email = "inactive@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Active = false
        });
        await db.SaveChangesAsync();

        var svc = new AuthService(db, CreateConfig());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.LoginAsync(new LoginRequest("inactive@test.com", "password")));

        Assert.Equal("Acesso bloqueado. Entre em contato com seu administrador.", ex.Message);
    }

    [Fact]
    public async Task Login_InactiveTenant_ThrowsBlockedAccessMessageForWorker()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123", Active = false };
        db.Tenants.Add(tenant);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Worker",
            Email = "worker@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Worker,
            Active = true
        });
        await db.SaveChangesAsync();

        var svc = new AuthService(db, CreateConfig());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.LoginAsync(new LoginRequest("worker@test.com", "password")));

        Assert.Equal("Acesso bloqueado. Entre em contato com seu administrador.", ex.Message);
    }

    [Fact]
    public async Task Login_InactiveAdmin_ThrowsBlockedAccessMessageForAdmin()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        db.Tenants.Add(tenant);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Admin",
            Email = "admin@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = false
        });
        await db.SaveChangesAsync();

        var svc = new AuthService(db, CreateConfig());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.LoginAsync(new LoginRequest("admin@test.com", "password")));

        Assert.Equal("Acesso bloqueado. Entre em contato com o administrador da SmartCollect.", ex.Message);
    }

    [Fact]
    public async Task Login_SameEmail_DifferentTenants_ReturnsBothCorrectly()
    {
        // B-01 fix: same email in two tenants must resolve correctly by password
        var db = TestDbContextFactory.Create();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantA, CompanyName = "A", TaxId = "A01" });
        db.Tenants.Add(new Tenant { Id = tenantB, CompanyName = "B", TaxId = "B01" });
        db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = tenantA, Name = "A User", Email = "shared@test.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("passA"), Role = UserRole.Admin, Active = true });
        db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = tenantB, Name = "B User", Email = "shared@test.com", PasswordHash = BCrypt.Net.BCrypt.HashPassword("passB"), Role = UserRole.Worker, Active = true });
        await db.SaveChangesAsync();

        var svc = new AuthService(db, CreateConfig());

        var resultA = await svc.LoginAsync(new LoginRequest("shared@test.com", "passA"));
        var resultB = await svc.LoginAsync(new LoginRequest("shared@test.com", "passB"));

        Assert.NotNull(resultA);
        Assert.NotNull(resultB);
        Assert.Equal(tenantA, resultA!.TenantId);
        Assert.Equal(tenantB, resultB!.TenantId);
        Assert.Equal("Admin", resultA.Role);
        Assert.Equal("Worker", resultB.Role);
    }

    [Fact]
    public async Task Login_MasterUser_ReturnsEmptyTenantId()
    {
        var db = TestDbContextFactory.Create();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = null,  // Master has no tenant
            Name = "Master",
            Email = "master@atos.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("masterpass"),
            Role = UserRole.Master,
            Active = true
        });
        await db.SaveChangesAsync();

        var svc = new AuthService(db, CreateConfig());
        var result = await svc.LoginAsync(new LoginRequest("master@atos.com", "masterpass"));

        Assert.NotNull(result);
        Assert.Equal("Master", result!.Role);
        Assert.Equal(Guid.Empty, result.TenantId);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsNull()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        db.Tenants.Add(tenant);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Existing",
            Email = "dupe@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Active = true
        });
        await db.SaveChangesAsync();

        var svc = new AuthService(db, CreateConfig());
        var result = await svc.RegisterAsync(tenant.Id, new RegisterRequest("New User", "dupe@test.com", "password"));

        Assert.Null(result);
    }

    [Fact]
    public async Task Register_NewUser_DefaultsToWorkerRole()
    {
        var (svc, tenant) = await SetupAsync();
        var db = TestDbContextFactory.Create();
        db.Tenants.Add(new Tenant { Id = tenant.Id, CompanyName = tenant.CompanyName, TaxId = tenant.TaxId });
        await db.SaveChangesAsync();

        var freshSvc = new AuthService(db, CreateConfig());
        var result = await freshSvc.RegisterAsync(tenant.Id, new RegisterRequest("New Worker", "new@test.com", "password123"));

        Assert.NotNull(result);
        Assert.Equal("Worker", result!.Role);
    }

    [Fact]
    public async Task Register_NewUser_WithAdminRole_ReturnsAdminRole()
    {
        var (_, tenant) = await SetupAsync();
        var db = TestDbContextFactory.Create();
        db.Tenants.Add(new Tenant { Id = tenant.Id, CompanyName = tenant.CompanyName, TaxId = tenant.TaxId });
        await db.SaveChangesAsync();

        var freshSvc = new AuthService(db, CreateConfig());
        var result = await freshSvc.RegisterAsync(
            tenant.Id,
            new RegisterRequest("New Admin", "admin-new@test.com", "password123", "Admin"));

        Assert.NotNull(result);
        Assert.Equal("Admin", result!.Role);
    }

    [Fact]
    public async Task Register_WithInvalidRole_Throws()
    {
        var (_, tenant) = await SetupAsync();
        var db = TestDbContextFactory.Create();
        db.Tenants.Add(new Tenant { Id = tenant.Id, CompanyName = tenant.CompanyName, TaxId = tenant.TaxId });
        await db.SaveChangesAsync();

        var freshSvc = new AuthService(db, CreateConfig());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            freshSvc.RegisterAsync(
                tenant.Id,
                new RegisterRequest("Bad Role", "bad-role@test.com", "password123", "Master")));

        Assert.Equal("Perfil inválido. Use Admin ou Worker.", ex.Message);
    }
}
