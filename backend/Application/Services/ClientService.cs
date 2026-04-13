namespace SmartCollect.Application.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Clients;
using SmartCollect.Application.Interfaces;

public class ClientService : IClientService
{
    private readonly IAppDbContext _db;
    public ClientService(IAppDbContext db) => _db = db;

    public async Task<List<ClientResponse>> ListAsync(Guid tenantId)
    {
        var clients = await _db.Clients
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.LegalName)
            .Select(c => new
            {
                c.Id,
                c.LegalName,
                c.TaxId,
                c.TradeName,
                ContactCount = c.Contacts.Count,
                TitleCount = c.Titles.Count,
                c.SendToAllContacts,
                c.DispatchMode,
                c.SelectedDispatchContactIdsJson,
            })
            .ToListAsync();

        return clients
            .Select(c => new ClientResponse(
                c.Id,
                c.LegalName,
                c.TaxId,
                c.TradeName,
                c.ContactCount,
                c.TitleCount,
                c.SendToAllContacts,
                NormalizeDispatchMode(c.DispatchMode) ?? (c.SendToAllContacts ? "All" : "Primary"),
                ParseSelectedContactIds(c.SelectedDispatchContactIdsJson)))
            .ToList();
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
            0,
            client.SendToAllContacts,
            ResolveDispatchMode(client),
            new List<Guid>());
    }

    public async Task<ClientResponse?> UpdateDispatchPreferenceAsync(Guid tenantId, Guid clientId, UpdateClientDispatchPreferenceRequest request)
    {
        var client = await _db.Clients
            .Include(c => c.Contacts)
            .Include(c => c.Titles)
            .FirstOrDefaultAsync(c => c.Id == clientId && c.TenantId == tenantId);

        if (client is null)
            return null;

        var mode = NormalizeDispatchMode(request.DispatchMode)
            ?? (request.SendToAllContacts == true ? "All" : "Primary");

        List<Guid> selectedContactIds = [];
        if (mode == "Selected")
        {
            selectedContactIds = (request.SelectedContactIds ?? new List<Guid>())
                .Distinct()
                .Where(id => client.Contacts.Any(c => c.Id == id))
                .ToList();

            if (selectedContactIds.Count == 0)
                throw new InvalidOperationException("Selecione ao menos um contato para o modo de envio personalizado.");
        }

        client.DispatchMode = mode;
        client.SendToAllContacts = mode == "All";
        client.SelectedDispatchContactIdsJson = mode == "Selected"
            ? JsonSerializer.Serialize(selectedContactIds)
            : null;

        await _db.SaveChangesAsync();

        return new ClientResponse(
            client.Id,
            client.LegalName,
            client.TaxId,
            client.TradeName,
            client.Contacts.Count,
            client.Titles.Count,
            client.SendToAllContacts,
            ResolveDispatchMode(client),
            ParseSelectedContactIds(client.SelectedDispatchContactIdsJson));
    }

    private static string ResolveDispatchMode(SmartCollect.Domain.Entities.Client client)
        => NormalizeDispatchMode(client.DispatchMode)
            ?? (client.SendToAllContacts ? "All" : "Primary");

    private static string? NormalizeDispatchMode(string? rawMode)
    {
        if (string.Equals(rawMode, "Primary", StringComparison.OrdinalIgnoreCase))
            return "Primary";
        if (string.Equals(rawMode, "All", StringComparison.OrdinalIgnoreCase))
            return "All";
        if (string.Equals(rawMode, "Selected", StringComparison.OrdinalIgnoreCase))
            return "Selected";

        return null;
    }

    private static List<Guid> ParseSelectedContactIds(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
            return new List<Guid>();

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(serialized) ?? new List<Guid>();
        }
        catch
        {
            return new List<Guid>();
        }
    }
}
