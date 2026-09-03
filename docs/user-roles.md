# User Roles

This document explains how roles are modeled, stored, assigned, and enforced across the API and
UI. Roles, role assignments, and **role → permission grants** are all fully dynamic (DB-driven
CRUD, editable at runtime from the Roles admin screen/API) — only the fixed list of permission
*actions* the code understands (`PermissionNames`) requires a code change to extend.

## 1. How it's implemented

### Data model (Domain / Infrastructure)

- `Role` (`src/UserManagement.Domain/Entities/Role.cs`) — `Id`, `Name`, `NormalizedName`,
  `Description`, `IsSystemRole`, audit fields. Persisted in the `Roles` table.
- `UserRole` — join entity for the many-to-many `User` ↔ `Role` relationship.
- `RoleNames` (`src/UserManagement.Domain/Constants/RoleNames.cs`) — string constants for five
  well-known role names (`Admin`, `User`, `Manager`, `Auditor`, `Viewer`) plus
  `SystemRoles = [Admin, User]` and `IsSystemRole(name)`.
  - Only `Admin` and `User` are **system roles**: seeded automatically by `DbSeeder` on startup,
    and protected from rename/delete in `RoleService` (`role.IsSystemRole` or
    `RoleNames.IsSystemRole(name)` check).
  - `Manager`, `Auditor`, `Viewer` are **not system roles**, but `DbSeeder.SeedDefaultRolePermissionsAsync`
    creates them once (only while the `RolePermissions` table is completely empty, i.e. the very
    first startup after this feature shipped) with the same permissions the old hardcoded policies
    granted them, so existing deployments keep working unchanged. After that first run, they behave
    like any other admin-editable role - freely renamable, deletable, and re-permissionable via the
    Roles UI/API, and are never re-created if deleted.

### Role CRUD (Application / API)

- `IRoleService` / `RoleService` (`src/UserManagement.Application/Services/RoleService.cs`) —
  create/update/delete/list roles, enforcing: unique name, system roles can't be renamed or
  deleted, and roles with assigned users can't be deleted.
- `RolesController` (`src/UserManagement.API/Controllers/RolesController.cs`) — `GET/POST/PUT/DELETE
  /api/roles`, gated by the `RolesManage` policy (create/update/delete) and `UsersRead` (list/get).
- Role **assignment** to a user is separate: `UserService.AssignRolesAsync` /
  `PUT /api/users/{id}/roles` (`UsersController.AssignRoles`) replaces a user's role set by name.

So, at the data layer, roles are **fully flexible** — an admin can create, rename, describe, and
delete custom roles, and assign any combination of roles to any user, entirely through the UI/API
with zero code changes.

### Authorization enforcement — permission-based and DB-driven

> **Update:** the codebase originally enforced authorization with hardcoded
> `RequireRole(RoleNames.Admin, ...)` policies and `[Authorize(Roles = "Admin,Manager")]` attributes
> (see "Historical note" below). It has since been changed to a **permission-claim model**, so that
> *which roles* satisfy a given policy is entirely data-driven — creating a new role and granting it
> permissions through the Roles admin screen/API takes effect immediately, with no code change or
> redeploy.

- **Fixed permission actions** — `PermissionNames` (`src/UserManagement.Domain/Constants/PermissionNames.cs`)
  defines the small, code-tied list of actions the app understands: `Users.Read`, `Users.Write`,
  `Users.Delete`, `Roles.Manage`, `Audit.Read`, `Dashboard.AdminView`. This list is still fixed in
  code (each name corresponds to a real code path that checks for it) — extending *this* list still
  requires a code change (see §3 below). What's no longer fixed is which roles grant them.
