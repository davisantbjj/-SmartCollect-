using System.Text.Json.Serialization;

namespace SmartCollect.Application.DTOs.Sync;

public record ExternalTitleDto(
    [property: JsonPropertyName("nmCliente")] string ClientName,
    [property: JsonPropertyName("nrCNPJ")] string TaxId,
    [property: JsonPropertyName("dsEmail")] string? Email,
    [property: JsonPropertyName("nrTelefone")] string? Phone,
    [property: JsonPropertyName("cdTitulo")] int TitleCode,
    [property: JsonPropertyName("vlTitulo")] decimal Amount,
    [property: JsonPropertyName("dtVencimento")] DateTime DueDate,
    [property: JsonPropertyName("dtEmissao")] DateTime IssueDate,
    [property: JsonPropertyName("dsLinkBoleto")] string? BoletoUrl,
    [property: JsonPropertyName("dsStatus")] string Status
);
