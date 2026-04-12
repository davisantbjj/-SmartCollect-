namespace SmartCollect.Application.DTOs.Config;

public record SmtpConfigRequest(
    string Host,
    int Port,
    string User,
    string? Password,
    string SenderFrom,
    string SenderName
);
