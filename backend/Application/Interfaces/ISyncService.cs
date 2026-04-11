namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Config;

public interface ISyncService
{
    Task<int> SyncPendingTitlesAsync(Guid tenantId);
    Task<int> SyncOccurrencesAsync(Guid tenantId);
    Task<bool> CheckExternalApiConnectionAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<ExternalApiConfigResponse> GetExternalApiConfigAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task SaveExternalApiConfigAsync(Guid tenantId, ExternalApiConfigRequest request, CancellationToken cancellationToken = default);
}
