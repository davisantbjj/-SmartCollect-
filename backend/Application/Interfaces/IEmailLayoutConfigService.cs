namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Config;

public interface IEmailLayoutConfigService
{
    Task<EmailLayoutConfigResponse> GetAsync(Guid tenantId);
    Task SaveAsync(Guid tenantId, EmailLayoutConfigRequest request);
}
