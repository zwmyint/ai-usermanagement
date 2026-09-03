using UserManagement.UI.Contracts;

namespace UserManagement.UI.Services;

public interface IRoleApiService
{
    Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken ct = default);
    Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync(CancellationToken ct = default);
    Task<RoleDto> UpdatePermissionsAsync(Guid id, UpdateRolePermissionsRequest request, CancellationToken ct = default);
}

public class RoleApiService : ApiClientBase, IRoleApiService
{
    public RoleApiService(IHttpClientFactory httpClientFactory) : base(httpClientFactory.CreateClient("Api")) { }

    public Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<RoleDto>>(HttpMethod.Get, "api/roles", null, ct);

    public Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<RoleDto>(HttpMethod.Get, $"api/roles/{id}", null, ct);

    public Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct = default) =>
        SendAsync<RoleDto>(HttpMethod.Post, "api/roles", request, ct);

    public Task<RoleDto> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default) =>
        SendAsync<RoleDto>(HttpMethod.Put, $"api/roles/{id}", request, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/roles/{id}", null, ct);

    public Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync(CancellationToken ct = default) =>
        SendAsync<IReadOnlyList<PermissionDto>>(HttpMethod.Get, "api/roles/permissions", null, ct);

    public Task<RoleDto> UpdatePermissionsAsync(Guid id, UpdateRolePermissionsRequest request, CancellationToken ct = default) =>
        SendAsync<RoleDto>(HttpMethod.Put, $"api/roles/{id}/permissions", request, ct);
}
