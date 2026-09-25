namespace CodeGenNew.Core;

/// <summary> The shape of a table's primary key, cheap enough to compute in bulk (TableSummary, for the
/// TreeView's menu) and identical to what TableModel derives from its own PrimaryKeyColumns. Several
/// templates need more than just "has a primary key" (RequiresPrimaryKey, section 5.3) -- TS_Service.tt's
/// routes take the id as {id:int} or {id:guid}, CS_Repo's GenericRepo&lt;T&gt; only ever takes a plain int --
/// and refusing at generation time (a template's own Error() call) still leaves the menu offering an option
/// that can never work for that table. See TemplateConfig.RequiredPrimaryKeyShape. </summary>
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

    /// <summary> A single primary key column of any other type (varchar, char, ...) -- a natural key. </summary>
    SingleOther
}
