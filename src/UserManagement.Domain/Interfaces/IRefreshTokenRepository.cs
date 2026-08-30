using UserManagement.Domain.Entities;

namespace UserManagement.Domain.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> GetByFamilyAsync(Guid familyId, CancellationToken ct = default);
    Task<bool> TryRotateAsync(Guid tokenId, DateTime utcNow, string? revokedByIp,
        string replacementTokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    void Update(RefreshToken token);
}

public interface IPasswordResetTokenRepository
{
    Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<bool> TryUseAsync(Guid tokenId, DateTime utcNow, CancellationToken ct = default);
    Task InvalidateExistingAsync(Guid userId, DateTime utcNow, CancellationToken ct = default);
    Task AddAsync(PasswordResetToken token, CancellationToken ct = default);
    void Update(PasswordResetToken token);
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount, int FilteredCount)> SearchAsync(
        string? search, Guid? userId, int? action, DateTime? from, DateTime? to,
        string? sortBy, bool sortDescending, int skip, int take, CancellationToken ct = default);
    Task<int> CountAsync(Enums.AuditAction? action = null, bool? succeeded = null, DateTime? from = null,
        Guid? userId = null, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> GetRecentByUserAndActionAsync(Guid userId, Enums.AuditAction action, int take,
        CancellationToken ct = default);
}
