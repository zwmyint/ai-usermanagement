using Microsoft.EntityFrameworkCore;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;

    public RefreshTokenRepository(AppDbContext db) => _db = db;

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default) =>
        await _db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct);

    public async Task<IReadOnlyList<RefreshToken>> GetByFamilyAsync(Guid familyId, CancellationToken ct = default) =>
        await _db.RefreshTokens.Where(x => x.FamilyId == familyId).ToListAsync(ct);

    public async Task<bool> TryRotateAsync(Guid tokenId, DateTime utcNow, string? revokedByIp,
        string replacementTokenHash, CancellationToken ct = default) =>
        await _db.RefreshTokens
            .Where(x => x.Id == tokenId && x.RevokedAt == null && x.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAt, utcNow)
                .SetProperty(x => x.RevokedByIp, revokedByIp)
                .SetProperty(x => x.RevokedReason, "Rotated.")
                .SetProperty(x => x.ReplacedByTokenHash, replacementTokenHash), ct) == 1;

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    public void Update(RefreshToken token) => _db.RefreshTokens.Update(token);
}

public class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly AppDbContext _db;

    public PasswordResetTokenRepository(AppDbContext db) => _db = db;

    public Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.PasswordResetTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

    public async Task<bool> TryUseAsync(Guid tokenId, DateTime utcNow, CancellationToken ct = default) =>
        await _db.PasswordResetTokens
            .Where(x => x.Id == tokenId && x.UsedAt == null && x.ExpiresAt > utcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, utcNow), ct) == 1;

    public async Task InvalidateExistingAsync(Guid userId, DateTime utcNow, CancellationToken ct = default)
    {
        var outstanding = await _db.PasswordResetTokens
            .Where(x => x.UserId == userId && x.UsedAt == null)
            .ToListAsync(ct);

        foreach (var token in outstanding)
            token.UsedAt = utcNow;
    }

    public async Task AddAsync(PasswordResetToken token, CancellationToken ct = default) =>
        await _db.PasswordResetTokens.AddAsync(token, ct);

    public void Update(PasswordResetToken token) => _db.PasswordResetTokens.Update(token);
}

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _db;

    public AuditLogRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(AuditLog log, CancellationToken ct = default) => await _db.AuditLogs.AddAsync(log, ct);

    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount, int FilteredCount)> SearchAsync(
        string? search, Guid? userId, int? action, DateTime? from, DateTime? to,
        string? sortBy, bool sortDescending, int skip, int take, CancellationToken ct = default)
    {
        var baseQuery = _db.AuditLogs.AsNoTracking();
        var totalCount = await baseQuery.CountAsync(ct);

        var query = baseQuery;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                (x.UserName != null && x.UserName.Contains(term)) ||
                (x.Message != null && x.Message.Contains(term)) ||
                (x.IpAddress != null && x.IpAddress.Contains(term)));
        }

        if (userId.HasValue) query = query.Where(x => x.UserId == userId.Value);
        if (action.HasValue) query = query.Where(x => (int)x.Action == action.Value);
        if (from.HasValue) query = query.Where(x => x.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(x => x.Timestamp <= to.Value);

        var filteredCount = await query.CountAsync(ct);

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "username" => sortDescending ? query.OrderByDescending(x => x.UserName) : query.OrderBy(x => x.UserName),
            "action" => sortDescending ? query.OrderByDescending(x => x.Action) : query.OrderBy(x => x.Action),
            "succeeded" => sortDescending ? query.OrderByDescending(x => x.Succeeded) : query.OrderBy(x => x.Succeeded),
            "timestamp" => sortDescending ? query.OrderByDescending(x => x.Timestamp) : query.OrderBy(x => x.Timestamp),
            _ => query.OrderByDescending(x => x.Timestamp)
        };

        var items = await query.Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount, filteredCount);
    }

    public Task<int> CountAsync(Domain.Enums.AuditAction? action = null, bool? succeeded = null,
        DateTime? from = null, Guid? userId = null, CancellationToken ct = default)
    {
        var query = _db.AuditLogs.AsNoTracking();

        if (action.HasValue) query = query.Where(x => x.Action == action.Value);
        if (succeeded.HasValue) query = query.Where(x => x.Succeeded == succeeded.Value);
        if (from.HasValue) query = query.Where(x => x.Timestamp >= from.Value);
        if (userId.HasValue) query = query.Where(x => x.UserId == userId.Value);

        return query.CountAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLog>> GetRecentByUserAndActionAsync(Guid userId,
        Domain.Enums.AuditAction action, int take, CancellationToken ct = default) =>
        await _db.AuditLogs.AsNoTracking()
            .Where(x => x.UserId == userId && x.Action == action)
            .OrderByDescending(x => x.Timestamp)
            .Take(take)
            .ToListAsync(ct);
}

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await operation(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
