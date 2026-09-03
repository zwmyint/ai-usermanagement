using Microsoft.AspNetCore.Authorization;
using UserManagement.Domain.Constants;

namespace UserManagement.API.Authorization;

/// <summary>Requires the current principal to carry the given permission claim.</summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

/// <summary>
/// Succeeds if the user has a "permission" claim matching the requirement. Permission claims are
/// issued into the JWT based on the *database* Role -&gt; Permission assignments (see RoleService /
/// JwtTokenService) - so which roles satisfy a given policy is fully data-driven and never requires
/// a code change, unlike the old <c>RequireRole(...)</c> policies it replaces.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(AuthClaimTypes.Permission, requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
