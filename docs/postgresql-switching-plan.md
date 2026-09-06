# Database Provider Switching Plan — SQLite and PostgreSQL

Status: **Implemented — 2026-09-06**

## 1. Goal

Allow `UserManagement.API` to run against either SQLite or PostgreSQL, selected by
configuration, while keeping the repository/application APIs unchanged. SQLite remains the
development/test-friendly default, and PostgreSQL can be enabled for deployments that need a
server database and concurrent writers.

## 2. Current state

- `UserManagement.Infrastructure.DependencyInjection` always calls `UseSqlite(...)`.
- The API reads `Database:Provider` and the matching named connection string.
- The API applies pending EF Core migrations and runs `DbSeeder` during startup.
- Existing migrations are in `src/UserManagement.Infrastructure/Persistence/Migrations`.
- `AppDbContext` applies Guid and UTC `DateTime` string converters originally chosen for SQLite.
- Integration tests replace the production context with a temporary SQLite database.
- Infrastructure now references both `Microsoft.EntityFrameworkCore.Sqlite` and
  `Npgsql.EntityFrameworkCore.PostgreSQL`.

## 3. Proposed configuration

Use an explicit provider selector rather than guessing the provider from the connection-string
format:

```json
{
  "Database": {
    "Provider": "Sqlite"
  },
  "ConnectionStrings": {
    "Sqlite": "Data Source=usermanagement.db",
    "PostgreSql": ""
  }
}
```

The supported provider values are `Sqlite` and `PostgreSql` (case-insensitive). The selected
provider determines which named connection string is required:

- `Sqlite` → `ConnectionStrings:Sqlite`
- `PostgreSql` → `ConnectionStrings:PostgreSql`

The current `ConnectionStrings:DefaultConnection` setting will be handled deliberately during the
change: development can be migrated to the named SQLite key, while a compatibility fallback can
be retained temporarily if desired. Invalid provider names and missing selected connection strings
must fail at startup with a clear configuration error.

Production PostgreSQL credentials will be supplied through environment variables or a secret
store, for example `Database__Provider=PostgreSql` and
`ConnectionStrings__PostgreSql=Host=...;Database=...;Username=...;Password=...`; no real
credentials will be committed.

## 4. Design decisions

1. Keep a single `AppDbContext` and provider-agnostic repositories/services.
2. Select the EF Core provider only in Infrastructure dependency registration.
3. Add `Npgsql.EntityFrameworkCore.PostgreSQL` at the same EF Core major/minor version as the
   existing packages.
4. Keep startup `Database.MigrateAsync()` and seeding behavior, but verify it against both
   providers before rollout.
5. Preserve the existing SQLite storage format initially by retaining the Guid/DateTime
   converters. This minimizes schema and data compatibility risk. PostgreSQL-native types can be
   considered separately after the provider switch is stable.
6. Treat existing SQLite data migration as a separate operational step; changing the provider
   does not automatically copy application data from the `.db` file to PostgreSQL.

## 5. Implementation steps

### Infrastructure

- Add the Npgsql EF Core provider package to
  `src/UserManagement.Infrastructure/UserManagement.Infrastructure.csproj`.
- Add a small, strongly typed database settings model or constants for the provider values and
  configuration keys, following the existing Application settings pattern where appropriate.
- Refactor `AddInfrastructure` to:
  - read and validate the provider;
  - resolve the matching named connection string;
  - call `UseSqlite` for SQLite;
  - call `UseNpgsql` for PostgreSQL;
  - use the existing migrations assembly for both providers;
  - throw a clear startup exception for unsupported/missing configuration.
- Keep all repositories, `UnitOfWork`, seeding, and application services unchanged unless
  provider-specific behavior is discovered during testing.
- Review EF mappings for SQLite-only assumptions, especially generated SQL, key/value types,
  indexes, cascade behavior, and case-sensitive comparisons.

### API configuration and startup

- Update `appsettings.json`, `appsettings.Development.json`, and
  `appsettings.Production.json` with the provider selector and named connection-string template.
