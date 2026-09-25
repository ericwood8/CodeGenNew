namespace CodeGenNew.Core;

/// <summary> General-purpose string comparison helpers. Column/table/category name matching throughout this
/// codebase is almost always case-insensitive, ordinal (never culture-sensitive -- these are SQL Server
/// identifiers, not user-facing text) -- consolidating the "x.Equals(y, StringComparison.OrdinalIgnoreCase)"
/// spelling into one extension both shortens 40+ call sites and rules out someone reaching for
/// InvariantCultureIgnoreCase or CurrentCultureIgnoreCase by mistake at a new one. </summary>
public static class StringExtensions
{
    /// <summary> Same null-handling as string.Equals(a, b, StringComparison.OrdinalIgnoreCase): true if both
    /// are null, false if only one is. </summary>
    public static bool EqualsIgnoreCase(this string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool StartsWithIgnoreCase(this string value, string prefix) => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    public static bool ContainsIgnoreCase(this string value, string substring) => value.Contains(substring, StringComparison.OrdinalIgnoreCase);
    public static bool EndsWithIgnoreCase(this string value, string suffix) => value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
