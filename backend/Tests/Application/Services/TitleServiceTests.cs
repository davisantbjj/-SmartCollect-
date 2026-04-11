using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using SmartCollect.Application.DTOs.Titles;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;

public class TitleServiceTests
{
    [Fact]
    public async Task Create_WithClientFromAnotherTenant_ThrowsInvalidOperationException()
    {
        var db = TestDbContextFactory.Create();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantA, CompanyName = "Tenant A", TaxId = "111" });
        db.Tenants.Add(new Tenant { Id = tenantB, CompanyName = "Tenant B", TaxId = "222" });
        db.Users.Add(new User { Id = userA, TenantId = tenantA, Name = "A", Email = "a@test.com", PasswordHash = "x" });
        db.Users.Add(new User { Id = userB, TenantId = tenantB, Name = "B", Email = "b@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientB, TenantId = tenantB, UserId = userB, LegalName = "Client B", TaxId = "333" });
        await db.SaveChangesAsync();

        var service = new TitleService(db);

        var request = new CreateTitleRequest(
            clientB,
            "TIT-SEC-001",
            1000m,
            DateTime.UtcNow.AddDays(10),
            DateTime.UtcNow,
            null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(tenantA, request));
    }
}
