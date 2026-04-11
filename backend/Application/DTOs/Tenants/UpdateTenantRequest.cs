namespace SmartCollect.Application.DTOs.Tenants;

public record UpdateTenantRequest(
    string CompanyName,
    string TaxId,
    string EmailDomain,
    bool EditAdminLogin,
    string? AdminName,
    string? AdminEmail,
    string? AdminPassword
);