namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Clients;
using SmartCollect.Application.Interfaces;

public class ClientService : IClientService
{
    private readonly IAppDbContext _db;
    public ClientService(IAppDbContext db) => _db = db;

    public async Task<List<ClientResponse>> ListAsync(Guid tenantId)
    {
        return await _db.Clients
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.LegalName)
            .Select(c => new ClientResponse(
                c.Id,
                c.LegalName,
                c.TaxId,
                c.TradeName,
                c.Contacts.Count,
                c.Titles.Count))
            .ToListAsync();
    }

    public async Task<ClientResponse> CreateAsync(Guid tenantId, Guid userId, CreateClientRequest request)
    {
        var client = new SmartCollect.Domain.Entities.Client
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            LegalName = request.LegalName,
            TaxId = request.TaxId,
            TradeName = request.TradeName
        };

        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        return new ClientResponse(
            client.Id,
            client.LegalName,
            client.TaxId,
            client.TradeName,
            0,
            0);
    }
}
