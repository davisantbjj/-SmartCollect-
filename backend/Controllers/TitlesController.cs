namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Titles;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/titles")]
[Authorize(Roles = "Admin,Worker")]
public class TitlesController : ControllerBase
{
    private readonly ITitleService _titleService;
    public TitlesController(ITitleService titleService) => _titleService = titleService;

    private Guid GetTenantId() => TenantContextResolver.GetTenantIdOrThrow(User);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TitleFilterRequest filter)
        => Ok(await _titleService.ListAsync(GetTenantId(), filter));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _titleService.GetByIdAsync(GetTenantId(), id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid id)
        => Ok(await _titleService.GetHistoryAsync(GetTenantId(), id));

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTitleStatusRequest request)
    {
        try
        {
            var result = await _titleService.UpdateStatusAsync(GetTenantId(), id, request.Status);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Create or update a title by UniqueCode (RN01 — upsert). Admin and Worker allowed.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Create([FromBody] CreateTitleRequest request)
    {
        try
        {
            return Ok(await _titleService.CreateAsync(GetTenantId(), request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Manually trigger collection dispatches for a title. Admin and Worker allowed.</summary>
    [HttpPost("{id:guid}/collect")]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> SendCollection(Guid id, [FromBody] SendCollectionRequest? request = null)
    {
        try
        {
            var success = await _titleService.SendCollectionAsync(GetTenantId(), id, request);
            return success
                ? Ok(new { message = request?.UseQuickTemplate == true ? "Cobrança rápida enviada." : "Disparos de cobrança agendados." })
                : BadRequest(new { message = "Não foi possível cobrar este título. Verifique o status e os contatos." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
