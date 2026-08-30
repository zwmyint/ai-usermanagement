# Copilot Instructions — UserManagement

.NET 10 solution: a JWT-secured Web API (Clean Architecture, EF Core + SQLite) and a Bootstrap 5 MVC
front end that consumes it entirely over HTTP. **Two independently deployable apps** — the UI has no
project reference to any API/Application/Infrastructure project; it only talks to the API via
`HttpClient`.

## Build, test, run

The .NET 10 SDK may not be on `PATH` (commonly installed to `$HOME/.dotnet`). If `dotnet` isn't found:
```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
```

```bash
dotnet build UserManagement.slnx
dotnet test UserManagement.slnx
```

Run a single test (all three test projects use xUnit):
```bash
dotnet test tests/UserManagement.Application.UnitTests --filter "FullyQualifiedName~AuthServiceTests.Login_WithValidCredentials"
dotnet test tests/UserManagement.API.IntegrationTests --filter "DisplayName~Register"
```

Run the apps locally (two terminals; API must be running for the UI to work):
```bash
cd src/UserManagement.API && ASPNETCORE_ENVIRONMENT=Development dotnet run
cd ui/UserManagement.UI  && ASPNETCORE_ENVIRONMENT=Development dotnet run
```
Ports come from each project's `Properties/launchSettings.json`. The API auto-runs EF Core migrations
and seeds `Admin`/`User` roles + a default admin (`Seed:AdminEmail`/`Seed:AdminPassword`) on startup —
there is no manual `dotnet ef database update` step in any environment.

New EF Core migration:
```bash
dotnet ef migrations add <Name> -p src/UserManagement.Infrastructure -s src/UserManagement.API -o Persistence/Migrations
```

No linter is configured; there are no lint scripts to run.

## Architecture

Clean Architecture with **one .csproj per layer**, enforcing the dependency rule strictly:
`Domain ← Application ← Infrastructure`, and `API → Application + Infrastructure` (composition root
only — Infrastructure never references API).

- **`src/UserManagement.Domain`** — entities, enums, repository interfaces (`I*Repository`,
  `IUnitOfWork`), domain exceptions (`NotFoundException`, `ConflictException`, `ValidationException`,
  `ForbiddenException` — these map to specific HTTP status codes, see below). No external dependencies.
- **`src/UserManagement.Application`** — DTOs, service interfaces (`IUserService`, `IAuthService`,
  `IRoleService`, `IAuditService`) and their implementations, manual DTO↔entity mapping
  (`Mapping/MappingExtensions.cs` — no AutoMapper), DataAnnotations-only validation (no
  FluentValidation). **Configuration POCOs (`Jwt`, `Smtp`, `Security`, `Seed`, `Cors` settings) live
  here in `Common/Settings/AppSettings.cs`, not in the API project** — Infrastructure needs them for
  `JwtTokenService`/`SmtpEmailSender`/`DbSeeder` and cannot reference API.
- **`src/UserManagement.Infrastructure`** — `AppDbContext`, EF Core configurations, repositories,
  `Security/Pbkdf2PasswordHasher`, `Security/JwtTokenService`, `Email/SmtpEmailSender`,
  `Persistence/Seed/DbSeeder`. `Guid`/`DateTime` are persisted as invariant, sortable strings via
  global EF Core value converters (SQLite has no native types for them) — see
  `Persistence/Converters`.
- **`src/UserManagement.API`** — controllers (thin; delegate to Application services), `Common/`
  (`ApiResponse<T>` envelope, `CurrentUserAccessor`, `AuditContextExtensions.ToAuditContext()` for
  pulling IP/User-Agent off `HttpContext`), `Middleware/ExceptionHandlingMiddleware` (maps domain
  exceptions → HTTP status + `ApiResponse` body), `Filters/ValidateModelFilter`.
