using System.Text.Json.Serialization;

namespace SmartCollect.Application.DTOs.Sync;

public record ExternalOccurrenceDto(
    [property: JsonPropertyName("cdTitulo")] int TitleCode,
    [property: JsonPropertyName("dsStatus")] string UpdatedStatus,
    [property: JsonPropertyName("dtOcorrencia")] string? OccurrenceDate,
    [property: JsonPropertyName("data")] string? ReferenceDate
);
