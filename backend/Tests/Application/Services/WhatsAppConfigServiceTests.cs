using SmartCollect.Tests;
namespace SmartCollect.Tests.Application.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;

public class WhatsAppConfigServiceTests
{
    private static (WhatsAppConfigService service, SmartCollect.Infrastructure.Data.AppDbContext db, Guid tenantId) Setup()
    {
        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant Teste",
            TaxId = "123",
            EmailDomain = "teste.com"
        });
        db.SaveChanges();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicApp:BaseUrl"] = "https://smartcollect.app"
            })
            .Build();

        var dataProtection = DataProtectionProvider.Create("SmartCollect.Tests");

        return (new WhatsAppConfigService(db, dataProtection, config), db, tenantId);
    }

    [Fact]
    public async Task SaveAndGetAsync_WithTwilio_PersistsConfiguration()
    {
        var (service, _, tenantId) = Setup();

        await service.SaveAsync(tenantId, new WhatsAppConfigRequest(
            Provider: "Twilio",
            NumberId: "+14155238886",
            AccessToken: "token-1",
            ApiBaseUrl: "AC00000000000000000000000000000000"));

        var config = await service.GetAsync(tenantId);

        Assert.NotNull(config);
        Assert.Equal("Twilio", config!.Provider);
        Assert.Equal("+14155238886", config.NumberId);
        Assert.True(config.HasAccessToken);
        Assert.Equal("https://smartcollect.app/webhook/whatsapp", config.WebhookUrl);
    }

    [Fact]
    public async Task SaveAsync_WithTwilioSenderWithoutPlus_NormalizesToE164()
    {
        var (service, _, tenantId) = Setup();

        await service.SaveAsync(tenantId, new WhatsAppConfigRequest(
            Provider: "Twilio",
            NumberId: "14155238886",
            AccessToken: "token-1",
            ApiBaseUrl: "AC00000000000000000000000000000000"));

        var config = await service.GetAsync(tenantId);

        Assert.NotNull(config);
        Assert.Equal("+14155238886", config!.NumberId);
    }

    [Fact]
    public async Task SaveAsync_WithoutNewToken_KeepsExistingToken()
    {
        var (service, _, tenantId) = Setup();

        await service.SaveAsync(tenantId, new WhatsAppConfigRequest(
            Provider: "Z-API",
            NumberId: "instance-1",
            AccessToken: "token-original",
            ApiBaseUrl: null));

        await service.SaveAsync(tenantId, new WhatsAppConfigRequest(
            Provider: "Z-API",
            NumberId: "instance-2",
            AccessToken: null,
            ApiBaseUrl: "https://api.z-api.io"));

        var config = await service.GetAsync(tenantId);

        Assert.NotNull(config);
        Assert.Equal("instance-2", config!.NumberId);
        Assert.True(config.HasAccessToken);
    }

    [Fact]
    public async Task SaveAsync_ClearToken_RemovesCredentialAndMakesTestFail()
    {
        var (service, _, tenantId) = Setup();

        await service.SaveAsync(tenantId, new WhatsAppConfigRequest(
            Provider: "Evolution API",
            NumberId: "instance-evo",
            AccessToken: "token-evo",
            ApiBaseUrl: "https://evolution.example.com"));

        await service.SaveAsync(tenantId, new WhatsAppConfigRequest(
            Provider: "Evolution API",
            NumberId: "instance-evo",
            AccessToken: null,
            ApiBaseUrl: "https://evolution.example.com",
            ClearToken: true));

        var config = await service.GetAsync(tenantId);
        var testResult = await service.TestAsync(tenantId);

        Assert.NotNull(config);
        Assert.False(config!.HasAccessToken);
        Assert.False(testResult);
    }

    [Fact]
    public async Task SaveAsync_InvalidProvider_Throws()
    {
        var (service, _, tenantId) = Setup();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(tenantId, new WhatsAppConfigRequest(
                Provider: "Provider X",
                NumberId: "id-1",
                AccessToken: "token",
                ApiBaseUrl: null)));
    }
}
