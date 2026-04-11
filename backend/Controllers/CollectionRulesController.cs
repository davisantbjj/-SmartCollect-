namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateCollectionRuleRequest request)
        => Created("", await _service.CreateAsync(GetTenantId(), request));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateCollectionRuleRequest request)
    {
        var result = await _service.UpdateAsync(GetTenantId(), id, request);
        return result is null ? NotFound() : Ok(result);
    }
}
