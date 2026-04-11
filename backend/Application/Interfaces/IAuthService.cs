namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Auth;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<AuthResponse?> RegisterAsync(Guid tenantId, RegisterRequest request);
    Task<AuthResponse?> RegisterMasterAsync(RegisterRequest request);
    Task<AuthResponse?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
}
