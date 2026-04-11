namespace SmartCollect.Application.DTOs.Config;

public record SmtpConfigResponse(
    string Host,
    int Port,
    string User,
    string SenderFrom,
    string SenderName
);
