using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.API.Common;
using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;

namespace UserManagement.API.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly IUserService _userService;

    public ProfileController(IUserService userService) => _userService = userService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetProfile(CancellationToken ct)
    {
        var profile = await _userService.GetProfileAsync(GetUserId(), ct);
        return Ok(ApiResponse<UserDto>.Ok(profile));
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateProfile([FromBody] UpdateProfileDto dto, CancellationToken ct)
    {
        var profile = await _userService.UpdateProfileAsync(GetUserId(), dto, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<UserDto>.Ok(profile, "Profile updated."));
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse>> ChangePassword([FromBody] ChangePasswordDto dto, CancellationToken ct)
    {
        await _userService.ChangePasswordAsync(GetUserId(), dto, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse.Ok("Password changed."));
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : throw new UnauthorizedAccessException("Invalid user context.");
    }
}
