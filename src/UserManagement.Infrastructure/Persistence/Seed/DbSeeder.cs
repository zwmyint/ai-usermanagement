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
        await SeedAdminAsync(ct);
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
}
