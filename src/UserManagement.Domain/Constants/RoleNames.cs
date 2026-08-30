namespace UserManagement.Domain.Constants;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Manager = "Manager";
    public const string Auditor = "Auditor";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> SystemRoles = new[] { Admin, User };

    public static bool IsSystemRole(string name) =>
        SystemRoles.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase));
}
