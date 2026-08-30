using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace UserManagement.API.IntegrationTests.Controllers;

public class DashboardEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DashboardEndpointsTests(ApiWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task GetSummary_ReturnsUnauthorized_WhenAnonymous()
    {
        var response = await _client.GetAsync("/api/dashboard/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSummary_OmitsAdminStats_ForNonAdminUser()
    {
        var token = await RegisterAndLoginAsync();

        var summary = await GetSummaryAsync(token);

        Assert.Null(summary.AdminStats);
        Assert.NotNull(summary.MyActivity);
        Assert.NotNull(summary.MyActivity.LastLoginAt);
        Assert.Null(summary.MyActivity.PreviousLoginAt);
        Assert.Equal(1, summary.MyActivity.TotalLogins);
    }

    [Fact]
    public async Task GetSummary_ReturnsPopulatedAdminStats_ForSeededAdmin()
    {
        var token = await LoginAsync("admin@example.com", "ChangeMe123!");

        var summary = await GetSummaryAsync(token);

        Assert.NotNull(summary.AdminStats);
        Assert.True(summary.AdminStats!.TotalUsers >= 1);
        Assert.Equal(summary.AdminStats.TotalUsers, summary.AdminStats.ActiveUsers + summary.AdminStats.InactiveUsers);
        Assert.True(summary.AdminStats.TotalRoles >= 2);
        Assert.True(summary.AdminStats.TotalAuditLogs >= 1);
        Assert.True(summary.AdminStats.LoginsLast24Hours >= 1);
        Assert.Contains(summary.AdminStats.RoleBreakdown, r => r.RoleName == "Admin");
        Assert.NotEmpty(summary.AdminStats.RecentUsers);
    }

    [Fact]
    public async Task GetSummary_ReportsPreviousLogin_AfterSecondSignIn()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var userName = $"user{Guid.NewGuid():N}"[..20];
        await RegisterAsync(userName, email);

        await LoginAsync(email, "ValidPass1!");
        var token = await LoginAsync(email, "ValidPass1!");

        var summary = await GetSummaryAsync(token);

        Assert.NotNull(summary.MyActivity.PreviousLoginAt);
        Assert.Equal(2, summary.MyActivity.TotalLogins);
        Assert.True(summary.MyActivity.LastLoginAt >= summary.MyActivity.PreviousLoginAt);
    }

    private async Task<DashboardSummaryDto> GetSummaryAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DashboardSummaryDto>>();
        Assert.True(body!.Success);
        return body.Data!;
    }

    private async Task<string> RegisterAndLoginAsync()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var userName = $"user{Guid.NewGuid():N}"[..20];
        await RegisterAsync(userName, email);
        return await LoginAsync(email, "ValidPass1!");
    }

    private async Task RegisterAsync(string userName, string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            userName,
            email,
            password = "ValidPass1!",
            confirmPassword = "ValidPass1!"
        });
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> LoginAsync(string userNameOrEmail, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail, password });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        return body!.Data!.AccessToken;
    }
}
