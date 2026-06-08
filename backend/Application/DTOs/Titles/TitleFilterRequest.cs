namespace SmartCollect.Application.DTOs.Titles;

public class TitleFilterRequest
{
    public string? Status { get; set; }
    public string? Search { get; set; }
    public DateTime? DueDateStart { get; set; }
    public DateTime? DueDateEnd { get; set; }
    public string? OrderBy { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
