using System.Data;

namespace CodeGenNew.Core;

/// <summary> The two sides of a many-to-many junction table (<see cref="TableModel.IsJunctionTable"/>) as the junction routines and every junction screen read them: the anchor, whose row is being edited,
/// and the target, whose rows are linked to it. The key types are written once here, so the API, the desktop editors and the web components agree. </summary>
public sealed class JunctionShape
{
    internal JunctionShape(TableModel table)
    {
        AnchorForeignKey = table.JunctionForeignKeys[0];
        TargetForeignKey = table.JunctionForeignKeys[1];
        AnchorColumn = table.Columns.First(c => c.Name.EqualsIgnoreCase(AnchorForeignKey.ReferencingColumns[0]));
        TargetColumn = table.Columns.First(c => c.Name.EqualsIgnoreCase(TargetForeignKey.ReferencingColumns[0]));
    }

    public ForeignKeyModel AnchorForeignKey { get; }
    public ForeignKeyModel TargetForeignKey { get; }

    /// <summary> The junction column that holds the anchor's key. </summary>
    public ColumnModel AnchorColumn { get; }

    /// <summary> The junction column that holds the target's key. </summary>
    public ColumnModel TargetColumn { get; }

    /// <summary> The table whose row is being edited, and the table whose rows are linked to it. </summary>
    public string AnchorRole => AnchorForeignKey.ReferencedTable;
    public string TargetRole => TargetForeignKey.ReferencedTable;

    /// <summary> The columns of the target that name its rows in the two lists. </summary>
    public IReadOnlyList<string> DisplayColumns => TargetForeignKey.ReferencedDisplayColumns;

    /// <summary> The C# types of the two keys (<c>int</c> when the column is of a type the routes do not name). </summary>
    public string AnchorCs => CSharpType(AnchorColumn.SqlType);
    public string TargetCs => CSharpType(TargetColumn.SqlType);

    /// <summary> The TypeScript types of the two keys: <c>string</c> for a uniqueidentifier, else <c>number</c>. </summary>
    public string AnchorTs => TypeScriptType(AnchorColumn.SqlType);
    public string TargetTs => TypeScriptType(TargetColumn.SqlType);

    private static string CSharpType(SqlDbType type) => type switch
    {
        SqlDbType.Int => "int",
        SqlDbType.BigInt => "long",
        SqlDbType.SmallInt => "short",
        SqlDbType.TinyInt => "byte",
        SqlDbType.UniqueIdentifier => "Guid",
        SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar => "string",
        _ => "int"
    };

    private static string TypeScriptType(SqlDbType type) => type == SqlDbType.UniqueIdentifier ? "string" : "number";
}
