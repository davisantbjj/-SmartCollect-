namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Config;

public interface IDispatchWindowConfigService
{
    Task<DispatchWindowConfigResponse> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid tenantId, DispatchWindowConfigRequest request, CancellationToken cancellationToken = default);
}
