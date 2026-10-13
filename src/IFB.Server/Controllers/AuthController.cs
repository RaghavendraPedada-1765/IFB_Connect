using IFB.Server.Auth;
using IFB.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authentication;

namespace IFB.Server.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService auth, ILogger<AuthController> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _auth.LoginAsync(dto, ip);

        if (result == null)
        {
            // Generic message — don't reveal whether user exists
            return Unauthorized(new { error = "Invalid credentials." });
        }

        _logger.LogInformation("Login success for {User} from {IP}", dto.Username, ip);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto dto)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _auth.RefreshAsync(dto.RefreshToken, ip);
        return result == null ? Unauthorized() : Ok(result);
    }

    [HttpPost("windows")]
    public async Task<IActionResult> WindowsLogin()
    {
        if (!HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue("Auth:WindowsIntegrated:Enabled", false)) return NotFound();
        var authentication = await HttpContext.AuthenticateAsync(NegotiateDefaults.AuthenticationScheme);
        if (!authentication.Succeeded) return Challenge(NegotiateDefaults.AuthenticationScheme);
        var result = await _auth.WindowsLoginAsync(authentication.Principal!.Identity!.Name!);
        return result == null ? Unauthorized() : Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto dto)
    {
        await _auth.RevokeRefreshTokenAsync(dto.RefreshToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized();
        var user = await _auth.GetCurrentUserAsync(userId);
        return user == null ? NotFound() : Ok(user);
    }
}
