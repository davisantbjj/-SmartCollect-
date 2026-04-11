namespace SmartCollect.Application.DTOs.Titles;

public record TitleResponse(
    Guid Id,
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
