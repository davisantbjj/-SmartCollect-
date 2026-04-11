namespace SmartCollect.Application.DTOs.Tenants;

public record TenantResponse(
    Guid Id,
    string CompanyName,
    string TaxId,
    string EmailDomain,
    string Plan,
    bool Active,
    int UserCount,
    int TitleCount,
    DateTime CreatedAt,
    string? AdminName = null,
    string? AdminEmail = null
);
