namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Config;

public interface ISmtpConfigService
{
    Task<SmtpConfigResponse?> GetAsync(Guid tenantId);
    Task SaveAsync(Guid tenantId, SmtpConfigRequest request);
    Task<bool> TestAsync(Guid tenantId);
}
