namespace Kue.Api.Entities;

/// <summary>
/// Who can see a custom list.
/// </summary>
public static class CustomListVisibility
{
    /// <summary>Anyone, including signed-out visitors.</summary>
    public const string Public = "public";

    /// <summary>The owner and their accepted friends.</summary>
    public const string Friends = "friends";

    /// <summary>The owner only.</summary>
    public const string Private = "private";

    public static bool IsValid(string? value) =>
        value is Public or Friends or Private;

    public static string Normalize(string? value, string fallback)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return IsValid(trimmed) ? trimmed! : fallback;
    }
}
