namespace SmartCollect.Application.Services;

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartCollect.Application.DTOs.Common;
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
    private readonly IDispatchDeliveryService? _dispatchDeliveryService;

    private static readonly Regex TemplateRegex = new("\\{\\{\\s*([a-zA-Z0-9_]+)\\s*\\}\\}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public SyncService(
        IAppDbContext db,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<SyncService> logger,
        IDispatchDeliveryService? dispatchDeliveryService = null)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _protector = dataProtectionProvider.CreateProtector("SmartCollect.ExternalApiCredentials.v1");
        _configuration = configuration;
        _logger = logger;
        _dispatchDeliveryService = dispatchDeliveryService;
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
        var processed = 0;
        var referenceDates = new[] { DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date };

        foreach (var referenceDate in referenceDates)
        {
            processed += await SyncOccurrencesByDateAsync(tenantId, http, settings.OccurrencesPath, referenceDate);
        }

        return processed;
    }

    private async Task<int> SyncOccurrencesByDateAsync(
        Guid tenantId,
        HttpClient http,
        string occurrencesPathTemplate,
        DateTime referenceDate)
    {
        var formattedDate = referenceDate.ToString("yyyy-MM-dd");
        var occurrencesPath = BuildOccurrencesPath(occurrencesPathTemplate, formattedDate);

        using var response = await http.GetAsync(occurrencesPath);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "SyncOccurrences failed with status {StatusCode} for tenant {TenantId} and date {ReferenceDate}. Response: {Body}",
                response.StatusCode,
                tenantId,
                formattedDate,
                body);

            throw new HttpRequestException(
                $"External API returned {(int)response.StatusCode} while syncing occurrences for {formattedDate}.",
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

            var occurrenceDate = ResolveOccurrenceDate(occurrence, referenceDate);
            await ProcessOccurrenceAsync(tenantId, occurrence.UniqueCode, newStatus, occurrenceDate);
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
    public async Task ProcessOccurrenceAsync(Guid tenantId, string uniqueCode, TitleStatus newStatus, DateTime? occurrenceDate = null)
    {
        var effectiveOccurrenceDate = (occurrenceDate ?? DateTime.UtcNow).Date;
        var startDate = effectiveOccurrenceDate;
        var endDate = startDate.AddDays(1);

        var title = await _db.Titles
            .Include(t => t.Dispatches)
            .Include(t => t.Client)
                .ThenInclude(c => c.Contacts)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.UniqueCode == uniqueCode);

        if (title is null) return;

        var existingOccurrence = await _db.Occurrences.FirstOrDefaultAsync(o =>
            o.TitleId == title.Id
            && o.UpdatedStatus == newStatus
            && o.OccurrenceDate >= startDate
            && o.OccurrenceDate < endDate);

        if (existingOccurrence is not null)
        {
            if (newStatus == TitleStatus.Paid && !existingOccurrence.ThankYouSent)
            {
                var retryResult = await TrySendThankYouAsync(tenantId, title);
                if (retryResult.Sent)
                    existingOccurrence.ThankYouSent = true;

                await RegisterThankYouHistoryAsync(tenantId, title, retryResult, isRetry: true);
                await _db.SaveChangesAsync();
            }

            return;
        }

        // RN06: idempotent — if already in terminal state, just record occurrence
        var occurrence = new Domain.Entities.Occurrence
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            UpdatedStatus = newStatus,
            OccurrenceDate = effectiveOccurrenceDate,
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
            var thankYouResult = await TrySendThankYouAsync(tenantId, title);
            occurrence.ThankYouSent = thankYouResult.Sent;
            await RegisterThankYouHistoryAsync(tenantId, title, thankYouResult);
        }

        await _db.Occurrences.AddAsync(occurrence);
        await _db.SaveChangesAsync();
    }

    private async Task<ThankYouDispatchResult> TrySendThankYouAsync(Guid tenantId, Domain.Entities.Title title)
    {
        if (_dispatchDeliveryService is null)
            return ThankYouDispatchResult.Fail("Serviço de envio não disponível.");

        if (await _db.Occurrences.AnyAsync(o => o.TitleId == title.Id && o.ThankYouSent))
            return ThankYouDispatchResult.Success(null);

        var template = await _db.MessageTemplates
            .FirstOrDefaultAsync(t => t.TenantId == tenantId
                && t.Type == TemplateType.ThankYou
                && t.Active);

        if (template is null)
            return ThankYouDispatchResult.Fail("Template de agradecimento inexistente ou inativo.");

        var recipients = title.Client.Contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.CreatedAt)
            .ToList();

        if (recipients.Count == 0)
            return ThankYouDispatchResult.Fail("Sem contato com e-mail para envio do agradecimento.");

        var companyName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.CompanyName)
            .FirstOrDefaultAsync() ?? "SmartCollect";

        var subjectTemplate = string.IsNullOrWhiteSpace(template.Subject)
            ? "Pagamento confirmado"
            : template.Subject;

        var subject = RenderTemplate(subjectTemplate!, title, companyName);
        var body = RenderTemplate(template.Body, title, companyName);

        string? lastFailureReason = null;

        foreach (var recipient in recipients)
        {
            if (string.IsNullOrWhiteSpace(recipient.Email))
                continue;

            var sent = await _dispatchDeliveryService.SendQuickEmailAsync(
                tenantId,
                recipient.Name,
                recipient.Email,
                subject,
                body);

            if (sent.Sent)
                return ThankYouDispatchResult.Success(recipient.Email);

            lastFailureReason = sent.Detail;
        }

        return ThankYouDispatchResult.Fail(
            string.IsNullOrWhiteSpace(lastFailureReason)
                ? "Falha no envio SMTP para todos os contatos com e-mail."
                : lastFailureReason);
    }

    private async Task RegisterThankYouHistoryAsync(
        Guid tenantId,
        Domain.Entities.Title title,
        ThankYouDispatchResult result,
        bool isRetry = false)
    {
        if (result.Sent)
        {
            var description = string.IsNullOrWhiteSpace(result.RecipientEmail)
                ? "Template de agradecimento já havia sido confirmado anteriormente."
                : $"Template de agradecimento enviado para {result.RecipientEmail}.";

            if (isRetry)
                description = $"{description} (retry automático)";

            await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
            {
                Id = Guid.NewGuid(),
                TitleId = title.Id,
                TenantId = tenantId,
                Action = "Agradecimento enviado",
                Description = description
            });

            return;
        }

        var failureDescription = string.IsNullOrWhiteSpace(result.Reason)
            ? "Agradecimento não enviado."
            : $"Agradecimento não enviado: {result.Reason}";

        if (isRetry)
            failureDescription = $"{failureDescription} (retry automático)";

        await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
        {
            Id = Guid.NewGuid(),
            TitleId = title.Id,
            TenantId = tenantId,
            Action = "Agradecimento pendente",
            Description = failureDescription
        });
    }

    private sealed record ThankYouDispatchResult(bool Sent, string? Reason, string? RecipientEmail)
    {
        public static ThankYouDispatchResult Success(string? recipientEmail)
            => new(true, null, recipientEmail);

        public static ThankYouDispatchResult Fail(string reason)
            => new(false, reason, null);
    }

    private static string RenderTemplate(string template, Domain.Entities.Title title, string companyName)
    {
        var diasAtraso = Math.Max(0, (DateTime.UtcNow.Date - title.DueDate.Date).Days);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClienteNome"] = title.Client.LegalName,
            ["NomeCliente"] = title.Client.LegalName,
            ["RazaoSocial"] = title.Client.LegalName,
            ["Cnpj"] = title.Client.TaxId,
            ["TituloCodigo"] = title.UniqueCode,
            ["CodigoTitulo"] = title.UniqueCode,
            ["DiasAtraso"] = diasAtraso.ToString(),
            ["Valor"] = title.Amount.ToString("C", new CultureInfo("pt-BR")),
            ["DataVencimento"] = title.DueDate.ToString("dd/MM/yyyy"),
            ["DataEmissao"] = title.IssueDate.ToString("dd/MM/yyyy"),
            ["LinkBoleto"] = title.BoletoUrl ?? string.Empty,
            ["Empresa"] = companyName,
        };

        return TemplateRegex.Replace(template ?? string.Empty, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private static DateTime ResolveOccurrenceDate(ExternalOccurrenceDto occurrence, DateTime fallbackDate)
    {
        if (TryParseOccurrenceDate(occurrence.OccurrenceDate, out var parsedDate))
            return parsedDate;

        if (TryParseOccurrenceDate(occurrence.ReferenceDate, out parsedDate))
            return parsedDate;

        return DateTime.SpecifyKind(fallbackDate.Date, DateTimeKind.Utc);
    }

    private static bool TryParseOccurrenceDate(string? raw, out DateTime occurrenceDate)
    {
        occurrenceDate = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            || DateTime.TryParse(raw, new CultureInfo("pt-BR"), DateTimeStyles.AssumeLocal, out parsed))
        {
            occurrenceDate = parsed.Date;
            return true;
        }

        return false;
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
