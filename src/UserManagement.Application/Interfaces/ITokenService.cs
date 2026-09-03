using System.Security.Claims;
using UserManagement.Domain.Entities;

namespace UserManagement.Application.Interfaces;

public record AccessToken(string Token, DateTime ExpiresAt);

/// <summary>Raw token returned to the caller plus the hash persisted in the database.</summary>
public record TokenPair(string RawToken, string TokenHash, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user, IEnumerable<string> roles, IEnumerable<string> permissions);
    TokenPair CreateRefreshToken();
    TokenPair CreatePasswordResetToken();
    string HashToken(string rawToken);
    ClaimsPrincipal? ValidateAccessToken(string token, bool validateLifetime = true);
}

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsInRole(string role);
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
