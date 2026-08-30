using UserManagement.Domain.Common;

namespace UserManagement.Domain.Entities;

public class Role : BaseEntity, IAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
