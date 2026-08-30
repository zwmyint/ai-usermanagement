using UserManagement.Domain.Enums;

namespace UserManagement.Application.DTOs;

public class AuditLogDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public AuditAction Action { get; set; }
    public string ActionName { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Succeeded { get; set; }
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>Context captured from the incoming HTTP request for audit purposes.</summary>
public record AuditContext(string? IpAddress, string? UserAgent)
{
    public static readonly AuditContext Empty = new(null, null);
}
