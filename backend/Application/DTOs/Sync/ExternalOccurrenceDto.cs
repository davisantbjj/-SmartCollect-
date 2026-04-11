using System.Text.Json.Serialization;

namespace SmartCollect.Application.DTOs.Sync;

public record ExternalOccurrenceDto(
    [property: JsonPropertyName("codigo_unico")] string UniqueCode,
    [property: JsonPropertyName("status_atualizado")] string UpdatedStatus
);
