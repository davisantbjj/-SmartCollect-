namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Tenants;

public interface ITenantService
{
    Task<List<TenantResponse>> ListAllAsync();
    Task<TenantResponse?> GetByIdAsync(Guid tenantId);
    Task<TenantResponse> CreateAsync(CreateTenantRequest request);
    Task<TenantResponse?> UpdateAsync(Guid tenantId, UpdateTenantRequest request);
    Task<TenantResponse?> UpdateAccessAsync(Guid tenantId, UpdateTenantAccessRequest request);
}
