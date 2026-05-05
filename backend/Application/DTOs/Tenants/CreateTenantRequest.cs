namespace SmartCollect.Application.DTOs.Tenants;

public record CreateTenantRequest(
    string CompanyName,
    string TaxId,
    string EmailDomain,
    string? AdminName,
    string? AdminEmail,
    string? AdminPassword
);
