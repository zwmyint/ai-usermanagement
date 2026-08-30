using UserManagement.Application.DTOs;

namespace UserManagement.API.Common;

/// <summary>Builds an <see cref="AuditContext"/> from the current HTTP request (client IP + user agent).</summary>
public static class AuditContextExtensions
{
    public static AuditContext ToAuditContext(this HttpContext context)
    {
        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
            ?? context.Connection.RemoteIpAddress?.ToString();

        var userAgent = context.Request.Headers.UserAgent.ToString();

        return new AuditContext(ip, string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }
}
