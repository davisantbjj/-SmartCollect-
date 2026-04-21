namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.CollectionRules;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/collection-rules")]
[Authorize(Roles = "Admin,Worker")]
public class CollectionRulesController : ControllerBase
{
    private readonly ICollectionRuleService _service;
    public CollectionRulesController(ICollectionRuleService service) => _service = service;

    private Guid GetTenantId() => TenantContextResolver.GetTenantIdOrThrow(User);

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await _service.ListAsync(GetTenantId()));

    [HttpPost]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Create([FromBody] CreateCollectionRuleRequest request)
    {
        try
        {
            return Created("", await _service.CreateAsync(GetTenantId(), request));
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
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateCollectionRuleRequest request)
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
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var removed = await _service.DeleteAsync(GetTenantId(), id);
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
