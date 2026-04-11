using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class SyncServiceTests
{
    private static async Task<(SyncService service, SmartCollect.Infrastructure.Data.AppDbContext db, Guid tenantId)> SetupWithTitleAsync(
        TitleStatus initialStatus = TitleStatus.Open)
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Test Client", TaxId = "456" });
        db.Titles.Add(new Title
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "T001",
            Amount = 1000,
            DueDate = DateTime.UtcNow.AddDays(-5),
            IssueDate = DateTime.UtcNow.AddDays(-30),
            Status = initialStatus
        });
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApi:BaseUrl"] = "http://localhost/",
                ["ExternalApi:DocsUrl"] = "http://localhost/docs"
            })
            .Build();

        var dataProtectionProvider = DataProtectionProvider.Create("SmartCollect.Tests");

        return (
            new SyncService(
                db,
                new TestHttpClientFactory(),
                dataProtectionProvider,
                configuration,
                NullLogger<SyncService>.Instance),
            db,
            tenantId);
    }

    [Fact]
    public async Task RN06_PaidOccurrence_OnAlreadyPaidTitle_IsIdempotent()
    {
        var (svc, db, tenantId) = await SetupWithTitleAsync(TitleStatus.Paid);

        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid);

        var occurrences = await db.Occurrences.Where(o => o.Title.UniqueCode == "T001").ToListAsync();
        Assert.Single(occurrences);
    }

    [Fact]
    public async Task RN07_PaidOccurrence_CancelsPendingDispatches()
    {
        var (svc, db, tenantId) = await SetupWithTitleAsync();

        var title = await db.Titles.FirstAsync(t => t.UniqueCode == "T001");
        var contactId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        db.Contacts.Add(new Contact { Id = contactId, ClientId = title.ClientId, Name = "C1", IsPrimary = true });
        db.Triggers.Add(new Trigger { Id = triggerId, CollectionRuleId = Guid.NewGuid(), TemplateId = Guid.NewGuid() });
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(), TitleId = title.Id, ContactId = contactId, TriggerId = triggerId,
            Status = DispatchStatus.Pending, ScheduledFor = DateTime.UtcNow.AddDays(1)
        });
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(), TitleId = title.Id, ContactId = contactId, TriggerId = triggerId,
            Status = DispatchStatus.Sent, ScheduledFor = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid);

        var dispatches = await db.Dispatches.Where(d => d.TitleId == title.Id).ToListAsync();
        Assert.DoesNotContain(dispatches, d => d.Status == DispatchStatus.Pending);
        Assert.Contains(dispatches, d => d.Status == DispatchStatus.Sent); // already sent stays
    }

    [Fact]
    public async Task RN08_PaidOccurrence_SendsThankYou_OnlyIfTemplateActive()
    {
        var (svc, db, tenantId) = await SetupWithTitleAsync();

        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Thank You",
            Body = "Thank you!", Type = TemplateType.ThankYou, Active = true
        });
        await db.SaveChangesAsync();

        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid);

        var occurrence = await db.Occurrences.FirstAsync(o => o.Title.UniqueCode == "T001");
        Assert.True(occurrence.ThankYouSent);
    }

    [Fact]
    public async Task RN08_PaidOccurrence_NoThankYou_WhenNoTemplate()
    {
        var (svc, db, tenantId) = await SetupWithTitleAsync();

        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid);

        var occurrence = await db.Occurrences.FirstAsync(o => o.Title.UniqueCode == "T001");
        Assert.False(occurrence.ThankYouSent);
    }

    [Fact]
    public async Task SyncOccurrence_UnknownUniqueCode_DoesNotCrash()
    {
        var (svc, db, tenantId) = await SetupWithTitleAsync();

        // Should not throw
        await svc.ProcessOccurrenceAsync(tenantId, "NONEXISTENT", TitleStatus.Paid);

        var occurrences = await db.Occurrences.ToListAsync();
        Assert.Empty(occurrences);
    }
}

internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => new(new EmptyResponseHandler()) { BaseAddress = new Uri("http://localhost") };

    private sealed class EmptyResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
    }
}
