namespace CodeGenNew.Core;

/// <summary> What the database does with the rows that point at a row when that row is deleted (the foreign key's ON DELETE rule). </summary>
public enum ForeignKeyDeleteRule { NoAction, Restrict, Cascade, SetNull, SetDefault }

public static class ForeignKeyDeleteRules
{
    /// <summary> Reads the rule as each database words it: SQL Server <c>NO_ACTION</c>, <c>SET_NULL</c>; MySQL, SQLite and the ANSI text <c>NO ACTION</c>, <c>SET NULL</c>; PostgreSQL's one-letter codes in pg_constraint (a, r, c, n, d). Anything else is NoAction. </summary>
    public static ForeignKeyDeleteRule Parse(string? text) => (text ?? "").Trim().Replace('_', ' ').ToUpperInvariant() switch
    {
        "CASCADE" or "C" => ForeignKeyDeleteRule.Cascade,
        "SET NULL" or "N" => ForeignKeyDeleteRule.SetNull,
        "SET DEFAULT" or "D" => ForeignKeyDeleteRule.SetDefault,
        "RESTRICT" or "R" => ForeignKeyDeleteRule.Restrict,
        _ => ForeignKeyDeleteRule.NoAction
    };

    /// <summary> Whether deleting a parent row that has a child row is refused (so an API answers 400 "in use"): the rule neither removes the child nor changes it. </summary>
    public static bool RefusesInUse(this ForeignKeyDeleteRule rule) => rule is ForeignKeyDeleteRule.NoAction or ForeignKeyDeleteRule.Restrict;
}
