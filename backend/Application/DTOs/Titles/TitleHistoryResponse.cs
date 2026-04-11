namespace SmartCollect.Application.DTOs.Titles;

public record TitleHistoryResponse(
    Guid Id,
    DateTime Timestamp,
    string Action,
    string Description
);
