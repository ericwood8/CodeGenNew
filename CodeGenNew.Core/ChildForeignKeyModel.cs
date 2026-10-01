namespace CodeGenNew.Core;

/// <summary> A foreign key on ANOTHER table that points back at this one -- the mirror image of
/// ForeignKeyModel. Lets a template discover which other tables depend on this one (e.g. to generate a
/// master-detail screen's child grid, or to warn before generating a Delete). </summary>
public class ChildForeignKeyModel
{
    public required string ConstraintName { get; init; }
    public required string ReferencingSchema { get; init; }
    public required string ReferencingTable { get; init; }
    public required List<string> ReferencingColumns { get; init; }

    /// <summary> The column(s) on THIS table the child's foreign key points at (usually the primary key). </summary>
    public required List<string> ReferencedColumns { get; init; }

    /// <summary> The child (referencing) table's own primary key column name(s) -- lets a generated child grid
    /// exclude the child's own identity column, which is an internal row id with no business meaning to show a
    /// user. Empty unless the model was built with NeedsReferencedDisplayColumns=true (see ForeignKeyModel's own
    /// ReferencedDisplayColumns for the same "empty means not looked up" convention). </summary>
    public List<string> ReferencingPrimaryKeyColumns { get; init; } = [];

    /// <summary> Every foreign key ON the child (referencing) table -- not just the one pointing back to THIS
    /// table (that one is also in this list, matching ConstraintName/ReferencingColumns above). Lets a generated
    /// child grid resolve one of the child's OTHER foreign-keyed columns (e.g. a SalesInvoice child row's own
    /// CustomerId) to that referenced table's display name instead of a raw id, the same way a detail screen's
    /// own drop-downs do. Empty unless the model was built with NeedsReferencedDisplayColumns=true. </summary>
    public List<ForeignKeyModel> ReferencingTableForeignKeys { get; init; } = [];

    /// <summary> Every column of the child (referencing) table, so a generated child grid can order, caption and format its columns
    /// (money as currency, long text last) from the schema. Empty unless the model was built with NeedsReferencedDisplayColumns=true. </summary>
    public List<ColumnModel> ReferencingTableColumns { get; init; } = [];
}