- **Role ↔ Permission mapping (dynamic)** — a `Permission` entity and `RolePermission` join table
  (`src/UserManagement.Domain/Entities/{Permission,RolePermission}.cs`) store, per role, which
  permissions it grants. Managed via `RoleService.GetAllPermissionsAsync` /
  `UpdatePermissionsAsync` and `PUT /api/roles/{id}/permissions` (`RolesController`), exposed in the
  UI as a "Permissions" button on `Admin/Roles`. A built-in safety net
  (`RoleService.UpdatePermissionsAsync`) blocks an update that would leave **no** role holding
  `Roles.Manage`, so admins can't accidentally lock everyone out of role/permission management.
- **API policies** (`src/UserManagement.API/Program.cs`) are now permission-based, not role-based:
  ```csharp
  builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
  builder.Services.AddAuthorizationBuilder()
      .AddPolicy("UsersRead",   policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersRead)))
      .AddPolicy("UsersWrite",  policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersWrite)))
      .AddPolicy("UsersDelete", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.UsersDelete)))
      .AddPolicy("RolesManage", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.RolesManage)))
      .AddPolicy("AuditRead",   policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.AuditRead)));
  ```
  `PermissionAuthorizationHandler` (`src/UserManagement.API/Authorization/`) simply checks the JWT
  for a matching `permission` claim. Controllers still use the same `[Authorize(Policy = "...")]`
  attributes as before — only what each *policy* means changed, not where it's applied.
  `DashboardController` checks `User.HasClaim(AuthClaimTypes.Permission, PermissionNames.DashboardAdminView)`
  instead of `User.IsInRole(RoleNames.Admin)`.
- **UI** (`ui/UserManagement.UI/Security/PermissionAuthorizationHandler.cs`) mirrors the same
  pattern independently (no project reference to the API/Domain, consistent with the rest of the
  architecture): its own `PermissionNames`/`PermissionRequirement`/`PermissionAuthorizationHandler`,
  registered in `Program.cs` with the same policy names (`UsersRead`, `UsersWrite`, `UsersDelete`,
  `RolesManage`, `AuditRead`). Controllers use `[Authorize(Policy = "...")]` instead of
  `[Authorize(Roles = "...")]`, and Razor views (`_Layout.cshtml`, `Home/Index.cshtml`,
  `Users/Index.cshtml`) check `User.HasClaim(PermissionClaimTypes.Permission, PermissionNames.X)`
  instead of `User.IsInRole(...)`.

### JWT / auth cookie (Infrastructure + UI)

`JwtTokenService.CreateAccessToken` (`src/UserManagement.Infrastructure/Security/JwtTokenService.cs`)
now adds both a `role` claim per assigned role **and** a `permission` claim per permission granted
by any of those roles (resolved from the DB's `RolePermission` table at login/refresh time —
`AuthService.IssueTokensAsync` computes the distinct permission set). `AccountController.SignInAsync`
copies both role and permission claims from the JWT into the UI's encrypted auth cookie at sign-in.
Because permissions are resolved fresh from the database on every login, changing a role's
permissions takes effect for a user the next time they sign in (existing sessions carry the claims
issued at their last login, same as roles do today).

### Historical note: how this worked before

Previously, `Program.cs` used `RequireRole(RoleNames.Admin, RoleNames.Manager, RoleNames.Viewer)`-style
policies, and UI controllers used `[Authorize(Roles = "Admin,Manager,Viewer")]` directly — both
hardcoding the mapping from role name to permission. That's what made adding a new role with real
access require editing both `Program.cs` and every relevant UI controller. The permission-claim
model above removes that requirement.

### UI role and permission management

- `Views/Roles/_RoleModal.cshtml` + `Controllers/Admin/RolesController.cs` — modal-based create/
  edit/delete for roles, calling `RoleApiService` → `/api/roles`.
- `Views/Roles/_RolePermissionsModal.cshtml` + `RolesController.Permissions(...)` — a "Permissions"
  button per role opens a checklist of all `PermissionDto`s (`GET /api/roles/permissions`) with the
  role's current grants checked; saving posts to `PUT /api/roles/{id}/permissions`
  (`RoleApiService.UpdatePermissionsAsync`). This is the screen an admin uses to make a new role
  meaningful, with no code change.
