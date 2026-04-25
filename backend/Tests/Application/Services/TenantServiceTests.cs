using SmartCollect.Tests;

namespace SmartCollect.Tests.Application.Services;

using SmartCollect.Application.DTOs.Tenants;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class TenantServiceTests
{
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
    public async Task UpdateAsync_DuplicateAdminEmailGlobally_Throws()
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
                "222",
                "b.com",
                true,
                "Admin B",
                "admin-a@test.com",
                null)));

        Assert.Equal("E-mail do administrador já cadastrado no sistema.", ex.Message);
    }
}
