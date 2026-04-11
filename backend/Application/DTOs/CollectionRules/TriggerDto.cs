namespace SmartCollect.Application.DTOs.CollectionRules;

public record TriggerDto(
    Guid Id,
    Guid TemplateId,
    string Channel,
    int DaysOffset,
    string Reference,
    int Order,
    bool Active,
    string? TemplateName
);
