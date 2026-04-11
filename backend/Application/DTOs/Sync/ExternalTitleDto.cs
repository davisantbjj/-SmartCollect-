using System.Text.Json.Serialization;

namespace SmartCollect.Application.DTOs.Sync;

public record ExternalTitleDto(
    [property: JsonPropertyName("nome_cliente")] string ClientName,
    [property: JsonPropertyName("cnpj")] string TaxId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("telefone")] string Phone,
    [property: JsonPropertyName("codigo_unico")] string UniqueCode,
    [property: JsonPropertyName("valor")] decimal Amount,
    [property: JsonPropertyName("data_vencimento")] DateTime DueDate,
    [property: JsonPropertyName("data_emissao")] DateTime IssueDate,
    [property: JsonPropertyName("link_boleto")] string? BoletoUrl,
    [property: JsonPropertyName("status")] string Status
);
