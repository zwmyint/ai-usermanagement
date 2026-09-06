# User Management System — Implementation Plan

## Problem & Approach

Build a User Management system as **two independent applications** on **.NET 10 / C#**:

1. **UserManagement.API** — ASP.NET Core Web API, Clean Architecture (multi-project), EF Core + SQLite, JWT auth.
2. **UserManagement.UI** — ASP.NET Core MVC + Bootstrap 5, consumes the API over HTTPS via `HttpClient`.

Confirmed decisions:

| Decision | Choice |
|---|---|
| SDK | Install .NET 10 SDK as first step (machine currently has only .NET 8 runtime) |
| Identity | **Custom** `User`/`Role` entities + PBKDF2/BCrypt hashing (no ASP.NET Core Identity) |
| Solution layout | **Separate .csproj per layer** to enforce the dependency rule |
| UI token handling | **Hybrid** — JWT stored in encrypted auth cookie; DataTables loads via MVC server-side proxy actions |
| Password reset | SMTP email with configurable settings |
| Deployment | **IIS on Windows** (two sites/app pools) |
| Extras | Refresh tokens + revocation, audit logs, seed data, xUnit tests, Serilog, rate limiting on auth |
| Validation | DataAnnotations (server + client via jQuery unobtrusive) — no FluentValidation |

Dependency rule: `Domain ← Application ← Infrastructure`, `Application ← API`, `Infrastructure ← API` (composition root only).

---

## Solution Structure

```
ai-usermanagement/
├── UserManagement.sln
├── .editorconfig / .gitignore / Directory.Build.props
├── docs/
│   ├── deployment-iis.md
│   └── api.md
│
├── src/
│   ├── UserManagement.Domain/                # no dependencies
│   │   ├── Entities/  (User, Role, UserRole, RefreshToken, AuditLog, PasswordResetToken)
│   │   ├── Enums/     (AuditAction, UserStatus)
│   │   ├── Interfaces/(IUserRepository, IRoleRepository, IRefreshTokenRepository,
│   │   │               IAuditLogRepository, IPasswordHasher, IUnitOfWork)
│   │   ├── Common/    (BaseEntity, IAuditableEntity)
│   │   └── Exceptions/(DomainException, NotFoundException, ConflictException)
│   │
│   ├── UserManagement.Application/           # → Domain
│   │   ├── DTOs/        (UserDto, CreateUserDto, UpdateUserDto, RoleDto,
│   │   │                 LoginRequest/Response, RegisterRequest, RefreshRequest,
│   │   │                 ChangePasswordDto, ForgotPasswordDto, ResetPasswordDto,
│   │   │                 PagedRequest<T>, PagedResult<T>, DataTablesRequest/Response)
│   │   ├── Interfaces/  (IUserService, IAuthService, IRoleService, IAuditService,
│   │   │                 ITokenService, IEmailSender, ICurrentUser, IDateTime)
│   │   ├── Services/    (UserService, AuthService, RoleService, AuditService)
│   │   ├── Mapping/     (manual mapping extensions — no AutoMapper)
│   │   └── Common/      (Result<T>, PaginationExtensions)
│   │
│   ├── UserManagement.Infrastructure/        # → Application, Domain
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Configurations/ (UserConfiguration, RoleConfiguration, …)
│   │   │   ├── Repositories/   (UserRepository, RoleRepository, …, UnitOfWork)
│   │   │   ├── Seed/           (DbSeeder — roles + default admin)
│   │   │   └── Migrations/
│   │   ├── Security/    (Pbkdf2PasswordHasher, JwtTokenService)
│   │   ├── Email/       (SmtpEmailSender, EmailTemplates)
│   │   └── DependencyInjection.cs
│   │
│   └── UserManagement.API/                   # → Application, Infrastructure
│       ├── Controllers/ (AuthController, UsersController, RolesController,
│       │                 AuditLogsController, ProfileController)
│       ├── Middleware/  (ExceptionHandlingMiddleware, RequestLoggingMiddleware)
│       ├── Filters/     (AuditActionFilter, ValidateModelFilter)
│       ├── Common/      (ApiResponse<T>, CurrentUserAccessor)
│       ├── Settings/    (JwtSettings, SmtpSettings, CorsSettings, SeedSettings)
│       ├── appsettings.json / appsettings.Production.json
│       ├── web.config            # IIS: AspNetCoreModuleV2
│       └── Program.cs
│
├── ui/
│   └── UserManagement.UI/                    # MVC, no reference to API projects
│       ├── Controllers/ (HomeController, AccountController, ProfileController,
│       │                 AdminController, RolesController, AuditController)
│       ├── Models/ViewModels/ (LoginViewModel, RegisterViewModel, ProfileViewModel,
│       │                 ChangePasswordViewModel, ForgotPasswordViewModel,
│       │                 ResetPasswordViewModel, UserFormViewModel, RoleAssignViewModel)
│       ├── Services/    (IApiClient, ApiClient, TokenStore, AuthTokenHandler,
│       │                 ApiExceptionHandler)
│       ├── Views/
│       │   ├── Shared/  (_Layout, _Navbar, _Toasts, _ValidationScriptsPartial,
│       │   │             _Pagination, Error)
│       │   ├── Account/ (Login, Register, ForgotPassword, ResetPassword,
│       │   │             AccessDenied)
│       │   ├── Profile/ (Index, ChangePassword)
│       │   ├── Admin/   (UserList, UserForm, RoleManagement, AuditLogs)
│       │   └── Home/    (Index, Privacy)
│       ├── wwwroot/ (css/site.css, js/site.js, js/users-datatable.js,
│       │             js/toasts.js, lib/bootstrap, lib/jquery, lib/datatables)
│       ├── appsettings.json  (ApiSettings:BaseUrl)
│       ├── web.config
│       └── Program.cs
│
└── tests/
    ├── UserManagement.Application.UnitTests/
    ├── UserManagement.Infrastructure.UnitTests/
    └── UserManagement.API.IntegrationTests/   # WebApplicationFactory + SQLite in-memory/file
```

