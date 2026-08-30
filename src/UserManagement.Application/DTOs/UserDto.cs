using System.ComponentModel.DataAnnotations;

namespace UserManagement.Application.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class CreateUserDto
{
    [Required, StringLength(64, MinimumLength = 3)]
    [RegularExpression("^[a-zA-Z0-9._-]+$", ErrorMessage = "Username may only contain letters, digits, dot, underscore and hyphen.")]
    public string UserName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [Phone, StringLength(32)] public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;
    public List<string> Roles { get; set; } = new();
}

public class UpdateUserDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [Phone, StringLength(32)] public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UpdateProfileDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(100)] public string? FirstName { get; set; }
    [StringLength(100)] public string? LastName { get; set; }
    [Phone, StringLength(32)] public string? PhoneNumber { get; set; }
}

public class AssignRolesDto
{
    [Required, MinLength(1, ErrorMessage = "At least one role must be assigned.")]
    public List<string> Roles { get; set; } = new();
}
