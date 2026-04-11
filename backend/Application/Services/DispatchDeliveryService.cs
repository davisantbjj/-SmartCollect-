namespace SmartCollect.Application.Services;

using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class DispatchDeliveryService : IDispatchDeliveryService
{
    private static readonly Regex TemplateRegex = new("\\{\\{\\s*([a-zA-Z0-9_]+)\\s*\\}\\}", RegexOptions.Compiled);

    private readonly IAppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<DispatchDeliveryService> _logger;

    public DispatchDeliveryService(
        IAppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<DispatchDeliveryService> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("SmartCollect.SmtpCredentials.v1");
        _logger = logger;
    }

    public async Task<bool> SendQuickEmailAsync(
        Guid tenantId,
        string recipientName,
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
            return false;

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null || !tenant.Active)
            return false;

        if (string.IsNullOrWhiteSpace(tenant.SmtpHost)
            || !tenant.SmtpPort.HasValue
            || string.IsNullOrWhiteSpace(tenant.SmtpUser)
            || string.IsNullOrWhiteSpace(tenant.SmtpPasswordEncrypted))
            return false;

        var smtpPassword = TryUnprotectPassword(tenant.SmtpPasswordEncrypted);
        if (smtpPassword is null)
            return false;

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
            message.Body = new TextPart("plain") { Text = body ?? string.Empty };

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(tenant.SmtpHost, tenant.SmtpPort.Value, SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
            await smtp.AuthenticateAsync(tenant.SmtpUser, smtpPassword, cancellationToken);
            await smtp.SendAsync(message, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quick SMTP send failed for tenant {TenantId}", tenantId);
            return false;
        }
    }

    public async Task<int> ProcessPendingDispatchesAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var query = _db.Dispatches
            .Include(d => d.Contact)
            .Include(d => d.Trigger)
                .ThenInclude(tr => tr.Template)
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

        var processed = 0;

        foreach (var dispatch in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (dispatch.Title.Status is TitleStatus.Paid or TitleStatus.Cancelled)
            {
                dispatch.Status = DispatchStatus.Cancelled;
                continue;
            }

            if (dispatch.Channel is not CollectionChannel.Email and not CollectionChannel.Both)
                continue;

            if (!tenants.TryGetValue(dispatch.Title.TenantId, out var tenant) || !tenant.Active)
            {
                MarkError(dispatch, "Tenant inativo ou não encontrado.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(dispatch.Contact.Email))
            {
                MarkError(dispatch, "Contato sem e-mail para envio.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(tenant.SmtpHost)
                || !tenant.SmtpPort.HasValue
                || string.IsNullOrWhiteSpace(tenant.SmtpUser)
                || string.IsNullOrWhiteSpace(tenant.SmtpPasswordEncrypted))
            {
                MarkError(dispatch, "SMTP não configurado para o tenant.");
                continue;
            }

            var smtpPassword = TryUnprotectPassword(tenant.SmtpPasswordEncrypted);
            if (smtpPassword is null)
            {
                MarkError(dispatch, "Falha ao descriptografar senha SMTP.");
                continue;
            }

            var senderFrom = tenant.SmtpUser.Contains('@')
                ? tenant.SmtpUser
                : $"financeiro@{tenant.EmailDomain}";

            var senderName = tenant.CompanyName;

            var subjectTemplate = string.IsNullOrWhiteSpace(dispatch.Trigger.Template.Subject)
                ? $"SmartCollect - Cobranca {dispatch.Title.UniqueCode}"
                : dispatch.Trigger.Template.Subject!;

            var subject = RenderTemplate(subjectTemplate, dispatch, tenant.CompanyName);
            var body = RenderTemplate(dispatch.Trigger.Template.Body, dispatch, tenant.CompanyName);

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(senderName, senderFrom));
                message.To.Add(MailboxAddress.Parse(dispatch.Contact.Email));
                message.Subject = subject;
                message.Body = new TextPart("plain") { Text = body };

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(tenant.SmtpHost, tenant.SmtpPort.Value, SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
                await smtp.AuthenticateAsync(tenant.SmtpUser, smtpPassword, cancellationToken);
                await smtp.SendAsync(message, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);

                dispatch.Status = DispatchStatus.Sent;
                dispatch.SentAt = DateTime.UtcNow;

                await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
                {
                    Id = Guid.NewGuid(),
                    TitleId = dispatch.TitleId,
                    TenantId = dispatch.Title.TenantId,
                    Action = "Disparo enviado",
                    Description = $"{dispatch.Channel}: enviado para {dispatch.Contact.Email}"
                }, cancellationToken);

                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dispatch send failed for dispatch {DispatchId}", dispatch.Id);
                MarkError(dispatch, $"Falha no envio SMTP: {ex.Message}");
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return processed;
    }

    private static string RenderTemplate(string template, Domain.Entities.Dispatch dispatch, string companyName)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ClienteNome"] = dispatch.Title.Client.LegalName,
            ["RazaoSocial"] = dispatch.Title.Client.LegalName,
            ["Cnpj"] = dispatch.Title.Client.TaxId,
            ["TituloCodigo"] = dispatch.Title.UniqueCode,
            ["CodigoTitulo"] = dispatch.Title.UniqueCode,
            ["Valor"] = dispatch.Title.Amount.ToString("C"),
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

    private string? TryUnprotectPassword(string encrypted)
    {
        try
        {
            return _protector.Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not unprotect SMTP password.");
            return null;
        }
    }

    private static void MarkError(Domain.Entities.Dispatch dispatch, string reason)
    {
        _ = reason;
        dispatch.Status = DispatchStatus.Error;
        dispatch.SentAt = null;
    }
}