namespace SmartCollect.Application.DTOs.Users;

public record UpdateWorkerRequest(
    string Name,
    bool Active,
    string? Password
);
