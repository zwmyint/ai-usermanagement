using Microsoft.AspNetCore.Authorization;

namespace UserManagement.UI.Security;

/// <summary>Claim type carrying one of the user's effective permission names, copied from the API's JWT into the auth cookie at sign-in.</summary>
public static class PermissionClaimTypes
{
    public const string Permission = "permission";
}

/// <summary>Requires the current principal to carry the given permission claim.</summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

/// <summary>
/// Succeeds if the signed-in cookie principal has a "permission" claim matching the requirement.
/// These claims were copied from the API's JWT at login (see AccountController.SignInAsync), which
/// itself derives them from the database Role -&gt; Permission assignments - so, like on the API side,
/// which roles satisfy a policy here is data-driven and never requires a code change.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(PermissionClaimTypes.Permission, requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
