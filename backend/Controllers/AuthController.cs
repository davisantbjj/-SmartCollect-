namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using SmartCollect.Application.DTOs.Auth;
using SmartCollect.Application.Interfaces;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    public AuthController(IAuthService authService) => _authService = authService;

    private Guid? GetTenantId()
    {
        var tenantClaim = User.FindFirst("TenantId")?.Value;
        if (Guid.TryParse(tenantClaim, out var tenantId)) return tenantId;
        return null;
    }

    private bool IsMaster() => User.IsInRole("Master");

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId)) return userId;
        return null;
    }

    /// <summary>Login — works for all roles (Master, Admin, Worker)</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            if (result is null)
                return Unauthorized(new { message = "E-mail ou senha incorretos." });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>Register a new Worker inside the current tenant (Admin only)</summary>
    [HttpPost("register")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var tenantId = GetTenantId();
        if (tenantId is null)
            return Unauthorized(new { message = "Invalid tenant claim." });

        var result = await _authService.RegisterAsync(tenantId.Value, request);
        if (result is null)
            return Conflict(new { message = "E-mail já cadastrado neste tenant." });

        return Created("", result);
    }

    /// <summary>Register a new Master user (Master only — self-service for Atos Capital team)</summary>
    [HttpPost("register-master")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> RegisterMaster([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterMasterAsync(request);
        if (result is null)
            return Conflict(new { message = "E-mail já cadastrado como Master." });

        return Created("", result);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = GetUserId();
        if (userId is null)
            return Unauthorized(new { message = "Usuário inválido." });

        try
        {
            var result = await _authService.UpdateProfileAsync(userId.Value, request);
            return result is null
                ? NotFound(new { message = "Usuário não encontrado." })
                : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

