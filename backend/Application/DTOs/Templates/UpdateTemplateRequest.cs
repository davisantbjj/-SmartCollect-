namespace SmartCollect.Application.DTOs.Templates;

public record UpdateTemplateRequest(
    string Name,
    string Channel,
    string? Subject,
    string Body,
    string Type,
    bool Active
);
