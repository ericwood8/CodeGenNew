namespace CodeGenNew.Core;

/// <summary> One index of a table: its key columns in order, under the database's own column names. Filtered, partial, expression and included-only columns are left out, so an index listed
/// here applies to every row. </summary>
public sealed record IndexModel(string Name, bool IsUnique, IReadOnlyList<string> Columns);
