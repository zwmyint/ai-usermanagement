# User Management

A .NET 10 sample application: a JWT-secured **Web API** (Clean Architecture, EF Core + SQLite) and
a **Bootstrap 5 MVC** front end that consumes it entirely over HTTP. Two independently deployable
apps, matching the approved plan.

## Solution layout

```
UserManagement.slnx
├── src/
│   ├── UserManagement.Domain          # Entities, enums, repository interfaces, domain exceptions
│   ├── UserManagement.Application     # DTOs, service interfaces/implementations, mapping, settings
│   ├── UserManagement.Infrastructure  # EF Core + SQLite, repositories, JWT, PBKDF2 hashing, SMTP, seeding
│   └── UserManagement.API             # ASP.NET Core Web API host, controllers, middleware
├── ui/
│   └── UserManagement.UI              # ASP.NET Core MVC + Bootstrap 5, cookie auth, proxies calls to the API
├── tests/
│   ├── UserManagement.Application.UnitTests    # Service-level unit tests (Moq)
│   ├── UserManagement.Infrastructure.UnitTests  # Password hasher, JWT, EF Core/SQLite repository tests
│   └── UserManagement.API.IntegrationTests      # WebApplicationFactory end-to-end HTTP tests
└── docs/
    ├── api.md                 # API surface reference
    └── deployment-iis.md       # IIS deployment runbook
```

## Features

- Custom User/Role/RBAC model (no ASP.NET Core Identity) with PBKDF2-HMAC-SHA256 password hashing.
- JWT access tokens + rotating refresh tokens with family-based reuse/breach detection and revocation.
- Account lockout after repeated failed logins; password-reset via SMTP (or a dev "pickup directory").
- Admin dashboard: paged/searchable/sortable user list (DataTables server-side), user CRUD, role
  assignment, role CRUD, and an audit log viewer — all built on real API calls, no mock data.
- The UI never sees the JWT directly: it signs the user into its own encrypted auth cookie and
  transparently attaches/refreshes the API bearer token via an `HttpClient` delegating handler.
- Serilog structured logging (console in Development, rolling files in Production) in both apps.
- Rate limiting on `/api/auth/*`, global exception-handling middleware with a uniform response
  envelope, Swagger with a bearer auth scheme, seeded `Admin`/`User` roles and an optional default
  admin account.
- 41 automated tests across unit and integration layers (see [Testing](#testing)).

## Prerequisites

- **.NET 10 SDK**. If it's not on `PATH`:
  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
  ```

## Running locally

Terminal 1 — API (SQLite DB + roles/admin are auto-migrated/seeded on first run):

```bash
cd src/UserManagement.API
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

The default dev admin is `admin@example.com` / `ChangeMe123!` (see `appsettings.Development.json`,
`Seed:AdminPassword`) — **change it after first login**.

Terminal 2 — UI (points at the API via `Api:BaseUrl` in `appsettings.Development.json`):

```bash
cd ui/UserManagement.UI
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

Then browse to the UI's URL (see its `Properties/launchSettings.json`), sign in, and explore
**Profile**, and — as the seeded admin — **Users / Roles / Audit Logs** in the top nav.

API docs (Swagger) are available at `/swagger` on the API while running in Development.

## Configuration

Both apps read from `appsettings.json` → `appsettings.{Environment}.json` → environment variables
(double-underscore nesting, e.g. `Jwt__Key`). Key sections:

- **API** — `Jwt` (signing key/issuer/audience/lifetimes), `Smtp` (password-reset email delivery),
  `Security` (lockout policy, reset-token TTL, reset URL), `Seed` (default roles/admin), `Cors`
  (must include the UI's origin), `ConnectionStrings:DefaultConnection` (SQLite file path).
- **UI** — `Api:BaseUrl` (the API's base URL).

`Jwt:Key` must be at least 32 UTF-8 bytes; the API throws on startup otherwise. Never commit real
secrets — the checked-in `appsettings.Production.json` files are templates with blank secret values,
intended to be filled in via IIS app-pool environment variables (see the deployment doc).

## Building and testing

```bash
dotnet build UserManagement.slnx
dotnet test UserManagement.slnx
```

This runs all 41 tests: unit tests for password policy/normalisation/auth-service logic (mocked
repositories), EF Core/SQLite repository + JWT/PBKDF2 security tests, and full HTTP integration
tests (register → login → authorized/forbidden access) against an in-process `WebApplicationFactory`
with a real (temporary, per-test-class) SQLite database.

## Generating a new EF Core migration

```bash
dotnet tool install --global dotnet-ef   # once
dotnet ef migrations add <Name> -p src/UserManagement.Infrastructure -s src/UserManagement.API -o Persistence/Migrations
```

Migrations are applied automatically on API startup (`db.Database.MigrateAsync()`); no manual
`dotnet ef database update` step is required in any environment.

## Deployment

See [`docs/deployment-iis.md`](docs/deployment-iis.md) for the full IIS-on-Windows runbook (two
sites/app pools, hosting bundle, permissions, environment variables, verification steps) and
[`docs/api.md`](docs/api.md) for the API surface reference.

## Notable design decisions

- **Custom auth, not ASP.NET Core Identity** — a lean `User`/`Role`/`UserRole` model, PBKDF2 hashing,
  and a from-scratch refresh-token store were chosen per the approved plan.
- **Hybrid UI auth** — the browser only ever holds the MVC app's own encrypted cookie; the JWT/refresh
  token pair lives inside that cookie's (encrypted) authentication properties and is never exposed to
  client-side JavaScript.
- **Settings location** — `Jwt`/`Smtp`/`Security`/`Seed`/`Cors` settings classes live in
  `Application/Common/Settings` (not the API project) because `Infrastructure` (which needs them for
  `JwtTokenService`, `SmtpEmailSender`, `DbSeeder`) cannot reference the API project under the Clean
  Architecture dependency rule.
- **SQLite** was chosen for zero-install simplicity; `Guid`/`DateTime` are stored as invariant,
  sortable strings via global EF Core value converters.
