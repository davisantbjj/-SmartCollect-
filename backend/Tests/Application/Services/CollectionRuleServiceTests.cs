using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using SmartCollect.Application.DTOs.CollectionRules;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;

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
    public async Task RN13_ActivatingRule_DeactivatesOthers()
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

        // Rule 1 should now be inactive
        var allRules = await svc.ListAsync(tenantId);
        var updatedRule1 = allRules.First(r => r.Id == rule1.Id);
        Assert.False(updatedRule1.Active);
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
}
