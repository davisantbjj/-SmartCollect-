namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/sync")]
[Authorize(Roles = "Admin,Worker")]
public class SyncController : ControllerBase
{
    private readonly ISyncService _service;

    public SyncController(ISyncService service)
    {
        _service = service;
    }

    private Guid GetTenantId() => TenantContextResolver.GetTenantIdOrThrow(User);

    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var config = await _service.GetExternalApiConfigAsync(tenantId, cancellationToken);

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

    [HttpPost("pending-titles")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SyncPendingTitles()
    {
        try
        {
            var count = await _service.SyncPendingTitlesAsync(GetTenantId());
            return Ok(new { message = $"{count} títulos sincronizados.", count });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = $"API externa indisponível: {ex.Message}" });
        }
    }

    [HttpPost("occurrences")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SyncOccurrences()
    {
        try
        {
            var count = await _service.SyncOccurrencesAsync(GetTenantId());
            return Ok(new { message = $"{count} ocorrências processadas.", count });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = $"API externa indisponível: {ex.Message}" });
        }
    }
}
