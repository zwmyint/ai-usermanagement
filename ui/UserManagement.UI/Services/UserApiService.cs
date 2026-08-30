using System.Web;
using UserManagement.UI.Contracts;

namespace UserManagement.UI.Services;

public interface IUserApiService
{
    Task<PagedResult<UserDto>> GetUsersAsync(int page, int pageSize, string? search, string? role, bool? isActive,
        string? sortBy, bool sortDescending, CancellationToken ct = default);
    Task<UserDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<UserDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task<UserDto> AssignRolesAsync(Guid id, List<string> roles, CancellationToken ct = default);
}

public class UserApiService : ApiClientBase, IUserApiService
{
    public UserApiService(IHttpClientFactory httpClientFactory) : base(httpClientFactory.CreateClient("Api")) { }

    public Task<PagedResult<UserDto>> GetUsersAsync(int page, int pageSize, string? search, string? role,
        bool? isActive, string? sortBy, bool sortDescending, CancellationToken ct = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["page"] = page.ToString();
        query["pageSize"] = pageSize.ToString();
        if (!string.IsNullOrWhiteSpace(search)) query["search"] = search;
        if (!string.IsNullOrWhiteSpace(role)) query["role"] = role;
        if (isActive.HasValue) query["isActive"] = isActive.Value.ToString();
        if (!string.IsNullOrWhiteSpace(sortBy)) query["sortBy"] = sortBy;
        query["sortDescending"] = sortDescending.ToString();

        return SendAsync<PagedResult<UserDto>>(HttpMethod.Get, $"api/users?{query}", null, ct);
    }

    public Task<UserDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Get, $"api/users/{id}", null, ct);

    public Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Post, "api/users", request, ct);

    public Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Put, $"api/users/{id}", request, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/users/{id}", null, ct);

    public Task<UserDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Patch, $"api/users/{id}/active", new SetActiveRequest { IsActive = isActive }, ct);

    public Task<UserDto> AssignRolesAsync(Guid id, List<string> roles, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Put, $"api/users/{id}/roles", new AssignRolesRequest { Roles = roles }, ct);
}
