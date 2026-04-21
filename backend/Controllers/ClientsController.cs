namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Clients;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/clients")]
[Authorize(Roles = "Admin,Worker")]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    public ClientsController(IClientService clientService) => _clientService = clientService;

    private Guid GetTenantId() => TenantContextResolver.GetTenantIdOrThrow(User);

    private Guid GetUserId() => Guid.Parse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("Missing user claim."));

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _clientService.ListAsync(GetTenantId()));

    [HttpPost]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Create([FromBody] CreateClientRequest request)
    {
        try
        {
            return Created("", await _clientService.CreateAsync(GetTenantId(), GetUserId(), request));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPatch("{clientId:guid}/dispatch-preference")]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> UpdateDispatchPreference(Guid clientId, [FromBody] UpdateClientDispatchPreferenceRequest request)
    {
        try
        {
            var updated = await _clientService.UpdateDispatchPreferenceAsync(GetTenantId(), clientId, request);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
