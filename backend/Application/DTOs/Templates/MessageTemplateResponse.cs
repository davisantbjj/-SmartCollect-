namespace SmartCollect.Application.DTOs.Templates;

public record MessageTemplateResponse(
    Guid Id,
    string Name,
    string Channel,
    string? Subject,
    string Body,
    string Type,
    bool Active
);
