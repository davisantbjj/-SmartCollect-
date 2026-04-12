using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Contacts;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;

public class ContactServiceTests
{
    private static async Task<(ContactService service, SmartCollect.Infrastructure.Data.AppDbContext db, Guid tenantId, Guid clientId)> SetupAsync()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Client Co", TaxId = "456" });
        await db.SaveChangesAsync();

        return (new ContactService(db), db, tenantId, clientId);
    }

    [Fact]
    public async Task RN10_FirstContact_DefaultsToPrimary()
    {
        var (svc, db, tenantId, clientId) = await SetupAsync();

        var result = await svc.CreateAsync(tenantId, clientId,
            new CreateContactRequest("John", "john@test.com", null, "Finance", false));

        Assert.True(result.IsPrimary);
    }

    [Fact]
    public async Task RN10_SettingNewPrimary_UnprimariesPrevious()
    {
        var (svc, db, tenantId, clientId) = await SetupAsync();

        var first = await svc.CreateAsync(tenantId, clientId,
            new CreateContactRequest("First", "first@test.com", null, "Finance", true));
        Assert.True(first.IsPrimary);

        var second = await svc.CreateAsync(tenantId, clientId,
            new CreateContactRequest("Second", "second@test.com", null, "Finance", true));
        Assert.True(second.IsPrimary);

        // First should no longer be primary
        var contacts = await svc.ListByClientAsync(tenantId, clientId);
        var firstUpdated = contacts.First(c => c.Id == first.Id);
        Assert.False(firstUpdated.IsPrimary);
    }

    [Fact]
    public async Task Create_NonExistentClient_ThrowsException()
    {
        var (svc, _, tenantId, _) = await SetupAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(tenantId, Guid.NewGuid(),
                new CreateContactRequest("Test", "test@test.com", null, "Finance", false)));
    }

    [Fact]
    public async Task Create_WithOnlyInvalidPhone_MarksContactAsPending()
    {
        var (svc, _, tenantId, clientId) = await SetupAsync();

        await svc.CreateAsync(
            tenantId,
            clientId,
            new CreateContactRequest("Phone Only", "", "(", "Finance", false));

        var contacts = await svc.ListByClientAsync(tenantId, clientId);
        var created = contacts.Single(c => c.Name == "Phone Only");

        Assert.Equal("pending", created.Status);
        Assert.Null(created.WhatsAppPhone);
    }

    [Fact]
    public async Task Create_WithEmailAndInvalidPhone_MarksContactAsPartial()
    {
        var (svc, _, tenantId, clientId) = await SetupAsync();

        await svc.CreateAsync(
            tenantId,
            clientId,
            new CreateContactRequest("Email Valid", "email@test.com", "(", "Finance", false));

        var contacts = await svc.ListByClientAsync(tenantId, clientId);
        var created = contacts.Single(c => c.Name == "Email Valid");

        Assert.Equal("partial", created.Status);
        Assert.Null(created.WhatsAppPhone);
    }
}
