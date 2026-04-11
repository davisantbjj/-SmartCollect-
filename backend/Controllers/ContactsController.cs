namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Contacts;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/clients/{clientId:guid}/contacts")]
[Authorize(Roles = "Admin,Worker")]
public class ContactsController : ControllerBase
{
    private readonly IContactService _contactService;
    public ContactsController(IContactService contactService) => _contactService = contactService;

    private Guid GetTenantId() => TenantContextResolver.GetTenantIdOrThrow(User);

    [HttpGet]
    public async Task<IActionResult> List(Guid clientId)
        => Ok(await _contactService.ListByClientAsync(GetTenantId(), clientId));

    [HttpPost]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Create(Guid clientId, [FromBody] CreateContactRequest request)
    {
        try
        {
            return Created("", await _contactService.CreateAsync(GetTenantId(), clientId, request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{contactId:guid}")]
    [Authorize(Roles = "Admin,Worker")]
    public async Task<IActionResult> Update(Guid clientId, Guid contactId, [FromBody] UpdateContactRequest request)
    {
        var result = await _contactService.UpdateAsync(GetTenantId(), clientId, contactId, request);
        return result is null ? NotFound() : Ok(result);
    }
}
