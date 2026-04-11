namespace SmartCollect.Application.DTOs.Contacts;

public record CreateContactRequest(
    string Name,
    string Email,
    string? WhatsAppPhone,
    string Department,
    bool IsPrimary
);
