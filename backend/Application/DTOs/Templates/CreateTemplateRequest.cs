namespace SmartCollect.Application.DTOs.Templates;

public record CreateTemplateRequest(
    string Name,
    string Channel,
    string? Subject,
    string Body,
    string Type
);
