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
                HasMeaningfulEmail(c.Email) && HasMeaningfulPhone(c.WhatsAppPhone) ? "complete"
                    : HasMeaningfulEmail(c.Email) || HasMeaningfulPhone(c.WhatsAppPhone) ? "partial"
                    : "pending"))
            .ToListAsync();
    }

    public async Task<ContactResponse> CreateAsync(Guid tenantId, Guid clientId, CreateContactRequest request)
    {
        var client = await _db.Clients
            .Include(c => c.Contacts)
            .Include(c => c.Titles)
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
            Email = NullIfWhiteSpace(request.Email),
            WhatsAppPhone = NormalizePhoneOrNull(request.WhatsAppPhone),
            Department = dept,
            IsPrimary = isPrimary
        };

        await _db.Contacts.AddAsync(contact);
        await RecalculateClientTitlesStatusAsync(tenantId, client);
        await _db.SaveChangesAsync();

        return (await ListByClientAsync(tenantId, clientId)).First(c => c.Id == contact.Id);
    }

    public async Task<ContactResponse?> UpdateAsync(Guid tenantId, Guid clientId, Guid contactId, UpdateContactRequest request)
    {
        var contact = await _db.Contacts
            .Include(c => c.Client)
                .ThenInclude(cl => cl.Contacts)
            .Include(c => c.Client)
                .ThenInclude(cl => cl.Titles)
            .FirstOrDefaultAsync(c => c.Id == contactId && c.ClientId == clientId && c.Client.TenantId == tenantId);

        if (contact is null) return null;

        // RN10: un-primary others
        if (request.IsPrimary && !contact.IsPrimary)
        {
            foreach (var sibling in contact.Client.Contacts.Where(c => c.IsPrimary && c.Id != contactId))
                sibling.IsPrimary = false;
        }

        contact.Name = request.Name;
        contact.Email = NullIfWhiteSpace(request.Email);
        contact.WhatsAppPhone = NormalizePhoneOrNull(request.WhatsAppPhone);
        contact.IsPrimary = request.IsPrimary;

        if (Enum.TryParse<ContactDepartment>(request.Department, true, out var dept))
            contact.Department = dept;

        await RecalculateClientTitlesStatusAsync(tenantId, contact.Client);
        await _db.SaveChangesAsync();

        return (await ListByClientAsync(tenantId, clientId)).FirstOrDefault(c => c.Id == contactId);
    }

    private async Task RecalculateClientTitlesStatusAsync(Guid tenantId, Domain.Entities.Client client)
    {
        var hasContactInfo = client.Contacts.Any(c =>
            HasMeaningfulEmail(c.Email) ||
            HasMeaningfulPhone(c.WhatsAppPhone));

        var nowDate = DateTime.UtcNow.Date;

        foreach (var title in client.Titles.Where(t => t.Status is not TitleStatus.Paid and not TitleStatus.Cancelled))
        {
            var nextStatus = !hasContactInfo
                ? TitleStatus.PendingData
                : title.DueDate.Date < nowDate
                    ? TitleStatus.Overdue
                    : TitleStatus.Open;

            if (title.Status == nextStatus)
                continue;

            var oldStatus = title.Status;
            title.Status = nextStatus;

            await _db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
            {
                Id = Guid.NewGuid(),
                TitleId = title.Id,
                TenantId = tenantId,
                Action = "Atualizacao por contato",
                Description = $"Status ajustado de {oldStatus} para {nextStatus} após edição de contato"
            });
        }
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool HasMeaningfulEmail(string? email)
        => !string.IsNullOrWhiteSpace(email);

    private static bool HasMeaningfulPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length >= 10;
    }

    private static string? NormalizePhoneOrNull(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var raw = phone.Trim();
        var hasExplicitCountryCode = raw.StartsWith('+');
        var digits = new string(raw.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        if (digits.Length < 10 || digits.Length > 13)
            return null;

        // Persist canonical E.164 to keep manual/import flows consistent and Twilio-ready.
        if (!hasExplicitCountryCode && (digits.Length == 10 || digits.Length == 11))
            digits = $"55{digits}";

        return $"+{digits}";
    }
}
