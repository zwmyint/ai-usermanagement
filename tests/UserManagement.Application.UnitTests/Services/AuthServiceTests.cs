using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UserManagement.Application.Common.Settings;
using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;
using UserManagement.Application.Services;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Exceptions;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Application.UnitTests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IPasswordResetTokenRepository> _resetTokens = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    private readonly AuthService _sut;
    private readonly DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public AuthServiceTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(_now);
        _uow.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct));

        var securitySettings = Options.Create(new SecuritySettings());

        _sut = new AuthService(
            _users.Object, _roles.Object, _refreshTokens.Object, _resetTokens.Object,
            _hasher.Object, _tokens.Object, _email.Object, _audit.Object, _uow.Object,
            _clock.Object, securitySettings, NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task RegisterAsync_ThrowsConflict_WhenEmailAlreadyExists()
    {
        _users.Setup(u => u.EmailExistsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new RegisterRequest
        {
            UserName = "newuser",
            Email = "existing@example.com",
            Password = "ValidPass1!",
            ConfirmPassword = "ValidPass1!"
        };

        await Assert.ThrowsAsync<ConflictException>(() => _sut.RegisterAsync(request, AuditContext.Empty));
    }

    [Fact]
    public async Task RegisterAsync_ThrowsConflict_WhenUserNameAlreadyExists()
    {
        _users.Setup(u => u.EmailExistsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _users.Setup(u => u.UserNameExistsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var request = new RegisterRequest
        {
            UserName = "taken",
            Email = "new@example.com",
            Password = "ValidPass1!",
            ConfirmPassword = "ValidPass1!"
        };

        await Assert.ThrowsAsync<ConflictException>(() => _sut.RegisterAsync(request, AuditContext.Empty));
    }

    [Fact]
    public async Task RegisterAsync_CreatesUserWithDefaultRole_WhenRequestIsValid()
    {
        _users.Setup(u => u.EmailExistsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.UserNameExistsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns(("hash", "salt"));

        var userRole = new Role { Id = Guid.NewGuid(), Name = RoleNames.User, NormalizedName = "USER" };
        _roles.Setup(r => r.GetByNameAsync("USER", It.IsAny<CancellationToken>())).ReturnsAsync(userRole);

        User? addedUser = null;
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => addedUser = u)
            .Returns(Task.CompletedTask);

        var request = new RegisterRequest
        {
            UserName = "newuser",
            Email = "new@example.com",
            Password = "ValidPass1!",
            ConfirmPassword = "ValidPass1!",
            FirstName = "New",
            LastName = "User"
        };

        var result = await _sut.RegisterAsync(request, AuditContext.Empty);

        Assert.Equal("newuser", result.UserName);
        Assert.NotNull(addedUser);
        Assert.Single(addedUser!.UserRoles);
        Assert.Equal(userRole.Id, addedUser.UserRoles.First().RoleId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_ThrowsForbidden_WhenUserDoesNotExist()
    {
        _users.Setup(u => u.GetByUserNameOrEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var request = new LoginRequest { UserNameOrEmail = "ghost@example.com", Password = "whatever" };

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.LoginAsync(request, AuditContext.Empty));
    }

    [Fact]
    public async Task LoginAsync_ThrowsForbidden_WhenPasswordIsInvalid()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "someone",
            Email = "someone@example.com",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            IsActive = true
        };

        _users.Setup(u => u.GetByUserNameOrEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), "hash", "salt")).Returns(false);

        var request = new LoginRequest { UserNameOrEmail = "someone@example.com", Password = "wrong" };

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.LoginAsync(request, AuditContext.Empty));

        _users.Verify(u => u.Update(user), Times.Once);
        Assert.Equal(1, user.AccessFailedCount);
    }

    [Fact]
    public async Task LoginAsync_ThrowsForbidden_WhenAccountIsLockedOut()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "locked",
            Email = "locked@example.com",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            IsActive = true,
            LockoutEnd = _now.AddMinutes(10)
        };

        _users.Setup(u => u.GetByUserNameOrEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var request = new LoginRequest { UserNameOrEmail = "locked@example.com", Password = "whatever" };

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.LoginAsync(request, AuditContext.Empty));
    }

    [Fact]
    public async Task RefreshAsync_RevokesFamilyWithoutIssuingReplacement_WhenAtomicRotationFails()
    {
        var stored = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FamilyId = Guid.NewGuid(),
            TokenHash = "token-hash",
            ExpiresAt = _now.AddDays(1)
        };
        var user = new User
        {
            Id = stored.UserId,
            UserName = "someone",
            Email = "someone@example.com",
            IsActive = true
        };

        _tokens.Setup(t => t.HashToken("raw-token")).Returns(stored.TokenHash);
        _tokens.Setup(t => t.CreateRefreshToken())
            .Returns(new TokenPair("replacement", "replacement-hash", _now.AddDays(1)));
        _refreshTokens.Setup(r => r.GetByHashAsync(stored.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _refreshTokens.Setup(r => r.TryRotateAsync(
                stored.Id, _now, It.IsAny<string?>(), "replacement-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokens.Setup(r => r.GetByFamilyAsync(stored.FamilyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { stored });
        _users.Setup(u => u.GetByIdWithRolesAsync(stored.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.RefreshAsync(new RefreshRequest { RefreshToken = "raw-token" }, AuditContext.Empty));

        _refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_DoesNotSavePassword_WhenAtomicTokenUseFails()
    {
        var stored = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = "reset-token-hash",
            ExpiresAt = _now.AddMinutes(30)
        };
        var user = new User
        {
            Id = stored.UserId,
            UserName = "someone",
            Email = "someone@example.com",
            NormalizedEmail = "SOMEONE@EXAMPLE.COM",
            IsActive = true
        };

        _tokens.Setup(t => t.HashToken("raw-reset-token")).Returns(stored.TokenHash);
        _resetTokens.Setup(r => r.GetByHashAsync(stored.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _resetTokens.Setup(r => r.TryUseAsync(stored.Id, _now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _users.Setup(u => u.GetByIdAsync(stored.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Hash("ValidPass1!")).Returns(("new-hash", "new-salt"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = user.Email,
            Token = "raw-reset-token",
            NewPassword = "ValidPass1!",
            ConfirmPassword = "ValidPass1!"
        }, AuditContext.Empty));

        _refreshTokens.Verify(r => r.GetActiveByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
