using Microsoft.EntityFrameworkCore;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<User?> GetByIdWithRolesAsync(Guid id, CancellationToken ct = default) =>
        _db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        _db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, ct);

    public Task<User?> GetByUserNameOrEmailAsync(string normalizedValue, CancellationToken ct = default) =>
        _db.Users.Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedValue || x.NormalizedUserName == normalizedValue, ct);

    public Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeUserId = null, CancellationToken ct = default) =>
        _db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail && (excludeUserId == null || x.Id != excludeUserId), ct);

    public Task<bool> UserNameExistsAsync(string normalizedUserName, Guid? excludeUserId = null, CancellationToken ct = default) =>
        _db.Users.AnyAsync(x => x.NormalizedUserName == normalizedUserName && (excludeUserId == null || x.Id != excludeUserId), ct);

    public async Task<(IReadOnlyList<User> Items, int TotalCount, int FilteredCount)> SearchAsync(
        string? search, string? roleName, bool? isActive, string? sortBy, bool sortDescending,
        int skip, int take, CancellationToken ct = default)
    {
        var baseQuery = _db.Users.AsNoTracking();
        var totalCount = await baseQuery.CountAsync(ct);

        var query = baseQuery.Include(x => x.UserRoles).ThenInclude(x => x.Role).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(x =>
                x.NormalizedUserName.Contains(term) ||
                x.NormalizedEmail.Contains(term) ||
                (x.FirstName != null && x.FirstName.ToUpper().Contains(term)) ||
                (x.LastName != null && x.LastName.ToUpper().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(roleName))
        {
            var normalizedRole = roleName.Trim().ToUpperInvariant();
            query = query.Where(x => x.UserRoles.Any(ur => ur.Role.NormalizedName == normalizedRole));
        }

        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        var filteredCount = await query.CountAsync(ct);

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "username" => sortDescending ? query.OrderByDescending(x => x.UserName) : query.OrderBy(x => x.UserName),
            "email" => sortDescending ? query.OrderByDescending(x => x.Email) : query.OrderBy(x => x.Email),
            "firstname" => sortDescending ? query.OrderByDescending(x => x.FirstName) : query.OrderBy(x => x.FirstName),
            "lastname" => sortDescending ? query.OrderByDescending(x => x.LastName) : query.OrderBy(x => x.LastName),
            "fullname" => sortDescending
                ? query.OrderByDescending(x => x.FirstName).ThenByDescending(x => x.LastName)
                : query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName),
            "isactive" => sortDescending ? query.OrderByDescending(x => x.IsActive) : query.OrderBy(x => x.IsActive),
            "lastloginat" => sortDescending ? query.OrderByDescending(x => x.LastLoginAt) : query.OrderBy(x => x.LastLoginAt),
            "createdat" => sortDescending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => sortDescending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)
        };

        var items = await query.Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount, filteredCount);
    }

    public Task<int> CountActiveInRoleAsync(string normalizedRoleName, Guid? excludeUserId = null, CancellationToken ct = default) =>
        _db.Users.CountAsync(x => x.IsActive
            && (excludeUserId == null || x.Id != excludeUserId)
            && x.UserRoles.Any(ur => ur.Role.NormalizedName == normalizedRoleName), ct);

    public async Task<(int Total, int Active, int Inactive)> GetCountsAsync(CancellationToken ct = default)
    {
        var total = await _db.Users.CountAsync(ct);
        var active = await _db.Users.CountAsync(x => x.IsActive, ct);
        return (total, active, total - active);
    }

    public async Task<IReadOnlyList<User>> GetRecentlyCreatedAsync(int take, CancellationToken ct = default) =>
        await _db.Users.AsNoTracking()
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default) => await _db.Users.AddAsync(user, ct);

    public void Update(User user) => _db.Users.Update(user);

    public void Remove(User user) => _db.Users.Remove(user);
}
