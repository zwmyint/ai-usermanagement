using UserManagement.Domain.Exceptions;

namespace UserManagement.Application.Common;

/// <summary>
/// Server-side password complexity rules. Mirrored client-side by the UI for immediate feedback.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 128;

    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();
        password ??= string.Empty;

        if (password.Length < MinimumLength)
            errors.Add($"Password must be at least {MinimumLength} characters long.");
        if (password.Length > MaximumLength)
            errors.Add($"Password must not exceed {MaximumLength} characters.");
        if (!password.Any(char.IsUpper))
            errors.Add("Password must contain at least one uppercase letter.");
        if (!password.Any(char.IsLower))
            errors.Add("Password must contain at least one lowercase letter.");
        if (!password.Any(char.IsDigit))
            errors.Add("Password must contain at least one digit.");
        if (password.All(char.IsLetterOrDigit))
            errors.Add("Password must contain at least one non-alphanumeric character.");

        return errors;
    }

    public static void EnsureValid(string? password, string fieldName = "password")
    {
        var errors = Validate(password);
        if (errors.Count > 0)
            throw new ValidationException(new Dictionary<string, string[]> { [fieldName] = errors.ToArray() });
    }
}
