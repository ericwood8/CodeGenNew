using CodeGenNew.Core;

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

    /// <summary> Cheap, bulk-computed equivalent of TableModel.IsJunctionTable (same rule, same
    /// AuditColumnClassifier), so the TreeView's right-click menu can offer a junction-only template
    /// without building a full TableModel for every table up front. </summary>
    public bool IsJunctionTable { get; init; }

    /// <summary> Cheap, bulk-computed equivalent of TableModel.HasAtLeastOneChildForeignKey: true when at
    /// least one other table has a foreign key pointing back at this one, computed for every table in a
    /// single catalog query instead of building a full TableModel for every table up front. </summary>
    public bool HasChildForeignKeys { get; init; }

    /// <summary> Bulk-computed equivalent of TableModel.PrimaryKeyShape. </summary>
    public PrimaryKeyShape PrimaryKeyShape { get; init; }

    /// <summary> Bulk-computed equivalent of TableModel.IsNameActiveTable. </summary>
    public bool IsNameActiveTable { get; init; }

    public bool IsReservedWordName { get; init; }
    public bool IsCSharpReservedWordName { get; init; }
}
