namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Contacts;

public interface IContactService
{
    Task<List<ContactResponse>> ListByTenantAsync(Guid tenantId);
    Task<List<ContactResponse>> ListByClientAsync(Guid tenantId, Guid clientId);
    Task<ContactResponse> CreateAsync(Guid tenantId, Guid clientId, CreateContactRequest request);
    Task<ContactResponse?> UpdateAsync(Guid tenantId, Guid clientId, Guid contactId, UpdateContactRequest request);
    Task<bool> DeleteAsync(Guid tenantId, Guid clientId, Guid contactId);
}
