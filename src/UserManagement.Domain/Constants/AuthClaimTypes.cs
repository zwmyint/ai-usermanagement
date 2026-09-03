namespace UserManagement.Domain.Constants;

public static class AuthClaimTypes
{
    public const string AuthenticationVersion = "auth_version";

    /// <summary>Claim type carrying one of the user's effective permission names (see <see cref="PermissionNames"/>).</summary>
    public const string Permission = "permission";
}
