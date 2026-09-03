using UserManagement.Application.DTOs;
using UserManagement.Domain.Entities;

namespace UserManagement.Application.Mapping;

public static class MappingExtensions
{
    public static UserDto ToDto(this User user, DateTime utcNow) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        FullName = user.FullName,
        PhoneNumber = user.PhoneNumber,
        ProfilePicturePath = user.ProfilePicturePath,
        IsActive = user.IsActive,
        EmailConfirmed = user.EmailConfirmed,
        IsLockedOut = user.IsLockedOut(utcNow),
        LastLoginAt = user.LastLoginAt,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
        Roles = user.UserRoles
            .Where(ur => ur.Role is not null)
            .Select(ur => ur.Role.Name)
            .OrderBy(n => n)
            .ToList(),
        Permissions = user.UserRoles
            .Where(ur => ur.Role is not null)
            .SelectMany(ur => ur.Role.RolePermissions.Where(rp => rp.Permission is not null).Select(rp => rp.Permission.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList()
    };

    public static RoleDto ToDto(this Role role, int userCount = 0) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Description = role.Description,
        IsSystemRole = role.IsSystemRole,
        UserCount = userCount,
        CreatedAt = role.CreatedAt,
        Permissions = role.RolePermissions
            .Where(rp => rp.Permission is not null)
            .Select(rp => rp.Permission.Name)
            .OrderBy(n => n)
            .ToList()
    };

    public static PermissionDto ToDto(this Permission permission) => new()
    {
        Id = permission.Id,
        Name = permission.Name,
        Description = permission.Description
    };

    public static AuditLogDto ToDto(this AuditLog log) => new()
    {
        Id = log.Id,
        UserId = log.UserId,
        UserName = log.UserName,
        Action = log.Action,
        ActionName = log.Action.ToString(),
        EntityName = log.EntityName,
        EntityId = log.EntityId,
        IpAddress = log.IpAddress,
        UserAgent = log.UserAgent,
        Succeeded = log.Succeeded,
        Message = log.Message,
        Timestamp = log.Timestamp
    };
}
