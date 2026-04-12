namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCollect.Application.DTOs.Config;
using SmartCollect.Application.Interfaces;
using SmartCollect.Api.Security;

[ApiController]
[Route("api/config")]
[Authorize(Roles = "Admin")]
public class ConfigController : ControllerBase
{
    private readonly ISmtpConfigService _smtpService;
    private readonly IWhatsAppConfigService _whatsAppService;
    private readonly ISyncService _syncService;

    public ConfigController(
        ISmtpConfigService smtpService,
        IWhatsAppConfigService whatsAppService,
        ISyncService syncService)
    {
        _smtpService = smtpService;
        _whatsAppService = whatsAppService;
        _syncService = syncService;
    }

    private Guid GetTenantId() => TenantContextResolver.GetTenantIdOrThrow(User);

    [HttpGet("smtp")]
    public async Task<IActionResult> GetSmtp()
    {
        var result = await _smtpService.GetAsync(GetTenantId());
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("smtp")]
    public async Task<IActionResult> SaveSmtp([FromBody] SmtpConfigRequest request)
    {
        try
        {
            await _smtpService.SaveAsync(GetTenantId(), request);
            return Ok(new { message = "Configuração SMTP salva com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("smtp/test")]
    public async Task<IActionResult> TestSmtp()
    {
        var success = await _smtpService.TestAsync(GetTenantId());
        return success
            ? Ok(new { message = "Conexão SMTP testada com sucesso." })
            : BadRequest(new { message = "Falha no teste SMTP. Verifique host, porta e credenciais." });
    }

    [HttpGet("whatsapp")]
    public async Task<IActionResult> GetWhatsApp()
    {
        var result = await _whatsAppService.GetAsync(GetTenantId());
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("whatsapp")]
    public async Task<IActionResult> SaveWhatsApp([FromBody] WhatsAppConfigRequest request)
    {
        try
        {
            await _whatsAppService.SaveAsync(GetTenantId(), request);
            return Ok(new { message = "Configuração WhatsApp salva com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("whatsapp/test")]
    public async Task<IActionResult> TestWhatsApp()
    {
        var success = await _whatsAppService.TestAsync(GetTenantId());
        return success
            ? Ok(new { message = "Configuração WhatsApp validada com sucesso." })
            : BadRequest(new { message = "Falha na validação WhatsApp. Verifique provedor, ID e token." });
    }

    [HttpGet("external-api")]
    public async Task<IActionResult> GetExternalApiConfig(CancellationToken cancellationToken)
    {
        var result = await _syncService.GetExternalApiConfigAsync(GetTenantId(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("external-api")]
    public async Task<IActionResult> SaveExternalApiConfig([FromBody] ExternalApiConfigRequest request, CancellationToken cancellationToken)
    {
        await _syncService.SaveExternalApiConfigAsync(GetTenantId(), request, cancellationToken);
        return Ok(new { message = "Configuração da API externa salva com sucesso." });
    }

    [HttpPost("external-api/test")]
    public async Task<IActionResult> TestExternalApi(CancellationToken cancellationToken)
    {
        var connected = await _syncService.CheckExternalApiConnectionAsync(GetTenantId(), cancellationToken);
        return connected
            ? Ok(new { message = "Conexão com API externa validada com sucesso." })
            : StatusCode(502, new { message = "Não foi possível conectar na API externa com a configuração atual." });
    }
}
