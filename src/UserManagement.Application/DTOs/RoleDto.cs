using System.ComponentModel.DataAnnotations;

namespace UserManagement.Application.DTOs;

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public class PermissionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateRolePermissionsDto
{
    public List<string> Permissions { get; set; } = new();
}

public class CreateRoleDto
{
    [Required, StringLength(64, MinimumLength = 2)]
    [RegularExpression("^[a-zA-Z0-9 _-]+$", ErrorMessage = "Role name may only contain letters, digits, spaces, underscore and hyphen.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(256)] public string? Description { get; set; }
}

public class UpdateRoleDto
{
    [Required, StringLength(64, MinimumLength = 2)]
    [RegularExpression("^[a-zA-Z0-9 _-]+$", ErrorMessage = "Role name may only contain letters, digits, spaces, underscore and hyphen.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(256)] public string? Description { get; set; }
}
