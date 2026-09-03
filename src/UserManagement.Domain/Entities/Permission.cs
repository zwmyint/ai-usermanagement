using UserManagement.Domain.Common;

namespace UserManagement.Domain.Entities;

/// <summary>
/// A single permission action (e.g. "Users.Write"). The set of permissions is fixed/seeded from
/// <see cref="Constants.PermissionNames"/>; only the Role &lt;-&gt; Permission assignment is editable.
/// </summary>
public class Permission : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
