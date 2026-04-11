namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Common;
using SmartCollect.Application.DTOs.Titles;

public interface ITitleService
{
    Task<PaginatedResponse<TitleResponse>> ListAsync(Guid tenantId, TitleFilterRequest filter);
    Task<TitleResponse?> GetByIdAsync(Guid tenantId, Guid id);
    Task<List<TitleHistoryResponse>> GetHistoryAsync(Guid tenantId, Guid id);
    Task<TitleResponse> CreateAsync(Guid tenantId, CreateTitleRequest request);
    Task<TitleResponse?> UpdateStatusAsync(Guid tenantId, Guid id, string status);
    Task<bool> SendCollectionAsync(Guid tenantId, Guid titleId, SendCollectionRequest? request = null);
}
