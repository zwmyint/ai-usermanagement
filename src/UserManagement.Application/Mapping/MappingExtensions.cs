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
            .ToList()
    };

    public static RoleDto ToDto(this Role role, int userCount = 0) => new()
    {
        Id = role.Id,
        Name = role.Name,
        Description = role.Description,
        IsSystemRole = role.IsSystemRole,
        UserCount = userCount,
        CreatedAt = role.CreatedAt
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
