namespace SmartCollect.Application.Services;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SmartCollect.Application.DTOs.Auth;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class AuthService : IAuthService
{
    private readonly IAppDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(IAppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Find all users with this email (same email can exist in multiple tenants)
        var candidates = await _db.Users
            .Include(u => u.Tenant)
            .Where(u => u.Email == normalizedEmail)
            .ToListAsync();

        if (candidates.Count == 0) return null;

        var passwordMatchedCandidates = new List<Domain.Entities.User>();
        foreach (var candidate in candidates)
        {
            if (BCrypt.Net.BCrypt.Verify(request.Password, candidate.PasswordHash))
                passwordMatchedCandidates.Add(candidate);
        }

        if (passwordMatchedCandidates.Count == 0) return null;

        var user = passwordMatchedCandidates.FirstOrDefault(candidate =>
            candidate.Active &&
            (candidate.Role == UserRole.Master || (candidate.Tenant is not null && candidate.Tenant.Active)));

        if (user is null)
            throw new InvalidOperationException(GetBlockedAccessMessage(passwordMatchedCandidates[0].Role));

        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return GenerateToken(user);
    }

    private static string GetBlockedAccessMessage(UserRole role) => role switch
    {
        UserRole.Admin => "Acesso bloqueado. Entre em contato com o administrador da SmartCollect.",
        UserRole.Worker => "Acesso bloqueado. Entre em contato com seu administrador.",
        _ => "Acesso bloqueado."
    };

    public async Task<AuthResponse?> RegisterAsync(Guid tenantId, RegisterRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == tenantId && t.Active);
        if (!tenantExists) return null;

        var exists = await _db.Users.AnyAsync(u => u.TenantId == tenantId && u.Email == normalizedEmail);
        if (exists) return null;

        var user = new Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Worker,
            Active = true
        };

        await _db.Users.AddAsync(user);
        await _db.SaveChangesAsync();

        user = await _db.Users.Include(u => u.Tenant).FirstAsync(u => u.Id == user.Id);

        return GenerateToken(user);
    }

    public async Task<AuthResponse?> RegisterMasterAsync(RegisterRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Master users are unique by email globally (no TenantId)
        var exists = await _db.Users.AnyAsync(u => u.TenantId == null && u.Email == normalizedEmail);
        if (exists) return null;

        var user = new Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            Name = request.Name,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Master,
            Active = true
        };

        await _db.Users.AddAsync(user);
        await _db.SaveChangesAsync();

        return GenerateToken(user);
    }

    public async Task<AuthResponse?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId && u.Active);

        if (user is null)
            return null;

        var name = request.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Nome é obrigatório.");

        var normalizedEmail = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new InvalidOperationException("E-mail é obrigatório.");

        if (user.Role == UserRole.Master)
        {
            var emailExistsForMaster = await _db.Users.AnyAsync(u =>
                u.Id != user.Id &&
                u.TenantId == null &&
                u.Email == normalizedEmail);

            if (emailExistsForMaster)
                throw new InvalidOperationException("E-mail já cadastrado para usuário Master.");
        }
        else
        {
            var emailExistsForTenant = await _db.Users.AnyAsync(u =>
                u.Id != user.Id &&
                u.TenantId == user.TenantId &&
                u.Email == normalizedEmail);

            if (emailExistsForTenant)
                throw new InvalidOperationException("E-mail já cadastrado neste tenant.");
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
                throw new InvalidOperationException("Informe a senha atual para alterar a senha.");

            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                throw new InvalidOperationException("Senha atual inválida.");

            if (request.NewPassword.Trim().Length < 6)
                throw new InvalidOperationException("A nova senha deve ter pelo menos 6 caracteres.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        }

        user.Name = name;
        user.Email = normalizedEmail;

        if (request.RemovePhoto)
        {
            user.PhotoUrl = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.PhotoUrl))
        {
            var photoUrl = request.PhotoUrl.Trim();
            if (photoUrl.Length > 500_000)
                throw new InvalidOperationException("A foto é muito grande. Use uma imagem menor.");

            user.PhotoUrl = photoUrl;
        }

        await _db.SaveChangesAsync();

        return GenerateToken(user);
    }

    private AuthResponse GenerateToken(Domain.Entities.User user)
    {
        var jwtKeyValue = Environment.GetEnvironmentVariable("JWT_KEY")
            ?? _config["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT Key not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKeyValue));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expirationMinutes = int.Parse(
            Environment.GetEnvironmentVariable("JWT_EXPIRATION_MINUTES")
            ?? _config["Jwt:ExpirationMinutes"]
            ?? "480");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        // TenantId claim only for non-Master users
        if (user.TenantId.HasValue)
            claims.Add(new Claim("TenantId", user.TenantId.Value.ToString()));

        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);
        var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? _config["Jwt:Issuer"] ?? "SmartCollect";
        var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? _config["Jwt:Audience"] ?? "SmartCollect";

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return new AuthResponse(
            Token: new JwtSecurityTokenHandler().WriteToken(token),
            UserName: user.Name,
            Email: user.Email,
            Role: user.Role.ToString(),
            TenantId: user.TenantId ?? Guid.Empty,
            ExpiresAt: expiresAt,
            PhotoUrl: user.PhotoUrl);
    }
}
