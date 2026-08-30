using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using UserManagement.UI.Configuration;
using UserManagement.UI.Contracts;

namespace UserManagement.UI.Security;

/// <summary>
/// Attaches the signed-in user's JWT access token to outgoing API requests and transparently
/// refreshes it (via the API's refresh-token endpoint) when it has expired or the API returns 401.
/// The refreshed tokens are persisted back into the encrypted authentication cookie.
/// </summary>
public class ApiAuthTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApiSettings _apiSettings;

    public ApiAuthTokenHandler(IHttpContextAccessor contextAccessor, IHttpClientFactory httpClientFactory, IOptions<ApiSettings> apiSettings)
    {
        _contextAccessor = contextAccessor;
        _httpClientFactory = httpClientFactory;
        _apiSettings = apiSettings.Value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var httpContext = _contextAccessor.HttpContext;
        var accessToken = await GetValidAccessTokenAsync(httpContext, ct);

        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && httpContext is not null)
        {
            // The access token may have just expired between validation and send; try one forced refresh + retry.
            var refreshed = await RefreshTokensAsync(httpContext, ct);
            if (refreshed is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
                response.Dispose();
                response = await base.SendAsync(request, ct);
            }
        }

        return response;
    }

    private async Task<string?> GetValidAccessTokenAsync(HttpContext? httpContext, CancellationToken ct)
    {
        if (httpContext is null) return null;

        var result = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Properties is null) return null;

        var accessToken = result.Properties.Items.TryGetValue(AuthTokenNames.AccessToken, out var at) ? at : null;
        var expiresAtRaw = result.Properties.Items.TryGetValue(AuthTokenNames.AccessTokenExpiresAt, out var exp) ? exp : null;

        if (!string.IsNullOrEmpty(accessToken) && DateTime.TryParse(expiresAtRaw, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
        {
            // Refresh a little early to avoid races with the API's own clock skew tolerance.
            if (expiresAt > DateTime.UtcNow.AddSeconds(30))
                return accessToken;
        }

        return await RefreshTokensAsync(httpContext, ct);
    }

    private async Task<string?> RefreshTokensAsync(HttpContext httpContext, CancellationToken ct)
    {
        var result = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null || result.Properties is null) return null;

        var refreshToken = result.Properties.Items.TryGetValue(AuthTokenNames.RefreshToken, out var rtok) ? rtok : null;
        if (string.IsNullOrEmpty(refreshToken)) return null;

        try
        {
            var client = _httpClientFactory.CreateClient("ApiRaw");
            if (client.BaseAddress is null) client.BaseAddress = new Uri(_apiSettings.BaseUrl);

            using var response = await client.PostAsJsonAsync("api/auth/refresh", new RefreshRequest { RefreshToken = refreshToken }, ct);
            if (!response.IsSuccessStatusCode) return null;

            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>((System.Text.Json.JsonSerializerOptions?)null, ct);
            if (envelope is null || !envelope.Success || envelope.Data is null) return null;

            var auth = envelope.Data;
            result.Properties.Items[AuthTokenNames.AccessToken] = auth.AccessToken;
            result.Properties.Items[AuthTokenNames.RefreshToken] = auth.RefreshToken;
            result.Properties.Items[AuthTokenNames.AccessTokenExpiresAt] = auth.AccessTokenExpiresAt.ToString("o");

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal, result.Properties);

            return auth.AccessToken;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
