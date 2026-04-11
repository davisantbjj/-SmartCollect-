namespace SmartCollect.Application.DTOs.Import;

public record FileImportResponse(
    Guid Id,
    string FileName,
    string Type,
    string Status,
    int TotalRows,
    int SuccessRows,
    int ErrorRows,
    DateTime CreatedAt
);
