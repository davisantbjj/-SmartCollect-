namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.CollectionRules;

public interface ICollectionRuleService
{
    Task<List<CollectionRuleResponse>> ListAsync(Guid tenantId);
    Task<CollectionRuleResponse> CreateAsync(Guid tenantId, CreateCollectionRuleRequest request);
    Task<CollectionRuleResponse?> UpdateAsync(Guid tenantId, Guid id, CreateCollectionRuleRequest request);
    Task<bool> DeleteAsync(Guid tenantId, Guid id);
}
