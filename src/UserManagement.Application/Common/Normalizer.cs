namespace UserManagement.Application.Common;

/// <summary>Centralised normalisation so lookups are consistent across services and repositories.</summary>
public static class Normalizer
{
    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
}
