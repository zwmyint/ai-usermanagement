namespace UserManagement.UI.Security;

/// <summary>
/// Mirrors the API's fixed permission action names (see UserManagement.Domain.Constants.PermissionNames).
/// The UI has no project reference to the API/Domain, so - like the rest of ui/Contracts - these are
/// kept in sync manually. Only the *mapping* of roles to permissions is dynamic/DB-driven; these
/// action names themselves require a matching code change on both sides.
/// </summary>
public static class PermissionNames
{
    public const string UsersRead = "Users.Read";
    public const string UsersWrite = "Users.Write";
    public const string UsersDelete = "Users.Delete";
    public const string RolesManage = "Roles.Manage";
    public const string AuditRead = "Audit.Read";
    public const string DashboardAdminView = "Dashboard.AdminView";
}
