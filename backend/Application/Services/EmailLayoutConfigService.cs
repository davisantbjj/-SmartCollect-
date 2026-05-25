namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.Interfaces;

public class EmailLayoutConfigService : IEmailLayoutConfigService
{
    private readonly IAppDbContext _db;

    public EmailLayoutConfigService(IAppDbContext db) => _db = db;

    public async Task<EmailLayoutConfigResponse> GetAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found");

        return new EmailLayoutConfigResponse(
            tenant.EmailLayoutEnabled,
            tenant.EmailLayoutLogoUrl,
            tenant.EmailLayoutHeroUrl,
            tenant.EmailLayoutFooterMessage,
            tenant.EmailLayoutInstagramUrl,
            tenant.EmailLayoutLinkedInUrl,
            tenant.EmailLayoutWhatsAppUrl,
            tenant.EmailLayoutTelegramUrl);
    }

    public async Task SaveAsync(Guid tenantId, EmailLayoutConfigRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found");

        tenant.EmailLayoutEnabled = request.Enabled;
        tenant.EmailLayoutLogoUrl = request.LogoUrl;
        tenant.EmailLayoutHeroUrl = request.HeroUrl;
        tenant.EmailLayoutFooterMessage = request.FooterMessage;
        tenant.EmailLayoutInstagramUrl = request.InstagramUrl;
        tenant.EmailLayoutLinkedInUrl = request.LinkedInUrl;
        tenant.EmailLayoutWhatsAppUrl = request.WhatsAppUrl;
        tenant.EmailLayoutTelegramUrl = request.TelegramUrl;

        await _db.SaveChangesAsync();
    }
}
