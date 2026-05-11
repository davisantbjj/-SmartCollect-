namespace SmartCollect.Application.DTOs.Tenants;

public record UpdateTenantRequest(
    string CompanyName,
    string TaxId,
    string EmailDomain
);