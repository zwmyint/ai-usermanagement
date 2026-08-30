using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Domain.Exceptions;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Application.Services;

public class DashboardService : IDashboardService
{
    private const int RecentUserCount = 5;

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IDateTimeProvider _clock;

    public DashboardService(IUserRepository users, IRoleRepository roles, IAuditLogRepository auditLogs,
        IDateTimeProvider clock)
    {
        _users = users;
        _roles = roles;
        _auditLogs = auditLogs;
        _clock = clock;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw NotFoundException.For("User", userId);

        var summary = new DashboardSummaryDto
        {
            MyActivity = await BuildActivityAsync(user, ct)
        };

        if (isAdmin)
            summary.AdminStats = await BuildAdminStatsAsync(ct);

        return summary;
    }

    private async Task<MySignInActivityDto> BuildActivityAsync(User user, CancellationToken ct)
    {
        // The most recent LoginSucceeded row is the current session's sign-in; the one before it is the previous sign-in.
        var logins = await _auditLogs.GetRecentByUserAndActionAsync(user.Id, AuditAction.LoginSucceeded, 2, ct);
        var failed = await _auditLogs.GetRecentByUserAndActionAsync(user.Id, AuditAction.LoginFailed, 1, ct);

        return new MySignInActivityDto
        {
            LastLoginAt = logins.Count > 0 ? logins[0].Timestamp : user.LastLoginAt,
            LastLoginIp = logins.Count > 0 ? logins[0].IpAddress : null,
            PreviousLoginAt = logins.Count > 1 ? logins[1].Timestamp : null,
            LastFailedLoginAt = failed.Count > 0 ? failed[0].Timestamp : null,
            LastFailedLoginIp = failed.Count > 0 ? failed[0].IpAddress : null,
            TotalLogins = await _auditLogs.CountAsync(AuditAction.LoginSucceeded, userId: user.Id, ct: ct)
        };
    }

    private async Task<AdminStatsDto> BuildAdminStatsAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var last24Hours = now.AddHours(-24);
        var last7Days = now.AddDays(-7);

        var (total, active, inactive) = await _users.GetCountsAsync(ct);
        var roleBreakdown = await _roles.GetRoleUserCountsAsync(ct);
        var recentUsers = await _users.GetRecentlyCreatedAsync(RecentUserCount, ct);

        return new AdminStatsDto
        {
            TotalUsers = total,
            ActiveUsers = active,
            InactiveUsers = inactive,
            TotalRoles = await _roles.CountAsync(ct),
            TotalAuditLogs = await _auditLogs.CountAsync(ct: ct),
            LoginsLast24Hours = await _auditLogs.CountAsync(AuditAction.LoginSucceeded, from: last24Hours, ct: ct),
            LoginsLast7Days = await _auditLogs.CountAsync(AuditAction.LoginSucceeded, from: last7Days, ct: ct),
            FailedLoginsLast24Hours = await _auditLogs.CountAsync(AuditAction.LoginFailed, from: last24Hours, ct: ct),
            RoleBreakdown = roleBreakdown
                .Select(x => new RoleUserCountDto { RoleId = x.RoleId, RoleName = x.RoleName, UserCount = x.UserCount })
                .ToList(),
            RecentUsers = recentUsers.Select(x => new RecentUserDto
            {
                Id = x.Id,
                UserName = x.UserName,
                Email = x.Email,
                FullName = x.FullName,
                IsActive = x.IsActive,
                CreatedAt = x.CreatedAt,
                Roles = x.UserRoles.Select(ur => ur.Role.Name).OrderBy(n => n).ToList()
            }).ToList()
        };
    }
}
