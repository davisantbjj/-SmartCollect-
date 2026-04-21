namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.CollectionRules;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/collection-rules")]
[Authorize(Roles = "Admin,Worker,Master")]
public class CollectionRulesController : ControllerBase
{
    private readonly ICollectionRuleService _service;
    public CollectionRulesController(ICollectionRuleService service) => _service = service;

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
    public async Task<IActionResult> Create([FromBody] CreateCollectionRuleRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            return Created("", await _service.CreateAsync(ResolveTenantId(tenantId), request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { message = "Não foi possível salvar a régua. Verifique templates e gatilhos vinculados." });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateCollectionRuleRequest request, [FromQuery] Guid? tenantId = null)
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
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "A régua foi alterada por outro processo. Recarregue a tela e tente novamente." });
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { message = "Não foi possível salvar a régua. Verifique templates e gatilhos vinculados." });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            var removed = await _service.DeleteAsync(ResolveTenantId(tenantId), id);
            return removed ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { message = "Não foi possível excluir a régua." });
        }
    }
}
