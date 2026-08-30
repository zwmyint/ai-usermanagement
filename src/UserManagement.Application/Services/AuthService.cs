using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UserManagement.Application.Common;
using UserManagement.Application.Common.Settings;
using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;
using UserManagement.Application.Mapping;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Domain.Exceptions;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordResetTokenRepository _resetTokens;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly SecuritySettings _security;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IRoleRepository roles,
        IRefreshTokenRepository refreshTokens,
        IPasswordResetTokenRepository resetTokens,
        IPasswordHasher hasher,
        ITokenService tokens,
        IEmailSender email,
        IAuditService audit,
        IUnitOfWork uow,
        IDateTimeProvider clock,
        IOptions<SecuritySettings> security,
        ILogger<AuthService> logger)
    {
        _users = users;
        _roles = roles;
        _refreshTokens = refreshTokens;
        _resetTokens = resetTokens;
        _hasher = hasher;
        _tokens = tokens;
        _email = email;
        _audit = audit;
        _uow = uow;
        _clock = clock;
        _security = security.Value;
        _logger = logger;
    }

    public async Task<UserDto> RegisterAsync(RegisterRequest request, AuditContext audit, CancellationToken ct = default)
    {
        PasswordPolicy.EnsureValid(request.Password, nameof(request.Password));

        var normalizedEmail = Normalizer.Normalize(request.Email);
        var normalizedUserName = Normalizer.Normalize(request.UserName);

        if (await _users.EmailExistsAsync(normalizedEmail, ct: ct))
            throw new ConflictException("An account with this email address already exists.");
        if (await _users.UserNameExistsAsync(normalizedUserName, ct: ct))
            throw new ConflictException("This username is already taken.");

        var (hash, salt) = _hasher.Hash(request.Password);
        var now = _clock.UtcNow;

        var user = new User
        {
            UserName = request.UserName.Trim(),
            NormalizedUserName = normalizedUserName,
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
            IsActive = true,
            CreatedAt = now
        };

        var defaultRole = await _roles.GetByNameAsync(Normalizer.Normalize(RoleNames.User), ct)
            ?? throw new DomainException("Default role 'User' is missing. Run database seeding.");
        user.UserRoles.Add(new UserRole { Role = defaultRole, RoleId = defaultRole.Id, AssignedAt = now });

        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.Register, audit, user.Id, user.UserName,
            nameof(User), user.Id.ToString(), ct: ct);

        return user.ToDto(now);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, AuditContext audit, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var normalized = Normalizer.Normalize(request.UserNameOrEmail);
        var user = await _users.GetByUserNameOrEmailAsync(normalized, ct);

        if (user is null)
        {
            await _audit.LogAsync(AuditAction.LoginFailed, audit, userName: request.UserNameOrEmail,
                succeeded: false, message: "Unknown user.", ct: ct);
            throw new ForbiddenException("Invalid credentials.");
        }

        if (user.IsLockedOut(now))
        {
            await _audit.LogAsync(AuditAction.LoginFailed, audit, user.Id, user.UserName,
                succeeded: false, message: "Account locked out.", ct: ct);
            throw new ForbiddenException($"Account is locked. Try again after {user.LockoutEnd:u}.");
        }

        if (!_hasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= _security.MaxFailedAccessAttempts)
            {
                user.LockoutEnd = now.AddMinutes(_security.LockoutMinutes);
                user.AccessFailedCount = 0;
            }
            _users.Update(user);
            await _uow.SaveChangesAsync(ct);

            await _audit.LogAsync(AuditAction.LoginFailed, audit, user.Id, user.UserName,
                succeeded: false, message: "Invalid password.", ct: ct);
            throw new ForbiddenException("Invalid credentials.");
        }

        if (!user.IsActive || user.IsDeleted)
        {
            await _audit.LogAsync(AuditAction.LoginFailed, audit, user.Id, user.UserName,
                succeeded: false, message: "Account disabled.", ct: ct);
            throw new ForbiddenException("This account has been disabled.");
        }

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = now;
        _users.Update(user);

        var response = await IssueTokensAsync(user, Guid.NewGuid(), audit, now, ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.LoginSucceeded, audit, user.Id, user.UserName, ct: ct);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, AuditContext audit, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var hash = _tokens.HashToken(request.RefreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, ct)
            ?? throw new ForbiddenException("Invalid refresh token.");

        if (stored.IsRevoked)
        {
            // Reuse of an already-rotated token: revoke the whole family as a breach response.
            await RevokeFamilyAsync(stored.FamilyId, audit, now, "Refresh token reuse detected.", ct);
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditAction.TokenRevoked, audit, stored.UserId, succeeded: false,
                message: "Refresh token reuse detected; family revoked.", ct: ct);
            throw new ForbiddenException("Invalid refresh token.");
        }

        if (stored.IsExpired(now))
            throw new ForbiddenException("Refresh token has expired.");

        var user = await _users.GetByIdWithRolesAsync(stored.UserId, ct)
            ?? throw new ForbiddenException("Invalid refresh token.");

        if (!user.IsActive || user.IsDeleted)
            throw new ForbiddenException("This account has been disabled.");

        var replacement = _tokens.CreateRefreshToken();
        AuthResponse? response = null;
        var rotated = false;

        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            rotated = await _refreshTokens.TryRotateAsync(
                stored.Id, now, audit.IpAddress, replacement.TokenHash, transactionCt);
            if (!rotated) return;

            response = await IssueTokensAsync(user, stored.FamilyId, audit, now, transactionCt, replacement);
            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        if (!rotated)
        {
            await RevokeFamilyAsync(stored.FamilyId, audit, now, "Refresh token reuse detected.", ct);
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditAction.TokenRevoked, audit, stored.UserId, succeeded: false,
                message: "Refresh token reuse detected; family revoked.", ct: ct);
            throw new ForbiddenException("Invalid refresh token.");
        }

        await _audit.LogAsync(AuditAction.TokenRefreshed, audit, user.Id, user.UserName, ct: ct);
        return response!;
    }

    public async Task RevokeAsync(RevokeRequest request, AuditContext audit, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var hash = _tokens.HashToken(request.RefreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, ct);
        if (stored is null || !stored.IsActive(now)) return;

        stored.RevokedAt = now;
        stored.RevokedByIp = audit.IpAddress;
        stored.RevokedReason = "Revoked by user.";
        _refreshTokens.Update(stored);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.TokenRevoked, audit, stored.UserId, ct: ct);
    }

    public async Task LogoutAsync(Guid userId, AuditContext audit, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var user = await _users.GetByIdAsync(userId, ct) ?? throw NotFoundException.For(nameof(User), userId);
        user.AuthenticationVersion = Guid.NewGuid();
        user.UpdatedAt = now;
        _users.Update(user);

        var active = await _refreshTokens.GetActiveByUserAsync(userId, ct);
        foreach (var token in active)
        {
            token.RevokedAt = now;
            token.RevokedByIp = audit.IpAddress;
            token.RevokedReason = "Logout.";
            _refreshTokens.Update(token);
        }
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.Logout, audit, userId, ct: ct);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordDto dto, AuditContext audit, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var user = await _users.GetByEmailAsync(Normalizer.Normalize(dto.Email), ct);

        // Always behave identically to avoid disclosing whether the address is registered.
        if (user is null || !user.IsActive || user.IsDeleted)
        {
            _logger.LogInformation("Password reset requested for unknown or inactive address.");
            await _audit.LogAsync(AuditAction.PasswordResetRequested, audit, userName: dto.Email,
                succeeded: false, message: "Unknown or inactive address.", ct: ct);
            return;
        }

        await _resetTokens.InvalidateExistingAsync(user.Id, now, ct);

        var token = _tokens.CreatePasswordResetToken();
        await _resetTokens.AddAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = token.TokenHash,
            ExpiresAt = now.AddMinutes(_security.PasswordResetTokenMinutes),
            CreatedAt = now
        }, ct);
        await _uow.SaveChangesAsync(ct);

        var link = $"{_security.ResetPasswordUrl}?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(token.RawToken)}";
        var body = $"""
            <p>Hello {System.Net.WebUtility.HtmlEncode(user.FullName is { Length: > 0 } n ? n : user.UserName)},</p>
            <p>We received a request to reset your password. Click the link below to choose a new one.
               This link expires in {_security.PasswordResetTokenMinutes} minutes and can be used once.</p>
            <p><a href="{System.Net.WebUtility.HtmlEncode(link)}">Reset your password</a></p>
            <p>If you did not request this, you can safely ignore this email.</p>
            """;

        await _email.SendAsync(user.Email, "Reset your password", body, ct);

        await _audit.LogAsync(AuditAction.PasswordResetRequested, audit, user.Id, user.UserName, ct: ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto, AuditContext audit, CancellationToken ct = default)
    {
        PasswordPolicy.EnsureValid(dto.NewPassword, nameof(dto.NewPassword));

        var now = _clock.UtcNow;
        var hash = _tokens.HashToken(dto.Token);
        var stored = await _resetTokens.GetByHashAsync(hash, ct);

        if (stored is null || !stored.IsUsable(now))
            throw new ForbiddenException("This password reset link is invalid or has expired.");

        var user = await _users.GetByIdAsync(stored.UserId, ct)
            ?? throw new ForbiddenException("This password reset link is invalid or has expired.");

        if (!string.Equals(user.NormalizedEmail, Normalizer.Normalize(dto.Email), StringComparison.Ordinal))
            throw new ForbiddenException("This password reset link is invalid or has expired.");

        var (newHash, newSalt) = _hasher.Hash(dto.NewPassword);
        var resetApplied = false;
        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            resetApplied = await _resetTokens.TryUseAsync(stored.Id, now, transactionCt);
            if (!resetApplied) return;

            user.PasswordHash = newHash;
            user.PasswordSalt = newSalt;
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            user.AuthenticationVersion = Guid.NewGuid();
            user.UpdatedAt = now;
            _users.Update(user);

            // Password changed: invalidate every outstanding session.
            foreach (var token in await _refreshTokens.GetActiveByUserAsync(user.Id, transactionCt))
            {
                token.RevokedAt = now;
                token.RevokedByIp = audit.IpAddress;
                token.RevokedReason = "Password reset.";
                _refreshTokens.Update(token);
            }

            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        if (!resetApplied)
            throw new ForbiddenException("This password reset link is invalid or has expired.");

        await _audit.LogAsync(AuditAction.PasswordResetCompleted, audit, user.Id, user.UserName, ct: ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, Guid familyId, AuditContext audit, DateTime now,
        CancellationToken ct, TokenPair? refreshToken = null)
    {
        var roles = user.UserRoles.Where(ur => ur.Role is not null).Select(ur => ur.Role.Name).ToList();
        var access = _tokens.CreateAccessToken(user, roles);
        refreshToken ??= _tokens.CreateRefreshToken();

        await _refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshToken.TokenHash,
            FamilyId = familyId,
            ExpiresAt = refreshToken.ExpiresAt,
            CreatedAt = now,
            CreatedByIp = audit.IpAddress
        }, ct);

        return new AuthResponse
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = refreshToken.RawToken,
            RefreshTokenExpiresAt = refreshToken.ExpiresAt,
            User = user.ToDto(now)
        };
    }

    private async Task RevokeFamilyAsync(Guid familyId, AuditContext audit, DateTime now, string reason, CancellationToken ct)
    {
        foreach (var token in await _refreshTokens.GetByFamilyAsync(familyId, ct))
        {
            if (token.IsRevoked) continue;
            token.RevokedAt = now;
            token.RevokedByIp = audit.IpAddress;
            token.RevokedReason = reason;
            _refreshTokens.Update(token);
        }
    }
}
