using UserManagement.Domain.Entities;

namespace UserManagement.Domain.Interfaces;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Role?> GetByIdWithPermissionsAsync(Guid id, CancellationToken ct = default);
    Task<Role?> GetByNameAsync(string normalizedName, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByNamesAsync(IEnumerable<string> normalizedNames, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string normalizedName, Guid? excludeRoleId = null, CancellationToken ct = default);
    Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RoleUserCount>> GetRoleUserCountsAsync(CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
    void Update(Role role);
    void Remove(Role role);

    /// <summary>All roles that currently grant <paramref name="permissionName"/> to at least one assignment.</summary>
    Task<int> CountRolesWithPermissionAsync(string permissionNormalizedName, Guid? excludeRoleId = null, CancellationToken ct = default);
}

public interface IPermissionRepository
{
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken ct = default);
    Task<Permission?> GetByNameAsync(string normalizedName, CancellationToken ct = default);
    Task<IReadOnlyList<Permission>> GetByNamesAsync(IEnumerable<string> normalizedNames, CancellationToken ct = default);
}

/// <summary>Number of users assigned to a single role.</summary>
public record RoleUserCount(Guid RoleId, string RoleName, int UserCount);
