namespace SmartCollect.Application.DTOs.CollectionRules;

public record CreateCollectionRuleRequest(
    string Name,
    string? Description,
    bool Active,
    List<CreateTriggerDto> Triggers
);
