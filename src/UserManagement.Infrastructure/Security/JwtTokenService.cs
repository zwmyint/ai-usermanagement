using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UserManagement.Application.Common.Settings;
using UserManagement.Application.Interfaces;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Security;

public class JwtTokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly IDateTimeProvider _clock;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(IOptions<JwtSettings> settings, IDateTimeProvider clock)
    {
        _settings = settings.Value;
        _clock = clock;

        if (string.IsNullOrWhiteSpace(_settings.Key) || Encoding.UTF8.GetByteCount(_settings.Key) < 32)
            throw new InvalidOperationException("Jwt:Key must be configured with at least 32 bytes of entropy.");

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
    }

    public AccessToken CreateAccessToken(User user, IEnumerable<string> roles)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new("fullName", user.FullName),
            new(AuthClaimTypes.AuthenticationVersion, user.AuthenticationVersion.ToString())
        };

        claims.AddRange(roles.Distinct(StringComparer.OrdinalIgnoreCase).Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public TokenPair CreateRefreshToken() =>
        CreateOpaqueToken(_clock.UtcNow.AddDays(_settings.RefreshTokenDays));

    public TokenPair CreatePasswordResetToken() =>
        // Lifetime is applied by the caller from SecuritySettings; a placeholder is used here.
        CreateOpaqueToken(_clock.UtcNow.AddHours(1));

    public string HashToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken ?? string.Empty)));

    public ClaimsPrincipal? ValidateAccessToken(string token, bool validateLifetime = true)
    {
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = true,
                ValidAudience = _settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                ValidateLifetime = validateLifetime,
                ClockSkew = TimeSpan.FromSeconds(_settings.ClockSkewSeconds)
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    private TokenPair CreateOpaqueToken(DateTime expiresAt)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return new TokenPair(raw, HashToken(raw), expiresAt);
    }
}
