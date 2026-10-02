namespace CodeGenNew.Core;

public class ForeignKeyModel
{
    public required string ConstraintName { get; init; }
    public required List<string> ReferencingColumns { get; init; }
    public required string ReferencedSchema { get; init; }
    public required string ReferencedTable { get; init; }
    public required List<string> ReferencedColumns { get; init; }

    // The database's own names where the project's naming style changed the ones above (null: the same). SQL text uses these.
    public List<string>? ReferencingDatabaseColumns { get; init; }
    public string? ReferencedDatabaseTable { get; init; }
    public List<string>? ReferencedDatabaseColumns { get; init; }
    public List<string> ReferencingDbColumns => ReferencingDatabaseColumns ?? ReferencingColumns;
    public string ReferencedDbTable => ReferencedDatabaseTable ?? ReferencedTable;
    public List<string> ReferencedDbColumns => ReferencedDatabaseColumns ?? ReferencedColumns;

    /// <summary> The referenced table's display columns (DisplayColumnSelector), in that table's column order. Empty
    /// unless the model was built for a template whose .tt.config sets NeedsReferencedDisplayColumns=true --
    /// empty then means "not looked up", not "the table has none". </summary>
    public List<string> ReferencedDisplayColumns { get; init; } = [];

    /// <summary> The same display columns under the database's own names (parallel to <see cref="ReferencedDisplayColumns"/>); null when the naming style left them the same. </summary>
    public List<string>? ReferencedDisplayDatabaseColumns { get; init; }
    public List<string> ReferencedDisplayDbColumns => ReferencedDisplayDatabaseColumns ?? ReferencedDisplayColumns;

    /// <summary> Whether the referenced table looks like a small lookup table (see LookupShape). Always read, not
    /// gated behind a .tt.config flag; the default (not known) is never an enum. </summary>
    public LookupShape ReferencedLookupShape { get; init; }
}
