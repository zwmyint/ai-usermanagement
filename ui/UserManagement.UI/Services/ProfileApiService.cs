using UserManagement.UI.Contracts;

namespace UserManagement.UI.Services;

public interface IProfileApiService
{
    Task<UserDto> GetProfileAsync(CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
}

/// <summary>Calls the authenticated /api/profile and /api/auth/logout endpoints via the bearer-token client.</summary>
public class ProfileApiService : ApiClientBase, IProfileApiService
{
    public ProfileApiService(IHttpClientFactory httpClientFactory) : base(httpClientFactory.CreateClient("Api")) { }

    public Task<UserDto> GetProfileAsync(CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Get, "api/profile", null, ct);

    public Task<UserDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Put, "api/profile", request, ct);

    public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/profile/change-password", request, ct);
}
