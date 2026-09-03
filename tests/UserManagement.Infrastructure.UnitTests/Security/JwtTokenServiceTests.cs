using Microsoft.Extensions.Options;
using UserManagement.Application.Common.Settings;
using UserManagement.Application.Interfaces;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Infrastructure.Security;
using UserManagement.Infrastructure.Services;

namespace UserManagement.Infrastructure.UnitTests.Security;

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateSut(string key = "01234567890123456789012345678901") =>
        new(Options.Create(new JwtSettings
        {
            Key = key,
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7,
            ClockSkewSeconds = 30
        }), new SystemDateTimeProvider());

    [Fact]
    public void Constructor_Throws_WhenKeyIsTooShort()
    {
        Assert.Throws<InvalidOperationException>(() => CreateSut("short-key"));
    }

    [Fact]
    public void CreateAccessToken_ProducesTokenThatValidatesSuccessfully_WithClaims()
    {
        var sut = CreateSut();
        var user = new User { Id = Guid.NewGuid(), UserName = "jdoe", Email = "jdoe@example.com", FirstName = "John", LastName = "Doe" };

        var token = sut.CreateAccessToken(user, new[] { "Admin", "User" }, new[] { "Users.Read" });
        var principal = sut.ValidateAccessToken(token.Token);

        Assert.NotNull(principal);
        Assert.Equal(user.Id.ToString(), principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(user.AuthenticationVersion.ToString(), principal.FindFirst(AuthClaimTypes.AuthenticationVersion)?.Value);
        Assert.Contains(principal.FindAll(System.Security.Claims.ClaimTypes.Role), c => c.Value == "Admin");
        Assert.Contains(principal.FindAll(System.Security.Claims.ClaimTypes.Role), c => c.Value == "User");
        Assert.Contains(principal.FindAll(AuthClaimTypes.Permission), c => c.Value == "Users.Read");
    }

    [Fact]
    public void ValidateAccessToken_ReturnsNull_ForTamperedToken()
    {
        var sut = CreateSut();
        var user = new User { Id = Guid.NewGuid(), UserName = "jdoe", Email = "jdoe@example.com" };

        var token = sut.CreateAccessToken(user, Array.Empty<string>(), Array.Empty<string>());
        var tampered = token.Token[..^2] + (token.Token[^2] == 'A' ? "B" : "A") + token.Token[^1];

        Assert.Null(sut.ValidateAccessToken(tampered));
    }

    [Fact]
    public void ValidateAccessToken_ReturnsNull_WhenSignedWithDifferentKey()
    {
        var sut1 = CreateSut("11111111111111111111111111111111");
        var sut2 = CreateSut("22222222222222222222222222222222");
        var user = new User { Id = Guid.NewGuid(), UserName = "jdoe", Email = "jdoe@example.com" };

        var token = sut1.CreateAccessToken(user, Array.Empty<string>(), Array.Empty<string>());

        Assert.Null(sut2.ValidateAccessToken(token.Token));
    }

    [Fact]
    public void CreateRefreshToken_HashMatchesHashToken_ForTheSameRawValue()
    {
        var sut = CreateSut();

        var pair = sut.CreateRefreshToken();

        Assert.Equal(pair.TokenHash, sut.HashToken(pair.RawToken));
    }

    [Fact]
    public void HashToken_IsDeterministic()
    {
        var sut = CreateSut();

        Assert.Equal(sut.HashToken("same-input"), sut.HashToken("same-input"));
        Assert.NotEqual(sut.HashToken("input-a"), sut.HashToken("input-b"));
    }
}
