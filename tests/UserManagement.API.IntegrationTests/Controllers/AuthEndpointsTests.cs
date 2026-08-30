using System.Net;
using System.Net.Http.Json;

namespace UserManagement.API.IntegrationTests.Controllers;

public class AuthEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(ApiWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsOk_Anonymously()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_ThenLogin_ReturnsAccessAndRefreshTokens()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = $"user{Guid.NewGuid():N}"[..20],
            email,
            password = "ValidPass1!",
            confirmPassword = "ValidPass1!",
            firstName = "Test",
            lastName = "User"
        });

        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.NotNull(registerBody);
        Assert.True(registerBody!.Success);
        Assert.Contains("User", registerBody.Data!.Roles);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = email,
            password = "ValidPass1!"
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        Assert.NotNull(loginBody);
        Assert.True(loginBody!.Success);
        Assert.False(string.IsNullOrEmpty(loginBody.Data!.AccessToken));
        Assert.False(string.IsNullOrEmpty(loginBody.Data.RefreshToken));
    }

    [Fact]
    public async Task Register_ReturnsConflict_ForDuplicateEmail()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var payload = new
        {
            userName = $"user{Guid.NewGuid():N}"[..20],
            email,
            password = "ValidPass1!",
            confirmPassword = "ValidPass1!"
        };

        var first = await _client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = $"user{Guid.NewGuid():N}"[..20],
            email,
            password = "ValidPass1!",
            confirmPassword = "ValidPass1!"
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_ForInvalidCredentials()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "nonexistent@example.com",
            password = "WrongPass1!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_ReturnsUnauthorized_WithoutBearerToken()
    {
        var response = await _client.GetAsync("/api/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Profile_ReturnsOwnData_WithValidBearerToken()
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

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = email, password = "ValidPass1!" });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody!.Data!.AccessToken);

        var profileResponse = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profileBody = await profileResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.Equal(email, profileBody!.Data!.Email);
    }

    [Fact]
    public async Task ProfilePicture_UploadsValidatedImage_AndReturnsPublicPath()
    {
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            userNameOrEmail = "admin@example.com",
            password = "ChangeMe123!"
        });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();
        Assert.NotNull(loginBody?.Data);

        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        using var form = new MultipartFormDataContent();
        using var image = new ByteArrayContent(png);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(image, "profilePicture", "avatar.png");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/profile/profile-picture") { Content = form };
        upload.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody!.Data!.AccessToken);

        var uploadResponse = await _client.SendAsync(upload);

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.StartsWith("/uploads/profile-pictures/", uploadBody!.Data!.ProfilePicturePath);

        var imageResponse = await _client.GetAsync(uploadBody.Data.ProfilePicturePath);
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal("image/jpeg", imageResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Users_ReturnsForbidden_ForNonAdminUser()
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

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = email, password = "ValidPass1!" });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<ApiResponse<AuthResponseDto>>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody!.Data!.AccessToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
