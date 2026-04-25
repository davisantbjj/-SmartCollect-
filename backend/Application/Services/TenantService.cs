namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Tenants;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class TenantService : ITenantService
{
    private readonly IAppDbContext _db;

    public TenantService(IAppDbContext db) => _db = db;

    public async Task<List<TenantResponse>> ListAllAsync()
    {
        return await _db.Tenants
            .OrderBy(t => t.CompanyName)
            .Select(t => new TenantResponse(
                t.Id,
                t.CompanyName,
                t.TaxId,
                t.EmailDomain,
                t.Plan.ToString(),
                t.Active,
                t.Users.Count,
                t.Id == Guid.Empty ? 0 : _db.Titles.Count(ti => ti.TenantId == t.Id),
                t.CreatedAt,
                _db.Users
                    .Where(u => u.TenantId == t.Id && u.Role == UserRole.Admin)
                    .OrderBy(u => u.CreatedAt)
                    .Select(u => u.Name)
                    .FirstOrDefault(),
                _db.Users
                    .Where(u => u.TenantId == t.Id && u.Role == UserRole.Admin)
                    .OrderBy(u => u.CreatedAt)
                    .Select(u => u.Email)
                    .FirstOrDefault()))
            .ToListAsync();
    }

    public async Task<TenantResponse?> GetByIdAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants
            .Include(t => t.Users)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null) return null;

        var titleCount = await _db.Titles.CountAsync(ti => ti.TenantId == tenantId);
        var admin = tenant.Users.FirstOrDefault(u => u.Role == UserRole.Admin);

        return new TenantResponse(
            tenant.Id,
            tenant.CompanyName,
            tenant.TaxId,
            tenant.EmailDomain,
            tenant.Plan.ToString(),
            tenant.Active,
            tenant.Users.Count,
            titleCount,
            tenant.CreatedAt,
            admin?.Name,
            admin?.Email);
    }

    public async Task<TenantResponse> CreateAsync(CreateTenantRequest request)
    {
        var taxIdExists = await _db.Tenants.AnyAsync(t => t.TaxId == request.TaxId);
        if (taxIdExists)
            throw new InvalidOperationException($"A tenant with TaxId '{request.TaxId}' already exists.");

        var normalizedAdminEmail = request.AdminEmail.Trim().ToLowerInvariant();
        var adminEmailExists = await _db.Users.AnyAsync(u => u.Email == normalizedAdminEmail);
        if (adminEmailExists)
            throw new InvalidOperationException("E-mail do administrador já cadastrado no sistema.");

        var tenant = new Domain.Entities.Tenant
        {
            Id = Guid.NewGuid(),
            CompanyName = request.CompanyName,
            TaxId = request.TaxId,
            EmailDomain = request.EmailDomain,
            Plan = TenantPlan.Basic,
            Active = true
        };

        await _db.Tenants.AddAsync(tenant);

        var admin = new Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = request.AdminName,
            Email = normalizedAdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
            Role = UserRole.Admin,
            Active = true
        };

        await _db.Users.AddAsync(admin);
        await _db.SaveChangesAsync();

        return new TenantResponse(
            tenant.Id,
            tenant.CompanyName,
            tenant.TaxId,
            tenant.EmailDomain,
            tenant.Plan.ToString(),
            tenant.Active,
            1,
            0,
            tenant.CreatedAt,
            admin.Name,
            admin.Email);
    }

    public async Task<TenantResponse?> UpdateAsync(Guid tenantId, UpdateTenantRequest request)
    {
        var tenant = await _db.Tenants
            .Include(t => t.Users)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null) return null;

        var companyName = request.CompanyName?.Trim() ?? string.Empty;
        var taxId = request.TaxId?.Trim() ?? string.Empty;
        var emailDomain = request.EmailDomain?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(companyName)
            || string.IsNullOrWhiteSpace(taxId)
            || string.IsNullOrWhiteSpace(emailDomain))
        {
            throw new InvalidOperationException("Dados da empresa são obrigatórios.");
        }

        var taxIdExists = await _db.Tenants.AnyAsync(t => t.Id != tenantId && t.TaxId == taxId);
        if (taxIdExists)
            throw new InvalidOperationException($"A tenant with TaxId '{taxId}' already exists.");

        tenant.CompanyName = companyName;
        tenant.TaxId = taxId;
        tenant.EmailDomain = emailDomain;

        var admin = tenant.Users
            .Where(u => u.Role == UserRole.Admin)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefault();

        if (request.EditAdminLogin)
        {
            if (admin is null)
                throw new InvalidOperationException("Administrador do tenant não encontrado.");

            var adminName = request.AdminName?.Trim() ?? string.Empty;
            var adminEmail = request.AdminEmail?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(adminName) || string.IsNullOrWhiteSpace(adminEmail))
                throw new InvalidOperationException("Nome e e-mail do administrador são obrigatórios.");

            var duplicatedEmail = await _db.Users.AnyAsync(u =>
                u.Id != admin.Id &&
                u.Email == adminEmail);

            if (duplicatedEmail)
                throw new InvalidOperationException("E-mail do administrador já cadastrado no sistema.");

            admin.Name = adminName;
            admin.Email = adminEmail;

            if (!string.IsNullOrWhiteSpace(request.AdminPassword))
                admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword);
        }

        await _db.SaveChangesAsync();

        var titleCount = await _db.Titles.CountAsync(ti => ti.TenantId == tenantId);

        return new TenantResponse(
            tenant.Id,
            tenant.CompanyName,
            tenant.TaxId,
            tenant.EmailDomain,
            tenant.Plan.ToString(),
            tenant.Active,
            tenant.Users.Count,
            titleCount,
            tenant.CreatedAt,
            admin?.Name,
            admin?.Email);
    }

    public async Task<TenantResponse?> UpdateAccessAsync(Guid tenantId, UpdateTenantAccessRequest request)
    {
        var tenant = await _db.Tenants
            .Include(t => t.Users)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null) return null;

        tenant.Active = request.Active;
        await _db.SaveChangesAsync();

        var titleCount = await _db.Titles.CountAsync(ti => ti.TenantId == tenantId);
        var admin = tenant.Users.FirstOrDefault(u => u.Role == UserRole.Admin);

        return new TenantResponse(
            tenant.Id,
            tenant.CompanyName,
            tenant.TaxId,
            tenant.EmailDomain,
            tenant.Plan.ToString(),
            tenant.Active,
            tenant.Users.Count,
            titleCount,
            tenant.CreatedAt,
            admin?.Name,
            admin?.Email);
    }
}
