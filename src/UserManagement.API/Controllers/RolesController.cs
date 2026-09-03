using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserManagement.API.Common;
using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;

namespace UserManagement.API.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService) => _roleService = roleService;

    [HttpGet]
    [Authorize(Policy = "UsersRead")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleDto>>>> GetAll(CancellationToken ct)
    {
        var roles = await _roleService.GetAllAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<RoleDto>>.Ok(roles));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "UsersRead")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> GetById(Guid id, CancellationToken ct)
    {
        var role = await _roleService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<RoleDto>.Ok(role));
    }

    [HttpPost]
    [Authorize(Policy = "RolesManage")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Create([FromBody] CreateRoleDto dto, CancellationToken ct)
    {
        var role = await _roleService.CreateAsync(dto, HttpContext.ToAuditContext(), ct);
        return CreatedAtAction(nameof(GetById), new { id = role.Id }, ApiResponse<RoleDto>.Ok(role, "Role created."));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RolesManage")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Update(Guid id, [FromBody] UpdateRoleDto dto, CancellationToken ct)
    {
        var role = await _roleService.UpdateAsync(id, dto, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<RoleDto>.Ok(role, "Role updated."));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RolesManage")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken ct)
    {
        await _roleService.DeleteAsync(id, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse.Ok("Role deleted."));
    }

    [HttpGet("permissions")]
    [Authorize(Policy = "RolesManage")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PermissionDto>>>> GetAllPermissions(CancellationToken ct)
    {
        var permissions = await _roleService.GetAllPermissionsAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<PermissionDto>>.Ok(permissions));
    }

    [HttpPut("{id:guid}/permissions")]
    [Authorize(Policy = "RolesManage")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> UpdatePermissions(Guid id, [FromBody] UpdateRolePermissionsDto dto, CancellationToken ct)
    {
        var role = await _roleService.UpdatePermissionsAsync(id, dto, HttpContext.ToAuditContext(), ct);
        return Ok(ApiResponse<RoleDto>.Ok(role, "Role permissions updated."));
    }
}
