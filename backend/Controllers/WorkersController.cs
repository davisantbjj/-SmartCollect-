namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using SmartCollect.Api.Security;
using SmartCollect.Application.DTOs.Users;
using SmartCollect.Application.Interfaces;

[ApiController]
[Route("api/workers")]
[Authorize(Roles = "Admin,Master")]
public class WorkersController : ControllerBase
{
    private readonly IWorkerService _service;

    public WorkersController(IWorkerService service) => _service = service;

    private Guid ResolveTenantId(Guid? tenantId) => TenantContextResolver.ResolveTenantOrThrow(User, tenantId);

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? tenantId = null)
    {
        try
        {
            return Ok(await _service.ListAsync(ResolveTenantId(tenantId)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkerRequest request, [FromQuery] Guid? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Nome do usuário é obrigatório." });
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "E-mail do usuário é obrigatório." });

        var userId = GetUserId();
        if (userId is null)
            return Unauthorized(new { message = "Usuário inválido." });

        try
        {
            var result = await _service.UpdateAsync(ResolveTenantId(tenantId), userId.Value, id, request);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            var deleted = await _service.DeleteAsync(ResolveTenantId(tenantId), id);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
