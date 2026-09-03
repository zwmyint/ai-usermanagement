using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Application.Common;
using UserManagement.Application.Common.Settings;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Infrastructure.Persistence.Seed;

public class DbSeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly SeedSettings _settings;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(AppDbContext db, IPasswordHasher hasher, IOptions<SeedSettings> settings, ILogger<DbSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Database seeding is disabled.");
            return;
        }

        await SeedRolesAsync(ct);
        await SeedPermissionsAsync(ct);
        await SeedDefaultRolePermissionsAsync(ct);
        await SeedAdminAsync(ct);
        await SeedDemoUsersAsync(ct);
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        foreach (var name in RoleNames.SystemRoles)
        {
            var normalized = Normalizer.Normalize(name);
            if (await _db.Roles.AnyAsync(x => x.NormalizedName == normalized, ct)) continue;

            _db.Roles.Add(new Role
            {
                Name = name,
                NormalizedName = normalized,
                Description = name == RoleNames.Admin
                    ? "Full administrative access to users, roles and audit logs."
                    : "Standard authenticated user.",
                IsSystemRole = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "system"
            });

            _logger.LogInformation("Seeded system role {Role}.", name);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Additive, idempotent sync of the fixed <see cref="PermissionNames"/> list into the Permissions
    /// table. Runs on every startup so a future code change that adds a new permission constant is
    /// automatically picked up without a manual migration step.
    /// </summary>
    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var added = false;
        foreach (var name in PermissionNames.All)
        {
            var normalized = Normalizer.Normalize(name);
            if (await _db.Permissions.AnyAsync(x => x.NormalizedName == normalized, ct)) continue;

            _db.Permissions.Add(new Permission { Name = name, NormalizedName = normalized });
            added = true;
        }

        if (added) await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// One-time bootstrap of role -&gt; permission assignments, run only while the RolePermissions
    /// table is completely empty (i.e. the very first startup after this feature was introduced).
    /// After that, permission grants are entirely owned by administrators via the Roles admin
    /// screen/API and are never re-created or overwritten here. This also seeds the legacy
    /// Manager/Auditor/Viewer roles (previously hardcoded into authorization policies) so existing
    /// deployments keep working unchanged; from then on they behave like any other admin-editable role.
    /// </summary>
    private async Task SeedDefaultRolePermissionsAsync(CancellationToken ct)
    {
        if (await _db.RolePermissions.AnyAsync(ct)) return;

        var now = DateTime.UtcNow;
        var permissionsByName = await _db.Permissions.ToDictionaryAsync(p => p.NormalizedName, ct);

        var admin = await _db.Roles.FirstOrDefaultAsync(x => x.NormalizedName == Normalizer.Normalize(RoleNames.Admin), ct);
        if (admin is null)
        {
            _logger.LogError("Admin role missing; cannot seed default role permissions.");
            return;
        }

        Grant(admin, PermissionNames.All);
        Grant(await EnsureLegacyRoleAsync(RoleNames.Manager, "Can view and manage users, but not delete or manage roles.", ct),
            PermissionNames.UsersRead, PermissionNames.UsersWrite);
        Grant(await EnsureLegacyRoleAsync(RoleNames.Viewer, "Read-only access to users.", ct),
            PermissionNames.UsersRead);
        Grant(await EnsureLegacyRoleAsync(RoleNames.Auditor, "Read-only access to audit logs.", ct),
            PermissionNames.AuditRead);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded default role permission assignments.");

        void Grant(Role role, params IReadOnlyList<string> permissionNames)
        {
            foreach (var name in permissionNames)
            {
                if (!permissionsByName.TryGetValue(Normalizer.Normalize(name), out var permission)) continue;
                role.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id,
                    AssignedAt = now,
                    AssignedBy = "system"
                });
            }
        }
    }

    private async Task<Role> EnsureLegacyRoleAsync(string name, string description, CancellationToken ct)
    {
        var normalized = Normalizer.Normalize(name);
        var role = await _db.Roles.FirstOrDefaultAsync(x => x.NormalizedName == normalized, ct);
        if (role is not null) return role;

        role = new Role
        {
            Name = name,
            NormalizedName = normalized,
            Description = description,
            IsSystemRole = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system"
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
        return role;
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        var normalizedEmail = Normalizer.Normalize(_settings.AdminEmail);
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(x => x.NormalizedEmail == normalizedEmail, ct))
            return;

        if (string.IsNullOrWhiteSpace(_settings.AdminPassword))
        {
            _logger.LogWarning(
                "Seed:AdminPassword is not configured; the default administrator was not created. " +
                "Set it via environment variable Seed__AdminPassword.");
            return;
        }

        var adminRole = await _db.Roles.FirstOrDefaultAsync(
            x => x.NormalizedName == Normalizer.Normalize(RoleNames.Admin), ct);

        if (adminRole is null)
        {
            _logger.LogError("Admin role missing; cannot seed the default administrator.");
            return;
        }

        var (hash, salt) = _hasher.Hash(_settings.AdminPassword);
        var now = DateTime.UtcNow;

        var admin = new User
        {
            UserName = _settings.AdminUserName,
            NormalizedUserName = Normalizer.Normalize(_settings.AdminUserName),
            Email = _settings.AdminEmail,
            NormalizedEmail = normalizedEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            FirstName = "System",
            LastName = "Administrator",
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = now,
            CreatedBy = "system"
        };
        admin.UserRoles.Add(new UserRole { RoleId = adminRole.Id, AssignedAt = now, AssignedBy = "system" });

        _db.Users.Add(admin);
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Seeded default administrator '{Email}'. Change this password immediately after first login.",
            _settings.AdminEmail);
    }

    /// <summary>
    /// Additive, idempotent seeding of demo accounts: 5 with the User role and 2 with the Manager
    /// role. Each account is created only if no user with its normalized email exists yet, so this
    /// is safe to run on every startup and to extend later with more demo accounts. Skipped entirely
    /// when <see cref="SeedSettings.DemoUserPassword"/> is not configured, mirroring <see cref="SeedAdminAsync"/>.
    /// </summary>
    private async Task SeedDemoUsersAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.DemoUserPassword))
        {
            _logger.LogInformation(
                "Seed:DemoUserPassword is not configured; demo User/Manager accounts were not created.");
            return;
        }

        var demoAccounts = new (string UserName, string FirstName, string LastName, string RoleName)[]
        {
            ("demo.user1", "Demo", "User1", RoleNames.User),
            ("demo.user2", "Demo", "User2", RoleNames.User),
            ("demo.user3", "Demo", "User3", RoleNames.User),
            ("demo.user4", "Demo", "User4", RoleNames.User),
            ("demo.user5", "Demo", "User5", RoleNames.User),
            ("demo.manager1", "Demo", "Manager1", RoleNames.Manager),
            ("demo.manager2", "Demo", "Manager2", RoleNames.Manager),
        };

        var rolesByName = await _db.Roles.ToDictionaryAsync(r => r.NormalizedName, ct);
        var now = DateTime.UtcNow;
        var added = false;

        foreach (var account in demoAccounts)
        {
            var email = $"{account.UserName}@example.com";
            var normalizedEmail = Normalizer.Normalize(email);
            if (await _db.Users.IgnoreQueryFilters().AnyAsync(x => x.NormalizedEmail == normalizedEmail, ct))
                continue;

            if (!rolesByName.TryGetValue(Normalizer.Normalize(account.RoleName), out var role))
            {
                _logger.LogError("{Role} role missing; cannot seed demo user '{Email}'.", account.RoleName, email);
                continue;
            }

            var (hash, salt) = _hasher.Hash(_settings.DemoUserPassword);
            var user = new User
            {
                UserName = account.UserName,
                NormalizedUserName = Normalizer.Normalize(account.UserName),
                Email = email,
                NormalizedEmail = normalizedEmail,
                PasswordHash = hash,
                PasswordSalt = salt,
                FirstName = account.FirstName,
                LastName = account.LastName,
                IsActive = true,
                EmailConfirmed = true,
                CreatedAt = now,
                CreatedBy = "system"
            };
            user.UserRoles.Add(new UserRole { RoleId = role.Id, AssignedAt = now, AssignedBy = "system" });

            _db.Users.Add(user);
            added = true;
        }

        if (added)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded demo User/Manager accounts.");
        }
    }
}
