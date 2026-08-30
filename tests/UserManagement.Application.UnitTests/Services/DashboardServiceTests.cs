using Moq;
using UserManagement.Application.Services;
using UserManagement.Application.Interfaces;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Domain.Exceptions;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Application.UnitTests.Services;

public class DashboardServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IAuditLogRepository> _auditLogs = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly DashboardService _sut;
    private readonly DateTime _now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly User _user;

    public DashboardServiceTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(_now);

        _user = new User { Id = Guid.NewGuid(), UserName = "tester", Email = "tester@example.com" };
        _users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);

        _users.Setup(u => u.GetCountsAsync(It.IsAny<CancellationToken>())).ReturnsAsync((10, 7, 3));
        _users.Setup(u => u.GetRecentlyCreatedAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());
        _roles.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _roles.Setup(r => r.GetRoleUserCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoleUserCount> { new(Guid.NewGuid(), "Admin", 1) });
        _auditLogs.Setup(a => a.GetRecentByUserAndActionAsync(
                It.IsAny<Guid>(), It.IsAny<AuditAction>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog>());
        _auditLogs.Setup(a => a.CountAsync(
                It.IsAny<AuditAction?>(), It.IsAny<bool?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _sut = new DashboardService(_users.Object, _roles.Object, _auditLogs.Object, _clock.Object);
    }

    [Fact]
    public async Task GetSummaryAsync_ThrowsNotFound_WhenUserDoesNotExist()
    {
        var missingId = Guid.NewGuid();
        _users.Setup(u => u.GetByIdAsync(missingId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetSummaryAsync(missingId, isAdmin: true));
    }

    [Fact]
    public async Task GetSummaryAsync_OmitsAdminStats_ForNonAdmin()
    {
        var summary = await _sut.GetSummaryAsync(_user.Id, isAdmin: false);

        Assert.Null(summary.AdminStats);
        Assert.NotNull(summary.MyActivity);
        _users.Verify(u => u.GetCountsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSummaryAsync_PopulatesAdminStats_ForAdmin()
    {
        var summary = await _sut.GetSummaryAsync(_user.Id, isAdmin: true);

        Assert.NotNull(summary.AdminStats);
        Assert.Equal(10, summary.AdminStats!.TotalUsers);
        Assert.Equal(7, summary.AdminStats.ActiveUsers);
        Assert.Equal(3, summary.AdminStats.InactiveUsers);
        Assert.Equal(2, summary.AdminStats.TotalRoles);
        Assert.Single(summary.AdminStats.RoleBreakdown);
    }

    [Fact]
    public async Task GetSummaryAsync_UsesTwentyFourHourAndSevenDayWindows()
    {
        await _sut.GetSummaryAsync(_user.Id, isAdmin: true);

        _auditLogs.Verify(a => a.CountAsync(AuditAction.LoginSucceeded, null, _now.AddHours(-24), null,
            It.IsAny<CancellationToken>()), Times.Once);
        _auditLogs.Verify(a => a.CountAsync(AuditAction.LoginSucceeded, null, _now.AddDays(-7), null,
            It.IsAny<CancellationToken>()), Times.Once);
        _auditLogs.Verify(a => a.CountAsync(AuditAction.LoginFailed, null, _now.AddHours(-24), null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSummaryAsync_FallsBackToUserLastLogin_WhenNoLoginAuditRows()
    {
        _user.LastLoginAt = _now.AddDays(-1);

        var summary = await _sut.GetSummaryAsync(_user.Id, isAdmin: false);

        Assert.Equal(_now.AddDays(-1), summary.MyActivity.LastLoginAt);
        Assert.Null(summary.MyActivity.PreviousLoginAt);
    }

    [Fact]
    public async Task GetSummaryAsync_LeavesPreviousLoginNull_WhenOnlyOneLoginRecorded()
    {
        SetupLogins(new AuditLog { Timestamp = _now, IpAddress = "10.0.0.1", Action = AuditAction.LoginSucceeded });

        var summary = await _sut.GetSummaryAsync(_user.Id, isAdmin: false);

        Assert.Equal(_now, summary.MyActivity.LastLoginAt);
        Assert.Equal("10.0.0.1", summary.MyActivity.LastLoginIp);
        Assert.Null(summary.MyActivity.PreviousLoginAt);
    }

    [Fact]
    public async Task GetSummaryAsync_ResolvesPreviousLogin_FromSecondMostRecentLogin()
    {
        SetupLogins(
            new AuditLog { Timestamp = _now, Action = AuditAction.LoginSucceeded },
            new AuditLog { Timestamp = _now.AddDays(-3), Action = AuditAction.LoginSucceeded });

        var summary = await _sut.GetSummaryAsync(_user.Id, isAdmin: false);

        Assert.Equal(_now, summary.MyActivity.LastLoginAt);
        Assert.Equal(_now.AddDays(-3), summary.MyActivity.PreviousLoginAt);
    }

    [Fact]
    public async Task GetSummaryAsync_ReportsLastFailedLogin()
    {
        _auditLogs.Setup(a => a.GetRecentByUserAndActionAsync(
                _user.Id, AuditAction.LoginFailed, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog>
            {
                new() { Timestamp = _now.AddHours(-2), IpAddress = "10.0.0.9", Action = AuditAction.LoginFailed }
            });

        var summary = await _sut.GetSummaryAsync(_user.Id, isAdmin: false);

        Assert.Equal(_now.AddHours(-2), summary.MyActivity.LastFailedLoginAt);
        Assert.Equal("10.0.0.9", summary.MyActivity.LastFailedLoginIp);
    }

    private void SetupLogins(params AuditLog[] logins) =>
        _auditLogs.Setup(a => a.GetRecentByUserAndActionAsync(
                _user.Id, AuditAction.LoginSucceeded, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(logins.ToList());
}
