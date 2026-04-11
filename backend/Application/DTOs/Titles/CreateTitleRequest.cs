namespace SmartCollect.Application.DTOs.Titles;

public record CreateTitleRequest(
    Guid ClientId,
    string UniqueCode,
    decimal Amount,
    DateTime DueDate,
    DateTime IssueDate,
    string? BoletoUrl
);
