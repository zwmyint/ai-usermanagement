# Deploying to IIS on Windows Server

This guide covers publishing **UserManagement.API** and **UserManagement.UI** as two independent
IIS sites/app pools on the same or separate Windows Server machines.

## 1. Prerequisites (on the Windows Server)

1. **IIS** with the **Web Server (IIS)** role and these role services: Static Content, Default
   Document, HTTP Errors, Request Filtering, `ASP.NET` is *not* required (we use ASP.NET Core).
2. **.NET 10 Hosting Bundle** (includes the ASP.NET Core Module v2, `ANCM`, and the .NET runtime):
   - Download: `https://dotnet.microsoft.com/download/dotnet/10.0` → "Hosting Bundle" (Windows).
   - Install, then run `iisreset` (or reboot) so IIS picks up the module.
3. Verify: `dotnet --list-runtimes` should list `Microsoft.AspNetCore.App 10.0.x` and
   `Microsoft.NETCore.App 10.0.x`.
4. A valid TLS certificate for each public hostname (or a self-signed cert for internal testing),
   installed into the server's certificate store.

## 2. Publish the applications

From the repository root (or your CI/CD pipeline), using the .NET 10 SDK:

```powershell
dotnet publish src\UserManagement.API\UserManagement.API.csproj -c Release -o publish\api
dotnet publish ui\UserManagement.UI\UserManagement.UI.csproj  -c Release -o publish\ui
```

`dotnet publish` on an `Microsoft.NET.Sdk.Web` project automatically generates the `web.config`
IIS uses to launch the app via the ASP.NET Core Module (in-process hosting, the default). You do
**not** need to hand-author `web.config`.

Copy the contents of `publish\api` and `publish\ui` to the server, e.g.:

```
C:\inetpub\usermanagement\api\
C:\inetpub\usermanagement\ui\
```

Each folder needs a writable `App_Data` subfolder (create it if missing) for the SQLite database
when SQLite is selected, Serilog file logs, and persisted Data Protection keys:

```powershell
New-Item -ItemType Directory -Path C:\inetpub\usermanagement\api\App_Data\logs -Force
New-Item -ItemType Directory -Path C:\inetpub\usermanagement\ui\App_Data\logs -Force
```

## 3. Create two Application Pools

Open **IIS Manager** → **Application Pools** → **Add Application Pool** (repeat for both):

| Setting | API pool (`UserManagementApiPool`) | UI pool (`UserManagementUiPool`) |
|---|---|---|
| .NET CLR version | **No Managed Code** | **No Managed Code** |
| Managed pipeline mode | Integrated | Integrated |
| Identity | `ApplicationPoolIdentity` (default) or a dedicated service account | same |
| Start mode | AlwaysRunning (recommended) | AlwaysRunning |
| Idle time-out | 0 (disable, or increase) | 0 (disable, or increase) |

Grant the app pool identity **Modify** permissions on each app's `App_Data` folder (and the whole
publish folder for read):

```powershell
icacls "C:\inetpub\usermanagement\api" /grant "IIS AppPool\UserManagementApiPool:(OI)(CI)RX"
icacls "C:\inetpub\usermanagement\api\App_Data" /grant "IIS AppPool\UserManagementApiPool:(OI)(CI)M"
icacls "C:\inetpub\usermanagement\ui" /grant "IIS AppPool\UserManagementUiPool:(OI)(CI)RX"
icacls "C:\inetpub\usermanagement\ui\App_Data" /grant "IIS AppPool\UserManagementUiPool:(OI)(CI)M"
```

## 4. Create two Sites

