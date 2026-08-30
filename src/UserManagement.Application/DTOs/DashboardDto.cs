namespace UserManagement.Application.DTOs;

/// <summary>Payload backing the Home page dashboard. <see cref="AdminStats"/> is only populated for administrators.</summary>
public class DashboardSummaryDto
{
    public AdminStatsDto? AdminStats { get; set; }
    public MySignInActivityDto MyActivity { get; set; } = new();
}

public class AdminStatsDto
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }
    public int TotalRoles { get; set; }
    public int TotalAuditLogs { get; set; }
    public int LoginsLast24Hours { get; set; }
    public int LoginsLast7Days { get; set; }
    public int FailedLoginsLast24Hours { get; set; }
    public IReadOnlyList<RoleUserCountDto> RoleBreakdown { get; set; } = Array.Empty<RoleUserCountDto>();
    public IReadOnlyList<RecentUserDto> RecentUsers { get; set; } = Array.Empty<RecentUserDto>();
}

public class RoleUserCountDto
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public int UserCount { get; set; }
}

public class RecentUserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Roles { get; set; } = new();
}

/// <summary>Sign-in history for the calling user, derived from the audit log.</summary>
public class MySignInActivityDto
{
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PreviousLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTime? LastFailedLoginAt { get; set; }
    public string? LastFailedLoginIp { get; set; }
    public int TotalLogins { get; set; }
}
