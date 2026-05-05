using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Common;
using SmartCollect.Application.DTOs.Titles;
using SmartCollect.Application.Interfaces;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class TitleServiceTests
{
    [Fact]
    public async Task CreateAsync_WithUnspecifiedDates_NormalizesToUtc()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Tenant A", TaxId = "111" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "User", Email = "user@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "123" });
        await db.SaveChangesAsync();

        var due = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Unspecified);
        var issue = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var service = new TitleService(db);
        var created = await service.CreateAsync(tenantId, new CreateTitleRequest(
            clientId,
            "TIT-UTC-001",
            100m,
            due,
            issue,
            null));

        var persisted = await db.Titles.FirstAsync(t => t.Id == created.Id);
        Assert.Equal(DateTimeKind.Utc, persisted.DueDate.Kind);
        Assert.Equal(DateTimeKind.Utc, persisted.IssueDate.Kind);
        Assert.Equal(due.Date, persisted.DueDate.Date);
        Assert.Equal(issue.Date, persisted.IssueDate.Date);
    }

    [Fact]
    public async Task CreateAsync_UpsertWithUnspecifiedDates_NormalizesToUtcOnUpdate()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Tenant A", TaxId = "111" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "User", Email = "user@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "123" });

        db.Titles.Add(new Title
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-UTC-002",
            Amount = 50m,
            DueDate = DateTime.UtcNow.Date,
            IssueDate = DateTime.UtcNow.Date,
            Status = TitleStatus.Open
        });
        await db.SaveChangesAsync();

        var due = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var issue = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Unspecified);

        var service = new TitleService(db);
        await service.CreateAsync(tenantId, new CreateTitleRequest(
            clientId,
            "TIT-UTC-002",
            75m,
            due,
            issue,
            "https://boleto.local"));

        var persisted = await db.Titles.FirstAsync(t => t.TenantId == tenantId && t.UniqueCode == "TIT-UTC-002");
        Assert.Equal(DateTimeKind.Utc, persisted.DueDate.Kind);
        Assert.Equal(DateTimeKind.Utc, persisted.IssueDate.Kind);
        Assert.Equal(due.Date, persisted.DueDate.Date);
        Assert.Equal(issue.Date, persisted.IssueDate.Date);
    }

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

    [Fact]
    public async Task SendCollection_WithMultipleActiveRules_SchedulesFromAllAndInvokesEngine()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var template1Id = Guid.NewGuid();
        var template2Id = Guid.NewGuid();
        var rule1Id = Guid.NewGuid();
        var rule2Id = Guid.NewGuid();
        var trigger1Id = Guid.NewGuid();
        var trigger2Id = Guid.NewGuid();

        var dueDate = DateTime.UtcNow.Date.AddDays(10);
        var issueDate = DateTime.UtcNow.Date;

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Tenant A", TaxId = "111" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "User", Email = "user@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente Teste", TaxId = "123" });
        db.Contacts.Add(new Contact
        {
            Id = contactId,
            ClientId = clientId,
            Name = "Contato",
            Email = "contato@test.com",
            IsPrimary = true
        });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-001",
            Amount = 100m,
            DueDate = dueDate,
            IssueDate = issueDate,
            Status = TitleStatus.Open
        });

        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = template1Id,
            TenantId = tenantId,
            Name = "Template 1",
            Channel = CollectionChannel.Email,
            Subject = "Assunto 1",
            Body = "Body 1",
            Active = true
        });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = template2Id,
            TenantId = tenantId,
            Name = "Template 2",
            Channel = CollectionChannel.Email,
            Subject = "Assunto 2",
            Body = "Body 2",
            Active = true
        });

        db.CollectionRules.Add(new CollectionRule { Id = rule1Id, TenantId = tenantId, Name = "Regra 1", Active = true });
        db.CollectionRules.Add(new CollectionRule { Id = rule2Id, TenantId = tenantId, Name = "Regra 2", Active = true });
        db.Triggers.Add(new Trigger
        {
            Id = trigger1Id,
            CollectionRuleId = rule1Id,
            TemplateId = template1Id,
            Channel = CollectionChannel.Email,
            DaysOffset = -1,
            Reference = TriggerReference.DueDate,
            Order = 1,
            Active = true
        });
        db.Triggers.Add(new Trigger
        {
            Id = trigger2Id,
            CollectionRuleId = rule2Id,
            TemplateId = template2Id,
            Channel = CollectionChannel.Email,
            DaysOffset = 2,
            Reference = TriggerReference.DueDate,
            Order = 1,
            Active = true
        });

        await db.SaveChangesAsync();

        var fakeDispatchService = new FakeDispatchDeliveryService();
        var service = new TitleService(db, fakeDispatchService);

        var ok = await service.SendCollectionAsync(tenantId, titleId);

        Assert.True(ok);

        var dispatches = db.Dispatches.Where(d => d.TitleId == titleId).OrderBy(d => d.ScheduledFor).ToList();
        Assert.Equal(2, dispatches.Count);
        Assert.Contains(dispatches, d => d.TriggerId == trigger1Id);
        Assert.Contains(dispatches, d => d.TriggerId == trigger2Id);

        var history = db.TitleHistories.Single(h => h.TitleId == titleId && h.Action == "Cobranca manual");
        Assert.Contains("2 disparos agendados em 2 régua(s) ativa(s)", history.Description);

        Assert.True(fakeDispatchService.ProcessCalled);
        Assert.Equal(tenantId, fakeDispatchService.LastTenantId);
    }

    [Fact]
    public async Task SendCollection_QuickTemplateWithBoth_SendsEmailAndWhatsApp()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Tenant A", TaxId = "111" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "User", Email = "user@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente Teste", TaxId = "123" });
        db.Contacts.Add(new Contact
        {
            Id = contactId,
            ClientId = clientId,
            Name = "Contato",
            Email = "contato@test.com",
            WhatsAppPhone = "11999999999",
            IsPrimary = true
        });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-QUICK-001",
            Amount = 100m,
            DueDate = DateTime.UtcNow.Date.AddDays(2),
            IssueDate = DateTime.UtcNow.Date,
            Status = TitleStatus.Open
        });

        await db.SaveChangesAsync();

        var fakeDispatchService = new FakeDispatchDeliveryService();
        var service = new TitleService(db, fakeDispatchService);

        var ok = await service.SendCollectionAsync(tenantId, titleId, new SendCollectionRequest(
            UseQuickTemplate: true,
            Channel: "Both",
            Subject: "Cobrança {{TituloCodigo}}",
            Body: "Olá {{ClienteNome}}, valor {{Valor}}"));

        Assert.True(ok);
        Assert.Equal(1, fakeDispatchService.QuickEmailCalls);
        Assert.Equal(1, fakeDispatchService.QuickWhatsAppCalls);

        var history = db.TitleHistories.Single(h => h.TitleId == titleId && h.Action == "Cobranca manual rapida");
        Assert.Contains("E-mail enviado", history.Description);
        Assert.Contains("WhatsApp enviado", history.Description);
    }

    [Fact]
    public async Task SendCollection_QuickTemplate_SendToAllContacts_SendsForAllRecipients()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contact1Id = Guid.NewGuid();
        var contact2Id = Guid.NewGuid();
        var titleId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Tenant A", TaxId = "111" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "User", Email = "user@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente Teste", TaxId = "123", SendToAllContacts = true });
        db.Contacts.Add(new Contact
        {
            Id = contact1Id,
            ClientId = clientId,
            Name = "Contato 1",
            Email = "contato1@test.com",
            WhatsAppPhone = "11999999999",
            IsPrimary = true
        });
        db.Contacts.Add(new Contact
        {
            Id = contact2Id,
            ClientId = clientId,
            Name = "Contato 2",
            Email = "contato2@test.com",
            WhatsAppPhone = "11988888888",
            IsPrimary = false
        });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-QUICK-ALL-001",
            Amount = 100m,
            DueDate = DateTime.UtcNow.Date.AddDays(2),
            IssueDate = DateTime.UtcNow.Date,
            Status = TitleStatus.Open
        });

        await db.SaveChangesAsync();

        var fakeDispatchService = new FakeDispatchDeliveryService();
        var service = new TitleService(db, fakeDispatchService);

        var ok = await service.SendCollectionAsync(tenantId, titleId, new SendCollectionRequest(
            UseQuickTemplate: true,
            Channel: "Both",
            Subject: "Cobrança {{TituloCodigo}}",
            Body: "Olá {{ClienteNome}}, valor {{Valor}}"));

        Assert.True(ok);
        Assert.Equal(2, fakeDispatchService.QuickEmailCalls);
        Assert.Equal(2, fakeDispatchService.QuickWhatsAppCalls);
    }

    [Fact]
    public async Task SendCollection_RuleMode_SendToAllContacts_CreatesDispatchForEachEligibleContact()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contact1Id = Guid.NewGuid();
        var contact2Id = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Tenant A", TaxId = "111" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "User", Email = "user@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente Teste", TaxId = "123", SendToAllContacts = true });
        db.Contacts.Add(new Contact
        {
            Id = contact1Id,
            ClientId = clientId,
            Name = "Contato 1",
            Email = "contato1@test.com",
            IsPrimary = true
        });
        db.Contacts.Add(new Contact
        {
            Id = contact2Id,
            ClientId = clientId,
            Name = "Contato 2",
            Email = "contato2@test.com",
            IsPrimary = false
        });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-RULE-ALL-001",
            Amount = 100m,
            DueDate = DateTime.UtcNow.Date.AddDays(2),
            IssueDate = DateTime.UtcNow.Date,
            Status = TitleStatus.Open
        });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template",
            Channel = CollectionChannel.Email,
            Subject = "Assunto",
            Body = "Body",
            Active = true,
        });
        db.CollectionRules.Add(new CollectionRule
        {
            Id = ruleId,
            TenantId = tenantId,
            Name = "Regra",
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
            Active = true
        });

        await db.SaveChangesAsync();

        var fakeDispatchService = new FakeDispatchDeliveryService();
        var service = new TitleService(db, fakeDispatchService);

        var ok = await service.SendCollectionAsync(tenantId, titleId);

        Assert.True(ok);
        Assert.Equal(2, db.Dispatches.Count(d => d.TitleId == titleId));
        Assert.Contains(db.Dispatches, d => d.ContactId == contact1Id);
        Assert.Contains(db.Dispatches, d => d.ContactId == contact2Id);
    }

    [Fact]
    public async Task SendCollection_RuleMode_WithSelectedContacts_CreatesDispatchOnlyForChosenRecipients()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contact1Id = Guid.NewGuid();
        var contact2Id = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Tenant A", TaxId = "111" });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "User", Email = "user@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente Teste", TaxId = "123", SendToAllContacts = true });
        db.Contacts.Add(new Contact
        {
            Id = contact1Id,
            ClientId = clientId,
            Name = "Contato 1",
            Email = "contato1@test.com",
            IsPrimary = true
        });
        db.Contacts.Add(new Contact
        {
            Id = contact2Id,
            ClientId = clientId,
            Name = "Contato 2",
            Email = "contato2@test.com",
            IsPrimary = false
        });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-RULE-SELECT-001",
            Amount = 100m,
            DueDate = DateTime.UtcNow.Date.AddDays(2),
            IssueDate = DateTime.UtcNow.Date,
            Status = TitleStatus.Open
        });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template",
            Channel = CollectionChannel.Email,
            Subject = "Assunto",
            Body = "Body",
            Active = true,
        });
        db.CollectionRules.Add(new CollectionRule
        {
            Id = ruleId,
            TenantId = tenantId,
            Name = "Regra",
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
            Active = true
        });

        await db.SaveChangesAsync();

        var fakeDispatchService = new FakeDispatchDeliveryService();
        var service = new TitleService(db, fakeDispatchService);

        var ok = await service.SendCollectionAsync(tenantId, titleId, new SendCollectionRequest(
            UseQuickTemplate: false,
            Channel: null,
            Subject: null,
            Body: null,
            ContactIds: new List<Guid> { contact2Id }));

        Assert.True(ok);
        Assert.Single(db.Dispatches.Where(d => d.TitleId == titleId));
        Assert.Contains(db.Dispatches, d => d.TitleId == titleId && d.ContactId == contact2Id);
        Assert.DoesNotContain(db.Dispatches, d => d.TitleId == titleId && d.ContactId == contact1Id);
    }

    private sealed class FakeDispatchDeliveryService : IDispatchDeliveryService
    {
        public bool ProcessCalled { get; private set; }
        public Guid? LastTenantId { get; private set; }
        public int QuickEmailCalls { get; private set; }
        public int QuickWhatsAppCalls { get; private set; }
        public bool QuickEmailResult { get; set; } = true;
        public bool QuickWhatsAppResult { get; set; } = true;

        public Task<int> ProcessPendingDispatchesAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
        {
            ProcessCalled = true;
            LastTenantId = tenantId;
            return Task.FromResult(0);
        }

        public Task<QuickSendResult> SendQuickEmailAsync(
            Guid tenantId,
            string recipientName,
            string recipientEmail,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            QuickEmailCalls++;
            return Task.FromResult(QuickEmailResult
                ? QuickSendResult.Success()
                : QuickSendResult.Fail("Falha no envio SMTP"));
        }

        public Task<QuickSendResult> SendQuickWhatsAppAsync(
            Guid tenantId,
            string recipientName,
            string recipientPhone,
            string body,
            CancellationToken cancellationToken = default)
        {
            QuickWhatsAppCalls++;
            return Task.FromResult(QuickWhatsAppResult
                ? QuickSendResult.Success()
                : QuickSendResult.Fail("Falha no envio WhatsApp"));
        }
    }
}
