namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Templates;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/templates")]
[Authorize(Roles = "Admin,Worker,Master")]
public class TemplatesController : ControllerBase
{
    private readonly IMessageTemplateService _service;
    public TemplatesController(IMessageTemplateService service) => _service = service;

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

    [HttpPost]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> Create([FromBody] CreateTemplateRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            return Created("", await _service.CreateAsync(ResolveTenantId(tenantId), request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTemplateRequest request, [FromQuery] Guid? tenantId = null)
    {
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
