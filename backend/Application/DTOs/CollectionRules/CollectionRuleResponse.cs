namespace SmartCollect.Application.DTOs.CollectionRules;

public record CollectionRuleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool Active,
    List<TriggerDto> Triggers,
    bool IsDefault
);
