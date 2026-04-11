namespace SmartCollect.Application.DTOs.Auth;

public record UpdateProfileRequest(
    string Name,
    string Email,
    string? CurrentPassword,
    string? NewPassword,
    string? PhotoUrl,
    bool RemovePhoto
);
