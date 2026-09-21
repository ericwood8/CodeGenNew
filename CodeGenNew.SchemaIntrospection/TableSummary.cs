namespace CodeGenNew.SchemaIntrospection;

/// <summary> Lightweight per-table info for populating the TreeView (Docs/specs.md section 9.4) --
/// deliberately much cheaper than building a full TableModel for every table up front. A full
/// TableModel is only built for the one table the developer actually selects a template against. </summary>
public class TableSummary
{
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    public bool HasPrimaryKey { get; init; }
    public bool HasUniqueIndex { get; init; }
    public bool IsReservedWordName { get; init; }
    public bool IsCSharpReservedWordName { get; init; }
}
