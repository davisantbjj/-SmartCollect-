namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Clients;

public interface IClientService
{
    Task<List<ClientResponse>> ListAsync(Guid tenantId);
    Task<ClientResponse> CreateAsync(Guid tenantId, Guid userId, CreateClientRequest request);
}
