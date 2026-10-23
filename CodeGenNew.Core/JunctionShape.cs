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
        CreateDateColumn = table.Columns.FirstOrDefault(c => c.IsCreateDateColumn && c.CSharpBase() is "DateTime" or "DateTimeOffset");
        CreateUserColumn = table.Columns.FirstOrDefault(c => c.IsCreateUserColumn && c.IsStringColumn);
    }

    public ForeignKeyModel AnchorForeignKey { get; }
    public ForeignKeyModel TargetForeignKey { get; }

    /// <summary> The junction column that holds the anchor's key. </summary>
    public ColumnModel AnchorColumn { get; }

    /// <summary> The junction column that holds the target's key. </summary>
    public ColumnModel TargetColumn { get; }

    /// <summary> The junction's create-date column when it holds a date and time (date, datetime, datetime2 and datetimeoffset all do, whatever their precision), and its create-user column when it holds text.
    /// A link written without a routine (EF, SQLAlchemy, sqlx) sets the first to the current time and the second to an empty text; null when the table has no such column. </summary>
    public ColumnModel? CreateDateColumn { get; }
    public ColumnModel? CreateUserColumn { get; }

    /// <summary> The table whose row is being edited, and the table whose rows are linked to it. </summary>
    public string AnchorRole => AnchorForeignKey.ReferencedTable;
    public string TargetRole => TargetForeignKey.ReferencedTable;

    /// <summary> The columns of the target that name its rows in the two lists. </summary>
    public IReadOnlyList<string> DisplayColumns => TargetForeignKey.ReferencedDisplayColumns;

    /// <summary> Whether the anchor table has a lookup endpoint, which a screen that picks the anchor's row (the Blazor junction page) reads its names from. </summary>
    public bool AnchorHasLookup(ProjectSettings project) =>
        AnchorForeignKey.ReferencedColumns.Count == 1
        && !project.NoApiFor(AnchorForeignKey.ReferencedTable, AnchorForeignKey.ReferencedLookupShape)
        && !project.NoLookupFor(AnchorForeignKey.ReferencedTable, AnchorForeignKey.ReferencedLookupShape);

    /// <summary> Whether both sides have a REST API of their own, whose rows a test (or a screen) takes the two rows to link from. </summary>
    public bool BothSidesHaveApi(ProjectSettings project) => HasApi(AnchorForeignKey, project) && HasApi(TargetForeignKey, project);

    private static bool HasApi(ForeignKeyModel fk, ProjectSettings project) => fk.ReferencedColumns.Count == 1 && !project.NoApiFor(fk.ReferencedTable, fk.ReferencedLookupShape);

    /// <summary> Whether a side's key is text or a guid (a key that is not a whole number travels as text in the tests). </summary>
    public bool AnchorKeyIsText => IsTextKey(AnchorColumn);
    public bool TargetKeyIsText => IsTextKey(TargetColumn);

    private static bool IsTextKey(ColumnModel c) => c.SqlType is not (SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt or SqlDbType.TinyInt);

    /// <summary> The C# expression of a target key no row has, for the test that a missing target cannot be linked; blank when the key is text the person chooses (any text may exist). </summary>
    public string AbsentTargetCs => TargetColumn.SqlType switch
    {
        SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt => "int.MaxValue",
        SqlDbType.UniqueIdentifier => "Guid.NewGuid()",
        _ => ""
    };

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