- Keep production secrets blank in checked-in files.
- Confirm the existing startup migration and seeding path works for both providers.
- Document that switching an existing deployment to PostgreSQL requires schema migration plus
  data transfer; it is not a transparent live switch.

### Migrations and data

- Do not assume the current SQLite migration history is a PostgreSQL-ready database history.
- Generate and validate a PostgreSQL-compatible migration baseline/model snapshot using the
  selected provider and the current model. If EF Core requires provider-specific migration
  handling, keep provider-specific migration histories/assemblies rather than making one provider
  mutate the other provider’s history.
- Test a clean PostgreSQL database from startup through all current migrations and seeding.
- For SQLite-to-PostgreSQL adoption, provide a documented, repeatable export/import procedure:
  1. back up the SQLite database and profile-picture files;
  2. stop writes or use a maintenance window;
  3. create the PostgreSQL schema;
  4. transfer users, roles, permissions, tokens, and audit data with explicit type/format
     validation;
  5. verify row counts, key relationships, unique indexes, timestamps, and seeded/admin login;
  6. switch configuration and perform endpoint/UI smoke tests.
- Do not delete or overwrite the SQLite database as part of the code change.

### Tests

- Add/extend provider-registration tests for:
  - SQLite selection;
  - PostgreSQL selection;
  - unsupported provider;
  - missing selected connection string.
- Keep existing SQLite unit and API integration tests unchanged in behavior.
- Add PostgreSQL integration coverage using a real PostgreSQL instance when the test environment
  provides one (for example, a configured local/container connection); otherwise validate the
  provider registration and run a documented manual PostgreSQL smoke test.
- Run the full solution build and test suite after targeted tests.

### Documentation

- Update `docs/api.md` only if externally visible startup/configuration behavior needs to be
  referenced there.
- Update `docs/deployment-iis.md` with SQLite versus PostgreSQL configuration, permissions,
  secret handling, backup, migration, and rollback procedures.
- Add the final provider configuration and operational migration procedure to this plan after
  implementation.
- Update the older architecture/deployment notes that currently describe SQLite as the only
  database and say provider switching is future work.

## 6. Validation checklist

- Build succeeds with the Npgsql package and .NET 10.
- API starts with SQLite using the existing development database.
- API starts against a clean PostgreSQL database, applies migrations, and seeds successfully.
- Login, refresh-token rotation/reuse detection, user/role management, audit logging, profile
  pictures, and dashboard queries work on both providers.
- Existing SQLite integration tests pass.
- PostgreSQL schema has expected primary keys, foreign keys, unique indexes, query filters, and
  timestamp behavior.
- Invalid configuration fails fast with an actionable message.
- Switching back from PostgreSQL to SQLite is a configuration change only after the selected
  database has its own compatible schema/data; it does not synchronize data automatically.

## 7. Out of scope

- Running both databases simultaneously for read/write replication.
- Automatic bidirectional synchronization.
- Removing SQLite support.
- Replacing the custom authentication model with ASP.NET Core Identity.
- Optimizing PostgreSQL-specific indexes or changing Guid/DateTime columns to native PostgreSQL
  types in the same change unless validation proves the current shared model cannot work.

## 8. Confirmation gate

Implementation completed in this order:

1. Added provider package/settings and provider-aware registration.
2. Updated configuration templates and validation.
3. Added a PostgreSQL-specific migration assembly and provider-focused tests.
4. Updated deployment documentation.
5. Built the solution and passed the provider registration tests.

The supplied connection string was not committed or written to configuration. Configure it
outside source control, for example:

```powershell
$env:Database__Provider = "PostgreSql"
$env:ConnectionStrings__PostgreSql = "Host=192.168.123.150;Port=2665;Database=usermanagement;Username=root;Password=<secret>"
dotnet run --project src/UserManagement.API
```

The PostgreSQL host was reachable from the development environment, but no live migration or data
transfer was executed against it. Startup migration changes the selected database, so run that
operation only after taking a backup and confirming the target database is intended for this API.
