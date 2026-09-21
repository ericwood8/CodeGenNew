namespace CodeGenNew.Core;

public class ForeignKeyModel
{
    public required string ConstraintName { get; init; }
    public required List<string> ReferencingColumns { get; init; }
    public required string ReferencedSchema { get; init; }
    public required string ReferencedTable { get; init; }
    public required List<string> ReferencedColumns { get; init; }

    /// <summary> The referenced table's display columns (DisplayColumnSelector), in that table's column order. Empty
    /// unless the model was built for a template whose .tt.config sets NeedsReferencedDisplayColumns=true --
    /// empty then means "not looked up", not "the table has none". </summary>
    public List<string> ReferencedDisplayColumns { get; init; } = [];
}
