namespace SmartCollect.Application.Services;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.DTOs.Sync;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class SyncService : ISyncService
{
    private readonly IAppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SyncService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDataProtector _protector;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public SyncService(
        IAppDbContext db,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<SyncService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _protector = dataProtectionProvider.CreateProtector("SmartCollect.ExternalApiCredentials.v1");
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> SyncPendingTitlesAsync(Guid tenantId)
    {
        var (http, settings) = await CreateTenantApiClientAsync(tenantId);

        using var response = await http.GetAsync(settings.PendingTitlesPath);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "SyncPendingTitles failed with status {StatusCode} for tenant {TenantId}. Response: {Body}",
                response.StatusCode,
                tenantId,
                body);

            throw new HttpRequestException(
                $"External API returned {(int)response.StatusCode} while syncing pending titles.",
                null,
                response.StatusCode);
        }

        var externalTitles = await ReadPayloadListAsync<ExternalTitleDto>(response);

        var defaultUserId = await _db.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        if (defaultUserId == Guid.Empty)
            throw new InvalidOperationException("Cannot sync titles without a user in the tenant.");

        var clientsByTaxId = await _db.Clients
            .Where(c => c.TenantId == tenantId)
            .ToDictionaryAsync(c => c.TaxId, StringComparer.OrdinalIgnoreCase);

        var titlesByUniqueCode = await _db.Titles
            .Where(t => t.TenantId == tenantId)
            .ToDictionaryAsync(t => t.UniqueCode, StringComparer.OrdinalIgnoreCase);

