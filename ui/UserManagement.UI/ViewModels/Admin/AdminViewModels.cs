using System.ComponentModel.DataAnnotations;

namespace UserManagement.UI.ViewModels.Admin;

public class UserCreateViewModel
{
    [Required, StringLength(64, MinimumLength = 3)]
    public string UserName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [Phone] public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;
    public List<string> Roles { get; set; } = new();
}

public class UserEditViewModel
{
    public Guid Id { get; set; }

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [Phone] public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;
}

public class RoleEditViewModel
{
    public Guid Id { get; set; }

    [Required, StringLength(64, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(256)] public string? Description { get; set; }
}

public class AssignRolesViewModel
{
    public Guid UserId { get; set; }
    public List<string> Roles { get; set; } = new();
}
