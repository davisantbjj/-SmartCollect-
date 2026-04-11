namespace SmartCollect.Application.DTOs.Clients;

public record ClientResponse(
    Guid Id,
    string LegalName,
    string TaxId,
    string? TradeName,
    int ContactCount,
    int TitleCount
);
