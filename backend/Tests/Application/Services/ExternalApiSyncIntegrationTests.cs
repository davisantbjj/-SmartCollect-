namespace SmartCollect.Tests.Application.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;

public class ExternalApiSyncIntegrationTests
{
    private const string EnableLiveSyncTests = "SMARTCOLLECT_RUN_LIVE_EXTERNAL_API_SYNC_TESTS";

    [Fact]
    public async Task SyncPendingTitles_LiveExternalApi_ImportsEveryReturnedTitle()
    {
        if (!IsLiveSyncTestEnabled())
            return;

        var baseUrl = ReadRequiredSetting("EXTERNAL_API_BASE_URL");
        var pendingTitlesPath = ReadSetting("EXTERNAL_API_PENDING_TITLES_PATH")
            ?? "reguacobranca?colecao=1&pageSize=0&pageNumber=0";
        var token = ReadSetting("EXTERNAL_API_TOKEN");
        var authScheme = ReadSetting("EXTERNAL_API_AUTH_SCHEME") ?? (string.IsNullOrWhiteSpace(token) ? "None" : "Bearer");

        var db = TestDbContextFactory.Create();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            CompanyName = "Tenant Live Sync",
            TaxId = "123",
            ExternalApiBaseUrl = baseUrl,
            ExternalApiPendingTitlesPath = pendingTitlesPath,
            ExternalApiAuthScheme = authScheme,
            Active = true
        });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Name = "Op", Email = "op@test.com", PasswordHash = "x" });
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApi:BaseUrl"] = baseUrl,
                ["ExternalApi:Token"] = token,
                ["ExternalApi:DocsUrl"] = ReadSetting("EXTERNAL_API_DOCS_URL") ?? $"{baseUrl.TrimEnd('/')}/swagger"
            })
            .Build();

        var sync = new SyncService(
            db,
            new RealHttpClientFactory(),
            DataProtectionProvider.Create("SmartCollect.Tests.LiveExternalApi"),
            configuration,
            NullLogger<SyncService>.Instance);

        var processed = await sync.SyncPendingTitlesAsync(tenantId);
        var importedTitles = await db.Titles.CountAsync(t => t.TenantId == tenantId);

        Assert.True(processed > 0, "A API externa real nao retornou titulos para importar.");
        Assert.Equal(processed, importedTitles);
    }

    private static bool IsLiveSyncTestEnabled()
        => string.Equals(ReadSetting(EnableLiveSyncTests), "true", StringComparison.OrdinalIgnoreCase);

    private static string ReadRequiredSetting(string key)
        => ReadSetting(key)
            ?? throw new InvalidOperationException(
                $"{key} precisa estar configurado para rodar o teste de integracao real. " +
                $"Defina {EnableLiveSyncTests}=true apenas quando a API externa estiver acessivel.");

    private static string? ReadSetting(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class RealHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new();
    }
}
