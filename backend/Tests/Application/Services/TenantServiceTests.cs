using SmartCollect.Tests;

namespace SmartCollect.Tests.Application.Services;

using SmartCollect.Application.DTOs.Tenants;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class TenantServiceTests
{
    [Fact]
    public async Task CreateAsync_WithoutAdminInfo_CreatesTenantWithoutAdminUser()
    {
        var db = TestDbContextFactory.Create();
        var service = new TenantService(db);

        var created = await service.CreateAsync(new CreateTenantRequest(
            "Tenant Sem Admin",
            "12345678000100",
            "tenant.com",
            null,
            null,
            null));

        Assert.Equal("Tenant Sem Admin", created.CompanyName);
        Assert.Null(created.AdminName);
        Assert.Null(created.AdminEmail);
        Assert.Equal(0, created.UserCount);

        var users = db.Users.Where(u => u.TenantId == created.Id).ToList();
        Assert.Empty(users);
    }

    [Fact]
    public async Task CreateAsync_WithPartialAdminInfo_Throws()
    {
        var db = TestDbContextFactory.Create();
        var service = new TenantService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateTenantRequest(
                "Tenant Invalido",
                "12345678000101",
                "tenant.com",
                "Admin Sem Email",
                null,
                "senha123")));

        Assert.Contains("administrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_DuplicateAdminEmailGlobally_Throws()
    {
        var db = TestDbContextFactory.Create();
        var existingTenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Existing", TaxId = "111" };
        db.Tenants.Add(existingTenant);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = existingTenant.Id,
            Name = "Existing Admin",
            Email = "admin@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = true
        });
        await db.SaveChangesAsync();

        var service = new TenantService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateTenantRequest(
                "New Tenant",
                "222",
                "new.com",
                "Admin",
                "admin@test.com",
                "password")));

        Assert.Equal("E-mail do administrador já cadastrado no sistema.", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateTaxId_Throws()
    {
        var db = TestDbContextFactory.Create();
        var tenantA = new Tenant { Id = Guid.NewGuid(), CompanyName = "A", TaxId = "111", EmailDomain = "a.com" };
        var tenantB = new Tenant { Id = Guid.NewGuid(), CompanyName = "B", TaxId = "222", EmailDomain = "b.com" };
        var adminA = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Name = "Admin A",
            Email = "admin-a@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = true
        };
        var adminB = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB.Id,
            Name = "Admin B",
            Email = "admin-b@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = true
        };
        tenantA.Users.Add(adminA);
        tenantB.Users.Add(adminB);
        db.Tenants.AddRange(tenantA, tenantB);
        db.Users.AddRange(adminA, adminB);
        await db.SaveChangesAsync();

        var service = new TenantService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(tenantB.Id, new UpdateTenantRequest(
                "B",
                "111",
                "b.com")));

        Assert.Equal("A tenant with TaxId '111' already exists.", ex.Message);
    }
}
