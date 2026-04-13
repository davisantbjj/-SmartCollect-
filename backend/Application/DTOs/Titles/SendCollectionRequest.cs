namespace SmartCollect.Application.DTOs.Titles;

public record SendCollectionRequest(
    bool UseQuickTemplate,
    string? Channel,
    string? Subject,
    string? Body,
    List<Guid>? ContactIds = null
);