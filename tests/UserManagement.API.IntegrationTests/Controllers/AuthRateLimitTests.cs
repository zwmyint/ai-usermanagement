using System.Net;
using System.Net.Http.Json;

namespace UserManagement.API.IntegrationTests.Controllers;

public class AuthRateLimitTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthRateLimitTests(ApiWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task PasswordRecoveryLimit_ExhaustionDoesNotThrottleRegistration()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await _client.PostAsJsonAsync("/api/auth/forgot-password", new
            {
                email = "missing@example.com"
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limited = await _client.PostAsJsonAsync("/api/auth/forgot-password", new
        {
            email = "missing@example.com"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = $"user{Guid.NewGuid():N}"[..20],
            email = $"{Guid.NewGuid():N}@example.com",
            password = "ValidPass1!",
            confirmPassword = "ValidPass1!"
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
    }
}
