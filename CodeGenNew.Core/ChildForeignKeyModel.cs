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
}