        var processed = 0;
        foreach (var item in externalTitles)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.UniqueCode) || string.IsNullOrWhiteSpace(item.TaxId))
                    throw new InvalidOperationException("External title is missing required fields.");

                if (!clientsByTaxId.TryGetValue(item.TaxId, out var client))
                {
                    client = new Domain.Entities.Client
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = defaultUserId,
                        LegalName = item.ClientName,
                        TaxId = item.TaxId
                    };

                    await _db.Clients.AddAsync(client);
                    clientsByTaxId[item.TaxId] = client;
                }
                else if (!string.IsNullOrWhiteSpace(item.ClientName))
                {
                    client.LegalName = item.ClientName;
                }

                if (!string.IsNullOrWhiteSpace(item.Email) || !string.IsNullOrWhiteSpace(item.Phone))
                {
                    var primaryContact = await _db.Contacts
                        .FirstOrDefaultAsync(c => c.ClientId == client.Id && c.IsPrimary);

                    if (primaryContact is null)
                    {
                        primaryContact = new Domain.Entities.Contact
                        {
                            Id = Guid.NewGuid(),
                            ClientId = client.Id,
                            Name = string.IsNullOrWhiteSpace(item.ClientName) ? item.TaxId : item.ClientName,
                            Email = string.IsNullOrWhiteSpace(item.Email) ? null : item.Email,
                            WhatsAppPhone = string.IsNullOrWhiteSpace(item.Phone) ? null : item.Phone,
                            IsPrimary = true
                        };

                        await _db.Contacts.AddAsync(primaryContact);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(item.Email)) primaryContact.Email = item.Email;
                        if (!string.IsNullOrWhiteSpace(item.Phone)) primaryContact.WhatsAppPhone = item.Phone;
                    }
                }

                var hasContactInfo = !string.IsNullOrWhiteSpace(item.Email) || !string.IsNullOrWhiteSpace(item.Phone);
                var mappedStatus = TryMapTitleStatus(item.Status, out var parsedStatus)
                    ? parsedStatus
                    : hasContactInfo ? TitleStatus.Open : TitleStatus.PendingData;

                if (titlesByUniqueCode.TryGetValue(item.UniqueCode, out var existing))
                {
                    existing.ClientId = client.Id;
                    existing.Amount = item.Amount;
                    existing.DueDate = item.DueDate;
                    existing.IssueDate = item.IssueDate;
                    existing.BoletoUrl = item.BoletoUrl;
                    existing.Status = mappedStatus;
                }
                else
                {
                    var title = new Domain.Entities.Title
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        ClientId = client.Id,
                        UniqueCode = item.UniqueCode,
                        Amount = item.Amount,
                        DueDate = item.DueDate,
                        IssueDate = item.IssueDate,
                        BoletoUrl = item.BoletoUrl,
                        Status = mappedStatus
                    };

                    await _db.Titles.AddAsync(title);
                    titlesByUniqueCode[item.UniqueCode] = title;
                }

                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed processing external title {UniqueCode} for tenant {TenantId}",
                    item.UniqueCode,
                    tenantId);
            }
        }

        await _db.SaveChangesAsync();
        return processed;
    }

    public async Task<int> SyncOccurrencesAsync(Guid tenantId)
    {
        var (http, settings) = await CreateTenantApiClientAsync(tenantId);
        var referenceDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var occurrencesPath = BuildOccurrencesPath(settings.OccurrencesPath, referenceDate);

        using var response = await http.GetAsync(occurrencesPath);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "SyncOccurrences failed with status {StatusCode} for tenant {TenantId}. Response: {Body}",
                response.StatusCode,
                tenantId,
                body);

            throw new HttpRequestException(
                $"External API returned {(int)response.StatusCode} while syncing occurrences.",
                null,
                response.StatusCode);
        }

        var occurrences = await ReadPayloadListAsync<ExternalOccurrenceDto>(response);
        var processed = 0;

        foreach (var occurrence in occurrences)
        {
            if (string.IsNullOrWhiteSpace(occurrence.UniqueCode))
            {
                _logger.LogWarning("Occurrence ignored due to missing unique code for tenant {TenantId}", tenantId);
                continue;
            }

            if (!TryMapTitleStatus(occurrence.UpdatedStatus, out var newStatus))
            {
                _logger.LogWarning(
                    "Occurrence ignored due to unknown status '{Status}' for unique code {UniqueCode} in tenant {TenantId}",
                    occurrence.UpdatedStatus,
                    occurrence.UniqueCode,
                    tenantId);
                continue;
            }

            await ProcessOccurrenceAsync(tenantId, occurrence.UniqueCode, newStatus);
            processed++;
        }

        return processed;
    }

    public async Task<bool> CheckExternalApiConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var (http, settings) = await CreateTenantApiClientAsync(tenantId, cancellationToken);

        try
        {
            // We probe a real external route and treat auth errors as "reachable".
            using var response = await http.GetAsync(settings.PendingTitlesPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode)
                return true;

            return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "External API connection probe failed.");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "External API connection probe timed out.");
            return false;
        }
    }

    public async Task<ExternalApiConfigResponse> GetExternalApiConfigAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Tenant not found.");

        var settings = ResolveSyncSettings(tenant);
        var connected = await CheckExternalApiConnectionAsync(tenantId, cancellationToken);

        return new ExternalApiConfigResponse(
            settings.BaseUrl,
            settings.DocsUrl,
            settings.PendingTitlesPath,
            settings.OccurrencesPath,
            settings.AuthenticationScheme,
            settings.HasToken,
            DateTime.UtcNow.ToString("O"),
            connected);
    }

    public async Task SaveExternalApiConfigAsync(Guid tenantId, ExternalApiConfigRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Tenant not found.");

        var baseUrl = request.BaseUrl?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Base URL inválida para a API externa.");

        tenant.ExternalApiBaseUrl = baseUrl;
        tenant.ExternalApiDocsUrl = string.IsNullOrWhiteSpace(request.DocsUrl) ? null : request.DocsUrl.Trim();
        tenant.ExternalApiPendingTitlesPath = NormalizePath(request.PendingTitlesPath, "titulos-pendentes");
        tenant.ExternalApiOccurrencesPath = NormalizePath(request.OccurrencesPath, "ocorrencias?data={date}");
        tenant.ExternalApiAuthScheme = string.IsNullOrWhiteSpace(request.AuthenticationScheme)
            ? "Bearer"
            : request.AuthenticationScheme.Trim();

        if (request.ClearToken)
        {
            tenant.ExternalApiTokenEncrypted = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.Token))
        {
            tenant.ExternalApiTokenEncrypted = _protector.Protect(request.Token.Trim());
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Process a single occurrence. Called by the sync pipeline.
    /// Implements RN06, RN07, RN08.
    /// </summary>
    public async Task ProcessOccurrenceAsync(Guid tenantId, string uniqueCode, TitleStatus newStatus)
    {
        var title = await _db.Titles
            .Include(t => t.Dispatches)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.UniqueCode == uniqueCode);

        if (title is null) return;

        // RN06: idempotent — if already in terminal state, just record occurrence
        var occurrence = new Domain.Entities.Occurrence
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            UpdatedStatus = newStatus,
            OccurrenceDate = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };

        title.Status = newStatus;

        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            TenantId = tenantId,
            Action = "Atualizacao por ocorrencia",
            Description = $"Status atualizado para {newStatus}"
        });

        // RN07: Cancel all pending dispatches (use Cancelled, not Error — semantically correct)
        foreach (var dispatch in title.Dispatches.Where(d => d.Status == DispatchStatus.Pending))
            dispatch.Status = DispatchStatus.Cancelled;

        // RN08: Send thank-you if Paid and active ThankYou template exists
        if (newStatus == TitleStatus.Paid)
        {
            var thankYouTemplate = await _db.MessageTemplates
                .FirstOrDefaultAsync(t => t.TenantId == tenantId
                    && t.Type == TemplateType.ThankYou
                    && t.Active);

            if (thankYouTemplate != null)
            {
                occurrence.ThankYouSent = true;
                // In production, this would queue the actual send
            }
        }

        await _db.Occurrences.AddAsync(occurrence);
        await _db.SaveChangesAsync();
    }

    private static bool TryMapTitleStatus(string? rawStatus, out TitleStatus status)
    {
        status = TitleStatus.Open;

        if (string.IsNullOrWhiteSpace(rawStatus))
            return false;

        var normalized = rawStatus
            .Trim()
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        status = normalized switch
        {
            "open" or "aberto" => TitleStatus.Open,
            "paid" or "pago" => TitleStatus.Paid,
            "overdue" or "vencido" => TitleStatus.Overdue,
            "cancelled" or "cancelado" => TitleStatus.Cancelled,
            "pendingdata" or "dadospendentes" => TitleStatus.PendingData,
            _ => status
        };

        return normalized is "open" or "aberto"
            or "paid" or "pago"
            or "overdue" or "vencido"
            or "cancelled" or "cancelado"
            or "pendingdata" or "dadospendentes";
    }

    private async Task<List<T>> ReadPayloadListAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var direct = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
            if (direct is not null) return direct;
        }
        catch (JsonException)
        {
            // Try wrapped payload formats below.
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "items", "data", "results" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var nested)
                    && nested.ValueKind == JsonValueKind.Array)
                {
                    var nestedList = JsonSerializer.Deserialize<List<T>>(nested.GetRawText(), JsonOptions);
                    if (nestedList is not null) return nestedList;
                }
            }
        }

        _logger.LogWarning(
            "External API payload could not be parsed into {TypeName}. Returning empty list.",
            typeof(T).Name);

        return [];
    }

    private async Task<(HttpClient HttpClient, ResolvedSyncSettings Settings)> CreateTenantApiClientAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Tenant not found.");

        var settings = ResolveSyncSettings(tenant);
        var client = _httpClientFactory.CreateClient("ExternalSyncApi");

        client.BaseAddress = new Uri(settings.BaseUrl);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = null;

        if (settings.HasToken && !string.Equals(settings.AuthenticationScheme, "None", StringComparison.OrdinalIgnoreCase))
        {
            var scheme = string.Equals(settings.AuthenticationScheme, "Bearer Token", StringComparison.OrdinalIgnoreCase)
                ? "Bearer"
                : settings.AuthenticationScheme;

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, settings.Token);
        }

        return (client, settings);
    }

    private ResolvedSyncSettings ResolveSyncSettings(Domain.Entities.Tenant tenant)
    {
        var fallbackBaseUrl = Environment.GetEnvironmentVariable("EXTERNAL_API_BASE_URL")
            ?? _configuration["ExternalApi:BaseUrl"]
            ?? "https://api-mock.atoscapital.com.br/v1/";

        var fallbackDocsUrl = Environment.GetEnvironmentVariable("EXTERNAL_API_DOCS_URL")
            ?? _configuration["ExternalApi:DocsUrl"]
            ?? "https://api-mock.atoscapital.com.br/swagger";

        var fallbackToken = Environment.GetEnvironmentVariable("EXTERNAL_API_TOKEN")
            ?? _configuration["ExternalApi:Token"];

        var baseUrl = string.IsNullOrWhiteSpace(tenant.ExternalApiBaseUrl)
            ? fallbackBaseUrl
            : tenant.ExternalApiBaseUrl.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            baseUri = new Uri(fallbackBaseUrl);

        var normalizedBaseUrl = baseUri.AbsoluteUri.EndsWith('/')
            ? baseUri.AbsoluteUri
            : $"{baseUri.AbsoluteUri}/";

        var docsUrl = string.IsNullOrWhiteSpace(tenant.ExternalApiDocsUrl)
            ? fallbackDocsUrl
            : tenant.ExternalApiDocsUrl.Trim();

        var authScheme = string.IsNullOrWhiteSpace(tenant.ExternalApiAuthScheme)
            ? "Bearer"
            : tenant.ExternalApiAuthScheme.Trim();

        var token = ResolveToken(tenant, fallbackToken);

        return new ResolvedSyncSettings(
            normalizedBaseUrl,
            docsUrl,
            NormalizePath(tenant.ExternalApiPendingTitlesPath, "titulos-pendentes"),
            NormalizePath(tenant.ExternalApiOccurrencesPath, "ocorrencias?data={date}"),
            authScheme,
            token,
            !string.IsNullOrWhiteSpace(token));
    }

    private string? ResolveToken(Domain.Entities.Tenant tenant, string? fallbackToken)
    {
        if (string.IsNullOrWhiteSpace(tenant.ExternalApiTokenEncrypted))
            return string.IsNullOrWhiteSpace(fallbackToken) ? null : fallbackToken.Trim();

        try
        {
            return _protector.Unprotect(tenant.ExternalApiTokenEncrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt external API token for tenant {TenantId}.", tenant.Id);
            return string.IsNullOrWhiteSpace(fallbackToken) ? null : fallbackToken.Trim();
        }
    }

    private static string NormalizePath(string? value, string fallback)
    {
        var path = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return path.StartsWith('/') ? path[1..] : path;
    }

    private static string BuildOccurrencesPath(string templatePath, string referenceDate)
    {
        if (templatePath.Contains("{date}", StringComparison.OrdinalIgnoreCase))
            return templatePath.Replace("{date}", Uri.EscapeDataString(referenceDate), StringComparison.OrdinalIgnoreCase);

        var separator = templatePath.Contains('?') ? "&" : "?";
        return $"{templatePath}{separator}data={Uri.EscapeDataString(referenceDate)}";
    }

    private sealed record ResolvedSyncSettings(
        string BaseUrl,
        string DocsUrl,
        string PendingTitlesPath,
        string OccurrencesPath,
        string AuthenticationScheme,
        string? Token,
        bool HasToken);
}
