namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Contacts;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class ContactService : IContactService
{
    private readonly IAppDbContext _db;
    public ContactService(IAppDbContext db) => _db = db;

    public async Task<List<ContactResponse>> ListByClientAsync(Guid tenantId, Guid clientId)
    {
        return await _db.Contacts
            .Include(c => c.Client)
            .Where(c => c.ClientId == clientId && c.Client.TenantId == tenantId)
            .Select(c => new ContactResponse(
                c.Id,
                c.ClientId,
                c.Client.LegalName,
                c.Client.TaxId,
                c.Name,
                c.Department.ToString(),
                c.Email,
                c.WhatsAppPhone,
                c.IsPrimary,
                c.Client.Titles.Count,
                c.Email != null && c.WhatsAppPhone != null ? "complete"
                    : c.Email != null || c.WhatsAppPhone != null ? "partial"
                    : "pending"))
            .ToListAsync();
    }

    public async Task<ContactResponse> CreateAsync(Guid tenantId, Guid clientId, CreateContactRequest request)
    {
        var client = await _db.Clients
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == clientId && c.TenantId == tenantId)
            ?? throw new InvalidOperationException("Client not found");

        var isPrimary = request.IsPrimary || !client.Contacts.Any();

        // RN10: un-primary others if this one is primary
        if (isPrimary)
        {
            foreach (var existing in client.Contacts.Where(c => c.IsPrimary))
                existing.IsPrimary = false;
        }

        if (!Enum.TryParse<ContactDepartment>(request.Department, true, out var dept))
            dept = ContactDepartment.Finance;

        var contact = new Domain.Entities.Contact
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = request.Name,
            Email = request.Email,
            WhatsAppPhone = request.WhatsAppPhone,
            Department = dept,
            IsPrimary = isPrimary
        };

        await _db.Contacts.AddAsync(contact);
        await _db.SaveChangesAsync();

        return (await ListByClientAsync(tenantId, clientId)).First(c => c.Id == contact.Id);
    }

    public async Task<ContactResponse?> UpdateAsync(Guid tenantId, Guid clientId, Guid contactId, UpdateContactRequest request)
    {
        var contact = await _db.Contacts
            .Include(c => c.Client)
                .ThenInclude(cl => cl.Contacts)
            .FirstOrDefaultAsync(c => c.Id == contactId && c.ClientId == clientId && c.Client.TenantId == tenantId);

        if (contact is null) return null;

        // RN10: un-primary others
        if (request.IsPrimary && !contact.IsPrimary)
        {
            foreach (var sibling in contact.Client.Contacts.Where(c => c.IsPrimary && c.Id != contactId))
                sibling.IsPrimary = false;
        }

        contact.Name = request.Name;
        contact.Email = request.Email;
        contact.WhatsAppPhone = request.WhatsAppPhone;
        contact.IsPrimary = request.IsPrimary;

        if (Enum.TryParse<ContactDepartment>(request.Department, true, out var dept))
            contact.Department = dept;

        await _db.SaveChangesAsync();

        return (await ListByClientAsync(tenantId, clientId)).FirstOrDefault(c => c.Id == contactId);
    }
}
