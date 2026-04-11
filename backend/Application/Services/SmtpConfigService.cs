namespace SmartCollect.Application.Services;

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.Interfaces;

public class SmtpConfigService : ISmtpConfigService
{
    private readonly IAppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<SmtpConfigService> _logger;

    public SmtpConfigService(
        IAppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SmtpConfigService> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("SmartCollect.SmtpCredentials.v1");
        _logger = logger;
    }

    public async Task<SmtpConfigResponse?> GetAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant?.SmtpHost is null) return null;

        var senderFrom = !string.IsNullOrWhiteSpace(tenant.SmtpUser) && tenant.SmtpUser.Contains('@')
            ? tenant.SmtpUser
            : $"financeiro@{tenant.EmailDomain}";

        return new SmtpConfigResponse(
            tenant.SmtpHost,
            tenant.SmtpPort ?? 587,
            tenant.SmtpUser ?? string.Empty,
            senderFrom,
            tenant.CompanyName);
    }

    public async Task SaveAsync(Guid tenantId, SmtpConfigRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found");

        tenant.SmtpHost = request.Host;
        tenant.SmtpPort = request.Port;
        tenant.SmtpUser = request.User;

        // RN20: protect secrets with authenticated encryption via ASP.NET Data Protection.
        tenant.SmtpPasswordEncrypted = _protector.Protect(request.Password);

        if (!string.IsNullOrWhiteSpace(request.SenderFrom) && request.SenderFrom.Contains('@'))
        {
            var domain = request.SenderFrom.Split('@').Last();
            if (!string.IsNullOrWhiteSpace(domain))
                tenant.EmailDomain = domain.Trim();
        }

        await _db.SaveChangesAsync();
    }

    public async Task<bool> TestAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant?.SmtpHost is null) return false;

        if (!tenant.SmtpPort.HasValue
            || string.IsNullOrWhiteSpace(tenant.SmtpUser)
            || string.IsNullOrWhiteSpace(tenant.SmtpPasswordEncrypted))
            return false;

        try
        {
            var password = _protector.Unprotect(tenant.SmtpPasswordEncrypted);

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(tenant.SmtpHost, tenant.SmtpPort.Value, SecureSocketOptions.StartTlsWhenAvailable);
            await smtp.AuthenticateAsync(tenant.SmtpUser, password);
            await smtp.DisconnectAsync(true);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP connectivity test failed for tenant {TenantId}", tenantId);
            return false;
        }
    }
}
