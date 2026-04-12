namespace SmartCollect.Application.DTOs.Config;

public record WhatsAppConfigResponse(
    string Provider,
    string NumberId,
    string? ApiBaseUrl,
    bool HasAccessToken,
    string WebhookUrl
);
