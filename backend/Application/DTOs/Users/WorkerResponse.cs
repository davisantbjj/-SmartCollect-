namespace SmartCollect.Application.DTOs.Users;

public record WorkerResponse(
    Guid Id,
    string Name,
    string Email,
    bool Active,
    DateTime CreatedAt,
    DateTime? LastLogin
);
