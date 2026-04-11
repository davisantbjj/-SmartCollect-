namespace SmartCollect.Application.DTOs.Contacts;

public record UpdateContactRequest(
    string Name,
    string Email,
    string? WhatsAppPhone,
    string Department,
    bool IsPrimary
);
