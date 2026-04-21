namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Contacts;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/clients/{clientId:guid}/contacts")]
[Authorize(Roles = "Admin,Worker,Master")]
public class ContactsController : ControllerBase
{
    private readonly IContactService _contactService;
    public ContactsController(IContactService contactService) => _contactService = contactService;

    private Guid ResolveTenantId(Guid? tenantId) => TenantContextResolver.ResolveTenantOrThrow(User, tenantId);

    [HttpGet]
    public async Task<IActionResult> List(Guid clientId, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            return Ok(await _contactService.ListByClientAsync(ResolveTenantId(tenantId), clientId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> Create(Guid clientId, [FromBody] CreateContactRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            return Created("", await _contactService.CreateAsync(ResolveTenantId(tenantId), clientId, request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{contactId:guid}")]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> Update(Guid clientId, Guid contactId, [FromBody] UpdateContactRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            var result = await _contactService.UpdateAsync(ResolveTenantId(tenantId), clientId, contactId, request);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{contactId:guid}")]
    [Authorize(Roles = "Admin,Worker,Master")]
    public async Task<IActionResult> Delete(Guid clientId, Guid contactId, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            var removed = await _contactService.DeleteAsync(ResolveTenantId(tenantId), clientId, contactId);
            return removed ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