**Site 1 — API** (e.g. `api.usermanagement.local`):
- Physical path: `C:\inetpub\usermanagement\api`
- Application pool: `UserManagementApiPool`
- Binding: `https` on port 443 with your certificate (add an `http→https` redirect binding or rely
  on the app's own `UseHttpsRedirection`/`UseHsts`).

**Site 2 — UI** (e.g. `usermanagement.local` or `www.usermanagement.local`):
- Physical path: `C:\inetpub\usermanagement\ui`
- Application pool: `UserManagementUiPool`
- Binding: `https` on port 443 (different hostname/IP/port than the API site) with your certificate.

## 5. Configure environment-specific settings

Prefer **environment variables on the Application Pool** (via IIS Manager → site → Configuration
Editor → `system.webServer/aspNetCore/environmentVariables`, or `appcmd`) over editing the shipped
`appsettings.Production.json` in place, especially for secrets:

```powershell
# API pool — required
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='ASPNETCORE_ENVIRONMENT',value='Production']" /commit:apphost
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='Jwt__Key',value='<a random 32+ byte secret, same value the UI never needs>']" /commit:apphost
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='Seed__AdminPassword',value='<a strong one-time admin password>']" /commit:apphost
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='Smtp__Host',value='smtp.your-provider.com']" /commit:apphost
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='Smtp__UserName',value='...']" /commit:apphost
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='Smtp__Password',value='...']" /commit:apphost
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='Cors__AllowedOrigins__0',value='https://usermanagement.local']" /commit:apphost
# Select PostgreSQL instead of SQLite. Store the password in a secret-management system where possible.
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='Database__Provider',value='PostgreSql']" /commit:apphost
appcmd.exe set config "UserManagementApi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='ConnectionStrings__PostgreSql',value='Host=db.example.com;Port=5432;Database=usermanagement;Username=usermanagement;Password=...']" /commit:apphost

# UI pool — required
appcmd.exe set config "UserManagementUi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='ASPNETCORE_ENVIRONMENT',value='Production']" /commit:apphost
appcmd.exe set config "UserManagementUi/" -section:system.webServer/aspNetCore /+"environmentVariables.[name='Api__BaseUrl',value='https://api.usermanagement.local/']" /commit:apphost
```

Double-underscore (`__`) is the standard ASP.NET Core convention for nested configuration keys in
environment variables (`Jwt:Key` → `Jwt__Key`).

Update `appsettings.Production.json` in each publish folder for the non-secret values (connection
string path, log paths, token lifetimes, `Security:ResetPasswordUrl`, `Cors:AllowedOrigins`) —
these files are checked into source control as *templates* with blank secrets.

## 6. First run / database migration

The API applies pending EF Core migrations and seeds the `Admin`/`User` roles (and an optional
default admin, if `Seed:AdminPassword` is set) automatically on startup — no manual `dotnet ef
database update` step is required in production. With SQLite selected, confirm the database file
was created at `App_Data\usermanagement.db`; with PostgreSQL selected, verify the
`__EFMigrationsHistory` table and application tables in the configured database. Check
`App_Data\logs\api-*.log` for the seeding messages.

**Change the seeded admin password immediately after first login**, then consider removing
`Seed__AdminPassword` from the app pool configuration (seeding is idempotent — it only creates the
account if it does not already exist).

## 7. Verify

- `https://api.usermanagement.local/health` → `{"status":"Healthy", ...}`
- `https://api.usermanagement.local/swagger` → Swagger UI (only in `Development`; disable/remove
  access in production, or restrict via IP/authentication rules if you want it available).
- `https://usermanagement.local/` → the UI home page; sign in with the seeded admin, confirm
  **Users / Roles / Audit Logs** are reachable and populated.

## 8. Notes & hardening

### Database provider selection

The API supports `Sqlite` (the default) and `PostgreSql`. Set `Database__Provider` and the
matching connection string (`ConnectionStrings__Sqlite` or `ConnectionStrings__PostgreSql`) as
environment variables. The PostgreSQL migration baseline is kept in the
`UserManagement.Infrastructure.PostgreSqlMigrations` assembly; SQLite uses the existing
Infrastructure migrations. Switching providers does not copy data. For an existing SQLite
deployment, back up the `.db` file (including `-wal`/`-shm` when present), export/import the
application data into PostgreSQL, verify relationships and login, and only then switch the API
configuration. To roll back, stop the API, restore the previous provider configuration and its
database backup, and verify the API before reopening traffic.

- **HTTPS only.** Both apps call `UseHsts()`/`UseHttpsRedirection()` outside Development; make sure
  IIS bindings terminate TLS (or forward `X-Forwarded-Proto` if TLS is terminated upstream by a
  load balancer, and add `UseForwardedHeaders()` accordingly).
- **CORS**: the API's `Cors:AllowedOrigins` must list the UI's public URL(s) exactly, or browser
  calls made directly from JS would be blocked (the current UI calls the API server-side via
  `HttpClient`, not from the browser, so CORS mainly matters if you build additional API consumers).
- **Data Protection keys** are persisted to each app's `App_Data\keys` folder so that cookies/JWT
  validation created before an app-pool recycle remain valid — back this folder up like the
  database, and do not point two different app instances at the same physical path.
- **Rate limiting** on `/api/auth/*` is enabled in-process (10 requests/minute per instance). If you
  scale the API to multiple servers behind a load balancer, consider a distributed limiter or an
  API Management layer instead.
- **Logs** roll daily under `App_Data\logs` for both apps (`retainedFileCountLimit: 30`); ship them
  to your centralized logging solution if required.
- **Backups**: back up `App_Data\usermanagement.db` (SQLite) regularly; there is no separate DB
  server to manage since this is intentionally SQLite-based per the approved plan.
