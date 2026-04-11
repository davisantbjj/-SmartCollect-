namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Templates;

public interface IMessageTemplateService
{
    Task<List<MessageTemplateResponse>> ListAsync(Guid tenantId);
    Task<MessageTemplateResponse> CreateAsync(Guid tenantId, CreateTemplateRequest request);
    Task<MessageTemplateResponse?> UpdateAsync(Guid tenantId, Guid id, UpdateTemplateRequest request);
}
