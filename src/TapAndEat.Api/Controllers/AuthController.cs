using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TapAndEat.Api.DTOs;
using TapAndEat.Api.Services;

namespace TapAndEat.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Task 1.2 — registration.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        try
        {
            var result = await _authService.RegisterAsync(request);
            return CreatedAtAction(nameof(Me), null, result);
        }
        catch (AuthException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Task 1.2 — login. Valid credentials return an auth token; invalid ones return a clear error.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        try
        {
            return Ok(await _authService.LoginAsync(request));
        }
        catch (AuthException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>Task 1.5 — request a reset link/OTP.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(ForgotPasswordRequest request)
    {
        return Ok(await _authService.ForgotPasswordAsync(request));
    }

    /// <summary>Task 1.5 — set a new password using the reset token.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        try
        {
            await _authService.ResetPasswordAsync(request);
            return Ok(new { message = "Password has been reset. Please log in with your new password." });
        }
        catch (AuthException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Task 1.3 — confirms token-based auth is working and returns the caller's profile.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserSummary>> Me()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _authService.GetCurrentUserAsync(userId);
        return user is null ? NotFound() : Ok(user);
    }
}
