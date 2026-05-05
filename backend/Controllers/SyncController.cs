namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/sync")]
[Authorize(Roles = "Admin,Worker,Master")]
public class SyncController : ControllerBase
{
    private readonly ISyncService _service;

    public SyncController(ISyncService service)
    {
        _service = service;
    }

    private Guid ResolveTenantId(Guid? tenantId) => TenantContextResolver.ResolveTenantOrThrow(User, tenantId);

    [HttpGet("health")]
    public async Task<IActionResult> Health([FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedTenantId = ResolveTenantId(tenantId);
            var config = await _service.GetExternalApiConfigAsync(resolvedTenantId, cancellationToken);

            return Ok(new
            {
                connected = config.Connected ?? false,
                checkedAt = config.CheckedAt,
                baseUrl = config.BaseUrl,
                docsUrl = config.DocsUrl,
                authentication = config.AuthenticationScheme,
                endpoints = new
                {
                    pendingTitles = $"GET /{config.PendingTitlesPath}",
                    occurrences = $"GET /{config.OccurrencesPath}"
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("pending-titles")]
    [Authorize(Roles = "Admin,Master")]
    public async Task<IActionResult> SyncPendingTitles([FromQuery] Guid? tenantId = null)
    {
        try
        {
            var count = await _service.SyncPendingTitlesAsync(ResolveTenantId(tenantId));
            return Ok(new { message = $"{count} títulos sincronizados.", count });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = $"API externa indisponível: {ex.Message}" });
        }
    }

    [HttpPost("occurrences")]
    [Authorize(Roles = "Admin,Master")]
    public async Task<IActionResult> SyncOccurrences([FromQuery] Guid? tenantId = null)
    {
        try
        {
            var count = await _service.SyncOccurrencesAsync(ResolveTenantId(tenantId));
            return Ok(new { message = $"{count} ocorrências processadas.", count });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = $"API externa indisponível: {ex.Message}" });
        }
    }
}
