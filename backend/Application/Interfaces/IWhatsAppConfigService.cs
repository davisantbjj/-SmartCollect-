namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Config;

public interface IWhatsAppConfigService
{
    Task<WhatsAppConfigResponse?> GetAsync(Guid tenantId);
    Task SaveAsync(Guid tenantId, WhatsAppConfigRequest request);
    Task<bool> TestAsync(Guid tenantId);
}
