namespace SmartCollect.Application.DTOs.Auth;

public record AuthResponse(
    string Token,
    string UserName,
    string Email,
    string Role,
    Guid TenantId,
    DateTime ExpiresAt,
    string? PhotoUrl
);
