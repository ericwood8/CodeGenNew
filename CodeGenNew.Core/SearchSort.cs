using System.Data;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The user-chosen sort of a search grid, as the search routine of each database writes it. The caller passes a column NAME and a direction; the routine
/// compares that name with a fixed list of the table's own sortable columns inside one <c>ORDER BY</c>, so a name from outside is never pasted into SQL (an unknown
/// name matches nothing and the fixed default order applies). The default order (the best display column, then the primary key) always follows as the tie-breaker. </summary>
public static class SearchSort
{
    /// <summary> The longest column name the routines accept. </summary>
    public const int MaxNameLength = 128;

    /// <summary> One column a grid can be sorted by: its generated name (what the caller sends) and the SQL expression to order by. A foreign key sorts by the parent's
    /// display column (what the grid shows), not by the id. </summary>
    public sealed record Entry(string Name, ColumnModel Column, ForeignKeyModel? Parent);

    /// <summary> The columns that can be sorted: every column except long text (notes, memos), binary data and xml. Which of them a grid shows is up to the screen. </summary>
    public static List<Entry> Entries(TableModel m) => m.Columns
        .Where(IsSortable)
        .Select(c => new Entry(c.Name, c, ParentOf(m, c)))
        .ToList();

    public static bool IsSortable(ColumnModel c) =>
        !c.IsLongTextColumn
        && c.SqlType is not (SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image or SqlDbType.Xml or SqlDbType.Timestamp or SqlDbType.Udt or SqlDbType.Structured or SqlDbType.Variant);

    // A single-column foreign key whose parent has a display column: the grid shows the parent's name, so that is what to sort by.
    private static ForeignKeyModel? ParentOf(TableModel m, ColumnModel c) => m.ForeignKeys.FirstOrDefault(fk =>
        fk.ReferencingColumns.Count == 1 && fk.ReferencingColumns[0].Equals(c.Name, StringComparison.OrdinalIgnoreCase)
        && fk.ReferencedColumns.Count == 1 && fk.ReferencedDisplayColumns.Count > 0);

    private static string DisplayDbColumn(ForeignKeyModel fk) => fk.ReferencedDisplayDbColumns[0];

    // ------------------------------------------------------------------------------------------------ SQL Server

    /// <summary> The leading part of the ORDER BY (each item ends with a comma) for a procedure with <c>@SortColumn NVARCHAR(128)</c> and <c>@SortDescending BIT</c>.
    /// The table is aliased <c>t</c> in the query. Empty when nothing can be sorted. </summary>
    public static string SqlServer(TableModel m)
    {
        static string Q(string name) => "[" + name.Replace("]", "]]") + "]";
        var o = new StringBuilder();
        foreach (var e in Entries(m))
        {
            string expr = e.Parent is { } fk
                ? $"(SELECT TOP 1 p.{Q(DisplayDbColumn(fk))} FROM {Q(fk.ReferencedSchema)}.{Q(fk.ReferencedDbTable)} AS p WHERE p.{Q(fk.ReferencedDbColumns[0])} = t.{Q(e.Column.DbName)})"
                : "t." + Q(e.Column.DbName);
            string name = e.Name.Replace("'", "''");
            o.Append($"\t\tCASE WHEN @SortColumn = N'{name}' AND @SortDescending = 0 THEN {expr} END ASC,\r\n");
            o.Append($"\t\tCASE WHEN @SortColumn = N'{name}' AND @SortDescending = 1 THEN {expr} END DESC,\r\n");
        }
        return o.ToString();
    }

    // ------------------------------------------------------------------------------------------------ PostgreSQL

    /// <summary> The same for a function with <c>"SortColumn" text</c> and <c>"SortDescending" boolean</c>; the table is aliased <c>t</c>. The names are compared in lower case. </summary>
    public static string Postgres(TableModel m)
    {
        static string Q(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
        var o = new StringBuilder();
        foreach (var e in Entries(m))
        {
            string expr = e.Parent is { } fk
                ? $"(SELECT p.{Q(DisplayDbColumn(fk))} FROM {Q(fk.ReferencedSchema)}.{Q(fk.ReferencedDbTable)} AS p WHERE p.{Q(fk.ReferencedDbColumns[0])} = t.{Q(e.Column.DbName)} LIMIT 1)"
                : "t." + Q(e.Column.DbName);
            string name = e.Name.ToLowerInvariant().Replace("'", "''");
            o.Append($"\t\tCASE WHEN lower(\"SortColumn\") = '{name}' AND NOT \"SortDescending\" THEN {expr} END ASC,\n");
            o.Append($"\t\tCASE WHEN lower(\"SortColumn\") = '{name}' AND \"SortDescending\" THEN {expr} END DESC,\n");
        }
        return o.ToString();
    }

    // ------------------------------------------------------------------------------------------------ MySQL

    /// <summary> The same for a procedure with <c>IN `SortColumn` varchar(128)</c> and <c>IN `SortDescending` tinyint(1)</c>; the table is aliased <c>t</c>. </summary>
    public static string MySql(TableModel m)
    {
        static string Q(string name) => "`" + name.Replace("`", "``") + "`";
        var o = new StringBuilder();
        foreach (var e in Entries(m))
        {
            string expr = e.Parent is { } fk
                ? $"(SELECT p.{Q(DisplayDbColumn(fk))} FROM {Q(fk.ReferencedDbTable)} AS p WHERE p.{Q(fk.ReferencedDbColumns[0])} = t.{Q(e.Column.DbName)} LIMIT 1)"
                : "t." + Q(e.Column.DbName);
            string name = e.Name.Replace("\\", "\\\\").Replace("'", "''");
            o.Append($"\t\tCASE WHEN `SortColumn` = '{name}' AND `SortDescending` = 0 THEN {expr} END ASC,\n");
            o.Append($"\t\tCASE WHEN `SortColumn` = '{name}' AND `SortDescending` = 1 THEN {expr} END DESC,\n");
        }
        return o.ToString();
    }
}
