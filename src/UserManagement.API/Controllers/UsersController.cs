using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.API.Common;
using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;

namespace UserManagement.API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService) => _userService = userService;

    [HttpGet]
    [Authorize(Policy = "UsersRead")]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUsers([FromQuery] UserQueryRequest request, CancellationToken ct)
    {
        var result = await _userService.GetUsersAsync(request, ct);
        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "UsersRead")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetById(Guid id, CancellationToken ct)
    {
        var user = await _userService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<UserDto>.Ok(user));
    }

    [HttpPost]
    [Authorize(Policy = "UsersWrite")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var user = await _userService.CreateAsync(dto, HttpContext.ToAuditContext(), ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ApiResponse<UserDto>.Ok(user, "User created."));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "UsersWrite")]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(Guid id, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        var user = await _userService.UpdateAsync(id, dto, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<UserDto>.Ok(user, "User updated."));
    }

    [HttpPost("{id:guid}/profile-picture")]
    [Authorize(Policy = "UsersWrite")]
    public async Task<ActionResult<ApiResponse<UserDto>>> UploadProfilePicture(
        Guid id, [FromForm] IFormFile profilePicture, CancellationToken ct)
    {
        if (profilePicture.Length == 0)
            return BadRequest(ApiResponse.Fail("A profile picture is required.", traceId: HttpContext.TraceIdentifier));

        await using var image = profilePicture.OpenReadStream();
        var user = await _userService.UpdateProfilePictureAsync(
            id, image, profilePicture.FileName, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<UserDto>.Ok(user, "Profile picture updated."));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "UsersDelete")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken ct)
    {
        await _userService.DeleteAsync(id, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse.Ok("User deleted."));
    }

    [HttpPatch("{id:guid}/active")]
    [Authorize(Policy = "UsersWrite")]
    public async Task<ActionResult<ApiResponse<UserDto>>> SetActive(Guid id, [FromBody] SetActiveRequest request, CancellationToken ct)
    {
        var user = await _userService.SetActiveAsync(id, request.IsActive, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<UserDto>.Ok(user, user.IsActive ? "User activated." : "User deactivated."));
    }

    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = "UsersWrite")]
    public async Task<ActionResult<ApiResponse<UserDto>>> AssignRoles(Guid id, [FromBody] AssignRolesDto dto, CancellationToken ct)
    {
        var user = await _userService.AssignRolesAsync(id, dto, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<UserDto>.Ok(user, "Roles updated."));
    }
}

public class SetActiveRequest
{
    public bool IsActive { get; set; }
}
