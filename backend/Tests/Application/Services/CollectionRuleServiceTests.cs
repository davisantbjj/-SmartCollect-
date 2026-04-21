using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.CollectionRules;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class CollectionRuleServiceTests
{
    private static async Task<(CollectionRuleService service, SmartCollect.Infrastructure.Data.AppDbContext db, Guid tenantId, Guid templateId)> SetupAsync()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        db.MessageTemplates.Add(new MessageTemplate { Id = templateId, TenantId = tenantId, Name = "Default", Body = "test" });
        await db.SaveChangesAsync();

        return (new CollectionRuleService(db), db, tenantId, templateId);
    }

    [Fact]
    public async Task MultiActive_ActivatingAnotherRule_KeepsPreviousActive()
    {
        var (svc, db, tenantId, templateId) = await SetupAsync();

        var rule1 = await svc.CreateAsync(tenantId, new CreateCollectionRuleRequest(
            "Rule 1", "First", true,
            new List<CreateTriggerDto> { new(templateId, "Email", -3, "DueDate", 1, true) }));

        Assert.True(rule1.Active);

        var rule2 = await svc.CreateAsync(tenantId, new CreateCollectionRuleRequest(
            "Rule 2", "Second", true,
            new List<CreateTriggerDto> { new(templateId, "Email", 1, "DueDate", 1, true) }));

        Assert.True(rule2.Active);

        // Rule 1 remains active when another active rule is created.
        var allRules = await svc.ListAsync(tenantId);
        var updatedRule1 = allRules.First(r => r.Id == rule1.Id);
        Assert.True(updatedRule1.Active);
    }

    [Fact]
    public async Task RN13_DifferentTenants_CanEachHaveActiveRule()
    {
        var db = TestDbContextFactory.Create();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var tmpl1 = Guid.NewGuid();
        var tmpl2 = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = t1, CompanyName = "T1", TaxId = "111" });
        db.Tenants.Add(new Tenant { Id = t2, CompanyName = "T2", TaxId = "222" });
        db.MessageTemplates.Add(new MessageTemplate { Id = tmpl1, TenantId = t1, Name = "T1", Body = "x" });
        db.MessageTemplates.Add(new MessageTemplate { Id = tmpl2, TenantId = t2, Name = "T2", Body = "x" });
        await db.SaveChangesAsync();

        var svc = new CollectionRuleService(db);

        var r1 = await svc.CreateAsync(t1, new CreateCollectionRuleRequest(
            "R1", null, true, new List<CreateTriggerDto> { new(tmpl1, "Email", -1, "DueDate", 1, true) }));
        var r2 = await svc.CreateAsync(t2, new CreateCollectionRuleRequest(
            "R2", null, true, new List<CreateTriggerDto> { new(tmpl2, "Email", 1, "DueDate", 1, true) }));

        Assert.True(r1.Active);
        Assert.True(r2.Active);
    }

    [Fact]
    public async Task UpdateRule_WhenTriggerHasDispatch_PreservesOldTriggerAndDispatch()
    {
        var (svc, db, tenantId, templateId) = await SetupAsync();

        var created = await svc.CreateAsync(tenantId, new CreateCollectionRuleRequest(
            "Rule Histórico", "Teste", true,
            new List<CreateTriggerDto> { new(templateId, "Email", -3, "DueDate", 1, true) }));

        var oldTrigger = await db.Triggers
            .SingleAsync(t => t.CollectionRuleId == created.Id && t.Order == 1);

        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x", Role = UserRole.Admin, Active = true });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", Email = "contato@test.com", IsPrimary = true });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "T-HIST-001",
            Amount = 100,
            DueDate = DateTime.UtcNow.AddDays(3),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(),
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = oldTrigger.Id,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var updated = await svc.UpdateAsync(tenantId, created.Id, new CreateCollectionRuleRequest(
            "Rule Histórico v2", "Teste 2", true,
            new List<CreateTriggerDto> { new(templateId, "Email", 2, "DueDate", 1, true) }));

        Assert.NotNull(updated);
        Assert.Single(updated!.Triggers);
        Assert.Equal(2, updated.Triggers[0].DaysOffset);

        var preservedOldTrigger = await db.Triggers.FirstOrDefaultAsync(t => t.Id == oldTrigger.Id);
        Assert.NotNull(preservedOldTrigger);
        Assert.False(preservedOldTrigger!.Active);

        var preservedDispatch = await db.Dispatches.FirstOrDefaultAsync(d => d.TriggerId == oldTrigger.Id);
        Assert.NotNull(preservedDispatch);

        var activeTriggers = await db.Triggers
            .Where(t => t.CollectionRuleId == created.Id && t.Active)
            .ToListAsync();
        Assert.Single(activeTriggers);
        Assert.NotEqual(oldTrigger.Id, activeTriggers[0].Id);
    }

    [Fact]
    public async Task UpdateRule_DeactivatingRule_CancelsPendingDispatches()
    {
        var (svc, db, tenantId, templateId) = await SetupAsync();

        var created = await svc.CreateAsync(tenantId, new CreateCollectionRuleRequest(
            "Rule Disable", "Teste", true,
            new List<CreateTriggerDto> { new(templateId, "Email", 0, "DueDate", 1, true) }));

        var trigger = await db.Triggers
            .SingleAsync(t => t.CollectionRuleId == created.Id && t.Order == 1);

        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x", Role = UserRole.Admin, Active = true });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", Email = "contato@test.com", IsPrimary = true });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "T-DISABLE-001",
            Amount = 100,
            DueDate = DateTime.UtcNow.AddDays(1),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(),
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = trigger.Id,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        var updated = await svc.UpdateAsync(tenantId, created.Id, new CreateCollectionRuleRequest(
            "Rule Disable", "Teste", false,
            new List<CreateTriggerDto> { new(templateId, "Email", 0, "DueDate", 1, true) }));

        Assert.NotNull(updated);

        var dispatch = await db.Dispatches.SingleAsync(d => d.TitleId == titleId);
        Assert.Equal(DispatchStatus.Cancelled, dispatch.Status);
    }

    [Fact]
    public async Task List_WhenCreatingDefaultRules_UsesBothChannelInCombinedTriggers()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();

        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Tenant Defaults", TaxId = "123" });

        db.MessageTemplates.AddRange(
            new MessageTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Lembrete D-3",
                Body = "Body D-3",
                Channel = CollectionChannel.Email,
                Active = true,
            },
            new MessageTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Lembrete D-1",
                Body = "Body D-1",
                Channel = CollectionChannel.WhatsApp,
                Active = true,
            },
            new MessageTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Cobranca D+1",
                Body = "Body D+1",
                Channel = CollectionChannel.Both,
                Active = true,
            },
            new MessageTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Cobranca D+7",
                Body = "Body D+7",
                Channel = CollectionChannel.Email,
                Active = true,
            });

        await db.SaveChangesAsync();

        var svc = new CollectionRuleService(db);
        var rules = await svc.ListAsync(tenantId);

        var moderada = rules.Single(r => r.Name == "Régua Moderada");
        Assert.Contains(moderada.Triggers, t => t.DaysOffset == 1 && t.Channel == "Both");

        var escalonada = rules.Single(r => r.Name == "Régua Escalonada");
        Assert.Contains(escalonada.Triggers, t => t.DaysOffset == 2 && t.Channel == "Both");
    }

    [Fact]
    public async Task DeleteRule_WithoutDispatch_RemovesRuleAndTriggers()
    {
        var (svc, db, tenantId, templateId) = await SetupAsync();

        var created = await svc.CreateAsync(tenantId, new CreateCollectionRuleRequest(
            "Rule Delete", "To remove", false,
            new List<CreateTriggerDto>
            {
                new(templateId, "Email", -1, "DueDate", 1, true),
                new(templateId, "WhatsApp", 1, "DueDate", 2, true),
            }));

        var removed = await svc.DeleteAsync(tenantId, created.Id);

        Assert.True(removed);
        Assert.False(await db.CollectionRules.AnyAsync(r => r.Id == created.Id));
        Assert.False(await db.Triggers.AnyAsync(t => t.CollectionRuleId == created.Id));
    }

    [Fact]
    public async Task DeleteRule_WithDispatch_ArchivesRuleAndDeactivatesTriggers()
    {
        var (svc, db, tenantId, templateId) = await SetupAsync();

        var created = await svc.CreateAsync(tenantId, new CreateCollectionRuleRequest(
            "Rule Bound", "Has dispatch", true,
            new List<CreateTriggerDto> { new(templateId, "Email", 0, "DueDate", 1, true) }));

        var trigger = await db.Triggers.SingleAsync(t => t.CollectionRuleId == created.Id && t.Active);

        var userId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var titleId = Guid.NewGuid();

        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "U", Email = "u@test.com", PasswordHash = "x", Role = UserRole.Admin, Active = true });
        db.Clients.Add(new Client { Id = clientId, TenantId = tenantId, UserId = userId, LegalName = "Cliente", TaxId = "999" });
        db.Contacts.Add(new Contact { Id = contactId, ClientId = clientId, Name = "Contato", Email = "contato@test.com", IsPrimary = true });
        db.Titles.Add(new Title
        {
            Id = titleId,
            TenantId = tenantId,
            ClientId = clientId,
            UniqueCode = "T-DEL-001",
            Amount = 100,
            DueDate = DateTime.UtcNow.AddDays(1),
            IssueDate = DateTime.UtcNow,
            Status = TitleStatus.Open
        });
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(),
            TitleId = titleId,
            ContactId = contactId,
            TriggerId = trigger.Id,
            Channel = CollectionChannel.Email,
            Status = DispatchStatus.Pending,
            ScheduledFor = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var removed = await svc.DeleteAsync(tenantId, created.Id);

        Assert.True(removed);

        var persistedRule = await db.CollectionRules
            .Include(r => r.Triggers)
            .FirstAsync(r => r.Id == created.Id);

        Assert.False(persistedRule.Active);
        Assert.All(persistedRule.Triggers, t => Assert.False(t.Active));

        var listedRules = await svc.ListAsync(tenantId);
        Assert.DoesNotContain(listedRules, r => r.Id == created.Id);
    }

    [Fact]
    public async Task DeleteRule_DefaultRule_ThrowsInvalidOperationException()
    {
        var (svc, _, tenantId, templateId) = await SetupAsync();

        var created = await svc.CreateAsync(tenantId, new CreateCollectionRuleRequest(
            "Régua Preventiva", "Default", true,
            new List<CreateTriggerDto> { new(templateId, "Email", 0, "DueDate", 1, true) }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.DeleteAsync(tenantId, created.Id));
        Assert.Contains("régua padrão", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
