namespace SmartCollect.Application.DTOs.Titles;

public record TitleFilterRequest(
    string? Status = null,
    string? Search = null,
    DateTime? DueDateStart = null,
    DateTime? DueDateEnd = null,
    string? OrderBy = null,
    int Page = 1,
    int PageSize = 20
);
