namespace UserManagement.Application.Common.Settings;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "UserManagement.API";
    public string Audience { get; set; } = "UserManagement.UI";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public int ClockSkewSeconds { get; set; } = 30;
}

public class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@example.com";
    public string FromName { get; set; } = "User Management";
    /// <summary>When true, emails are written to the log instead of being sent (development only).</summary>
    public bool PickupDirectoryOnly { get; set; }
    public string? PickupDirectory { get; set; }
}

public class SecuritySettings
{
    public const string SectionName = "Security";

    public int MaxFailedAccessAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int PasswordResetTokenMinutes { get; set; } = 30;
    /// <summary>Base URL of the UI used to build the password-reset link sent by email.</summary>
    public string ResetPasswordUrl { get; set; } = "https://localhost:7002/Account/ResetPassword";
}

public class SeedSettings
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; } = true;
    public string AdminUserName { get; set; } = "admin";
    public string AdminEmail { get; set; } = "admin@example.com";
    public string AdminPassword { get; set; } = string.Empty;
}

public class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}
