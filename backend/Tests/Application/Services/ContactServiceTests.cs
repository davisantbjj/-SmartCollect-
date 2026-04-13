using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Contacts;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

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

    [Fact]
    public async Task Delete_PrimaryContact_ReassignsPrimaryToAnotherContact()
    {
        var (svc, _, tenantId, clientId) = await SetupAsync();

        var first = await svc.CreateAsync(tenantId, clientId,
            new CreateContactRequest("Primeiro", "p1@test.com", null, "Finance", true));
        var second = await svc.CreateAsync(tenantId, clientId,
            new CreateContactRequest("Segundo", "p2@test.com", null, "Finance", false));

        var removed = await svc.DeleteAsync(tenantId, clientId, first.Id);

        Assert.True(removed);

        var contacts = await svc.ListByClientAsync(tenantId, clientId);
        Assert.Single(contacts);
        Assert.Equal(second.Id, contacts[0].Id);
        Assert.True(contacts[0].IsPrimary);
    }

    [Fact]
    public async Task Delete_WithDispatchHistory_ThrowsInvalidOperationException()
    {
        var (svc, db, tenantId, clientId) = await SetupAsync();

        var contact = await svc.CreateAsync(tenantId, clientId,
            new CreateContactRequest("Contato", "contato@test.com", null, "Finance", true));

        var titleId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();

        db.CollectionRules.Add(new CollectionRule { Id = ruleId, TenantId = tenantId, Name = "Regra", Active = true });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template",
            Channel = CollectionChannel.Email,
            Body = "Body",
            Type = TemplateType.Collection,
            Active = true,
        });
        db.Triggers.Add(new Trigger
        {
            Id = triggerId,
            CollectionRuleId = ruleId,
            TemplateId = templateId,
            Channel = CollectionChannel.Email,
            DaysOffset = 0,
            Reference = TriggerReference.DueDate,
            Order = 1,
            Active = true,
        });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "T-CONT-001",
            Amount = 100,
            DueDate = DateTime.UtcNow.AddDays(1),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open,
        });
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(),
            TitleId = titleId,
            ContactId = contact.Id,
            TriggerId = triggerId,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(tenantId, clientId, contact.Id));
    }
}
