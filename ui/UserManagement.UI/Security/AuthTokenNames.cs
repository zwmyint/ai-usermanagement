namespace UserManagement.UI.Security;

/// <summary>Keys used to stash the API's JWT/refresh tokens inside the encrypted authentication cookie's properties.</summary>
public static class AuthTokenNames
{
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";
    public const string AccessTokenExpiresAt = "access_token_expires_at";
}
