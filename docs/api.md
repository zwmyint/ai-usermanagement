# API Reference

Base URL (development): `http://127.0.0.1:5299` (see `src/UserManagement.API/Properties/launchSettings.json`
for HTTPS ports). All responses use a uniform envelope:

```json
{ "success": true, "data": { }, "message": "...", "errors": null, "traceId": null }
```

On failure, `success` is `false`, `message` describes the problem, and `errors` (if present) maps
field names to validation messages. Authenticated endpoints require `Authorization: Bearer <token>`.

## Auth — `/api/auth` (rate-limited per client IP; see `docs/security-improvements.md` §4)

| Method | Route | Auth | Rate limit | Description |
|---|---|---|---|---|
| POST | `/register` | Anonymous | 5 req/min/IP | Create an account (assigned the `User` role). |
| POST | `/login` | Anonymous | 100 req/min/IP | Returns access + refresh tokens and the user profile. |
| POST | `/refresh` | Anonymous | 20 req/min/IP | Exchanges a valid refresh token for a new token pair (rotates + detects reuse). |
| POST | `/revoke` | Anonymous | 20 req/min/IP | Revokes a specific refresh token (used on logout). |
| POST | `/logout` | Bearer | 20 req/min/IP | Revokes all of the caller's active refresh tokens and rotates the session's authentication version. |
| POST | `/forgot-password` | Anonymous | 5 req/min/IP | Always returns success; emails a reset link if the address exists. |
| POST | `/reset-password` | Anonymous | 5 req/min/IP | Completes a password reset using the emailed token (single-use, atomically consumed). |

## Profile — `/api/profile` (Bearer, any authenticated user)

| Method | Route | Description |
|---|---|---|
| GET | `/` | Get the caller's own profile. |
| PUT | `/` | Update the caller's own profile (email, name, phone). |
| POST | `/change-password` | Change the caller's own password. |

## Users — `/api/users` (Bearer; see [Roles & Permissions](#roles--permissions))

| Method | Route | Policy | Description |
|---|---|---|---|
| GET | `/` | `UsersRead` | Paged/sortable/searchable user list (`page`, `pageSize`, `search`, `role`, `isActive`, `sortBy`, `sortDescending`). |
| GET | `/{id}` | `UsersRead` | Get a single user. |
| POST | `/` | `UsersWrite` | Create a user. |
| PUT | `/{id}` | `UsersWrite` | Update a user's profile fields. |
| DELETE | `/{id}` | `UsersDelete` | Soft-delete a user (blocked for the last active Admin or self). |
| PATCH | `/{id}/active` | `UsersWrite` | Activate/deactivate a user. |
| PUT | `/{id}/roles` | `UsersWrite` | Replace a user's role assignments. |

## Roles — `/api/roles` (Bearer; see [Roles & Permissions](#roles--permissions))

| Method | Route | Policy | Description |
|---|---|---|---|
| GET | `/` | `UsersRead` | List all roles (needed by Users list/filter and Create/Edit user dropdowns). |
| GET | `/{id}` | `UsersRead` | Get a single role. |
| POST | `/` | `RolesManage` | Create a role. |
| PUT | `/{id}` | `RolesManage` | Update a role. |
| DELETE | `/{id}` | `RolesManage` | Delete a role. |

The seeded `Admin` and `User` roles are marked `isSystemRole: true` and cannot be deleted.

## Audit Logs — `/api/audit-logs` (Bearer, `AuditRead` policy)

| Method | Route | Description |
|---|---|---|
| GET | `/` | Paged/filterable audit trail (`userId`, `action`, `from`, `to`, plus standard paging). |

## Roles & Permissions

Roles are stored dynamically in the `Roles` table (created/edited via `Admin/Roles` in the UI or
`POST /api/roles`), but only the roles below are wired into API authorization policies
(`src/UserManagement.API/Program.cs`) and UI `[Authorize(Roles = ...)]` attributes. Any other
role you create carries no special access beyond a plain authenticated user until code is added
for it.

| Role | View users | Create/edit/activate users | Delete users | Manage roles | View audit logs |
|---|---|---|---|---|---|
| **Admin** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Manager** | ✅ | ✅ | ❌ | ❌ | ❌ |
| **Auditor** | ❌ | ❌ | ❌ | ❌ | ✅ (read-only) |
| **Viewer** | ✅ (read-only) | ❌ | ❌ | ❌ | ❌ |
| **User** | ❌ | ❌ | ❌ | ❌ | ❌ |

API policies (defined once in `Program.cs`, reused across controllers):
- `UsersRead` — Admin, Manager, Viewer
- `UsersWrite` — Admin, Manager
- `UsersDelete` — Admin only
- `RolesManage` — Admin only
- `AuditRead` — Admin, Auditor

> **Note:** `UsersWrite` also covers `PUT /{id}/roles`, so a Manager can currently assign *any*
> role (including Admin) to a user. If you need to prevent Manager from granting Admin/Manager
> themselves, add a stricter check inside `UserService.AssignRolesAsync`.

The UI mirrors this with `[Authorize(Roles = "...")]` on `Admin/UsersController` and
`Admin/AuditLogsController` actions, plus `User.IsInRole(...)` checks in
`Views/Users/Index.cshtml` and `Views/Shared/_Layout.cshtml` to hide buttons/nav links a user
isn't permitted to use (defense in depth — the server-side policy is the actual enforcement).

The Create/Edit User modals (`Views/Users/_CreateModal.cshtml` / `_EditModal.cshtml`) assign
roles via a multi-select `<select multiple>` dropdown (populated from `GET /api/roles`), not
checkboxes — a user can still be assigned more than one role at a time.

## Dashboard — `/api/dashboard` (Bearer)

| Method | Route | Description |
|---|---|---|
| GET | `/summary` | Home page dashboard data for the calling user. |

`myActivity` is always populated for the caller; `adminStats` is `null` unless the caller is in the
`Admin` role.

```jsonc
{
  "success": true,
  "data": {
    "adminStats": {                    // null for non-admin callers
      "totalUsers": 12,
      "activeUsers": 10,
      "inactiveUsers": 2,
      "totalRoles": 2,
      "totalAuditLogs": 348,
      "loginsLast24Hours": 7,
      "loginsLast7Days": 41,
      "failedLoginsLast24Hours": 1,
      "roleBreakdown": [ { "roleId": "...", "roleName": "Admin", "userCount": 1 } ],
      "recentUsers": [
        { "id": "...", "userName": "jdoe", "email": "jdoe@example.com", "fullName": "J Doe",
          "isActive": true, "createdAt": "2026-08-30T09:12:44", "roles": ["User"] }
      ]
    },
    "myActivity": {
      "lastLoginAt": "2026-08-30T09:12:44",     // most recent LoginSucceeded audit entry
      "previousLoginAt": "2026-08-28T18:02:10", // the one before it, null on first sign-in
      "lastLoginIp": "127.0.0.1",
      "lastFailedLoginAt": null,
      "lastFailedLoginIp": null,
      "totalLogins": 9
    }
  },
  "message": null,
  "errors": null,
  "traceId": "..."
}
```

## Health

`GET /health` — anonymous liveness probe: `{ "status": "Healthy", "timestampUtc": "..." }`.

## Interactive docs

Swagger UI is available at `/swagger` when `ASPNETCORE_ENVIRONMENT=Development`, including a
"Bearer" security scheme you can paste an access token into to try authenticated endpoints.
