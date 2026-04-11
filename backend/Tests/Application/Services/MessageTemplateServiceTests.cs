using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using SmartCollect.Application.DTOs.Templates;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;

public class MessageTemplateServiceTests
{
    [Fact]
    public async Task RN16_EmailTemplate_RequiresSubject()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        await db.SaveChangesAsync();

        var svc = new MessageTemplateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateAsync(tenantId, new CreateTemplateRequest("Test", "Email", null, "body", "Collection")));
    }

    [Fact]
    public async Task RN16_WhatsAppTemplate_DoesNotRequireSubject()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, CompanyName = "Test", TaxId = "123" });
        await db.SaveChangesAsync();

        var svc = new MessageTemplateService(db);
        var result = await svc.CreateAsync(tenantId,
            new CreateTemplateRequest("WA Template", "WhatsApp", null, "Hello {{NomeCliente}}", "Collection"));

        Assert.NotNull(result);
        Assert.Equal("WhatsApp", result.Channel);
        Assert.Null(result.Subject);
    }
}
