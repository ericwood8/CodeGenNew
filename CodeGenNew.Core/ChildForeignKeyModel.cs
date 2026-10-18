namespace CodeGenNew.Core;

/// <summary> A foreign key on ANOTHER table that points back at this one -- the mirror image of
/// ForeignKeyModel. Lets a template discover which other tables depend on this one (e.g. to generate a
/// master-detail screen's child grid, an editable child list or the data dictionary). </summary>
public class ChildForeignKeyModel
{
    public required string ConstraintName { get; init; }
    public required string ReferencingSchema { get; init; }
    public required string ReferencingTable { get; init; }
    public required List<string> ReferencingColumns { get; init; }

    /// <summary> The child table's real name in the database when the naming style changed <see cref="ReferencingTable"/> (null: the same). </summary>
    public string? ReferencingDatabaseTable { get; init; }
    public string ReferencingDbTable => ReferencingDatabaseTable ?? ReferencingTable;

    /// <summary> The column(s) on THIS table the child's foreign key points at (usually the primary key). </summary>
    public required List<string> ReferencedColumns { get; init; }

    /// <summary> The child (referencing) table's own primary key column name(s): a generated child grid hides them (an internal row id has no business meaning to show a user), and an editable child
    /// list takes the row's key from them. Empty unless the template's config sets NeedsReferencedDisplayColumns (see ForeignKeyModel's own ReferencedDisplayColumns for the same "empty means not looked up" convention). </summary>
    public List<string> ReferencingPrimaryKeyColumns { get; init; } = [];

    /// <summary> Every foreign key ON the child (referencing) table -- not just the one pointing back to THIS
    /// table (that one is also in this list, matching ConstraintName/ReferencingColumns above). Lets a generated
    /// child grid resolve one of the child's OTHER foreign-keyed columns (e.g. a SalesInvoice child row's own
    /// CustomerId) to that referenced table's display name instead of a raw id, the same way a detail screen's
    /// own drop-downs do. Empty unless the template's config sets NeedsReferencedDisplayColumns. </summary>
    public List<ForeignKeyModel> ReferencingTableForeignKeys { get; init; } = [];

    /// <summary> Every column of the child (referencing) table, so a generated child grid can caption and format its columns
    /// (money amounts, dates, long text) from the schema. Empty unless the template's config sets NeedsReferencedDisplayColumns. </summary>
    public List<ColumnModel> ReferencingTableColumns { get; init; } = [];

    /// <summary> Whether the child table itself has child tables, which makes its screen master-detail (unless the project's DetailMasterTables lists the master-detail tables instead). False unless the template's config sets NeedsReferencedDisplayColumns. </summary>
    public bool ReferencingTableHasChildren { get; init; }
}