---

## Data Model

- **User**: Id (Guid), UserName, Email (unique), NormalizedEmail, PasswordHash, PasswordSalt, FirstName, LastName, PhoneNumber, IsActive, EmailConfirmed, LockoutEnd, AccessFailedCount, CreatedAt/By, UpdatedAt/By.
- **Role**: Id, Name (unique), NormalizedName, Description, IsSystemRole.
- **UserRole**: composite key (UserId, RoleId), AssignedAt, AssignedBy.
- **RefreshToken**: Id, UserId, TokenHash, ExpiresAt, CreatedAt, CreatedByIp, RevokedAt, RevokedByIp, ReplacedByTokenHash.
- **PasswordResetToken**: Id, UserId, TokenHash, ExpiresAt, UsedAt.
- **AuditLog**: Id, UserId?, UserName, Action, EntityName, EntityId, OldValues, NewValues, IpAddress, UserAgent, Succeeded, Timestamp.

Seed: roles `Admin`, `User`; default admin from `Seed:AdminEmail` / `Seed:AdminPassword` config (must be overridden in production).

---

## API Surface

**/api/auth**
- `POST /register`, `POST /login`, `POST /refresh`, `POST /revoke`, `POST /logout`
- `POST /forgot-password`, `POST /reset-password`

**/api/profile** (authenticated)
- `GET /`, `PUT /`, `POST /change-password`

**/api/users** (Admin)
- `GET /` (paged/sorted/filtered — DataTables compatible), `GET /{id}`, `POST /`, `PUT /{id}`,
  `DELETE /{id}` (soft delete), `POST /{id}/roles`, `POST /{id}/activate`, `POST /{id}/deactivate`

**/api/roles** (Admin)
- `GET /`, `GET /{id}`, `POST /`, `PUT /{id}`, `DELETE /{id}`

**/api/audit-logs** (Admin)
- `GET /` (paged, filter by user/action/date range)

All responses wrapped in `ApiResponse<T>` `{ success, data, message, errors, traceId }`.
Swagger/OpenAPI with JWT bearer security definition; enabled in Development, optionally behind auth in Production.

---

## Todos

1. **env-sdk** — Install .NET 10 SDK (`dotnet-install` script), verify `dotnet --list-sdks`.
2. **solution-scaffold** — Create solution, 4 src projects + 1 ui project + 3 test projects, wire project references per dependency rule, add `Directory.Build.props`, `.gitignore`, `.editorconfig`.
3. **domain-layer** — Entities, enums, repository/hasher interfaces, domain exceptions, `BaseEntity`.
4. **infra-persistence** — `AppDbContext`, entity configurations, repositories, `UnitOfWork`, SQLite provider, initial EF Core migration, `DbSeeder`.
5. **infra-security** — `Pbkdf2PasswordHasher`, `JwtTokenService` (access + refresh token hashing/rotation).
6. **infra-email** — `SmtpEmailSender` + `SmtpSettings` + HTML templates for password reset / welcome.
7. **app-services** — `AuthService` (register/login/refresh/revoke/reset), `UserService`, `RoleService`, `AuditService`; DTOs, mapping, `Result<T>`, paging.
8. **api-host** — `Program.cs`: DI, JWT bearer auth, role-based authorization policies, CORS for UI origin, HTTPS redirect + HSTS, Serilog, rate limiting on `/api/auth/*`, Swagger, exception middleware, audit filter, health endpoint.
9. **api-controllers** — Auth, Users, Roles, Profile, AuditLogs controllers with DataAnnotations validation and `ApiResponse<T>`.
10. **ui-scaffold** — MVC project, Bootstrap 5 + jQuery + DataTables via LibMan/wwwroot/lib, `_Layout` with responsive navbar, toast partial, `ApiSettings:BaseUrl`.
11. **ui-auth** — Cookie auth in MVC, `TokenStore` (JWT + refresh in encrypted cookie), `AuthTokenHandler` delegating handler with auto-refresh on 401, `AccountController` (Login/Register/Logout/Forgot/Reset), `AccessDenied`.
12. **ui-profile** — Profile view/edit, change password, client + server validation, toasts.
13. **ui-admin** — User list with server-side DataTables via MVC proxy action, create/edit/delete modals, role management page, role assignment, audit log viewer with filters.
14. **tests** — xUnit: unit tests for AuthService/UserService/hasher/token service; integration tests via `WebApplicationFactory` against a temp SQLite file covering auth flow, RBAC enforcement, and user CRUD.
15. **deploy-iis** — Publish profiles, `web.config` for both apps, IIS setup doc: install ASP.NET Core Hosting Bundle, two app pools (No Managed Code), HTTPS bindings, folder ACLs for the SQLite file, environment variables, migration-on-deploy strategy.
16. **docs-verify** — README (setup, run, config), `docs/api.md`, `docs/deployment-iis.md`; final build + full test run.

