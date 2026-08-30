namespace UserManagement.API.IntegrationTests;

/// <summary>Mirrors the API's response contracts for deserialization inside the integration tests.</summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    public UserDto User { get; set; } = new();
}

public class UserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}

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
    public List<RoleUserCountDto> RoleBreakdown { get; set; } = new();
    public List<RecentUserDto> RecentUsers { get; set; } = new();
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
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MySignInActivityDto
{
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PreviousLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTime? LastFailedLoginAt { get; set; }
    public string? LastFailedLoginIp { get; set; }
    public int TotalLogins { get; set; }
}
