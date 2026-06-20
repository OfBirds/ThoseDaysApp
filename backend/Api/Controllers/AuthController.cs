using Api.DTOs;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// The local (break-glass) email/password login. Anonymous by design — the dual-auth
/// front door is CrimsonRaven (OIDC); this path issues the app's own signed JWT and is the
/// fallback used when the IdP is offline or unconfigured. See docs/auth-crimsonraven.md.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController(IAuthService authService, ITokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var user = await authService.RegisterAsync(request.Email, request.Password);
        if (user == null)
            return BadRequest(new { error = "Registration failed. " + AuthService.PasswordPolicyMessage });

        return Ok(new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Token = tokenService.CreateToken(user.Id, user.Email),
            NotifyReleases = user.NotifyReleases
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await authService.LoginAsync(request.Email, request.Password);
        if (user == null)
            return Unauthorized(new { error = "Invalid email or password" });

        return Ok(new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Token = tokenService.CreateToken(user.Id, user.Email),
            NotifyReleases = user.NotifyReleases
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var success = await authService.ResetPasswordAsync(request.Email, request.NewPassword);
        if (!success)
            return BadRequest(new { error = "Password reset failed. " + AuthService.PasswordPolicyMessage });

        return Ok(new { message = "Password reset successful" });
    }
}
