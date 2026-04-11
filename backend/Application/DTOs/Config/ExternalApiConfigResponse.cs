namespace SmartCollect.Application.DTOs.Config;

public record ExternalApiConfigResponse(
    string BaseUrl,
    string? DocsUrl,
    string PendingTitlesPath,
    string OccurrencesPath,
    string AuthenticationScheme,
    bool HasToken,
    string? CheckedAt,
    bool? Connected);
