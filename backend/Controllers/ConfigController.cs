namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/config")]
[Authorize(Roles = "Admin,Master")]
public class ConfigController : ControllerBase
{
    private readonly ISmtpConfigService _smtpService;
    private readonly IWhatsAppConfigService _whatsAppService;
    private readonly ISyncService _syncService;
    private readonly IDispatchWindowConfigService _dispatchWindowService;
    private readonly IEmailLayoutConfigService _emailLayoutService;

    public ConfigController(
        ISmtpConfigService smtpService,
        IWhatsAppConfigService whatsAppService,
        ISyncService syncService,
        IDispatchWindowConfigService dispatchWindowService,
        IEmailLayoutConfigService emailLayoutService)
    {
        _smtpService = smtpService;
        _whatsAppService = whatsAppService;
        _syncService = syncService;
        _dispatchWindowService = dispatchWindowService;
        _emailLayoutService = emailLayoutService;
    }

    private Guid ResolveTenantId(Guid? tenantId) => TenantContextResolver.ResolveTenantOrThrow(User, tenantId);

    [HttpGet("smtp")]
    public async Task<IActionResult> GetSmtp([FromQuery] Guid? tenantId = null)
    {
        try
        {
            var result = await _smtpService.GetAsync(ResolveTenantId(tenantId));
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("smtp")]
    public async Task<IActionResult> SaveSmtp([FromBody] SmtpConfigRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            await _smtpService.SaveAsync(ResolveTenantId(tenantId), request);
            return Ok(new { message = "Configuração SMTP salva com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("smtp/test")]
    public async Task<IActionResult> TestSmtp([FromQuery] Guid? tenantId = null)
    {
        try
        {
            var success = await _smtpService.TestAsync(ResolveTenantId(tenantId));
            return success
                ? Ok(new { message = "Conexão SMTP testada com sucesso." })
                : BadRequest(new { message = "Falha no teste SMTP. Verifique host, porta e credenciais." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("whatsapp")]
    public async Task<IActionResult> GetWhatsApp([FromQuery] Guid? tenantId = null)
    {
        try
        {
            var result = await _whatsAppService.GetAsync(ResolveTenantId(tenantId));
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("whatsapp")]
    public async Task<IActionResult> SaveWhatsApp([FromBody] WhatsAppConfigRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            await _whatsAppService.SaveAsync(ResolveTenantId(tenantId), request);
            return Ok(new { message = "Configuração WhatsApp salva com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("whatsapp/test")]
    public async Task<IActionResult> TestWhatsApp([FromQuery] Guid? tenantId = null)
    {
        try
        {
            var success = await _whatsAppService.TestAsync(ResolveTenantId(tenantId));
            return success
                ? Ok(new { message = "Configuração WhatsApp validada com sucesso." })
                : BadRequest(new { message = "Falha na validação WhatsApp. Verifique provedor, ID e token." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("external-api")]
    public async Task<IActionResult> GetExternalApiConfig([FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _syncService.GetExternalApiConfigAsync(ResolveTenantId(tenantId), cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("external-api")]
    public async Task<IActionResult> SaveExternalApiConfig([FromBody] ExternalApiConfigRequest request, [FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _syncService.SaveExternalApiConfigAsync(ResolveTenantId(tenantId), request, cancellationToken);
            return Ok(new { message = "Configuração da API externa salva com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("external-api/test")]
    public async Task<IActionResult> TestExternalApi([FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var connected = await _syncService.CheckExternalApiConnectionAsync(ResolveTenantId(tenantId), cancellationToken);
            return connected
                ? Ok(new { message = "Conexão com API externa validada com sucesso." })
                : StatusCode(502, new { message = "Não foi possível conectar na API externa com a configuração atual." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("dispatch-window")]
    public async Task<IActionResult> GetDispatchWindow([FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _dispatchWindowService.GetAsync(ResolveTenantId(tenantId), cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("dispatch-window")]
    public async Task<IActionResult> SaveDispatchWindow([FromBody] DispatchWindowConfigRequest request, [FromQuery] Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dispatchWindowService.SaveAsync(ResolveTenantId(tenantId), request, cancellationToken);
            return Ok(new { message = "Janela de envio salva com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("email-layout")]
    public async Task<IActionResult> GetEmailLayout([FromQuery] Guid? tenantId = null)
    {
        try
        {
            var result = await _emailLayoutService.GetAsync(ResolveTenantId(tenantId));
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("email-layout")]
    public async Task<IActionResult> SaveEmailLayout([FromBody] EmailLayoutConfigRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            await _emailLayoutService.SaveAsync(ResolveTenantId(tenantId), request);
            return Ok(new { message = "Layout de e-mail salvo com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
