namespace CodeGenNew.Core;

/// <summary> One index of a table: its key columns in order, under the database's own column names. Filtered, partial, expression and included-only columns are left out, so an index listed
/// here applies to every row. </summary>
public sealed record IndexModel(string Name, bool IsUnique, IReadOnlyList<string> Columns);

/// <summary> An index that holds only some of the rows (SQL Server filtered, PostgreSQL partial) or carries extra columns: kept apart from <see cref="IndexModel"/>, which other code reads as "applies to every row", and used
/// only by the create script. </summary>
/// <param name="Filter"> The row condition as the database writes it, with the keyword left out. </param>
public sealed record PartialIndexModel(string Name, bool IsUnique, IReadOnlyList<string> Columns, IReadOnlyList<string> IncludedColumns, string Filter);

/// <summary> One CHECK constraint of a table. </summary>
/// <param name="Definition"> The text that follows the constraint's name when it is added: <c>CHECK ([Total]&gt;=(0))</c>. </param>
public sealed record CheckConstraintModel(string Name, string Definition);
