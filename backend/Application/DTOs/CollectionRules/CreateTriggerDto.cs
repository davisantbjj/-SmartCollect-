namespace SmartCollect.Application.DTOs.CollectionRules;

public record CreateTriggerDto(
    Guid TemplateId,
    string Channel,
    int DaysOffset,
    string Reference,
    int Order,
    bool Active
);
