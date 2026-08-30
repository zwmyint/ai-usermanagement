using System.Text.Json;
using UserManagement.Application.Common;
using UserManagement.Application.DTOs;
using UserManagement.Application.Interfaces;
using UserManagement.Application.Mapping;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Domain.Exceptions;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IProfilePictureStorage _profilePictures;

    public UserService(IUserRepository users, IRoleRepository roles, IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher, IAuditService audit, IUnitOfWork uow, IDateTimeProvider clock, ICurrentUser currentUser,
        IProfilePictureStorage profilePictures)
    {
        _users = users;
        _roles = roles;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _audit = audit;
        _uow = uow;
        _clock = clock;
        _currentUser = currentUser;
        _profilePictures = profilePictures;
    }

    public async Task<PagedResult<UserDto>> GetUsersAsync(UserQueryRequest request, CancellationToken ct = default)
    {
        var (items, total, filtered) = await _users.SearchAsync(
            request.Search, request.Role, request.IsActive, request.SortBy, request.SortDescending,
            request.Skip, request.PageSize, ct);

        var now = _clock.UtcNow;
        return PagedResult<UserDto>.Create(items.Select(u => u.ToDto(now)).ToList(), request, total, filtered);
    }

    public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(id, ct) ?? throw NotFoundException.For(nameof(User), id);
        return user.ToDto(_clock.UtcNow);
    }

    public async Task<UserDto> CreateAsync(CreateUserDto dto, AuditContext audit, CancellationToken ct = default)
    {
        PasswordPolicy.EnsureValid(dto.Password, nameof(dto.Password));

        var normalizedEmail = Normalizer.Normalize(dto.Email);
        var normalizedUserName = Normalizer.Normalize(dto.UserName);

        if (await _users.EmailExistsAsync(normalizedEmail, ct: ct))
            throw new ConflictException("An account with this email address already exists.");
        if (await _users.UserNameExistsAsync(normalizedUserName, ct: ct))
            throw new ConflictException("This username is already taken.");

        var roleNames = dto.Roles.Count > 0 ? dto.Roles : new List<string> { RoleNames.User };
        var roles = await ResolveRolesAsync(roleNames, ct);

        var now = _clock.UtcNow;
        var (hash, salt) = _hasher.Hash(dto.Password);

        var user = new User
        {
            UserName = dto.UserName.Trim(),
            NormalizedUserName = normalizedUserName,
            Email = dto.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = hash,
            PasswordSalt = salt,
            FirstName = dto.FirstName?.Trim(),
            LastName = dto.LastName?.Trim(),
            PhoneNumber = dto.PhoneNumber?.Trim(),
            IsActive = dto.IsActive,
            CreatedAt = now,
            CreatedBy = _currentUser.UserName
        };

        foreach (var role in roles)
            user.UserRoles.Add(new UserRole { Role = role, RoleId = role.Id, AssignedAt = now, AssignedBy = _currentUser.UserName });

        await _users.AddAsync(user, ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.UserCreated, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(User), user.Id.ToString(), newValues: Serialize(user), ct: ct);

        return user.ToDto(now);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserDto dto, AuditContext audit, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(id, ct) ?? throw NotFoundException.For(nameof(User), id);
        var before = Serialize(user);

        var normalizedEmail = Normalizer.Normalize(dto.Email);
        if (await _users.EmailExistsAsync(normalizedEmail, id, ct))
            throw new ConflictException("An account with this email address already exists.");

        var now = _clock.UtcNow;
        var activeStateChanged = user.IsActive != dto.IsActive;
        user.Email = dto.Email.Trim();
        user.NormalizedEmail = normalizedEmail;
        user.FirstName = dto.FirstName?.Trim();
        user.LastName = dto.LastName?.Trim();
        user.PhoneNumber = dto.PhoneNumber?.Trim();
        user.IsActive = dto.IsActive;
        if (activeStateChanged)
            user.AuthenticationVersion = Guid.NewGuid();
        user.UpdatedAt = now;
        user.UpdatedBy = _currentUser.UserName;

        _users.Update(user);
        if (activeStateChanged && !dto.IsActive)
        {
            await _uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                await EnsureNotLastAdminAsync(user, transactionCt);
                await _uow.SaveChangesAsync(transactionCt);
            }, ct);
        }
        else
        {
            await _uow.SaveChangesAsync(ct);
        }

        await _audit.LogAsync(AuditAction.UserUpdated, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(User), user.Id.ToString(), before, Serialize(user), ct: ct);

        return user.ToDto(now);
    }

    public async Task<UserDto> UpdateProfilePictureAsync(
        Guid id, Stream image, string fileName, AuditContext audit, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(id, ct) ?? throw NotFoundException.For(nameof(User), id);
        var before = Serialize(user);
        var previousPath = user.ProfilePicturePath;
        var newPath = await _profilePictures.SaveAsync(image, fileName, ct);
        var now = _clock.UtcNow;

        try
        {
            user.ProfilePicturePath = newPath;
            user.UpdatedAt = now;
            user.UpdatedBy = _currentUser.UserName;
            _users.Update(user);
            await _uow.SaveChangesAsync(ct);
        }
        catch
        {
            await _profilePictures.DeleteAsync(newPath, ct);
            throw;
        }

        await _profilePictures.DeleteAsync(previousPath, ct);
        await _audit.LogAsync(AuditAction.ProfileUpdated, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(User), user.Id.ToString(), before, Serialize(user), ct: ct);

        return user.ToDto(now);
    }

    public async Task DeleteAsync(Guid id, AuditContext audit, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(id, ct) ?? throw NotFoundException.For(nameof(User), id);

        if (_currentUser.UserId == id)
            throw new ConflictException("You cannot delete your own account.");

        var now = _clock.UtcNow;
        await _uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            await EnsureNotLastAdminAsync(user, transactionCt);

            user.IsDeleted = true;
            user.DeletedAt = now;
            user.IsActive = false;
            user.AuthenticationVersion = Guid.NewGuid();
            user.UpdatedAt = now;
            user.UpdatedBy = _currentUser.UserName;
            _users.Update(user);

            foreach (var token in await _refreshTokens.GetActiveByUserAsync(user.Id, transactionCt))
            {
                token.RevokedAt = now;
                token.RevokedByIp = audit.IpAddress;
                token.RevokedReason = "User deleted.";
                _refreshTokens.Update(token);
            }

            await _uow.SaveChangesAsync(transactionCt);
        }, ct);

        await _audit.LogAsync(AuditAction.UserDeleted, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(User), user.Id.ToString(), ct: ct);
        await _profilePictures.DeleteAsync(user.ProfilePicturePath, ct);
    }

    public async Task<UserDto> SetActiveAsync(Guid id, bool isActive, AuditContext audit, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(id, ct) ?? throw NotFoundException.For(nameof(User), id);

        if (!isActive)
        {
            if (_currentUser.UserId == id)
                throw new ConflictException("You cannot deactivate your own account.");
        }

        var now = _clock.UtcNow;
        if (user.IsActive != isActive)
            user.AuthenticationVersion = Guid.NewGuid();
        user.IsActive = isActive;
        user.UpdatedAt = now;
        user.UpdatedBy = _currentUser.UserName;
        if (isActive)
        {
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
        }
        if (!isActive)
        {
            await _uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                await EnsureNotLastAdminAsync(user, transactionCt);
                _users.Update(user);

                foreach (var token in await _refreshTokens.GetActiveByUserAsync(user.Id, transactionCt))
                {
                    token.RevokedAt = now;
                    token.RevokedByIp = audit.IpAddress;
                    token.RevokedReason = "User deactivated.";
                    _refreshTokens.Update(token);
                }

                await _uow.SaveChangesAsync(transactionCt);
            }, ct);
        }
        else
        {
            _users.Update(user);
            await _uow.SaveChangesAsync(ct);
        }

        await _audit.LogAsync(isActive ? AuditAction.UserActivated : AuditAction.UserDeactivated, audit,
            _currentUser.UserId, _currentUser.UserName, nameof(User), user.Id.ToString(), ct: ct);

        return user.ToDto(now);
    }

    public async Task<UserDto> AssignRolesAsync(Guid id, AssignRolesDto dto, AuditContext audit, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(id, ct) ?? throw NotFoundException.For(nameof(User), id);
        var before = string.Join(", ", user.UserRoles.Select(ur => ur.Role.Name).OrderBy(n => n));

        var roles = await ResolveRolesAsync(dto.Roles, ct);
        var willBeAdmin = roles.Any(r => string.Equals(r.Name, RoleNames.Admin, StringComparison.OrdinalIgnoreCase));

        var now = _clock.UtcNow;
        var rolesChanged = !user.UserRoles.Select(ur => ur.RoleId).Order().SequenceEqual(roles.Select(r => r.Id).Order());
        async Task ApplyRolesAsync(CancellationToken transactionCt)
        {
            user.UserRoles.Clear();
            foreach (var role in roles)
                user.UserRoles.Add(new UserRole { UserId = user.Id, Role = role, RoleId = role.Id, AssignedAt = now, AssignedBy = _currentUser.UserName });

            user.UpdatedAt = now;
            user.UpdatedBy = _currentUser.UserName;
            if (rolesChanged)
                user.AuthenticationVersion = Guid.NewGuid();
            _users.Update(user);
            await _uow.SaveChangesAsync(transactionCt);
        }

        if (!willBeAdmin)
        {
            await _uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                await EnsureNotLastAdminAsync(user, transactionCt);
                await ApplyRolesAsync(transactionCt);
            }, ct);
        }
        else
        {
            await ApplyRolesAsync(ct);
        }

        await _audit.LogAsync(AuditAction.RolesAssigned, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(User), user.Id.ToString(), before, string.Join(", ", roles.Select(r => r.Name).OrderBy(n => n)), ct: ct);

        return user.ToDto(now);
    }

    public async Task<UserDto> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(userId, ct) ?? throw NotFoundException.For(nameof(User), userId);
        return user.ToDto(_clock.UtcNow);
    }

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto, AuditContext audit, CancellationToken ct = default)
    {
        var user = await _users.GetByIdWithRolesAsync(userId, ct) ?? throw NotFoundException.For(nameof(User), userId);
        var before = Serialize(user);

        var normalizedEmail = Normalizer.Normalize(dto.Email);
        if (await _users.EmailExistsAsync(normalizedEmail, userId, ct))
            throw new ConflictException("An account with this email address already exists.");

        var now = _clock.UtcNow;
        user.Email = dto.Email.Trim();
        user.NormalizedEmail = normalizedEmail;
        user.FirstName = dto.FirstName?.Trim();
        user.LastName = dto.LastName?.Trim();
        user.PhoneNumber = dto.PhoneNumber?.Trim();
        user.UpdatedAt = now;
        user.UpdatedBy = user.UserName;

        _users.Update(user);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.ProfileUpdated, audit, user.Id, user.UserName,
            nameof(User), user.Id.ToString(), before, Serialize(user), ct: ct);

        return user.ToDto(now);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto dto, AuditContext audit, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct) ?? throw NotFoundException.For(nameof(User), userId);

        if (!_hasher.Verify(dto.CurrentPassword, user.PasswordHash, user.PasswordSalt))
            throw new ValidationException(nameof(dto.CurrentPassword), "Current password is incorrect.");

        PasswordPolicy.EnsureValid(dto.NewPassword, nameof(dto.NewPassword));

        if (_hasher.Verify(dto.NewPassword, user.PasswordHash, user.PasswordSalt))
            throw new ValidationException(nameof(dto.NewPassword), "New password must be different from the current password.");

        var now = _clock.UtcNow;
        var (hash, salt) = _hasher.Hash(dto.NewPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.AuthenticationVersion = Guid.NewGuid();
        user.UpdatedAt = now;
        user.UpdatedBy = user.UserName;
        _users.Update(user);

        foreach (var token in await _refreshTokens.GetActiveByUserAsync(user.Id, ct))
        {
            token.RevokedAt = now;
            token.RevokedByIp = audit.IpAddress;
            token.RevokedReason = "Password changed.";
            _refreshTokens.Update(token);
        }

        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditAction.PasswordChanged, audit, user.Id, user.UserName, ct: ct);
    }

    private async Task<List<Role>> ResolveRolesAsync(IEnumerable<string> roleNames, CancellationToken ct)
    {
        var requested = roleNames
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
            throw new ValidationException("Roles", "At least one role must be assigned.");

        var normalized = requested.Select(Normalizer.Normalize).ToList();
        var roles = (await _roles.GetByNamesAsync(normalized, ct)).ToList();

        var missing = requested
            .Where(r => !roles.Any(x => string.Equals(x.NormalizedName, Normalizer.Normalize(r), StringComparison.Ordinal)))
            .ToArray();

        if (missing.Length > 0)
            throw new ValidationException("Roles", $"Unknown role(s): {string.Join(", ", missing)}.");

        return roles;
    }

    /// <summary>Prevents removing, disabling or demoting the last remaining active administrator.</summary>
    private async Task EnsureNotLastAdminAsync(User user, CancellationToken ct)
    {
        var isAdmin = user.UserRoles.Any(ur => ur.Role is not null
            && string.Equals(ur.Role.Name, RoleNames.Admin, StringComparison.OrdinalIgnoreCase));
        if (!isAdmin) return;

        var remaining = await _users.CountActiveInRoleAsync(Normalizer.Normalize(RoleNames.Admin), user.Id, ct);
        if (remaining == 0)
            throw new ConflictException("The last active administrator cannot be removed, disabled or demoted.");
    }

    private static string Serialize(User user) => JsonSerializer.Serialize(new
    {
        user.UserName,
        user.Email,
        user.FirstName,
        user.LastName,
        user.PhoneNumber,
        user.ProfilePicturePath,
        user.IsActive
    });
}
