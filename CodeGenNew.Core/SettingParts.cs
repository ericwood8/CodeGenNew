namespace CodeGenNew.Core;

/// <summary> The parts of a list setting whose items read <c>Table:Part:Part</c> (<c>QueueTables</c>, <c>TreeTables</c>, <c>SoftDeleteTables</c>, <c>EffectiveDatedTables</c>, <c>ArchiveTables</c>): one place that
/// splits an item, trims it and says which part is missing, so every planner reads an item the same way. </summary>
public static class SettingParts
{
    /// <summary> The items of the setting that name a table, each split at the colons and trimmed; the first part is the table. </summary>
    public static IReadOnlyList<string[]> Items(IEnumerable<string> setting) =>
        setting.Select(item => item.Split(':', StringSplitOptions.TrimEntries)).Where(parts => parts[0].Length > 0).ToList();

    /// <summary> The part at <paramref name="index"/> of an item, or null when the item stops before it or the part is empty. </summary>
    public static string? At(string[] parts, int index) => index < parts.Length && parts[index].Length > 0 ? parts[index] : null;
}
