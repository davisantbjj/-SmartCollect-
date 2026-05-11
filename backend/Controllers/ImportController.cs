namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/import")]
[Authorize(Roles = "Admin,Worker,Master")]
public class ImportController : ControllerBase
{
    private readonly IFileImportService _service;
    public ImportController(IFileImportService service) => _service = service;

    private Guid ResolveTenantId(Guid? tenantId) => TenantContextResolver.ResolveTenantOrThrow(User, tenantId);

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] Guid? tenantId = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Nenhum arquivo enviado." });

        try
        {
            var result = await _service.UploadAsync(ResolveTenantId(tenantId), file);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
