using Microsoft.EntityFrameworkCore;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Interfaces;

namespace UserManagement.Infrastructure.Persistence.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _db;

    public RoleRepository(AppDbContext db) => _db = db;

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Roles.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Role?> GetByNameAsync(string normalizedName, CancellationToken ct = default) =>
        _db.Roles.FirstOrDefaultAsync(x => x.NormalizedName == normalizedName, ct);

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Roles.OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Role>> GetByNamesAsync(IEnumerable<string> normalizedNames, CancellationToken ct = default)
    {
        var names = normalizedNames.ToList();
        return await _db.Roles.Where(x => names.Contains(x.NormalizedName)).ToListAsync(ct);
    }

    public Task<bool> NameExistsAsync(string normalizedName, Guid? excludeRoleId = null, CancellationToken ct = default) =>
        _db.Roles.AnyAsync(x => x.NormalizedName == normalizedName && (excludeRoleId == null || x.Id != excludeRoleId), ct);

    public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) =>
        _db.UserRoles.CountAsync(x => x.RoleId == roleId, ct);

    public Task<int> CountAsync(CancellationToken ct = default) => _db.Roles.CountAsync(ct);

    public async Task<IReadOnlyList<RoleUserCount>> GetRoleUserCountsAsync(CancellationToken ct = default) =>
        await _db.Roles.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new RoleUserCount(x.Id, x.Name, x.UserRoles.Count()))
            .ToListAsync(ct);

    public async Task AddAsync(Role role, CancellationToken ct = default) => await _db.Roles.AddAsync(role, ct);

    public void Update(Role role) => _db.Roles.Update(role);

    public void Remove(Role role) => _db.Roles.Remove(role);
}
