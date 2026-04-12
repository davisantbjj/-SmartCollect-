using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Common;
using SmartCollect.Application.Interfaces;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class SyncServiceTests
{
    private static async Task<(SyncService service, SmartCollect.Infrastructure.Data.AppDbContext db, Guid tenantId, FakeDispatchDeliveryService mailer)> SetupWithTitleAsync(
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
        var fakeMailer = new FakeDispatchDeliveryService();

        return (
            new SyncService(
                db,
                new TestHttpClientFactory(),
                dataProtectionProvider,
                configuration,
                NullLogger<SyncService>.Instance,
                fakeMailer),
            db,
            tenantId,
            fakeMailer);
    }

    [Fact]
    public async Task RN06_PaidOccurrence_OnAlreadyPaidTitle_IsIdempotent()
    {
        var (svc, db, tenantId, _) = await SetupWithTitleAsync(TitleStatus.Paid);

        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid);

        var occurrences = await db.Occurrences.Where(o => o.Title.UniqueCode == "T001").ToListAsync();
        Assert.Single(occurrences);
    }

    [Fact]
    public async Task RN07_PaidOccurrence_CancelsPendingDispatches()
    {
        var (svc, db, tenantId, _) = await SetupWithTitleAsync();

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
        var (svc, db, tenantId, mailer) = await SetupWithTitleAsync();

        var title = await db.Titles.FirstAsync(t => t.UniqueCode == "T001");
        db.Contacts.Add(new Contact
        {
            Id = Guid.NewGuid(),
            ClientId = title.ClientId,
            Name = "Finance",
            Email = "finance@test.com",
            IsPrimary = true
        });

        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Thank You",
            Subject = "Pagamento confirmado {{CodigoTitulo}}",
            Body = "Obrigado pelo pagamento do título {{CodigoTitulo}}.",
            Type = TemplateType.ThankYou,
            Active = true
        });
        await db.SaveChangesAsync();

        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid);

        var occurrence = await db.Occurrences.FirstAsync(o => o.Title.UniqueCode == "T001");
        Assert.True(occurrence.ThankYouSent);
        Assert.Equal(1, mailer.QuickEmailAttempts);
        Assert.Equal("finance@test.com", mailer.AttemptedRecipients.Single());
    }

    [Fact]
    public async Task RN08_PaidOccurrence_NoThankYou_WhenNoTemplate()
    {
        var (svc, db, tenantId, mailer) = await SetupWithTitleAsync();

        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid);

        var occurrence = await db.Occurrences.FirstAsync(o => o.Title.UniqueCode == "T001");
        Assert.False(occurrence.ThankYouSent);
        Assert.Equal(0, mailer.QuickEmailAttempts);
    }

    [Fact]
    public async Task ProcessOccurrenceAsync_DuplicateStatusSameDay_IsIgnored()
    {
        var (svc, db, tenantId, _) = await SetupWithTitleAsync();

        var day = DateTime.UtcNow.Date;
        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Overdue, day);
        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Overdue, day.AddHours(3));

        var occurrences = await db.Occurrences.Where(o => o.Title.UniqueCode == "T001").ToListAsync();
        Assert.Single(occurrences);
    }

    [Fact]
    public async Task RN08_PaidOccurrence_FallbacksToSecondaryEmail_WhenPrimaryFails()
    {
        var (svc, db, tenantId, mailer) = await SetupWithTitleAsync();

        var title = await db.Titles.FirstAsync(t => t.UniqueCode == "T001");
        db.Contacts.Add(new Contact
        {
            Id = Guid.NewGuid(),
            ClientId = title.ClientId,
            Name = "Primary",
            Email = "primary@test.com",
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow
        });
        db.Contacts.Add(new Contact
        {
            Id = Guid.NewGuid(),
            ClientId = title.ClientId,
            Name = "Secondary",
            Email = "secondary@test.com",
            IsPrimary = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(1)
        });

        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Thank You",
            Subject = "Pagamento confirmado {{CodigoTitulo}}",
            Body = "Obrigado pelo pagamento do título {{CodigoTitulo}}.",
            Type = TemplateType.ThankYou,
            Active = true
        });
        await db.SaveChangesAsync();

        mailer.EnqueueOutcome(false);
        mailer.EnqueueOutcome(true);

        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid);

        var occurrence = await db.Occurrences.FirstAsync(o => o.Title.UniqueCode == "T001");
        Assert.True(occurrence.ThankYouSent);
        Assert.Equal(2, mailer.QuickEmailAttempts);
        Assert.Equal(new[] { "primary@test.com", "secondary@test.com" }, mailer.AttemptedRecipients);
    }

    [Fact]
    public async Task RN08_PaidOccurrence_SameDayRetry_UpdatesExistingOccurrenceWhenSendSucceeds()
    {
        var (svc, db, tenantId, mailer) = await SetupWithTitleAsync();

        var title = await db.Titles.FirstAsync(t => t.UniqueCode == "T001");
        db.Contacts.Add(new Contact
        {
            Id = Guid.NewGuid(),
            ClientId = title.ClientId,
            Name = "Finance",
            Email = "finance@test.com",
            IsPrimary = true
        });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Thank You",
            Subject = "Pagamento confirmado {{CodigoTitulo}}",
            Body = "Obrigado pelo pagamento do título {{CodigoTitulo}}.",
            Type = TemplateType.ThankYou,
            Active = true
        });
        await db.SaveChangesAsync();

        var day = DateTime.UtcNow.Date;

        mailer.EnqueueOutcome(false);
        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid, day);

        var firstOccurrence = await db.Occurrences.SingleAsync(o => o.Title.UniqueCode == "T001");
        Assert.False(firstOccurrence.ThankYouSent);
        Assert.Equal(1, mailer.QuickEmailAttempts);

        mailer.EnqueueOutcome(true);
        await svc.ProcessOccurrenceAsync(tenantId, "T001", TitleStatus.Paid, day.AddHours(2));

        var occurrences = await db.Occurrences.Where(o => o.Title.UniqueCode == "T001").ToListAsync();
        Assert.Single(occurrences);
        Assert.True(occurrences[0].ThankYouSent);
        Assert.Equal(2, mailer.QuickEmailAttempts);
    }

    [Fact]
    public async Task SyncOccurrence_UnknownUniqueCode_DoesNotCrash()
    {
        var (svc, db, tenantId, _) = await SetupWithTitleAsync();

        // Should not throw
        await svc.ProcessOccurrenceAsync(tenantId, "NONEXISTENT", TitleStatus.Paid);

        var occurrences = await db.Occurrences.ToListAsync();
        Assert.Empty(occurrences);
    }
}

internal sealed class FakeDispatchDeliveryService : IDispatchDeliveryService
{
    public int QuickEmailAttempts { get; private set; }
    public List<string> AttemptedRecipients { get; } = new();
    private readonly Queue<bool> _outcomes = new();

    public void EnqueueOutcome(bool sent)
        => _outcomes.Enqueue(sent);

    public Task<int> ProcessPendingDispatchesAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<QuickSendResult> SendQuickEmailAsync(
        Guid tenantId,
        string recipientName,
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        QuickEmailAttempts++;
        AttemptedRecipients.Add(recipientEmail);

        if (_outcomes.Count > 0)
            return Task.FromResult(_outcomes.Dequeue()
                ? QuickSendResult.Success()
                : QuickSendResult.Fail("Falha no envio SMTP"));

        return Task.FromResult(QuickSendResult.Success());
    }

    public Task<QuickSendResult> SendQuickWhatsAppAsync(
        Guid tenantId,
        string recipientName,
        string recipientPhone,
        string body,
        CancellationToken cancellationToken = default)
        => Task.FromResult(QuickSendResult.Success());
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
