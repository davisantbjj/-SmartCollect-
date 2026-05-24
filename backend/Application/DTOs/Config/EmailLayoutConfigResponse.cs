namespace SmartCollect.Application.DTOs.Config;

public record EmailLayoutConfigResponse(
    bool Enabled,
    string? LogoUrl,
    string? HeroUrl,
    string? FooterMessage,
    string? InstagramUrl,
    string? LinkedInUrl,
    string? WhatsAppUrl,
    string? TelegramUrl
);
