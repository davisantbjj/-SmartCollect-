namespace SmartCollect.Application.DTOs.Import;

public record ImportResultResponse(
    Guid ImportId,
    string FileName,
    int TotalRows,
    int SuccessRows,
    int ErrorRows,
    string Status
);
