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

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roles;
    private readonly IPermissionRepository _permissions;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;

    public RoleService(IRoleRepository roles, IPermissionRepository permissions, IAuditService audit, IUnitOfWork uow,
        IDateTimeProvider clock, ICurrentUser currentUser)
    {
        _roles = roles;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken ct = default)
    {
        var roles = await _roles.GetAllAsync(ct);
        var result = new List<RoleDto>(roles.Count);
        foreach (var role in roles)
            result.Add(role.ToDto(await _roles.CountUsersInRoleAsync(role.Id, ct)));
        return result;
    }

    public async Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var role = await _roles.GetByIdAsync(id, ct) ?? throw NotFoundException.For(nameof(Role), id);
        return role.ToDto(await _roles.CountUsersInRoleAsync(role.Id, ct));
    }

    public async Task<RoleDto> CreateAsync(CreateRoleDto dto, AuditContext audit, CancellationToken ct = default)
    {
        var normalized = Normalizer.Normalize(dto.Name);
        if (await _roles.NameExistsAsync(normalized, ct: ct))
            throw new ConflictException($"A role named '{dto.Name}' already exists.");

        var role = new Role
        {
            Name = dto.Name.Trim(),
            NormalizedName = normalized,
            Description = dto.Description?.Trim(),
            IsSystemRole = false,
            CreatedAt = _clock.UtcNow,
            CreatedBy = _currentUser.UserName
        };

        await _roles.AddAsync(role, ct);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.RoleCreated, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(Role), role.Id.ToString(), newValues: role.Name, ct: ct);

        return role.ToDto();
    }

    public async Task<RoleDto> UpdateAsync(Guid id, UpdateRoleDto dto, AuditContext audit, CancellationToken ct = default)
    {
        var role = await _roles.GetByIdAsync(id, ct) ?? throw NotFoundException.For(nameof(Role), id);
        var before = role.Name;

        var normalized = Normalizer.Normalize(dto.Name);
        if (role.IsSystemRole && !string.Equals(role.NormalizedName, normalized, StringComparison.Ordinal))
            throw new ConflictException("System roles cannot be renamed.");

        if (await _roles.NameExistsAsync(normalized, id, ct))
            throw new ConflictException($"A role named '{dto.Name}' already exists.");

        role.Name = dto.Name.Trim();
        role.NormalizedName = normalized;
        role.Description = dto.Description?.Trim();
        role.UpdatedAt = _clock.UtcNow;
        role.UpdatedBy = _currentUser.UserName;

        _roles.Update(role);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.RoleUpdated, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(Role), role.Id.ToString(), before, role.Name, ct: ct);

        return role.ToDto(await _roles.CountUsersInRoleAsync(role.Id, ct));
    }

    public async Task DeleteAsync(Guid id, AuditContext audit, CancellationToken ct = default)
    {
        var role = await _roles.GetByIdAsync(id, ct) ?? throw NotFoundException.For(nameof(Role), id);

        if (role.IsSystemRole || RoleNames.IsSystemRole(role.Name))
            throw new ConflictException("System roles cannot be deleted.");

        var userCount = await _roles.CountUsersInRoleAsync(role.Id, ct);
        if (userCount > 0)
            throw new ConflictException($"This role is assigned to {userCount} user(s) and cannot be deleted.");

        _roles.Remove(role);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.RoleDeleted, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(Role), role.Id.ToString(), role.Name, ct: ct);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync(CancellationToken ct = default)
    {
        var permissions = await _permissions.GetAllAsync(ct);
        return permissions.Select(p => p.ToDto()).ToList();
    }

    public async Task<RoleDto> UpdatePermissionsAsync(Guid id, UpdateRolePermissionsDto dto, AuditContext audit, CancellationToken ct = default)
    {
        var role = await _roles.GetByIdWithPermissionsAsync(id, ct) ?? throw NotFoundException.For(nameof(Role), id);

        var requestedNames = dto.Permissions.Select(Normalizer.Normalize).Distinct().ToList();
        var permissions = await _permissions.GetByNamesAsync(requestedNames, ct);
        if (permissions.Count != requestedNames.Count)
            throw new ValidationException(nameof(dto.Permissions), "One or more permission names are unknown.");

        // Safety net: never allow a change that would leave no role able to manage roles/permissions -
        // that would permanently lock every administrator out of undoing the mistake.
        var keepsRolesManage = permissions.Any(p => p.NormalizedName == Normalizer.Normalize(PermissionNames.RolesManage));
        var currentlyHasRolesManage = role.RolePermissions.Any(rp =>
            rp.Permission.NormalizedName == Normalizer.Normalize(PermissionNames.RolesManage));
        if (currentlyHasRolesManage && !keepsRolesManage)
        {
            var otherRolesWithIt = await _roles.CountRolesWithPermissionAsync(
                Normalizer.Normalize(PermissionNames.RolesManage), role.Id, ct);
            if (otherRolesWithIt == 0)
                throw new ConflictException(
                    $"Cannot remove '{PermissionNames.RolesManage}' from this role - no other role would be able to manage roles/permissions afterwards.");
        }

        var now = _clock.UtcNow;
        role.RolePermissions.Clear();
        foreach (var permission in permissions)
        {
            role.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                AssignedAt = now,
                AssignedBy = _currentUser.UserName
            });
        }
        role.UpdatedAt = now;
        role.UpdatedBy = _currentUser.UserName;

        _roles.Update(role);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditAction.RolePermissionsUpdated, audit, _currentUser.UserId, _currentUser.UserName,
            nameof(Role), role.Id.ToString(), newValues: string.Join(", ", permissions.Select(p => p.Name)), ct: ct);

        return role.ToDto(await _roles.CountUsersInRoleAsync(role.Id, ct));
    }
}
