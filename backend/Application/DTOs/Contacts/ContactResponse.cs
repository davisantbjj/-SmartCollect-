namespace SmartCollect.Application.DTOs.Contacts;

public record ContactResponse(
    Guid Id,
    Guid ClientId,
    string CompanyName,
    string CompanyTaxId,
    string Name,
    string Department,
    string? Email,
    string? WhatsAppPhone,
    bool IsPrimary,
    int TitleCount,
    string Status
);
