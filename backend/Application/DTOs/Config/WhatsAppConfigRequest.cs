namespace SmartCollect.Application.DTOs.Config;

public record WhatsAppConfigRequest(
    string Provider,
    string NumberId,
    string? AccessToken,
    string? ApiBaseUrl,
    bool ClearToken = false
);
