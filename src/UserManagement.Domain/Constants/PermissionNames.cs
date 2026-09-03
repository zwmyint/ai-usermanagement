namespace UserManagement.Domain.Constants;

/// <summary>
/// The fixed set of permission actions the application understands. This list is tied to the
/// code that enforces it (controllers/services check for these exact names) and requires a code
/// change to extend. What's dynamic is which <see cref="Role"/>s grant which of these permissions -
/// that mapping is stored in the database and editable at runtime via the Roles admin screen/API,
/// so adding a brand-new role with real access never requires a code change.
/// </summary>
public static class PermissionNames
{
    public const string UsersRead = "Users.Read";
    public const string UsersWrite = "Users.Write";
    public const string UsersDelete = "Users.Delete";
    public const string RolesManage = "Roles.Manage";
    public const string AuditRead = "Audit.Read";
    public const string DashboardAdminView = "Dashboard.AdminView";

    public static readonly IReadOnlyList<string> All = new[]
    {
        UsersRead, UsersWrite, UsersDelete, RolesManage, AuditRead, DashboardAdminView
    };
}