Dependencies: 1 → 2 → 3 → {4,5,6} → 7 → 8 → 9 → {10 → 11 → {12,13}}, {9,13} → 14 → 15 → 16.

---

## Deployment Plan (IIS on Windows)

**Prerequisites**
- Windows Server with IIS, feature: Web Server (IIS) + Web Sockets.
- **ASP.NET Core 10 Hosting Bundle** installed (adds `AspNetCoreModuleV2`); restart IIS with `iisreset`.
- Valid TLS certificate bound in IIS.

**Build & publish**
```
dotnet publish src/UserManagement.API/UserManagement.API.csproj -c Release -o C:\inetpub\usermgmt-api
dotnet publish ui/UserManagement.UI/UserManagement.UI.csproj  -c Release -o C:\inetpub\usermgmt-ui
```

**API site**
- New IIS site `UserManagement.API`, app pool `UserMgmtApiPool` — .NET CLR Version = **No Managed Code**, Identity = dedicated `ApplicationPoolIdentity`.
- HTTPS binding e.g. `https://api.example.com`.
- SQLite file placed **outside wwwroot**, e.g. `C:\AppData\UserManagement\usermanagement.db`; grant `IIS AppPool\UserMgmtApiPool` Modify permission on that folder only. Connection string `Data Source=C:\AppData\UserManagement\usermanagement.db`.
- Secrets via environment variables in `web.config` `<environmentVariables>` or app-pool env: `Jwt__Key`, `Smtp__Password`, `Seed__AdminPassword`, `ASPNETCORE_ENVIRONMENT=Production`.
- Enable `stdoutLogEnabled` temporarily for first-run diagnostics; Serilog writes rolling files to a writable log folder.
- CORS: allow only the UI origin, `AllowCredentials` not required (bearer tokens).

**UI site**
- Site `UserManagement.UI`, app pool `UserMgmtUiPool` (No Managed Code), binding `https://app.example.com`.
- `ApiSettings:BaseUrl = https://api.example.com`.
- Data Protection keys persisted to a shared folder (`PersistKeysToFileSystem`) so auth cookies survive app-pool recycles; set `setProfileEnvironment`/`loadUserProfile=true` on the app pool.

**Migration strategy**
- Migrations are **not** applied automatically in Production. Generate an idempotent script and apply during a maintenance window:
  ```
  dotnet ef migrations script --idempotent -o migrate.sql -p src/UserManagement.Infrastructure -s src/UserManagement.API
  ```
- Alternative: bundle (`dotnet ef migrations bundle`) executed by the deploy step.
- Always back up (copy) the `.db` file before applying; SQLite in WAL mode — also copy `-wal`/`-shm` or checkpoint first.

**Post-deploy verification**
- `GET https://api.example.com/health` → 200.
- Swagger reachable (if enabled), login with seeded admin, rotate the seeded password immediately.
- UI login → dashboard → user list loads via DataTables.

**Scaling note**
SQLite is file-based and single-writer; suitable for small deployments. The API now supports
SQLite and PostgreSQL through `Database:Provider`. Repositories and application services remain
provider-agnostic, while each provider has its own EF Core migration set. Switching providers does
not synchronize or transfer existing data.

---

## Notes & Considerations

- **Refresh token rotation**: single-use; on reuse detection, revoke the whole token family for that user.
- **Rate limiting**: fixed/sliding window on `/api/auth/login`, `/register`, `/forgot-password`; plus account lockout after N failed attempts.
- **Password reset tokens**: stored hashed, short TTL (e.g. 30 min), single-use; `forgot-password` always returns 200 to avoid user enumeration.
- **Audit logs**: written for login success/failure, logout, user CRUD, role assignment, password changes/resets.
- **SQLite specifics**: enable WAL, set `busy_timeout`, use `Guid`→`TEXT` conversion, and be explicit about `DateTime` UTC storage.
- **Security headers** in both apps: HSTS, X-Content-Type-Options, Referrer-Policy, CSP (relaxed enough for Bootstrap/DataTables CDN-free local libs).
- **System roles** (`Admin`, `User`) are non-deletable; the last active Admin cannot be deleted or demoted.
- **Soft delete** for users with a global query filter, so audit history stays intact.
