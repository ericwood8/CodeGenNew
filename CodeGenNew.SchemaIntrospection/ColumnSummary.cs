namespace CodeGenNew.SchemaIntrospection;

/// <summary> Lightweight per-column info for showing a table's columns under it in the TreeView --
/// cheaper than the full ColumnModel built by BuildTableModelAsync, since it's only used for display. </summary>
public class ColumnSummary
{
    public required string Name { get; init; }
    public required string SqlTypeName { get; init; }
    public bool IsNullable { get; init; }
    public bool IsPrimaryKey { get; init; }
}
