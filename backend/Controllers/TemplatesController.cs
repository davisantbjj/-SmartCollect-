namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Templates;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/templates")]
[Authorize(Roles = "Admin,Worker")]
public class TemplatesController : ControllerBase
{
    private readonly IMessageTemplateService _service;
    public TemplatesController(IMessageTemplateService service) => _service = service;

    private Guid GetTenantId() => TenantContextResolver.GetTenantIdOrThrow(User);

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _service.ListAsync(GetTenantId()));

    [HttpPost]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Create([FromBody] CreateTemplateRequest request)
    {
        try
        {
            return Created("", await _service.CreateAsync(GetTenantId(), request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTemplateRequest request)
    {
        try
        {
            var result = await _service.UpdateAsync(GetTenantId(), id, request);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
