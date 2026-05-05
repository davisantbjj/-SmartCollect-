namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Clients;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/clients")]
[Authorize(Roles = "Admin,Worker,Master")]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    public ClientsController(IClientService clientService) => _clientService = clientService;

    private Guid ResolveTenantId(Guid? tenantId) => TenantContextResolver.ResolveTenantOrThrow(User, tenantId);

    private Guid GetUserId() => Guid.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("Missing user claim."));

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? tenantId = null)
    {
        try
        {
            return Ok(await _clientService.ListAsync(ResolveTenantId(tenantId)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> Create([FromBody] CreateClientRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            return Created("", await _clientService.CreateAsync(ResolveTenantId(tenantId), GetUserId(), request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{clientId:guid}/dispatch-preference")]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> UpdateDispatchPreference(Guid clientId, [FromBody] UpdateClientDispatchPreferenceRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            var updated = await _clientService.UpdateDispatchPreferenceAsync(ResolveTenantId(tenantId), clientId, request);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
