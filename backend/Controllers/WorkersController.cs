namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Api.Security;
using SmartCollect.Application.DTOs.Users;
using SmartCollect.Application.Interfaces;

[ApiController]
[Route("api/workers")]
[Authorize(Roles = "Admin")]
public class WorkersController : ControllerBase
{
    private readonly IWorkerService _service;

    public WorkersController(IWorkerService service) => _service = service;

    private Guid GetTenantId() => TenantContextResolver.GetTenantIdOrThrow(User);

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _service.ListAsync(GetTenantId()));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Nome do worker é obrigatório." });

        var result = await _service.UpdateAsync(GetTenantId(), id, request);
        return result is null ? NotFound() : Ok(result);
    }
}
