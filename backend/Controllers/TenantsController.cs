namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Tenants;
using SmartCollect.Application.Interfaces;

[ApiController]
[Route("api/tenants")]
[Authorize(Roles = "Master")]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _service;
    public TenantsController(ITenantService service) => _service = service;

    /// <summary>List all tenants (Master only)</summary>
    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _service.ListAllAsync());

    /// <summary>Get a single tenant by ID (Master only)</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Create a new tenant with its first Admin user (Master only)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request)
    {
        try
        {
            var result = await _service.CreateAsync(request);
            return Created($"api/tenants/{result.Id}", result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Update tenant data and optionally admin login data (Master only)</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest request)
    {
        try
        {
            var result = await _service.UpdateAsync(id, request);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Enable or disable tenant access (Master only)</summary>
    [HttpPatch("{id:guid}/access")]
    public async Task<IActionResult> UpdateAccess(Guid id, [FromBody] UpdateTenantAccessRequest request)
    {
        var result = await _service.UpdateAccessAsync(id, request);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Enable or disable tenant access (Master only)</summary>
    [HttpPut("{id:guid}/access/{active}")]
    public async Task<IActionResult> UpdateAccessByRoute(Guid id, string active)
    {
        if (!bool.TryParse(active, out var activeValue))
            return BadRequest(new { message = "Valor inválido para acesso. Use true ou false." });

        var result = await _service.UpdateAccessAsync(id, new UpdateTenantAccessRequest(activeValue));
        return result is null ? NotFound() : Ok(result);
    }
}
