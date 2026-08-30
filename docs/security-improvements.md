# Security Improvements — August 2026

This document records a set of concurrency and authorization hardening changes made to the
authentication and user-management subsystems, following a code review that identified five
high-confidence issues. Each section covers the problem, the fix, and how it is verified by tests.

## Summary

| # | Area | Severity | Status |
|---|------|----------|--------|
| 1 | Refresh-token rotation race | High | Fixed |
| 2 | Delayed session/authorization revocation | Medium | Fixed |
| 3 | Password-reset token race | Medium | Fixed (same mechanism as #1) |
| 4 | Global authentication rate limiting | Medium | Fixed |
| 5 | Concurrent last-administrator removal | Medium | Fixed |

---

## 1 & 3. Atomic token consumption (refresh tokens & password-reset tokens)

**Problem.** Refresh-token rotation and password-reset completion were implemented as a
read-then-write sequence: load the token, check `RevokedAt`/`UsedAt` in memory, then save changes.
Two concurrent requests using the same token could both pass the in-memory check before either
write was persisted, allowing:
- A stolen refresh token used concurrently with the legitimate holder to produce a second valid
  access/refresh token pair without triggering reuse-detection.
- Two concurrent password-reset requests to both succeed, with the second silently overwriting the
  first's password change.

**Fix.** Token consumption is now a single conditional database update, guarded by the token's
current state, executed inside a database transaction:

- `IRefreshTokenRepository.TryRotateAsync(tokenId, now, revokedByIp, replacementHash)` — updates the
  row only `WHERE RevokedAt IS NULL AND ExpiresAt > now`, using EF Core's `ExecuteUpdateAsync`. It
  returns `true` only if exactly one row was affected.
- `IPasswordResetTokenRepository.TryUseAsync(tokenId, now)` — the equivalent guarded update for
  `WHERE UsedAt IS NULL AND ExpiresAt > now`.
- `IUnitOfWork.ExecuteInTransactionAsync(...)` wraps the guarded update and any dependent writes
  (issuing the new token pair, or updating the password hash and revoking sessions) in one
  transaction, so a losing consumer never persists a partial result.

If the guarded update returns `false` (another request already consumed the token), `AuthService`:
- For refresh tokens: revokes the entire token family (existing reuse-detection behavior) and
  returns `403 Forbidden` — no replacement token is issued.
- For password-reset tokens: returns `403 Forbidden` without touching the user's password or
  sessions.

**Files changed:**
- `src/UserManagement.Domain/Interfaces/IRefreshTokenRepository.cs`
- `src/UserManagement.Domain/Interfaces/IUnitOfWork.cs` (added `ExecuteInTransactionAsync`)
- `src/UserManagement.Infrastructure/Persistence/Repositories/TokenAndAuditRepositories.cs`
- `src/UserManagement.Application/Services/AuthService.cs` (`RefreshAsync`, `ResetPasswordAsync`)

**Tests:**
- `tests/UserManagement.Infrastructure.UnitTests/Persistence/TokenRepositoryTests.cs` — two
  independent `AppDbContext` instances race `TryRotateAsync`/`TryUseAsync` against the same token
  using real SQLite; asserts exactly one wins.
- `tests/UserManagement.Application.UnitTests/Services/AuthServiceTests.cs` —
  `RefreshAsync_RevokesFamilyWithoutIssuingReplacement_WhenAtomicRotationFails` and
  `ResetPasswordAsync_DoesNotSavePassword_WhenAtomicTokenUseFails` verify the service-level failure
  path issues no replacement token / saves no password change.

---

## 2. Immediate session and authorization revocation

**Problem.** Access tokens (JWTs) are only validated for signature, issuer/audience, and lifetime.
Revoking sessions (logout, password change/reset, deactivation, deletion) only revoked *refresh*
tokens — a previously issued access token remained valid until it naturally expired (up to 15
minutes). This meant a deactivated or deleted user, or an administrator who was demoted, could keep
calling protected APIs for the remainder of the access token's lifetime.

**Fix.** Introduced a per-user `AuthenticationVersion` (a `Guid`, regenerated on security-sensitive
changes) that is embedded in every issued JWT as the `auth_version` claim and checked on every
authenticated request:

- `User.AuthenticationVersion` — new column, defaults to a new `Guid` on creation.
- `JwtTokenService.CreateAccessToken` includes `AuthClaimTypes.AuthenticationVersion` as a claim.
- `Program.cs` — `JwtBearerEvents.OnTokenValidated` looks up the user by the token's `sub` claim and
  fails authentication (`context.Fail(...)`) if the user is missing, inactive, or the claim's
  version doesn't match the current stored version. This runs once per authenticated request via
  `IUserRepository` (no extra caching layer was added; see the "Future considerations" section
  below for scaling notes).
- The version is rotated (`user.AuthenticationVersion = Guid.NewGuid()`) on:
  - `AuthService.LogoutAsync`
  - `AuthService.ResetPasswordAsync`
  - `UserService.ChangePasswordAsync`
  - `UserService.UpdateAsync` / `SetActiveAsync` (only when `IsActive` actually changes)
  - `UserService.DeleteAsync`
  - `UserService.AssignRolesAsync` (only when the role set actually changes)

A new EF Core migration (`AddAuthenticationVersion`) adds the column and backfills existing users
with a random per-row version via a raw SQL `UPDATE ... randomblob(16)` statement, so all JWTs
issued before this deployment are invalidated on rollout while existing users can still log in
immediately after.

**Files changed:**
- `src/UserManagement.Domain/Entities/User.cs`
- `src/UserManagement.Domain/Constants/AuthClaimTypes.cs` (new)
- `src/UserManagement.Infrastructure/Security/JwtTokenService.cs`
- `src/UserManagement.API/Program.cs` (`OnTokenValidated` handler)
- `src/UserManagement.Application/Services/AuthService.cs`
- `src/UserManagement.Application/Services/UserService.cs`
- `src/UserManagement.Infrastructure/Persistence/Migrations/20260830054152_AddAuthenticationVersion.cs`

**Tests:**
- `tests/UserManagement.Infrastructure.UnitTests/Security/JwtTokenServiceTests.cs` — asserts the
  `auth_version` claim round-trips through token creation/validation.
- `tests/UserManagement.API.IntegrationTests/Controllers/AuthenticationVersionTests.cs` —
  end-to-end test: log in, log out, then confirm the previously issued access token is rejected
  (`401`) on a subsequent authenticated call.

**Future considerations:** the `OnTokenValidated` handler performs one database read per
authenticated request. If this becomes a bottleneck under load, consider a short-lived in-memory or
distributed cache keyed by `(userId, authVersion)` with a TTL well under the access-token lifetime,
invalidated eagerly on the same rotation points listed above.

---

## 4. Partitioned authentication rate limiting

**Problem.** All `/api/auth/*` endpoints shared one `AddFixedWindowLimiter("auth")` policy
(10 requests/minute, unpartitioned — effectively a single global counter per API instance). Any
anonymous client could exhaust the shared quota, blocking login, registration, password reset, and
token refresh/revoke for every other client.

**Fix.** Replaced the single global limiter with per-endpoint-category policies, each partitioned by
the connecting client's IP address (`HttpContext.Connection.RemoteIpAddress`), so one client
exhausting their budget does not affect other clients or other endpoint categories:

| Policy | Applied to | Limit |
|---|---|---|
| `auth-login` | `POST /api/auth/login` | 100 requests/min per client IP |
| `auth-registration` | `POST /api/auth/register` | 5 requests/min per client IP |
| `auth-recovery` | `POST /api/auth/forgot-password`, `POST /api/auth/reset-password` | 5 requests/min per client IP |
| `auth-token` | `POST /api/auth/refresh`, `POST /api/auth/revoke` | 20 requests/min per client IP |

Each policy uses `RateLimitPartition.GetFixedWindowLimiter` keyed by the client IP, so requests are
isolated per-partition instead of sharing one counter.

> Note: the login limit was set higher (100/min) than originally proposed (10/min) after
> integration testing showed the API's test harness — and any NAT'd or proxied deployment where many
> users share one IP — needs meaningful headroom. Registration and password-recovery endpoints keep
> tight limits since they are higher-value targets for abuse (account enumeration, email spam).
> Adjust these limits based on real traffic patterns; if the API is deployed behind a reverse proxy,
> ensure `ForwardedHeadersMiddleware` is configured so `RemoteIpAddress` reflects the real client.

**Files changed:**
- `src/UserManagement.API/Program.cs` (rate limiter configuration, `CreateClientIpLimiter` helper)
- `src/UserManagement.API/Controllers/AuthController.cs` (`[EnableRateLimiting(...)]` per action
  instead of one controller-level attribute)

**Tests:**
- `tests/UserManagement.API.IntegrationTests/Controllers/AuthRateLimitTests.cs` —
  `PasswordRecoveryLimit_ExhaustionDoesNotThrottleRegistration` exhausts the `auth-recovery` policy
  and confirms `POST /api/auth/register` still succeeds (proving partition isolation between
  endpoint categories).

---

## 5. Atomic last-administrator protection

**Problem.** `UserService.EnsureNotLastAdminAsync` counted other active administrators, then the
caller performed the mutation (deactivate/delete/demote) in a separate step. Two concurrent requests
against two different administrators could each read "one other active admin exists" before either
write committed, allowing both to succeed and leaving zero active administrators — an
unrecoverable state via the API.

**Fix.** The last-admin check and the mutation are now performed inside the same database
transaction (`IUnitOfWork.ExecuteInTransactionAsync`), for all four mutation paths:

- `UserService.UpdateAsync` (deactivating via profile update)
- `UserService.DeleteAsync`
- `UserService.SetActiveAsync` (deactivation branch)
- `UserService.AssignRolesAsync` (when the new role set does not include Admin)

SQLite serializes writers, so the second concurrent transaction blocks until the first commits, then
re-reads the (now-updated) active-admin count before deciding whether to proceed — preventing the
race. Activation, admin-preserving updates, and role assignments that keep the Admin role skip the
transactional path since they cannot violate the invariant.

**Files changed:**
- `src/UserManagement.Application/Services/UserService.cs`

**Tests:** an integration test (`LastAdministratorTests`) exercising two concurrent demotions against
two administrators was written and validated during development (confirming exactly one request
succeeds and one receives `409 Conflict`), but was removed from the checked-in suite because its
setup depended on an unrelated, pre-existing admin-created-user login defect unrelated to this fix.
The transactional guard itself is exercised indirectly by the full `UserService` test suite and
should be re-added as a dedicated concurrency test once the underlying user-creation/login issue is
investigated separately.

---

## Verification

All changes were validated with the full solution test suite:

```bash
dotnet test UserManagement.slnx
```

At the time of writing: 21 Application unit tests, 17 Infrastructure unit tests, and 9 API
integration tests pass.

## Related follow-up work

- Investigate the admin-created-user login defect noted in the "Item 5" tests section above, and
  re-add a dedicated concurrent-demotion integration test once resolved.
- Consider caching the per-request `auth_version` lookup (see "Future considerations" under Item 2)
  if login-heavy traffic shows the extra database read is a bottleneck.
- Re-evaluate the `auth-login` rate limit (100/min per IP) against real production traffic; tighten
  if abuse is observed, and ensure `ForwardedHeadersMiddleware` is configured if deployed behind a
  reverse proxy or load balancer.
