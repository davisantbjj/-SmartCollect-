namespace SmartCollect.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using SmartCollect.Api.Security;
using SmartCollect.Application.DTOs.Auth;
using SmartCollect.Application.Interfaces;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    public AuthController(IAuthService authService) => _authService = authService;

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId)) return userId;
        return null;
    }

    /// <summary>Login — works for all roles (Master, Admin, Worker)</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
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

    /// <summary>Register a new tenant user (Admin/Worker) inside the selected tenant.</summary>
    [HttpPost("register")]
    [Authorize(Roles = "Admin,Master")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, [FromQuery] Guid? tenantId = null)
    {
        try
        {
            var resolvedTenantId = TenantContextResolver.ResolveTenantOrThrow(User, tenantId);
            var result = await _authService.RegisterAsync(resolvedTenantId, request);
            return Created("", result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Register a new Master user (Master only — self-service for Atos Capital team)</summary>
    [HttpPost("register-master")]
    [Authorize(Roles = "Master")]
    public async Task<IActionResult> RegisterMaster([FromBody] RegisterRequest request)
    {
        try
        {
            var result = await _authService.RegisterMasterAsync(request);
            return Created("", result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
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

