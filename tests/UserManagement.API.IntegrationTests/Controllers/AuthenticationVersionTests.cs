using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace UserManagement.API.IntegrationTests.Controllers;

public class AuthenticationVersionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthenticationVersionTests(ApiWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Logout_ImmediatelyInvalidatesTheAccessToken()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var userName = $"user{Guid.NewGuid():N}"[..20];
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            userName,
            email,
            password = "ValidPass1!",
            confirmPassword = "ValidPass1!"
        });

        var login = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = email,
            password = "ValidPass1!"
        });
        var loginBody = await login.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        var accessToken = loginBody!.Data!.AccessToken;

        var logout = await SendAuthenticatedAsync(HttpMethod.Post, "/api/auth/logout", accessToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var profile = await SendAuthenticatedAsync(HttpMethod.Get, "/api/profile", accessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpMethod method, string url, string accessToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

}
