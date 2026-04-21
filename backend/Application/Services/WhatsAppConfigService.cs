namespace SmartCollect.Application.Services;

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.Interfaces;

public class WhatsAppConfigService : IWhatsAppConfigService
{
    private static readonly string[] SupportedProviders = ["Twilio", "Z-API", "Evolution API", "360dialog"];

    private readonly IAppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;

    public WhatsAppConfigService(
        IAppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("SmartCollect.WhatsAppCredentials.v1");
        _configuration = configuration;
    }

    public async Task<WhatsAppConfigResponse?> GetAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null)
            throw new InvalidOperationException("Tenant not found");

        var config = ReadStoredConfig(tenant.WhatsAppApiToken);
        if (config is null)
            return null;

        return new WhatsAppConfigResponse(
            config.Provider,
            config.NumberId,
            config.ApiBaseUrl,
            !string.IsNullOrWhiteSpace(config.AccessToken),
            BuildWebhookUrl());
    }

    public async Task SaveAsync(Guid tenantId, WhatsAppConfigRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found");

        var provider = NormalizeProvider(request.Provider);
        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidOperationException("Selecione um provedor WhatsApp válido.");

        if (!SupportedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Provedor WhatsApp não suportado.");

        var numberId = request.NumberId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(numberId))
            throw new InvalidOperationException("ID do número é obrigatório.");

        if (string.Equals(provider, "Twilio", StringComparison.OrdinalIgnoreCase))
        {
            numberId = NormalizeTwilioSenderNumber(numberId)
                ?? throw new InvalidOperationException(
                    "Número remetente (Twilio) inválido. Use +14155238886 (sandbox) ou +55DDDNÚMERO.");
        }

        var existing = ReadStoredConfig(tenant.WhatsAppApiToken);
        var accessToken = ResolveToken(request, existing?.AccessToken);

        var config = new StoredWhatsAppConfig(
            provider,
            numberId,
            string.IsNullOrWhiteSpace(request.ApiBaseUrl) ? null : request.ApiBaseUrl.Trim(),
            accessToken);

        ValidateConfiguration(config, requireToken: false);

        var serialized = JsonSerializer.Serialize(config);
        tenant.WhatsAppApiToken = _protector.Protect(serialized);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> TestAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant is null)
            return false;

        var config = ReadStoredConfig(tenant.WhatsAppApiToken);
        if (config is null)
            return false;

        try
        {
            ValidateConfiguration(config, requireToken: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return string.Empty;

        var normalized = provider.Trim();

        return normalized.ToLowerInvariant() switch
        {
            "twilio" => "Twilio",
            "z-api" or "zapi" => "Z-API",
            "evolution api" or "evolution" => "Evolution API",
            "360dialog" => "360dialog",
            _ => normalized,
        };
    }

    private static string? ResolveToken(WhatsAppConfigRequest request, string? existingToken)
    {
        if (request.ClearToken)
            return null;

        if (!string.IsNullOrWhiteSpace(request.AccessToken))
            return request.AccessToken.Trim();

        return existingToken;
    }

    private static string? NormalizeTwilioSenderNumber(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return null;

        var raw = rawPhone.Trim();
        var hasExplicitCountryCode = raw.StartsWith('+');

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsDigit(ch))
                sb.Append(ch);
        }

        var digits = sb.ToString();
        if (string.IsNullOrWhiteSpace(digits))
            return null;

        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        if (hasExplicitCountryCode)
            return $"+{digits}";

        // Twilio sandbox sender is +14155238886.
        if (digits.StartsWith("1", StringComparison.Ordinal) && digits.Length == 11)
            return $"+{digits}";

        // Common BR numbers without country code.
        if (digits.Length == 10 || digits.Length == 11)
            return $"+55{digits}";

        // Fallback for already country-coded values without plus.
        return digits.Length is >= 11 and <= 15
            ? $"+{digits}"
            : null;
    }

    private static void ValidateConfiguration(StoredWhatsAppConfig config, bool requireToken)
    {
        if (!SupportedProviders.Contains(config.Provider, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Provedor WhatsApp não suportado.");

        if (string.IsNullOrWhiteSpace(config.NumberId))
            throw new InvalidOperationException("ID do número é obrigatório.");

        if (requireToken && string.IsNullOrWhiteSpace(config.AccessToken))
            throw new InvalidOperationException("Token de acesso é obrigatório.");

        if (string.Equals(config.Provider, "Twilio", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
                throw new InvalidOperationException("Account SID (Twilio) é obrigatório.");

            if (NormalizeTwilioSenderNumber(config.NumberId) is null)
                throw new InvalidOperationException("Número remetente (Twilio) inválido. Use +14155238886 (sandbox) ou +55DDDNÚMERO.");

            var accountSid = config.ApiBaseUrl.Trim();
            if (!accountSid.StartsWith("AC", StringComparison.OrdinalIgnoreCase)
                && !accountSid.Contains("/Accounts/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Account SID (Twilio) inválido.");
            }

            return;
        }

        if (string.Equals(config.Provider, "Evolution API", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(config.ApiBaseUrl))
        {
            throw new InvalidOperationException("Base URL da Evolution API é obrigatória.");
        }

        if (!string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            && !Uri.TryCreate(config.ApiBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("URL base da API WhatsApp inválida.");
        }
    }

    private StoredWhatsAppConfig? ReadStoredConfig(string? encryptedPayload)
    {
        if (string.IsNullOrWhiteSpace(encryptedPayload))
            return null;

        try
        {
            var json = _protector.Unprotect(encryptedPayload);
            var config = JsonSerializer.Deserialize<StoredWhatsAppConfig>(json);
            return config;
        }
        catch
        {
            return null;
        }
    }

    private string BuildWebhookUrl()
    {
        var baseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL")
            ?? _configuration["PublicApp:BaseUrl"]
            ?? "https://smartcollect.app";

        return $"{baseUrl.TrimEnd('/')}/webhook/whatsapp";
    }

    private sealed record StoredWhatsAppConfig(
        string Provider,
        string NumberId,
        string? ApiBaseUrl,
        string? AccessToken
    );
}
