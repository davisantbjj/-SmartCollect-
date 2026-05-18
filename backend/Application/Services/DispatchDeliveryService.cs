namespace SmartCollect.Application.Services;

using System.Text.RegularExpressions;
using System.Globalization;
using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using SmartCollect.Application.DTOs.Common;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class DispatchDeliveryService : IDispatchDeliveryService
{
    private static readonly Regex TemplateRegex = new("\\{\\{\\s*([a-zA-Z0-9_]+)\\s*\\}\\}", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<\\s*([a-z][a-z0-9]*|!doctype)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StripHtmlRegex = new("<[^>]+>", RegexOptions.Compiled);

    private readonly IAppDbContext _db;
    private readonly IDataProtector _smtpProtector;
    private readonly IDataProtector _whatsAppProtector;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<DispatchDeliveryService> _logger;
    private readonly IDispatchExecutionGuard _dispatchExecutionGuard;

    public DispatchDeliveryService(
        IAppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<DispatchDeliveryService> logger,
        IHttpClientFactory? httpClientFactory = null,
        IDispatchExecutionGuard? dispatchExecutionGuard = null)
    {
        _db = db;
        _smtpProtector = dataProtectionProvider.CreateProtector("SmartCollect.SmtpCredentials.v1");
        _whatsAppProtector = dataProtectionProvider.CreateProtector("SmartCollect.WhatsAppCredentials.v1");
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _dispatchExecutionGuard = dispatchExecutionGuard ?? new InMemoryDispatchExecutionGuard();
    }

    public async Task<QuickSendResult> SendQuickEmailAsync(
        Guid tenantId,
        string recipientName,
        string recipientEmail,
        string subject,
        string body,
        string? boletoUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
            return QuickSendResult.Fail("Contato sem e-mail para envio.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null || !tenant.Active)
            return QuickSendResult.Fail("Tenant inativo ou não encontrado.");

        if (string.IsNullOrWhiteSpace(tenant.SmtpHost)
            || !tenant.SmtpPort.HasValue
            || string.IsNullOrWhiteSpace(tenant.SmtpUser)
            || string.IsNullOrWhiteSpace(tenant.SmtpPasswordEncrypted))
            return QuickSendResult.Fail("SMTP não configurado para o tenant.");

        var smtpPassword = TryUnprotectSmtpPassword(tenant.SmtpPasswordEncrypted);
        if (smtpPassword is null)
            return QuickSendResult.Fail("Falha ao descriptografar senha SMTP.");

        var senderFrom = tenant.SmtpUser.Contains('@')
            ? tenant.SmtpUser
            : $"financeiro@{tenant.EmailDomain}";

        var senderName = tenant.CompanyName;

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderFrom));
            message.To.Add(new MailboxAddress(recipientName ?? string.Empty, recipientEmail));
            message.Subject = string.IsNullOrWhiteSpace(subject)
                ? "Cobranca"
                : subject;
            message.Body = BuildEmailBody(body, message.Subject, senderName, boletoUrl);

            using var smtp = new SmtpClient();
            var security = ResolveSmtpSecurity(tenant.SmtpPort.Value);
            await smtp.ConnectAsync(tenant.SmtpHost, tenant.SmtpPort.Value, security, cancellationToken);
            await smtp.AuthenticateAsync(tenant.SmtpUser, smtpPassword, cancellationToken);
            await smtp.SendAsync(message, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);

            return QuickSendResult.Success("Enviado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quick SMTP send failed for tenant {TenantId}", tenantId);
            return QuickSendResult.Fail($"Falha no envio SMTP: {ex.Message}");
        }
    }

    public async Task<QuickSendResult> SendQuickWhatsAppAsync(
        Guid tenantId,
        string recipientName,
        string recipientPhone,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientPhone) || string.IsNullOrWhiteSpace(body))
            return QuickSendResult.Fail("Contato sem WhatsApp ou mensagem vazia.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null || !tenant.Active)
            return QuickSendResult.Fail("Tenant inativo ou não encontrado.");

        var result = await SendWhatsAppAsync(tenant, recipientPhone, body, cancellationToken);
        return new QuickSendResult(result.Sent, result.Detail);
    }

    public async Task<int> ProcessPendingDispatchesAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var query = _db.Dispatches
            .Include(d => d.Contact)
            .Include(d => d.Trigger)
                .ThenInclude(tr => tr.Template)
            .Include(d => d.Trigger)
                .ThenInclude(tr => tr.CollectionRule)
            .Include(d => d.Title)
                .ThenInclude(t => t.Client)
            .Where(d => d.Status == DispatchStatus.Pending)
            .Where(d => d.ScheduledFor <= now)
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(d => d.Title.TenantId == tenantId.Value);

        var pending = await query
            .OrderBy(d => d.ScheduledFor)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0) return 0;

        var tenantIds = pending.Select(d => d.Title.TenantId).Distinct().ToList();
        var tenants = await _db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var tenantIdsWithProcessingImport = await _db.FileImports
            .Where(i => i.Status == ImportStatus.Processing)
            .Where(i => tenantIds.Contains(i.TenantId))
            .Select(i => i.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var processingImportSet = tenantIdsWithProcessingImport.ToHashSet();

        var processed = 0;

        foreach (var dispatch in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (dispatch.Title.Status is TitleStatus.Paid or TitleStatus.Cancelled)
            {
                dispatch.Status = DispatchStatus.Cancelled;
                continue;
            }

            if (!dispatch.Trigger.Active || !dispatch.Trigger.CollectionRule.Active)
            {
                dispatch.Status = DispatchStatus.Cancelled;
                continue;
            }

            if (!tenants.TryGetValue(dispatch.Title.TenantId, out var tenant) || !tenant.Active)
            {
                MarkError(dispatch, "Tenant inativo ou não encontrado.");
                continue;
            }

            if (tenant.PauseAutomaticDispatchDuringProcessing)
            {
                if (processingImportSet.Contains(dispatch.Title.TenantId))
                    continue;

                if (_dispatchExecutionGuard.IsTenantBlocked(dispatch.Title.TenantId))
                    continue;
            }

            if (tenant.DispatchWindowEnabled
                && !IsWithinDispatchWindowUtc(tenant, now, out var nextAllowedUtc))
            {
                if (dispatch.ScheduledFor < nextAllowedUtc)
                    dispatch.ScheduledFor = nextAllowedUtc;

                continue;
            }

            var subjectTemplate = string.IsNullOrWhiteSpace(dispatch.Trigger.Template.Subject)
                ? $"SmartCollect - Cobranca {dispatch.Title.UniqueCode}"
                : dispatch.Trigger.Template.Subject!;

            var subject = RenderTemplate(subjectTemplate, dispatch, tenant.CompanyName);
            var body = RenderTemplate(dispatch.Trigger.Template.Body, dispatch, tenant.CompanyName);

            var wantsEmail = dispatch.Channel is CollectionChannel.Email or CollectionChannel.Both;
            var wantsWhatsApp = dispatch.Channel is CollectionChannel.WhatsApp or CollectionChannel.Both;

            var sentChannels = new List<string>();
            var failedChannels = new List<string>();

            try
            {
                if (wantsEmail)
                {
                    var emailResult = await SendEmailAsync(
                        tenant,
                        dispatch.Contact.Name,
                        dispatch.Contact.Email,
                        subject,
                        body,
                        dispatch.Title.BoletoUrl,
                        cancellationToken);

                    if (emailResult.Sent)
                        sentChannels.Add("E-mail");
                    else
                        failedChannels.Add($"E-mail: {emailResult.Detail}");
                }

                if (wantsWhatsApp)
                {
                    var whatsAppResult = await SendWhatsAppAsync(
                        tenant,
                        dispatch.Contact.WhatsAppPhone,
                        body,
                        cancellationToken);

                    if (whatsAppResult.Sent)
                        sentChannels.Add("WhatsApp");
                    else
                        failedChannels.Add($"WhatsApp: {whatsAppResult.Detail}");
                }

                if (sentChannels.Count == 0)
                {
                    MarkError(dispatch, string.Join(" | ", failedChannels));
                    continue;
                }

                dispatch.Status = DispatchStatus.Sent;
                dispatch.SentAt = DateTime.UtcNow;

                var description = $"{dispatch.Channel}: enviado via {string.Join(" + ", sentChannels)}";
                if (failedChannels.Count > 0)
                    description = $"{description}. Falhas parciais: {string.Join(" | ", failedChannels)}";

                await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
                {
                    Id = Guid.NewGuid(),
                    TitleId = dispatch.TitleId,
                    TenantId = dispatch.Title.TenantId,
                    Action = "Disparo enviado",
                    Description = description
                }, cancellationToken);

                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dispatch send failed for dispatch {DispatchId}", dispatch.Id);
                MarkError(dispatch, $"Falha no envio de canais: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return processed;
    }

    private static string RenderTemplate(string template, Domain.Entities.Dispatch dispatch, string companyName)
    {
        var diasAtraso = Math.Max(0, (DateTime.UtcNow.Date - dispatch.Title.DueDate.Date).Days);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClienteNome"] = dispatch.Title.Client.LegalName,
            ["NomeCliente"] = dispatch.Title.Client.LegalName,
            ["RazaoSocial"] = dispatch.Title.Client.LegalName,
            ["Cnpj"] = dispatch.Title.Client.TaxId,
            ["TituloCodigo"] = dispatch.Title.UniqueCode,
            ["CodigoTitulo"] = dispatch.Title.UniqueCode,
            ["DiasAtraso"] = diasAtraso.ToString(),
            ["Valor"] = dispatch.Title.Amount.ToString("C", new CultureInfo("pt-BR")),
            ["DataVencimento"] = dispatch.Title.DueDate.ToString("dd/MM/yyyy"),
            ["DataEmissao"] = dispatch.Title.IssueDate.ToString("dd/MM/yyyy"),
            ["LinkBoleto"] = dispatch.Title.BoletoUrl ?? string.Empty,
            ["Empresa"] = companyName,
        };

        return TemplateRegex.Replace(template ?? string.Empty, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private static MimeEntity BuildEmailBody(string? body, string subject, string companyName, string? boletoUrl)
    {
        var content = body ?? string.Empty;
        var builder = new BodyBuilder();
        TryNormalizeHttpUrl(boletoUrl, out var boletoHref);

        if (HtmlTagRegex.IsMatch(content))
        {
            builder.HtmlBody = BuildStyledHtml(content, subject, companyName, boletoHref);
            builder.TextBody = BuildTextBody(ToPlainText(content), boletoHref);
        }
        else
        {
            builder.TextBody = BuildTextBody(content, boletoHref);
            builder.HtmlBody = BuildStyledHtml(ToSimpleHtml(content), subject, companyName, boletoHref);
        }

        return builder.ToMessageBody();
    }

    private static string BuildStyledHtml(string contentHtml, string subject, string companyName, string? boletoHref)
    {
        var safeSubject = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(subject) ? "Cobrança" : subject.Trim());
        var safeCompanyName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(companyName) ? "SmartCollect" : companyName.Trim());
        var boletoButton = string.IsNullOrWhiteSpace(boletoHref)
            ? string.Empty
            : $"""
              <tr>
                <td style="padding: 8px 32px 28px 32px;">
                  <a href="{WebUtility.HtmlEncode(boletoHref)}" target="_blank" rel="noopener noreferrer" style="display:inline-block;background:#dc2626;color:#ffffff;text-decoration:none;font-family:Arial,sans-serif;font-size:15px;font-weight:700;padding:13px 22px;border-radius:8px;">
                    Boleto
                  </a>
                </td>
              </tr>
              """;

        return $"""
          <!doctype html>
          <html lang="pt-BR">
          <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{safeSubject}</title>
          </head>
          <body style="margin:0;padding:0;background:#f3f4f6;">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f4f6;margin:0;padding:28px 12px;">
              <tr>
                <td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;background:#ffffff;border:1px solid #e5e7eb;border-radius:14px;overflow:hidden;">
                    <tr>
                      <td style="background:#111827;padding:24px 32px;">
                        <div style="font-family:Arial,sans-serif;color:#ffffff;font-size:20px;font-weight:800;line-height:1.25;">{safeCompanyName}</div>
                        <div style="font-family:Arial,sans-serif;color:#cbd5e1;font-size:13px;line-height:1.4;margin-top:6px;">{safeSubject}</div>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:30px 32px 18px 32px;font-family:Arial,sans-serif;color:#111827;font-size:15px;line-height:1.6;">
                        {contentHtml}
                      </td>
                    </tr>
                    {boletoButton}
                    <tr>
                      <td style="border-top:1px solid #e5e7eb;padding:18px 32px;background:#f9fafb;">
                        <div style="font-family:Arial,sans-serif;color:#6b7280;font-size:12px;line-height:1.5;">
                          Este e-mail é automático. Não responda.
                        </div>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
          </body>
          </html>
          """;
    }

    private static string BuildTextBody(string content, string? boletoHref)
    {
        var text = content ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(boletoHref))
            text = $"{text}\n\nBoleto: {boletoHref}";

        return $"{text}\n\nEste e-mail é automático. Não responda.";
    }

    private static string ToSimpleHtml(string text)
    {
        var encoded = WebUtility.HtmlEncode(text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal);

        return $"<div>{encoded}</div>";
    }

    private static string ToPlainText(string html)
    {
        var withBreaks = Regex.Replace(html ?? string.Empty, "<\\s*br\\s*/?\\s*>", "\n", RegexOptions.IgnoreCase);
        var withoutTags = StripHtmlRegex.Replace(withBreaks, " ");
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private static bool TryNormalizeHttpUrl(string? value, out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme is not ("http" or "https"))
            return false;

        url = parsed.AbsoluteUri;
        return true;
    }

    private static bool IsWithinDispatchWindowUtc(Domain.Entities.Tenant tenant, DateTime nowUtc, out DateTime nextAllowedUtc)
    {
        nextAllowedUtc = nowUtc;

        if (!DispatchWindowTimeZoneResolver.TryResolve(tenant.DispatchWindowTimeZone, out var timeZone))
            return true;

        var startMinutes = NormalizeWindowMinutes(tenant.DispatchWindowStartMinutes, 9 * 60);
        var endMinutes = NormalizeWindowMinutes(tenant.DispatchWindowEndMinutes, 18 * 60);

        if (startMinutes == endMinutes)
            return true;

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);
        var minutesNow = localNow.Hour * 60 + localNow.Minute;

        var wrapsMidnight = startMinutes > endMinutes;
        var insideWindow = wrapsMidnight
            ? minutesNow >= startMinutes || minutesNow < endMinutes
            : minutesNow >= startMinutes && minutesNow < endMinutes;

        if (insideWindow)
            return true;

        var nextStartDate = ResolveNextWindowStartDate(localNow.Date, minutesNow, startMinutes, wrapsMidnight);
        var nextStartLocal = nextStartDate.AddMinutes(startMinutes);
        nextAllowedUtc = TimeZoneInfo.ConvertTimeToUtc(nextStartLocal, timeZone);

        return false;
    }

    private static DateTime ResolveNextWindowStartDate(
        DateTime localDate,
        int minutesNow,
        int startMinutes,
        bool wrapsMidnight)
    {
        if (wrapsMidnight)
        {
            // Outside interval for overnight windows is always between end and start.
            return minutesNow < startMinutes ? localDate : localDate.AddDays(1);
        }

        return minutesNow < startMinutes ? localDate : localDate.AddDays(1);
    }

    private static int NormalizeWindowMinutes(int value, int fallback)
        => value is < 0 or >= 1440 ? fallback : value;

    private async Task<ChannelSendResult> SendEmailAsync(
        Domain.Entities.Tenant tenant,
        string? recipientName,
        string? recipientEmail,
        string subject,
        string body,
        string? boletoUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
            return ChannelSendResult.Fail("Contato sem e-mail para envio.");

        if (string.IsNullOrWhiteSpace(tenant.SmtpHost)
            || !tenant.SmtpPort.HasValue
            || string.IsNullOrWhiteSpace(tenant.SmtpUser)
            || string.IsNullOrWhiteSpace(tenant.SmtpPasswordEncrypted))
        {
            return ChannelSendResult.Fail("SMTP não configurado para o tenant.");
        }

        var smtpPassword = TryUnprotectSmtpPassword(tenant.SmtpPasswordEncrypted);
        if (smtpPassword is null)
            return ChannelSendResult.Fail("Falha ao descriptografar senha SMTP.");

        var senderFrom = tenant.SmtpUser.Contains('@')
            ? tenant.SmtpUser
            : $"financeiro@{tenant.EmailDomain}";

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(tenant.CompanyName, senderFrom));
            message.To.Add(new MailboxAddress(recipientName ?? string.Empty, recipientEmail));
            message.Subject = subject;
            message.Body = BuildEmailBody(body, message.Subject, tenant.CompanyName, boletoUrl);

            using var smtp = new SmtpClient();
            var security = ResolveSmtpSecurity(tenant.SmtpPort.Value);
            await smtp.ConnectAsync(tenant.SmtpHost, tenant.SmtpPort.Value, security, cancellationToken);
            await smtp.AuthenticateAsync(tenant.SmtpUser, smtpPassword, cancellationToken);
            await smtp.SendAsync(message, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);

            return ChannelSendResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP send failed for tenant {TenantId}", tenant.Id);
            return ChannelSendResult.Fail($"Falha no envio SMTP: {ex.Message}");
        }
    }

    private async Task<ChannelSendResult> SendWhatsAppAsync(
        Domain.Entities.Tenant tenant,
        string? recipientPhone,
        string body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipientPhone))
            return ChannelSendResult.Fail("Contato sem WhatsApp para envio.");

        var config = TryReadWhatsAppConfig(tenant.WhatsAppApiToken);
        if (config is null)
            return ChannelSendResult.Fail("WhatsApp não configurado para o tenant.");

        if (string.IsNullOrWhiteSpace(config.AccessToken))
            return ChannelSendResult.Fail("Token WhatsApp não configurado.");

        var toPhone = NormalizePhone(recipientPhone);
        if (string.IsNullOrWhiteSpace(toPhone))
            return ChannelSendResult.Fail("Número de WhatsApp inválido.");

        var message = body.Trim();
        if (string.IsNullOrWhiteSpace(message))
            return ChannelSendResult.Fail("Mensagem vazia para envio WhatsApp.");

        try
        {
            return config.Provider switch
            {
                "Twilio" => await SendViaTwilioAsync(config, toPhone, message, cancellationToken),
                "Z-API" => await SendViaZApiAsync(config, toPhone, message, cancellationToken),
                "Evolution API" => await SendViaEvolutionAsync(config, toPhone, message, cancellationToken),
                "360dialog" => await SendVia360DialogAsync(config, toPhone, message, cancellationToken),
                _ => ChannelSendResult.Fail("Provedor WhatsApp não suportado."),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp send failed for tenant {TenantId}", tenant.Id);
            return ChannelSendResult.Fail($"Falha no envio WhatsApp: {ex.Message}");
        }
    }

    private async Task<ChannelSendResult> SendViaTwilioAsync(
        StoredWhatsAppConfig config,
        string toPhone,
        string message,
        CancellationToken cancellationToken)
    {
        var accountSid = ResolveTwilioAccountSid(config.ApiBaseUrl);
        if (string.IsNullOrWhiteSpace(accountSid))
            return ChannelSendResult.Fail("Account SID (Twilio) inválido.");

        var fromPhone = NormalizePhone(config.NumberId);
        if (string.IsNullOrWhiteSpace(fromPhone))
            return ChannelSendResult.Fail("Número remetente (Twilio) inválido.");

        var client = CreateHttpClient();
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{accountSid}:{config.AccessToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = $"whatsapp:{fromPhone}",
            ["To"] = $"whatsapp:{toPhone}",
            ["Body"] = message,
        });

        using var response = await client.PostAsync(url, content, cancellationToken);
        if (response.IsSuccessStatusCode)
            return ChannelSendResult.Success();

        var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
        return ChannelSendResult.Fail(BuildTwilioFailureMessage((int)response.StatusCode, bodyText, accountSid, fromPhone, toPhone));
    }

    private static string BuildTwilioFailureMessage(
        int statusCode,
        string bodyText,
        string accountSid,
        string fromPhone,
        string toPhone)
    {
        try
        {
            using var document = JsonDocument.Parse(bodyText);
            var root = document.RootElement;

            var code = root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : (int?)null;

            var providerMessage = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;

            var sidHint = BuildSidHint(accountSid);
            var routingHint = $"From usado: whatsapp:{fromPhone} | To usado: whatsapp:{toPhone} | SID: {sidHint}.";

            if (code == 63007)
            {
                return $"Twilio retornou {statusCode} (63007): remetente WhatsApp inválido ou não habilitado para esta conta. " +
                       "No painel Twilio, configure o número remetente como sender WhatsApp ativo " +
                       "(sandbox +14155238886 ou número WhatsApp aprovado) e confirme Account SID/Auth Token da mesma conta. " +
                       routingHint;
            }

            if (code.HasValue && !string.IsNullOrWhiteSpace(providerMessage))
                return $"Twilio retornou {statusCode} ({code}): {providerMessage} {routingHint}";

            if (!string.IsNullOrWhiteSpace(providerMessage))
                return $"Twilio retornou {statusCode}: {providerMessage} {routingHint}";
        }
        catch
        {
            // Ignore parse errors and fallback to generic HTTP message.
        }

        return $"Twilio retornou {statusCode}. From usado: whatsapp:{fromPhone} | To usado: whatsapp:{toPhone} | SID: {BuildSidHint(accountSid)}.";
    }

    private static string BuildSidHint(string accountSid)
    {
        if (string.IsNullOrWhiteSpace(accountSid) || accountSid.Length < 8)
            return "(inválido)";

        var prefix = accountSid[..4];
        var suffix = accountSid[^4..];
        return $"{prefix}...{suffix}";
    }

    private async Task<ChannelSendResult> SendViaZApiAsync(
        StoredWhatsAppConfig config,
        string toPhone,
        string message,
        CancellationToken cancellationToken)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://api.z-api.io"
            : config.ApiBaseUrl.TrimEnd('/');

        var instanceId = config.NumberId.Trim();
        var token = config.AccessToken!.Trim();

        var client = CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = null;
        var url = $"{baseUrl}/instances/{instanceId}/token/{token}/send-text";
        var payload = JsonSerializer.Serialize(new { phone = toPhone.TrimStart('+'), message });

        using var response = await client.PostAsync(
            url,
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);

        if (response.IsSuccessStatusCode)
            return ChannelSendResult.Success();

        var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
        return ChannelSendResult.Fail($"Z-API retornou {(int)response.StatusCode}. {bodyText}");
    }

    private async Task<ChannelSendResult> SendViaEvolutionAsync(
        StoredWhatsAppConfig config,
        string toPhone,
        string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            return ChannelSendResult.Fail("Base URL da Evolution API não configurada.");

        var baseUrl = config.ApiBaseUrl.TrimEnd('/');
        var instance = config.NumberId.Trim();

        var client = CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Remove("apikey");
        client.DefaultRequestHeaders.Add("apikey", config.AccessToken!.Trim());

        var url = $"{baseUrl}/message/sendText/{instance}";
        var payload = JsonSerializer.Serialize(new { number = toPhone.TrimStart('+'), text = message });

        using var response = await client.PostAsync(
            url,
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);

        if (response.IsSuccessStatusCode)
            return ChannelSendResult.Success();

        var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
        return ChannelSendResult.Fail($"Evolution API retornou {(int)response.StatusCode}. {bodyText}");
    }

    private async Task<ChannelSendResult> SendVia360DialogAsync(
        StoredWhatsAppConfig config,
        string toPhone,
        string message,
        CancellationToken cancellationToken)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? "https://waba-v2.360dialog.io"
            : config.ApiBaseUrl.TrimEnd('/');

        var client = CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Remove("D360-API-KEY");
        client.DefaultRequestHeaders.Add("D360-API-KEY", config.AccessToken!.Trim());

        var url = $"{baseUrl}/messages";
        var payload = JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = toPhone.TrimStart('+'),
            type = "text",
            text = new { body = message },
        });

        using var response = await client.PostAsync(
            url,
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);

        if (response.IsSuccessStatusCode)
            return ChannelSendResult.Success();

        var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
        return ChannelSendResult.Fail($"360dialog retornou {(int)response.StatusCode}. {bodyText}");
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory?.CreateClient("ExternalSyncApi") ?? new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private StoredWhatsAppConfig? TryReadWhatsAppConfig(string? encryptedPayload)
    {
        if (string.IsNullOrWhiteSpace(encryptedPayload))
            return null;

        try
        {
            var json = _whatsAppProtector.Unprotect(encryptedPayload);
            return JsonSerializer.Deserialize<StoredWhatsAppConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveTwilioAccountSid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        if (value.StartsWith("AC", StringComparison.OrdinalIgnoreCase))
            return value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var accountIndex = Array.FindIndex(segments, s => s.Equals("Accounts", StringComparison.OrdinalIgnoreCase));
            if (accountIndex >= 0 && accountIndex + 1 < segments.Length)
                return segments[accountIndex + 1];
        }

        return null;
    }

    private static string? NormalizePhone(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return null;

        var raw = rawPhone.Trim();
        var hasExplicitCountryCode = raw.StartsWith('+');
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
            return null;

        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        if (hasExplicitCountryCode)
            return $"+{digits}";

        // Twilio sandbox sender uses country code 1 (+14155238886).
        if (digits.StartsWith("1", StringComparison.Ordinal) && digits.Length == 11)
            return $"+{digits}";

        if (digits.StartsWith("55", StringComparison.Ordinal) && digits.Length is 12 or 13)
            return $"+{digits}";

        if (digits.Length == 10 || digits.Length == 11)
            return $"+55{digits}";

        if (digits.Length is >= 11 and <= 15)
            return $"+{digits}";

        return null;
    }

    private string? TryUnprotectSmtpPassword(string encrypted)
    {
        try
        {
            return _smtpProtector.Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not unprotect SMTP password.");
            return null;
        }
    }

    private sealed record StoredWhatsAppConfig(
        string Provider,
        string NumberId,
        string? ApiBaseUrl,
        string? AccessToken);

    private sealed record ChannelSendResult(bool Sent, string Detail)
    {
        public static ChannelSendResult Success(string detail = "OK") => new(true, detail);
        public static ChannelSendResult Fail(string detail) => new(false, detail);
    }

    private static void MarkError(Domain.Entities.Dispatch dispatch, string reason)
    {
        _ = reason;
        dispatch.Status = DispatchStatus.Error;
        dispatch.SentAt = null;
    }

    private static SecureSocketOptions ResolveSmtpSecurity(int port)
        => port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
}
