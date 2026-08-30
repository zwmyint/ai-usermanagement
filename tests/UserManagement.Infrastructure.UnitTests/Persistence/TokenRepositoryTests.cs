using Microsoft.EntityFrameworkCore;
using UserManagement.Domain.Entities;
using UserManagement.Infrastructure.Persistence;
using UserManagement.Infrastructure.Persistence.Repositories;

namespace UserManagement.Infrastructure.UnitTests.Persistence;

public class TokenRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

    public TokenRepositoryTests()
    {
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task TryRotateAsync_AllowsOnlyOneConcurrentConsumer()
    {
        var now = DateTime.UtcNow;
        var token = await AddRefreshTokenAsync(now);

        await using var firstDb = CreateDbContext();
        await using var secondDb = CreateDbContext();
        var first = new RefreshTokenRepository(firstDb);
        var second = new RefreshTokenRepository(secondDb);

        var results = await Task.WhenAll(
            first.TryRotateAsync(token.Id, now, "127.0.0.1", "replacement-one"),
            second.TryRotateAsync(token.Id, now, "127.0.0.1", "replacement-two"));

        Assert.Equal(1, results.Count(result => result));

        await using var verificationDb = CreateDbContext();
        var persisted = await verificationDb.RefreshTokens.SingleAsync(x => x.Id == token.Id);
        Assert.NotNull(persisted.RevokedAt);
        Assert.Contains(persisted.ReplacedByTokenHash, new[] { "replacement-one", "replacement-two" });
    }

    [Fact]
    public async Task TryUseAsync_AllowsOnlyOneConcurrentConsumer()
    {
        var now = DateTime.UtcNow;
        var token = await AddPasswordResetTokenAsync(now);

        await using var firstDb = CreateDbContext();
        await using var secondDb = CreateDbContext();
        var first = new PasswordResetTokenRepository(firstDb);
        var second = new PasswordResetTokenRepository(secondDb);

        var results = await Task.WhenAll(
            first.TryUseAsync(token.Id, now),
            second.TryUseAsync(token.Id, now));

        Assert.Equal(1, results.Count(result => result));

        await using var verificationDb = CreateDbContext();
        var persisted = await verificationDb.PasswordResetTokens.SingleAsync(x => x.Id == token.Id);
        Assert.NotNull(persisted.UsedAt);
    }

    public void Dispose()
    {
        foreach (var path in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath};Default Timeout=5")
            .Options);

    private async Task<RefreshToken> AddRefreshTokenAsync(DateTime now)
    {
        await using var db = CreateDbContext();
        var user = CreateUser();
        var token = new RefreshToken
        {
            User = user,
            UserId = user.Id,
            TokenHash = Guid.NewGuid().ToString("N"),
            FamilyId = Guid.NewGuid(),
            ExpiresAt = now.AddHours(1),
            CreatedAt = now
        };
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<PasswordResetToken> AddPasswordResetTokenAsync(DateTime now)
    {
        await using var db = CreateDbContext();
        var user = CreateUser();
        var token = new PasswordResetToken
        {
            User = user,
            UserId = user.Id,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = now.AddHours(1),
            CreatedAt = now
        };
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    private static User CreateUser() => new()
    {
        UserName = Guid.NewGuid().ToString("N")[..16],
        NormalizedUserName = Guid.NewGuid().ToString("N")[..16],
        Email = $"{Guid.NewGuid():N}@example.com",
        NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM",
        PasswordHash = "hash",
        PasswordSalt = "salt",
        IsActive = true
    };
}
