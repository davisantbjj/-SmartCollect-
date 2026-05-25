namespace SmartCollect.Application.Services;

using System.Text.RegularExpressions;
using System.IO;
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
using MimeKit.Utils;
using SmartCollect.Application.DTOs.Common;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class DispatchDeliveryService : IDispatchDeliveryService
{
    private static readonly Regex TemplateRegex = new("\\{\\{\\s*([a-zA-Z0-9_]+)\\s*\\}\\}", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<\\s*([a-z][a-z0-9]*|!doctype)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StripHtmlRegex = new("<[^>]+>", RegexOptions.Compiled);
    private const string EmailSurface = "#111827";
    private const string EmailSurface2 = "#1F2937";
    private const string EmailSurface3 = "#374151";
    private const string EmailBorder = "rgba(255, 255, 255, 0.15)";
    private const string EmailAccent = "#DC2626";
    private const string EmailText = "#F9FAFB";
    private const string EmailTextSecondary = "#D1D5DB";
    private const string EmailTextMuted = "#9CA3AF";

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
            message.Body = BuildEmailBody(body, message.Subject, tenant, boletoUrl);
            LogEmailBodyDiagnostics(tenant, message.Body);

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
                        dispatch.Trigger.Template.Type == TemplateType.ThankYou ? null : dispatch.Title.BoletoUrl,
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
            ["NomeEmpresa"] = companyName,
        };

        return TemplateRegex.Replace(template ?? string.Empty, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

        private static MimeEntity BuildEmailBody(string? body, string subject, Domain.Entities.Tenant tenant, string? boletoUrl)
    {
        var content = body ?? string.Empty;
        var builder = new BodyBuilder();
        TryNormalizeHttpUrl(boletoUrl, out var boletoHref);
            var htmlContent = HtmlTagRegex.IsMatch(content)
                ? RemoveBoletoUrlFromHtml(content, boletoHref)
                : ToSimpleHtml(RemoveBoletoUrlFromText(content, boletoHref));

            var textContent = HtmlTagRegex.IsMatch(content)
                ? RemoveBoletoUrlFromText(ToPlainText(content), boletoHref)
                : RemoveBoletoUrlFromText(content, boletoHref);

        string? logoSource = null;
        string? footerImageSource = null;

        if (tenant.EmailLayoutEnabled)
        {
            logoSource = ResolveInlineImageSource(builder, tenant.EmailLayoutLogoUrl, "logo");
            footerImageSource = ResolveInlineImageSource(builder, tenant.EmailLayoutHeroUrl, "footer");
        }

                builder.TextBody = BuildTextBody(textContent, boletoHref);
                builder.HtmlBody = tenant.EmailLayoutEnabled
                ? BuildTenantLayoutHtml(builder, htmlContent, subject, tenant, boletoHref, logoSource, footerImageSource)
                    : BuildStyledHtml(htmlContent, subject, tenant.CompanyName, boletoHref);

        return builder.ToMessageBody();
    }

        private static string BuildTenantLayoutHtml(
            BodyBuilder builder,
            string contentHtml,
            string subject,
            Domain.Entities.Tenant tenant,
            string? boletoHref,
            string? logoSource = null,
            string? footerImageSource = null)
        {
                var safeSubject = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(subject) ? "Cobrança" : subject.Trim());
                var logo = BuildOptionalImageRow(logoSource ?? tenant.EmailLayoutLogoUrl, "Logo", 210, "12px 32px 6px 32px");
                var footerImage = BuildOptionalImageRow(footerImageSource ?? tenant.EmailLayoutHeroUrl, "Imagem de rodape", 560, "6px 16px 6px 16px");
                var footer = string.IsNullOrWhiteSpace(tenant.EmailLayoutFooterMessage)
                        ? string.Empty
                    : $"<div style=\"margin-top:18px;font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailTextMuted};font-size:12px;line-height:1.5;\">{WebUtility.HtmlEncode(tenant.EmailLayoutFooterMessage.Trim())}</div>";

                var socialRow = BuildSocialRow(builder, tenant);

                var boletoButton = string.IsNullOrWhiteSpace(boletoHref)
                        ? string.Empty
                        : $"""
                            <tr>
                                <td align="center" style="padding: 18px 32px 30px 32px;text-align:center;">
                                    <a href="{WebUtility.HtmlEncode(boletoHref)}" target="_blank" rel="noopener noreferrer" style="display:inline-block;background:{EmailAccent};color:#ffffff;text-decoration:none;font-family:'Plus Jakarta Sans',Arial,sans-serif;font-size:15px;font-weight:800;padding:13px 22px;border-radius:8px;margin:0 auto;">
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
                        <body style="margin:0;padding:0;background:{EmailSurface};">
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:{EmailSurface};margin:0;padding:28px 12px;">
                                <tr>
                                    <td align="center">
                                        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;background:{EmailSurface2};border:1px solid {EmailBorder};border-radius:14px;overflow:hidden;">
                                            <tr>
                                                <td align="center" style="background:{EmailSurface3};padding:18px 32px 10px 32px;border-bottom:2px solid {EmailAccent};">
                                                    <table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 auto;">
                                                        {logo}
                                                    </table>
                                                    <div style="font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailTextSecondary};font-size:13px;line-height:1.45;margin-top:6px;">{safeSubject}</div>
                                                </td>
                                            </tr>
                                            <tr>
                                                <td align="center" style="padding:18px 32px 12px 32px;">
                                                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:{EmailSurface3};border:1px solid {EmailBorder};border-radius:10px;margin:0 auto;">
                                                        <tr>
                                                            <td style="padding:26px 28px 22px 28px;font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailTextSecondary};font-size:15px;line-height:1.6;text-align:left;">
                                                                {contentHtml}
                                                                {footer}
                                                            </td>
                                                        </tr>
                                                    </table>
                                                </td>
                                            </tr>
                                            {boletoButton}
                                            {footerImage}
                                            {socialRow}
                                            <tr>
                                                <td align="center" style="padding:18px 32px 24px 32px;background:{EmailSurface3};">
                                                    <div style="font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailTextMuted};font-size:12px;line-height:1.5;">
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

        private static string BuildOptionalImageRow(string? imageUrl, string alt, int maxWidth, string padding)
        {
            if (!TryNormalizeImageSource(imageUrl, out var url))
                        return string.Empty;

                return $"""
                    <tr>
                        <td align="center" style="padding:{padding};">
                            <img src="{WebUtility.HtmlEncode(url)}" alt="{WebUtility.HtmlEncode(alt)}" width="{maxWidth}" style="display:block;border:none;width:100%;max-width:{maxWidth}px;height:auto;border-radius:8px;" />
                        </td>
                    </tr>
                    """;
        }

        private static string BuildSocialRow(BodyBuilder builder, Domain.Entities.Tenant tenant)
        {
                var links = new List<string>
                {
                BuildSocialLink(builder, "Instagram", tenant.EmailLayoutInstagramUrl),
                BuildSocialLink(builder, "LinkedIn", tenant.EmailLayoutLinkedInUrl),
                BuildSocialLink(builder, "WhatsApp", tenant.EmailLayoutWhatsAppUrl),
                BuildSocialLink(builder, "Telegram", tenant.EmailLayoutTelegramUrl)
                };

                var linksHtml = string.Join(string.Empty, links.Where(link => !string.IsNullOrWhiteSpace(link)));
                if (string.IsNullOrWhiteSpace(linksHtml))
                        return string.Empty;

                return $"""
                    <tr>
                        <td align="center" style="padding: 10px 32px 18px 32px;background:{EmailSurface3};">
                            <div style="font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailTextSecondary};font-size:12px;line-height:1.4;margin-bottom:8px;">
                                Confira nossas redes sociais
                            </div>
                            <table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 auto;">
                                <tr>
                                    {linksHtml}
                                </tr>
                            </table>
                        </td>
                    </tr>
                    """;
        }

        private static string BuildSocialLink(BodyBuilder builder, string label, string? url)
        {
                if (!TryNormalizeHttpUrl(url, out var safeUrl))
                        return string.Empty;

            var iconCid = ResolveInlineSocialIcon(builder, label);
            var iconHtml = string.IsNullOrWhiteSpace(iconCid)
                    ? string.Empty
                : $"<img src=\"{iconCid}\" alt=\"{WebUtility.HtmlEncode(label)}\" width=\"18\" height=\"18\" style=\"display:block;\" />";

                return $"""
                    <td align="center" style="padding:0 6px;">
                        <a href="{WebUtility.HtmlEncode(safeUrl)}" target="_blank" rel="noopener noreferrer" aria-label="{WebUtility.HtmlEncode(label)}" style="display:inline-block;background:transparent;color:#ffffff;text-decoration:none;font-family:'Plus Jakarta Sans',Arial,sans-serif;line-height:1;padding:6px;border-radius:999px;border:1px solid {EmailBorder};">
                            {iconHtml}
                        </a>
                    </td>
                    """;
        }

                private static string? ResolveInlineSocialIcon(BodyBuilder builder, string label)
                {
                    var base64 = GetSocialIconPngBase64(label);
                    if (string.IsNullOrWhiteSpace(base64))
                        return null;

                    var bytes = Convert.FromBase64String(base64);
                    var contentId = MimeUtils.GenerateMessageId();
                    var part = new MimePart("image", "png")
                    {
                        Content = new MimeContent(new MemoryStream(bytes)),
                        ContentId = contentId,
                        ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                        ContentTransferEncoding = ContentEncoding.Base64,
                    };

                    part.ContentDisposition.FileName = null;
                    part.ContentType.Name = null;

                    builder.LinkedResources.Add(part);
                    return $"cid:{contentId}";
                }

                private static string GetSocialIconPngBase64(string label)
                {
                    return label switch
                    {
                        "Instagram" => "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAAsTAAALEwEAmpwYAAADEklEQVRYhbWXu06VQRDHPw5YCh1gAoJCQEBeQS3EUrw0PgAijVLoi2CBvUZJsBALvDwBSKFILMUIgtyRBORigJ8ZMusZlt3v4yBM8iVndy7/mdnZOTtJEiCgFugGXgLDwDiwACybz6cNjz8HfAIGgEdAfQjLB64AngHbAYD/pR2gTzBi4K3AZIaRX16U3zU7y4HvT8SGYLSGIrfgks5eoA2oAooz0xcOqhK4DDwGfhv7E0C5FewzzK9Aw1EAM5xpUNuOnjpGvZ4P6uWxgQMXgPvARV03anbROqtNtEId9Rwj+DmT9k2gSfefGLxu2XhtNi4VAFAGtADN8jvAv+MVX4fut5m9/kTvqqPyIFre6CmgCxgBdo3eru51AiVJvgDnze2p0f1qozcsGzO62AKKUsClVr6QTWNAneqUA+3AGWOn2NTcpGws6mI1A3zBA5oGBvX76fHmnRMRe1sqNyuLFV2spKTdRj4FXLfZkt/ATc+RMXccAZtrKrMoi1VdLEWEuzzw6pTIzmpmHHVG5PJBA+subRHhEWOwPQZu5G8Z+aGIjDv2NXseMwHBMlPt02lFanRyprCl2EoDMo6/mZiKnAoItphoBrPAjd47o9cU4E8pb9s68CMg2GwMvS3AgfeFOLCpi9mA4EkdgbstW/uvRNjgBxPNjUM4cPsQRTin/PVE2yQpfeCeMTidcQ1rvF7QEZFbUv5qYjrcWkS4RJuKdUKaTs7I5DRyCz6a0oj29YH8lYhHVmf+WByJ3hspTqkfjyey51Psuea31wknTMFEn17qxGeyaTQDvGhf79Fnt6OqmGKSP467qrNj9OT3kJx5LO3GRoXR+ygb/WbjapqyZ6hU+4R8pwvQu2LwBhIdQBz1HtbQUUmefQbvoZuC3CAiD8bGEwRvMO/EnX/TkjyRjVcn9Sxv9J7lzy2z3NwG1MsePa/KIwLmdKi5pkPOhrE/eeD9mTGa+YPnst79cf2WPF7abDlxYDTzMvHCu2LHRdt61Kkvb+dIvQ4sr+Su6p+HjXAjAOAPrt+0X8g1f7A3BQXoLxjuPbrBzNYoAAAAAElFTkSuQmCC",
                        "LinkedIn" => "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAAsTAAALEwEAmpwYAAABN0lEQVRYhe2XsUoDQRRFh4T0FsZ8gRbRUvQH9F8sLSIIIqIgaVL5DWlTBNKIWkgkmmgjgoWCgoWCkCKNhSYeGdliGF5AFt5bi72wzdzLfYdld4ZxwBLQB8bYyc+6BKoOuDEcHGvgASYZAkxchsN/NQ3gDagDO8CtNcArMOcSASXgzBJgz0UC1iwBNgWAZUuAC6AQATQsAbxawEqySdU1N6l/+xum1QdwDjSBNvCQBuBU+Aj3A39b8D+BI2BW8FaBO22A3Xgt8svAiyZAIV4TMhtqAEFuHliY4lWAb603MOOP1iBzDBSF3LsWQE3oWRdy91oAHaFnS8h1tQB6Qs+BkDvRAhgIPYc5QA6QA6QFGCbB8HkK/EfBHwk9z0Ju+BcAU2V9NRt7gOsMAfoeYBG4Mr6efyXnR/UHnnaL6kM1MfMAAAAASUVORK5CYII=",
                        "WhatsApp" => "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAAsTAAALEwEAmpwYAAADNUlEQVRYha2Xa2jPYRTH/2zLfSG3GLkkUROlKbm8IC/UhEbk8kLeLCm3UCOJWkiR9oLUWHmFZSZkJOUWTeaWW8Qwl00sd9s+Ojo/Oz17/r//b/vtvHvO5Xu+v+d3nvOcJ5GIKEAXIA8oAq4D74FfQAPwEagEioGlQM+ouFES9wJ2AV+ILj+BEmBMnMRpwDrgkyeBkHkAXADOAFW6C678AfYCXVubPBMod8BqgJ3AJKBjkrixQAHw1IkVsiOjJs8C7pvgemAL0K0VH5AB5APvDM4HYHyqwG7AbRP0GBgVNbEHb4AWbCBSuCOSOXcAjhnnK1KAbU1ucDsDxw3uHdH5HJcYp+dA37jJDbYc4ZsGv9B16AS8UONvINuxS12sFZIxSAwC6jSH9I9h1rjasCtyAvsBr4x9YgwS6715gEeq/Ab0d4IOOkeqJAYBqYdqxZFe0kWU2Qa81BNU7RD4AfSJQWK3wcoVxSqjWOYJqKelxKmFqQZnjygOGMVQT0Clk/xlnMsGSNdCF6kQxTldNMlp8ARsdAhMa2tygxkU9RNZXNVFbRJnuRfeGALb2oFA0BNqZHFJF3UhAXMNAdm+mTEJVClWtSxKdSGDRVpI0D5DQgozx+MjV3hWBAK1inPXPRbZIUFpevcHIj1judwhxkeGkEb9qAlJcDK13vh37IGFBjQ/BXPp6WVOUcptNw/YSkspcwsbWGDsBUGrbVDF5QjbJ8fokCeZT2Q3ujvxR409J1BWqKIp7Dc4QIt0SgqTa07MYOC72p79/33AbBNUHoVAonmAkYvsrie53PvDHf9iY19jDTKM3DPGIVFJGAyZB1fotT3LnRuB6fpLRF63GFR1CBF5ayu7PUTGMDMLiOS5DiON8Ug7Jx+t/zuQvT6nlcZhsWNLj5E8F/hssM978YCT5hTIJDtOHyXSeL4CZ32dLyRxttO0RE4BPXzOGebZ9SfJCyeQW8AGYDLQO9HcIQcKQWATcMMUW/BRhckeMwIwJSRhgzMPutJomphPpEtOTrVd252gh8B+YE4weAAzdAt/hCSzpC5qe059moATwGF9Vg9M4ZuphbVZm4rUzmltrzuA+a19T/wFzXgQm91YhpIAAAAASUVORK5CYII=",
                        "Telegram" => "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAAsTAAALEwEAmpwYAAACO0lEQVRYhb2Xz0tUURTHn0E6RbpIgrKdYDtNaBMIBgZCtTarbetg/oQEKZQI2vgr27S2lS3EXZsZS4sgKGjXr4EpCFIrp7BPHDovno937ztv3jy/q+HOuef7vfeeXy8IjAC6gKvAfWAd+AL8Bn4Bn4FnwAJwBei0+rUQ9wDzwA/s+A4sAn15iA8Bk+qsWcjtTAOlrOQngKc5iOOoik8r+WngYwvJQ3wABtLIj6thUfgEnHSRl4C1AslDbEh8JQm4sw/kISbi5Kc0YvcLW/LcUQEPWkxQA5ZTUngmJO/KmeshfgIPgSHjwbaBI2J0LSfxG6AMHE2Iq6WUveOB1vas2AUeA6NAmyetX6b4mQ8yVrxvwL14jZer1LLdGVlr02v2YS3QrpaGt8CNpC4HjACv5TZi6z0Gv3UxbHgMVoCLwIEEYgneOQ2+Cwn/DxsE7PgEXPK8rbz9O83n8w6b61YBridYBcZi79qtff4P8BU46xF51yCgbgnChvaIZQ1CwSYw6CJXAasGAdVAx6iseOQjVwE1g5+5QGe4ZjAFtDvIu40+xsIcTstXF14BZxIEnDPs/VeKdcNikwLQLnoTOBgRUDbsW4gq7mtBO34uWQEcBl6k2Epg98avbTqngCy4nRQ4JZ1ei0YF6HCljgyl7wskdw+lEREDBU3GcrB+L3lExDHgSQvJK3tmQKOIDplec9QINNpvOd/cKETiYiajkG39oO01UJiFSMUc1xlAmlNdT9jQ37I2C1z+X+EM+AviE8UBuTSokAAAAABJRU5ErkJggg==",
                        _ => string.Empty
                    };
                }

        private static bool TryNormalizeImageSource(string? value, out string? url)
        {
                url = null;
                if (string.IsNullOrWhiteSpace(value))
                        return false;

                var trimmed = value.Trim();
            if (trimmed.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
            {
                url = trimmed;
                return true;
            }
                if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                        url = trimmed;
                        return true;
                }

                return TryNormalizeHttpUrl(trimmed, out url);
        }

                private static string? ResolveInlineImageSource(BodyBuilder builder, string? value, string contentIdPrefix)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        return null;

                    var trimmed = value.Trim();
                    if (TryParseDataUrlImage(trimmed, out var bytes, out var mediaType))
                    {
        var contentId = MimeUtils.GenerateMessageId();
        var part = new MimePart(ContentType.Parse(mediaType))
        {
            Content = new MimeContent(new MemoryStream(bytes)),
            ContentId = contentId,
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
            ContentTransferEncoding = ContentEncoding.Base64,
        };

        part.ContentDisposition.FileName = null;
        part.ContentType.Name = null;

        builder.LinkedResources.Add(part);
        return $"cid:{contentId}";
                    }

                    return trimmed;
                }

                private static bool TryParseDataUrlImage(string value, out byte[] bytes, out string mediaType)
                {
                    bytes = Array.Empty<byte>();
                    mediaType = "application/octet-stream";

                    if (string.IsNullOrWhiteSpace(value))
                        return false;

                    var trimmed = value.Trim();
                    if (!trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                        return false;

                    var commaIndex = trimmed.IndexOf(',');
                    if (commaIndex < 0)
                        return false;

                    var metadata = trimmed[..commaIndex];
                    var payload = trimmed[(commaIndex + 1)..].Trim();

                    var semiIndex = metadata.IndexOf(';');
                    if (semiIndex < 0)
                        return false;

                    if (!metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase))
                        return false;

                    mediaType = metadata["data:".Length..semiIndex];

                    try
                    {
                        bytes = Convert.FromBase64String(payload);
                        return true;
                    }
                    catch
                    {
                        bytes = Array.Empty<byte>();
                        mediaType = "application/octet-stream";
                        return false;
                    }
                }

    private static string BuildStyledHtml(string contentHtml, string subject, string companyName, string? boletoHref)
    {
        var safeSubject = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(subject) ? "Cobrança" : subject.Trim());
        var safeCompanyName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(companyName) ? "SmartCollect" : companyName.Trim());
        var boletoButton = string.IsNullOrWhiteSpace(boletoHref)
            ? string.Empty
            : $"""
              <tr>
                <td align="center" style="padding: 8px 32px 28px 32px;text-align:center;">
                  <a href="{WebUtility.HtmlEncode(boletoHref)}" target="_blank" rel="noopener noreferrer" style="display:inline-block;background:{EmailAccent};color:#ffffff;text-decoration:none;font-family:'Plus Jakarta Sans',Arial,sans-serif;font-size:15px;font-weight:800;padding:13px 22px;border-radius:8px;margin:0 auto;">
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
          <body style="margin:0;padding:0;background:{EmailSurface};">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:{EmailSurface};margin:0;padding:28px 12px;">
              <tr>
                <td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:640px;background:{EmailSurface2};border:1px solid {EmailBorder};border-radius:14px;overflow:hidden;">
                    <tr>
                      <td style="background:{EmailSurface3};padding:24px 32px;border-bottom:2px solid {EmailAccent};">
                        <div style="font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailText};font-size:20px;font-weight:800;line-height:1.25;">{safeCompanyName}</div>
                        <div style="font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailTextSecondary};font-size:13px;line-height:1.4;margin-top:6px;">{safeSubject}</div>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:30px 32px 18px 32px;font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailTextSecondary};font-size:15px;line-height:1.6;">
                        {contentHtml}
                      </td>
                    </tr>
                    {boletoButton}
                    <tr>
                      <td style="border-top:1px solid {EmailBorder};padding:18px 32px;background:{EmailSurface3};">
                        <div style="font-family:'Plus Jakarta Sans',Arial,sans-serif;color:{EmailTextMuted};font-size:12px;line-height:1.5;">
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

    private static string RemoveBoletoUrlFromText(string content, string? boletoHref)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(boletoHref))
            return content ?? string.Empty;

        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        var lines = normalized
            .Split('\n')
            .Where(line => !line.Contains(boletoHref, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return string.Join("\n", lines).Trim();
    }

    private static string RemoveBoletoUrlFromHtml(string content, string? boletoHref)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(boletoHref))
            return content ?? string.Empty;

        var escapedUrl = Regex.Escape(boletoHref);
        var encodedUrl = Regex.Escape(WebUtility.HtmlEncode(boletoHref));
        var result = content;

        result = Regex.Replace(
            result,
            $@"<a\b[^>]*href\s*=\s*[""'](?:{escapedUrl}|{encodedUrl})[""'][^>]*>.*?</a>",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        result = Regex.Replace(result, escapedUrl, string.Empty, RegexOptions.IgnoreCase);
        result = Regex.Replace(result, encodedUrl, string.Empty, RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"(Link\s+para\s+pagamento|Pagamento|Boleto)\s*:\s*(<br\s*/?>)?", string.Empty, RegexOptions.IgnoreCase);

        return result.Trim();
    }

    private static bool TryNormalizeHttpUrl(string? value, out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = $"https://{trimmed}";

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme is not ("http" or "https"))
            return false;

        url = parsed.AbsoluteUri;
        return true;
    }

    private void LogEmailBodyDiagnostics(Domain.Entities.Tenant tenant, MimeEntity body)
    {
        try
        {
            var hasHtml = TryGetHtmlBody(body, out var htmlBody);
            var inlineCount = CountInlineResources(body);
            _logger.LogInformation(
                "Email body diagnostics: TenantId={TenantId} LayoutEnabled={LayoutEnabled} HasHtml={HasHtml} HtmlLength={HtmlLength} InlineResources={InlineResources}",
                tenant.Id,
                tenant.EmailLayoutEnabled,
                hasHtml,
                hasHtml ? htmlBody.Length : 0,
                inlineCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log email body diagnostics for tenant {TenantId}", tenant.Id);
        }
    }

    private static bool TryGetHtmlBody(MimeEntity entity, out string html)
    {
        html = string.Empty;
        if (entity is TextPart textPart && textPart.IsHtml)
        {
            html = textPart.Text ?? string.Empty;
            return true;
        }

        if (entity is Multipart multipart)
        {
            foreach (var part in multipart)
            {
                if (TryGetHtmlBody(part, out html))
                    return true;
            }
        }

        return false;
    }

    private static int CountInlineResources(MimeEntity entity)
    {
        var count = 0;
        if (entity is MimePart part)
        {
            var disposition = part.ContentDisposition?.Disposition;
            if (string.Equals(disposition, ContentDisposition.Inline, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        if (entity is Multipart multipart)
        {
            foreach (var child in multipart)
                count += CountInlineResources(child);
        }

        return count;
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
            message.Body = BuildEmailBody(body, message.Subject, tenant, boletoUrl);
            LogEmailBodyDiagnostics(tenant, message.Body);

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