- `Controllers/Admin/UsersController.cs` — populates `ViewBag.Roles` (all roles, via
  `RoleApiService.GetAllAsync`) for the create/edit user forms, and submits selected role names to
  `UserApiService.AssignRolesAsync` → `PUT /api/users/{id}/roles`.

## 2. Are roles fixed or flexible?

**Roles and their permissions are now both fully flexible; only the permission *action list* itself is fixed:**

| Layer | Flexible? | Notes |
|---|---|---|
| Role storage (`Roles` table) | ✅ Fully flexible | Create/rename/describe/delete any non-system role via UI/API, no redeploy needed. |
| Role assignment to users | ✅ Fully flexible | Assign any existing role(s) to any user via UI/API. |
| **Role → permission grants** | ✅ Fully flexible | Assign any of the fixed permission actions to any role (including a brand-new one) via `Admin/Roles` → Permissions, or `PUT /api/roles/{id}/permissions`. Takes effect on the user's next login/token refresh - no code change or redeploy. |
| System roles (`Admin`, `User`) | ❌ Fixed | Seeded on startup, cannot be renamed/deleted (enforced in `RoleService` + `RoleNames.IsSystemRole`). |
| Permission **action list** (`Users.Read`, `Roles.Manage`, etc.) | ❌ Fixed in code | `PermissionNames` is the finite set of things the app's code actually checks for. Adding a wholly new *kind* of action (e.g. gating a brand-new endpoint) still needs a code change - see §3. |

In short: creating a role called `"Editor"` and giving it `Users.Read` through the Roles →
Permissions screen makes it immediately able to list users - verified end-to-end (registered a user,
assigned the new role, logged in, and confirmed `GET /api/users` → 200 while `POST /api/users` and
`POST /api/roles` correctly → 403). No code change or redeploy was needed for that. A safety net in
`RoleService.UpdatePermissionsAsync` also blocks removing `Roles.Manage` from every role at once, so
an admin can't lock everyone out of the Roles screen by mistake.

## 3. What code changes are still needed (and when none are needed)

**No code change needed** to:
- Create/rename/describe/delete a custom role.
- Assign any combination of the six existing permissions (`Users.Read/Write/Delete`, `Roles.Manage`,
  `Audit.Read`, `Dashboard.AdminView`) to any role, new or existing.
- Assign roles to users.

**A code change is only needed** to introduce a genuinely new *kind* of protected action that isn't
already covered by the existing permission list, e.g. gating a brand-new "Export Users" feature:

1. **Domain** — add a constant to `PermissionNames` (`src/UserManagement.Domain/Constants/PermissionNames.cs`).
   It will be auto-inserted into the `Permissions` table on next startup by `DbSeeder.SeedPermissionsAsync`
   (additive, idempotent diff-sync) - no manual seeding or migration needed for the table contents.
2. **API** — add a policy in `Program.cs` (`.AddPolicy("ExportUsers", policy => policy.Requirements.Add(new PermissionRequirement(PermissionNames.ExportUsers)))`)
   and apply `[Authorize(Policy = "ExportUsers")]` to the new endpoint.
3. **UI** (if the feature has a UI surface) — add the matching policy in the UI's `Program.cs` and
   apply `[Authorize(Policy = "ExportUsers")]` / `User.HasClaim(PermissionClaimTypes.Permission, PermissionNames.ExportUsers)`
   where needed.
4. **Tests** — extend unit/integration tests to cover the new permission.
5. **Redeploy both apps** — the new permission *action* only exists once the code is deployed;
   after that, granting it to any role (new or existing) is purely an admin/DB operation via the
   Roles → Permissions screen, no further deploys required.

If you only need to **relabel or reassign an existing role's users or permissions** (no new kind of
protected action), no code change is needed at all — just use the Roles/Users admin screens.

