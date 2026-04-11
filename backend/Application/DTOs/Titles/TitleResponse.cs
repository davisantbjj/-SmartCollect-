namespace SmartCollect.Application.DTOs.Titles;

public record TitleResponse(
    Guid Id,
    Guid ClientId,
    string ClientName,
    string ClientTaxId,
    string UniqueCode,
    decimal Amount,
    DateTime DueDate,
    DateTime IssueDate,
    string? BoletoUrl,
    string Status,
    List<string> Channels,
    string? LastAction,
    DateTime? LastActionAt,
    bool IsBoletoOverdue
);
