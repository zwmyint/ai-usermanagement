# Development Plan — Home Page Admin Dashboard

Status: **Approved** (2026-08-30)

## 1. Goal

Keep the existing Home page hero section exactly as-is, and append a **dashboard section below it**
showing:

- **Admin-only stats**: total users, active users, inactive users, total roles (+ per-role user
  counts), total audit log entries, logins in the last 24h / 7d, failed logins in the last 24h, and
  the 5 most recently registered users.
- **All signed-in users**: "My sign-in activity" — current/last login date-time, previous login
  date-time, last login IP and last failed login attempt, sourced from `AuditLog` where
  `Action = LoginSucceeded` / `LoginFailed`.
- Anonymous visitors: no change at all (hero only).

## 2. Design decisions

- A new **`GET /api/dashboard/summary`** endpoint, rather than having the UI call `/api/users`,
  `/api/roles` and `/api/audit-logs` separately — one round trip, no over-fetching, and audit search
  stays `AdminOnly`.
- The endpoint is `[Authorize]` (any authenticated user). The `adminStats` portion of the payload is
  populated **only** when the caller is in the `Admin` role; it is `null` otherwise. The `myActivity`
  portion is always populated for the caller.
- Counting is done with EF Core `CountAsync` in new repository methods (no in-memory paging or
  counting).
- The dashboard read is **not audited** — it is a non-mutating read, consistent with the existing
  convention that only mutating actions build an `AuditContext`.
- The Home page must still render if the API is unreachable, so `HomeController` catches
  `ApiException` / `HttpRequestException`, logs, and the view shows an inline warning alert instead
  of the cards.

## 3. Changes by layer

### Domain (`src/UserManagement.Domain`)

- `Interfaces/IUserRepository.cs` — add:
  - `Task<(int Total, int Active, int Inactive)> GetCountsAsync(CancellationToken ct)`
  - `Task<IReadOnlyList<User>> GetRecentlyCreatedAsync(int take, CancellationToken ct)`
- `Interfaces/IRoleRepository.cs` — add:
  - `Task<int> CountAsync(CancellationToken ct)`
  - `Task<IReadOnlyList<RoleUserCount>> GetRoleUserCountsAsync(CancellationToken ct)`
- `Interfaces/IRefreshTokenRepository.cs` (`IAuditLogRepository` lives here) — add:
  - `Task<int> CountAsync(AuditAction? action, bool? succeeded, DateTime? from, CancellationToken ct)`
  - `Task<IReadOnlyList<AuditLog>> GetRecentByUserAndActionAsync(Guid userId, AuditAction action, int take, CancellationToken ct)`

### Application (`src/UserManagement.Application`)

- `DTOs/DashboardDto.cs` (new):
  - `DashboardSummaryDto { AdminStatsDto? AdminStats; MySignInActivityDto MyActivity; }`
  - `AdminStatsDto { TotalUsers, ActiveUsers, InactiveUsers, TotalRoles, TotalAuditLogs,
    LoginsLast24Hours, LoginsLast7Days, FailedLoginsLast24Hours,
    IReadOnlyList<RoleUserCountDto> RoleBreakdown, IReadOnlyList<RecentUserDto> RecentUsers }`
  - `RoleUserCountDto { RoleId, RoleName, UserCount }`
  - `RecentUserDto { Id, UserName, Email, FullName, IsActive, CreatedAt }`
  - `MySignInActivityDto { DateTime? LastLoginAt; DateTime? PreviousLoginAt; string? LastLoginIp;
    DateTime? LastFailedLoginAt; int TotalLogins; }`
- `Interfaces/IUserService.cs` — add
  `IDashboardService { Task<DashboardSummaryDto> GetSummaryAsync(Guid userId, bool isAdmin, CancellationToken ct); }`
- `Services/DashboardService.cs` (new) — orchestrates the repository calls and uses
  `IDateTimeProvider` for the 24h / 7d windows; throws `NotFoundException` if the user id does not
  resolve.
  - *Last login* = 1st `LoginSucceeded` audit row for the user ordered by `Timestamp DESC`
    (falls back to `User.LastLoginAt`); *previous login* = 2nd row.
- `DependencyInjection.cs` — register `IDashboardService`.

### Infrastructure (`src/UserManagement.Infrastructure`)

- Implement the new methods in `UserRepository`, `RoleRepository` and `AuditLogRepository`.
- **No new EF Core migration is needed** — there is no schema change.

### API (`src/UserManagement.API`)

- `Controllers/DashboardController.cs` (new), `[Route("api/dashboard")]`, `[Authorize]`,
  `GET summary` → `ApiResponse<DashboardSummaryDto>`; resolves the caller via the existing
  `ICurrentUser` / `CurrentUserAccessor` and `IsInRole(RoleNames.Admin)`.
- No CORS or auth configuration changes.

### UI (`ui/UserManagement.UI`)

- `Contracts/Contracts.cs` — mirror the new DTOs (manual sync, per existing convention).
- `Services/DashboardApiService.cs` (new) — `IDashboardApiService.GetSummaryAsync(ct)` →
  `GET api/dashboard/summary`, deriving from `ApiClientBase`.
- `Program.cs` — register `IDashboardApiService`.
- `ViewModels/Home/HomeViewModels.cs` (new) —
  `HomeIndexViewModel { DashboardSummaryDto? Dashboard; bool DashboardUnavailable; }`.
- `Controllers/HomeController.cs` — inject `IDashboardApiService`; on `Index`, when
  `User.Identity.IsAuthenticated`, fetch the summary inside a try/catch and pass the view model.
- `Views/Home/Index.cshtml` — **existing hero block untouched**; append below it:
  - signed-in users → a "My sign-in activity" card row;
  - admins (and `AdminStats is not null`) → a row of Bootstrap 5 stat cards
    (Total Users / Active / Inactive / Roles), plus role-breakdown, login-activity and recent-users
    panels, with links through to the Users / Roles / Audit Logs pages;
  - a warning alert when `DashboardUnavailable`.
- Bootstrap 5 + the existing Bootstrap Icons only — **no new client packages**.

## 4. Tests

- `tests/UserManagement.Application.UnitTests/Services/DashboardServiceTests.cs` (new, Moq):
  admin vs non-admin payload shaping; previous-login resolution with 0 / 1 / 2+ login records;
  time-window filters.
- `tests/UserManagement.API.IntegrationTests/Controllers/DashboardEndpointsTests.cs` (new):
  401 for anonymous; non-admin gets `adminStats == null`; the seeded admin gets populated counts.
- Validate with `dotnet build UserManagement.slnx` then `dotnet test UserManagement.slnx`.

## 5. Docs

- `docs/api.md` — document `GET /api/dashboard/summary` (auth, response shape, admin-conditional
  fields).
- This plan is kept at `docs/dashboard-plan.md`.

## 6. Out of scope

Charts/graphs, auto-refresh or polling, date-range filters, per-user dashboards for non-admins beyond
sign-in activity, and caching.
