namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        try
        {
            var result = await _service.UpdateAsync(ResolveTenantId(tenantId), id, request);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
