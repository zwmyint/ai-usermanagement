using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UserManagement.API.Common;
using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;

namespace UserManagement.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService) => _authService = authService;

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-registration")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var user = await _authService.RegisterAsync(request, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<UserDto>.Ok(user, "Registration successful."));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Login successful."));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-token")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _authService.RefreshAsync(request, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result, "Token refreshed."));
    }

    [HttpPost("revoke")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-token")]
    public async Task<ActionResult<ApiResponse>> Revoke([FromBody] RevokeRequest request, CancellationToken ct)
    {
        await _authService.RevokeAsync(request, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse.Ok("Token revoked."));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> Logout(CancellationToken ct)
    {
        var userId = GetUserId();
        await _authService.LogoutAsync(userId, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse.Ok("Logged out."));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-recovery")]
    public async Task<ActionResult<ApiResponse>> ForgotPassword([FromBody] ForgotPasswordDto dto, CancellationToken ct)
    {
        await _authService.ForgotPasswordAsync(dto, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse.Ok("If the email exists, a reset link has been sent."));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-recovery")]
    public async Task<ActionResult<ApiResponse>> ResetPassword([FromBody] ResetPasswordDto dto, CancellationToken ct)
    {
        await _authService.ResetPasswordAsync(dto, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse.Ok("Password has been reset."));
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : throw new UnauthorizedAccessException("Invalid user context.");
    }
}
