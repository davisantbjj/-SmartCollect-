namespace SmartCollect.Application.DTOs.Titles;

public record TitleFilterRequest(
    string? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20
);