- **`ui/UserManagement.UI`** — `Services/*ApiService` (typed clients per API resource, e.g.
  `UserApiService`, deriving from `Services/ApiClientBase` which unwraps `ApiResponse<T>` and throws
  `ApiException` on failure), `Security/ApiAuthTokenHandler` (a `DelegatingHandler` that attaches the
  bearer token to every outgoing request and transparently refreshes it via `/api/auth/refresh` on
  expiry or a 401), `Contracts/` (DTOs mirrored from the API — kept in sync manually, not shared via a
  project reference).

Every API response is wrapped in `ApiResponse<T>` / `ApiResponse` (`{ success, data, message, errors,
traceId }`); UI API clients unwrap this envelope and never see raw HTTP error bodies.

## Key conventions

- **Auth is custom, not ASP.NET Core Identity.** Own `User`/`Role`/`UserRole` entities, PBKDF2-HMAC-
  SHA256 hashing, JWT access tokens + rotating refresh tokens with family-based reuse/breach detection
  (`RefreshToken.FamilyId`/`ReplacedByTokenHash`; reuse of a revoked token revokes the whole family).
- **The UI never exposes the JWT to the browser/JS.** It signs the user into its own encrypted MVC
  auth cookie; the JWT/refresh token pair is stored inside that cookie's encrypted authentication
  properties (`AuthTokenNames` constants) and only read server-side by `ApiAuthTokenHandler`.
- **Audit logging** — every mutating controller action builds an `AuditContext` via
  `HttpContext.ToAuditContext()` and passes it into the Application service call; `IAuditService`
  persists it. Don't bypass this when adding new mutating endpoints.
- **Last-admin protection** — `UserService` blocks deleting/deactivating/demoting the last active
  Admin, and blocks self-delete/self-deactivate. Preserve this when touching user-management logic.
- **Domain exceptions drive HTTP status codes** via `ExceptionHandlingMiddleware`: throw
  `NotFoundException` → 404, `ConflictException` → 409, `ValidationException` (carries
  `IDictionary<string,string[]>`) → 400, `ForbiddenException` → 401. Don't return raw
  `BadRequest()`/`NotFound()` from Application-layer services — throw instead.
- **appsettings hierarchy**: `appsettings.json` → `appsettings.{Environment}.json` → environment
  variables (double-underscore nesting, e.g. `Jwt__Key`). Checked-in `appsettings.Production.json`
  files are templates with blank secrets, filled via IIS app-pool env vars in production — never
  commit real secrets. `Jwt:Key` must be ≥32 UTF-8 bytes or the API throws on startup.
- **CORS `Cors:AllowedOrigins`** (API config) must match the UI's actual `launchSettings.json` ports
  exactly, and the UI's `Api:BaseUrl` (its config) must match the API's actual port — these are two
  independently configured apps and easy to let drift out of sync after changing launch profiles.
- **Tests**: `UserManagement.Application.UnitTests` mocks repositories (Moq) to test service logic in
  isolation; `UserManagement.Infrastructure.UnitTests` exercises real SQLite (via a temp file) for
  repositories/hasher/JWT; `UserManagement.API.IntegrationTests` uses a custom
  `WebApplicationFactory<Program>` (`ApiWebApplicationFactory`) against a unique on-disk SQLite file
  per test class, so migrations/seeding run exactly as in production.

## MCP servers

A `sqlite` MCP server (`mcp-server-sqlite-npx`) is registered for DB inspection, pointed at the API's
`usermanagement.db`. This repo isn't a git repository, so Copilot CLI's project-level `.mcp.json`
auto-discovery (which walks up to the git root) doesn't apply here — the server is registered instead
via `copilot mcp add sqlite -- npx -y mcp-server-sqlite-npx <path-to-usermanagement.db>` (user config
at `~/.copilot/mcp-config.json`). If this project is later initialized as a git repo, prefer moving
this to a committed `.mcp.json`/`.github/mcp.json` at the repo root so it's shared with collaborators.

## Reference docs

- `docs/api.md` — API endpoint reference.
- `docs/deployment-iis.md` — IIS-on-Windows deployment runbook (two sites/app pools).
- `docs/plan.md` — original approved implementation plan/decision record.
