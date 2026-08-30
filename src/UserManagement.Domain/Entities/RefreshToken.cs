using UserManagement.Domain.Common;

namespace UserManagement.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 hash of the raw refresh token. The raw value is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Groups a rotation chain so the whole family can be revoked on reuse detection.</summary>
    public Guid FamilyId { get; set; } = Guid.NewGuid();

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);
}
