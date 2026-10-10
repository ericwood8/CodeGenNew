namespace CodeGenNew.Core;

/// <summary> The shape of a table's primary key, cheap enough to compute in bulk (TableSummary, for the
/// TreeView's menu) and identical to what TableModel derives from its own PrimaryKeyColumns. Several
/// templates need more than just "has a primary key" (RequiresPrimaryKey, section 5.3): the routes of a
/// stack take the id as {id:int}, {id:guid} or {id}, and a repository is generic over the key's type (see
/// <see cref="KeyType"/>). Refusing at generation time (a template's own Error() call) still leaves the menu
/// offering an option that can never work for that table. See TemplateConfig.RequiredPrimaryKeyShape. </summary>
public enum PrimaryKeyShape
{
    /// <summary> No primary key at all. </summary>
    None,

    /// <summary> More than one primary key column. </summary>
    Composite,

    /// <summary> A single primary key column, one of int/bigint/smallint/tinyint. </summary>
    SingleInt,

    /// <summary> A single uniqueidentifier primary key column. </summary>
    SingleUniqueIdentifier,

    /// <summary> A single primary key column of a text type (char, varchar, nchar, nvarchar): a natural key such as a country or currency code. </summary>
    SingleText,

    /// <summary> A single primary key column of any other type (a date, a decimal, ...). </summary>
    SingleOther
}

/// <summary> The one rule that turns a primary key's column count and SQL type name into a <see cref="PrimaryKeyShape"/>, shared by every schema provider (they read the type name
/// in the SQL Server vocabulary) so the menu's bulk listing and <c>TableModel.PrimaryKeyShape</c> cannot disagree. </summary>
public static class PrimaryKeyShapes
{
    public static PrimaryKeyShape Classify(int primaryKeyColumnCount, string? primaryKeyTypeName) => primaryKeyColumnCount switch
    {
        0 => PrimaryKeyShape.None,
        > 1 => PrimaryKeyShape.Composite,
        _ => primaryKeyTypeName switch
        {
            "uniqueidentifier" => PrimaryKeyShape.SingleUniqueIdentifier,
            "int" or "bigint" or "smallint" or "tinyint" => PrimaryKeyShape.SingleInt,
            "char" or "varchar" or "nchar" or "nvarchar" => PrimaryKeyShape.SingleText,
            _ => PrimaryKeyShape.SingleOther
        }
    };
}
