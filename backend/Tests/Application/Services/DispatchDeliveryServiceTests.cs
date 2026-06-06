using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class DispatchDeliveryServiceTests
{
    [Fact]
    public async Task ProcessPendingDispatches_WithoutTenantSmtpConfig_MarksDispatchAsError()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var dispatchId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant SMTP Missing",
            TaxId = "123",
            Active = true,
            SmtpHost = null,
            SmtpPort = null,
            SmtpUser = null,
            SmtpPasswordEncrypted = null
        });

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", Email = "contato@test.com", IsPrimary = true });

        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-DISPATCH-001",
            Amount = 199.90m,
            DueDate = DateTime.UtcNow.AddDays(5),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });

        db.CollectionRules.Add(new CollectionRule { Id = ruleId, TenantId = tenantId, Name = "Regra", Active = true });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template",
            Channel = CollectionChannel.Email,
            Subject = "Cobrança {{CodigoTitulo}}",
            Body = "Olá {{ClienteNome}}, valor {{Valor}}",
            Type = TemplateType.Collection,
            Active = true
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

        db.Dispatches.Add(new Dispatch
        {
            Id = dispatchId,
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = triggerId,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-1)
        });

        await db.SaveChangesAsync();

        using var keyDir = new TempKeyDirectory();
        var dataProtection = DataProtectionProvider.Create(keyDir.Path);
        var service = new DispatchDeliveryService(db, dataProtection, NullLogger<DispatchDeliveryService>.Instance);

        var processed = await service.ProcessPendingDispatchesAsync(tenantId);

        Assert.Equal(0, processed);
        var dispatch = db.Dispatches.Single(d => d.Id == dispatchId);
        Assert.Equal(DispatchStatus.Error, dispatch.Status);
    }

    [Fact]
    public async Task ProcessPendingDispatches_WhatsAppWithoutConfig_MarksDispatchAsError()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var dispatchId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant WA Missing",
            TaxId = "123",
            Active = true,
            WhatsAppApiToken = null
        });

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", WhatsAppPhone = "11999999999", IsPrimary = true });

        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-WA-001",
            Amount = 199.90m,
            DueDate = DateTime.UtcNow.AddDays(5),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });

        db.CollectionRules.Add(new CollectionRule { Id = ruleId, TenantId = tenantId, Name = "Regra", Active = true });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template WA",
            Channel = CollectionChannel.WhatsApp,
            Subject = null,
            Body = "Olá {{ClienteNome}}, valor {{Valor}}",
            Type = TemplateType.Collection,
            Active = true
        });

        db.Triggers.Add(new Trigger
        {
            Id = triggerId,
            CollectionRuleId = ruleId,
            TemplateId = templateId,
            Channel = CollectionChannel.WhatsApp,
            DaysOffset = 0,
            Reference = TriggerReference.DueDate,
            Order = 1,
            Active = true
        });

        db.Dispatches.Add(new Dispatch
        {
            Id = dispatchId,
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = triggerId,
            Channel = CollectionChannel.WhatsApp,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-1)
        });

        await db.SaveChangesAsync();

        using var keyDir = new TempKeyDirectory();
        var dataProtection = DataProtectionProvider.Create(keyDir.Path);
        var service = new DispatchDeliveryService(db, dataProtection, NullLogger<DispatchDeliveryService>.Instance);

        var processed = await service.ProcessPendingDispatchesAsync(tenantId);

        Assert.Equal(0, processed);
        var dispatch = db.Dispatches.Single(d => d.Id == dispatchId);
        Assert.Equal(DispatchStatus.Error, dispatch.Status);
    }

    [Fact]
    public async Task ProcessPendingDispatches_WhenRuleInactive_CancelsDispatch()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var dispatchId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant Rule Inactive",
            TaxId = "123",
            Active = true,
        });

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", Email = "contato@test.com", IsPrimary = true });

        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-RULE-INACTIVE-001",
            Amount = 99.90m,
            DueDate = DateTime.UtcNow,
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });

        db.CollectionRules.Add(new CollectionRule { Id = ruleId, TenantId = tenantId, Name = "Regra", Active = false });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template",
            Channel = CollectionChannel.Email,
            Subject = "Assunto",
            Body = "Body",
            Type = TemplateType.Collection,
            Active = true
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

        db.Dispatches.Add(new Dispatch
        {
            Id = dispatchId,
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = triggerId,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-1)
        });

        await db.SaveChangesAsync();

        using var keyDir = new TempKeyDirectory();
        var dataProtection = DataProtectionProvider.Create(keyDir.Path);
        var service = new DispatchDeliveryService(db, dataProtection, NullLogger<DispatchDeliveryService>.Instance);

        var processed = await service.ProcessPendingDispatchesAsync(tenantId);

        Assert.Equal(0, processed);
        var dispatch = db.Dispatches.Single(d => d.Id == dispatchId);
        Assert.Equal(DispatchStatus.Cancelled, dispatch.Status);
    }

    [Fact]
    public async Task ProcessPendingDispatches_OutsideDispatchWindow_KeepsDispatchPending()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var dispatchId = Guid.NewGuid();

        var nowMinutes = DateTime.UtcNow.Hour * 60 + DateTime.UtcNow.Minute;
        var startMinutes = (nowMinutes + 120) % 1440;
        var endMinutes = (startMinutes + 120) % 1440;

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant Window",
            TaxId = "123",
            Active = true,
            DispatchWindowEnabled = true,
            DispatchWindowStartMinutes = startMinutes,
            DispatchWindowEndMinutes = endMinutes,
            DispatchWindowTimeZone = "UTC",
            PauseAutomaticDispatchDuringProcessing = true
        });

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", Email = "contato@test.com", IsPrimary = true });

        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-WINDOW-001",
            Amount = 100m,
            DueDate = DateTime.UtcNow.AddDays(1),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });

        db.CollectionRules.Add(new CollectionRule { Id = ruleId, TenantId = tenantId, Name = "Regra", Active = true });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template",
            Channel = CollectionChannel.Email,
            Subject = "Assunto",
            Body = "Body",
            Type = TemplateType.Collection,
            Active = true
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

        db.Dispatches.Add(new Dispatch
        {
            Id = dispatchId,
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = triggerId,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-5)
        });

        await db.SaveChangesAsync();

        using var keyDir = new TempKeyDirectory();
        var dataProtection = DataProtectionProvider.Create(keyDir.Path);
        var service = new DispatchDeliveryService(db, dataProtection, NullLogger<DispatchDeliveryService>.Instance);

        var processed = await service.ProcessPendingDispatchesAsync(tenantId);

        Assert.Equal(0, processed);

        var dispatch = db.Dispatches.Single(d => d.Id == dispatchId);
        Assert.Equal(DispatchStatus.Pending, dispatch.Status);
        Assert.True(dispatch.ScheduledFor > DateTime.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task ProcessPendingDispatches_WithImportProcessing_KeepsDispatchPending()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var dispatchId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant Import Lock",
            TaxId = "123",
            Active = true,
            DispatchWindowEnabled = false,
            PauseAutomaticDispatchDuringProcessing = true
        });

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", Email = "contato@test.com", IsPrimary = true });

        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-IMPORT-LOCK-001",
            Amount = 100m,
            DueDate = DateTime.UtcNow.AddDays(1),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });

        db.CollectionRules.Add(new CollectionRule { Id = ruleId, TenantId = tenantId, Name = "Regra", Active = true });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template",
            Channel = CollectionChannel.Email,
            Subject = "Assunto",
            Body = "Body",
            Type = TemplateType.Collection,
            Active = true
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

        db.Dispatches.Add(new Dispatch
        {
            Id = dispatchId,
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = triggerId,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-5)
        });

        db.FileImports.Add(new FileImport
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = "in-progress.csv",
            Type = ImportType.Csv,
            Status = ImportStatus.Processing,
            TotalRows = 0,
            SuccessRows = 0,
            ErrorRows = 0,
        });

        await db.SaveChangesAsync();

        using var keyDir = new TempKeyDirectory();
        var dataProtection = DataProtectionProvider.Create(keyDir.Path);
        var service = new DispatchDeliveryService(db, dataProtection, NullLogger<DispatchDeliveryService>.Instance);

        var processed = await service.ProcessPendingDispatchesAsync(tenantId);

        Assert.Equal(0, processed);

        var dispatch = db.Dispatches.Single(d => d.Id == dispatchId);
        Assert.Equal(DispatchStatus.Pending, dispatch.Status);
    }

    [Fact]
    public async Task ProcessPendingDispatches_WithRateLimit_CancelsDuplicateDispatch()
    {
        var db = TestDbContextFactory.Create();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var triggerId1 = Guid.NewGuid();
        var triggerId2 = Guid.NewGuid();
        var dispatchIdSent = Guid.NewGuid();
        var dispatchIdPending = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant Rate Limit",
            TaxId = "123",
            Active = true,
            DispatchWindowEnabled = false
        });

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x" });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", Email = "contato@test.com", IsPrimary = true });

        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "TIT-RATELIMIT-001",
            Amount = 100m,
            DueDate = DateTime.UtcNow.AddDays(1),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });

        db.CollectionRules.Add(new CollectionRule { Id = ruleId, TenantId = tenantId, Name = "Regra", Active = true });
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = templateId,
            TenantId = tenantId,
            Name = "Template",
            Channel = CollectionChannel.Email,
            Subject = "Assunto",
            Body = "Body",
            Type = TemplateType.Collection,
            Active = true
        });

        db.Triggers.Add(new Trigger
        {
            Id = triggerId1,
            CollectionRuleId = ruleId,
            TemplateId = templateId,
            Channel = CollectionChannel.Email,
            DaysOffset = 0,
            Reference = TriggerReference.DueDate,
            Order = 1,
            Active = true
        });

        db.Triggers.Add(new Trigger
        {
            Id = triggerId2,
            CollectionRuleId = ruleId,
            TemplateId = templateId,
            Channel = CollectionChannel.Email,
            DaysOffset = 0,
            Reference = TriggerReference.DueDate,
            Order = 2,
            Active = true
        });

        // Already sent today
        db.Dispatches.Add(new Dispatch
        {
            Id = dispatchIdSent,
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = triggerId1,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Sent,
            ScheduledFor = DateTime.UtcNow.AddHours(-2),
            SentAt = DateTime.UtcNow.AddHours(-1)
        });

        // Pending dispatch for the same title/contact/channel today
        db.Dispatches.Add(new Dispatch
        {
            Id = dispatchIdPending,
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = triggerId2,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-5)
        });

        await db.SaveChangesAsync();

        using var keyDir = new TempKeyDirectory();
        var dataProtection = DataProtectionProvider.Create(keyDir.Path);
        var service = new DispatchDeliveryService(db, dataProtection, NullLogger<DispatchDeliveryService>.Instance);

        var processed = await service.ProcessPendingDispatchesAsync(tenantId);

        // Expect it to process and cancel it (so 0 successful sends? Wait, cancellation counts as processing loop but processed is incremented only on success. Wait, processed is incremented on success only).
        // It returns processed = 0 because it was cancelled.
        Assert.Equal(0, processed);

        var dispatch = db.Dispatches.Single(d => d.Id == dispatchIdPending);
        Assert.Equal(DispatchStatus.Cancelled, dispatch.Status);
    }

    private sealed class TempKeyDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "smartcollect-tests-keys-" + Guid.NewGuid());

        public TempKeyDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
