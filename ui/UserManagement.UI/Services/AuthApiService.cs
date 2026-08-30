using UserManagement.UI.Contracts;

namespace UserManagement.UI.Services;

public interface IAuthApiService
{
    Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task RevokeAsync(string refreshToken, CancellationToken ct = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
}

/// <summary>Calls the anonymous auth endpoints using a plain HttpClient (no bearer token attached).</summary>
public class AuthApiService : ApiClientBase, IAuthApiService
{
    public AuthApiService(IHttpClientFactory httpClientFactory) : base(httpClientFactory.CreateClient("ApiRaw")) { }

    public Task<UserDto> RegisterAsync(RegisterRequest request, CancellationToken ct = default) =>
        SendAsync<UserDto>(HttpMethod.Post, "api/auth/register", request, ct);

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/login", request, ct);

    public Task RevokeAsync(string refreshToken, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/auth/revoke", new RevokeRequest { RefreshToken = refreshToken }, ct);

    public Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/auth/forgot-password", request, ct);

    public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/auth/reset-password", request, ct);
}
