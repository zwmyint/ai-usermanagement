using UserManagement.Domain.Entities;

namespace UserManagement.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<User?> GetByUserNameOrEmailAsync(string normalizedValue, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeUserId = null, CancellationToken ct = default);
    Task<bool> UserNameExistsAsync(string normalizedUserName, Guid? excludeUserId = null, CancellationToken ct = default);
    Task<(IReadOnlyList<User> Items, int TotalCount, int FilteredCount)> SearchAsync(
        string? search, string? roleName, bool? isActive, string? sortBy, bool sortDescending,
        int skip, int take, CancellationToken ct = default);
    Task<int> CountActiveInRoleAsync(string normalizedRoleName, Guid? excludeUserId = null, CancellationToken ct = default);
    Task<(int Total, int Active, int Inactive)> GetCountsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetRecentlyCreatedAsync(int take, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    void Update(User user);
    void Remove(User user);
}
