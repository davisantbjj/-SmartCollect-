namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Import;
using Microsoft.AspNetCore.Http;

public interface IFileImportService
{
    Task<ImportResultResponse> UploadAsync(Guid tenantId, IFormFile file);
}
