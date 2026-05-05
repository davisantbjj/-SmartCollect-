namespace SmartCollect.Application.DTOs.Users;

public record UpdateWorkerRequest(
    string Name,
    string Email,
    bool Active,
    string? Password
);
