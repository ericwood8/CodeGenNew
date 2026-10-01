using System.Data;

namespace CodeGenNew.Core;

/// <summary> What the schema says about whether a table looks like a small lookup table that a project would model as
/// a C# enum (so it has no entity, repository or API of its own): <see cref="LooksLikeLookup"/> is the structural
/// test and <see cref="RowCount"/> the approximate number of rows. Whether that makes it an enum is the project's call
/// (ProjectSettings.EnumMaxRows, EnumTables); this only reports what was read. The default value (false, 0) means
/// "not known", which is never an enum. </summary>
public readonly record struct LookupShape(bool LooksLikeLookup, long RowCount, bool HasIntKeyAndText = false)
{
    /// <summary> The loose test used together with a table NAME that says "lookup" (MeterTypeCodes, LeaveType): just a
    /// single integer primary key and at least one text column. Real enum tables often fail the strict test above -- a
    /// Name + IsActive pair, an audit-free flag column, a foreign key to a display table -- so for them the name decides. </summary>
    public static bool HasKeyAndText(IReadOnlyList<ColumnModel> columns)
    {
        var primaryKey = columns.Where(c => c.IsPrimaryKey).ToList();
        return primaryKey.Count == 1 && primaryKey[0].IsIntegerColumn && columns.Any(c => !c.IsPrimaryKey && c.IsStringColumn);
    }

    /// <summary> The structural test: a single integer primary key; no foreign keys of its own; not a Name + IsActive
    /// table (a user-managed list, not a fixed one); at least one non-audit text column; and at most four non-key,
    /// non-audit columns in all (a key, a name, perhaps a description, a flag). A table that fails this can still be an enum if its name says so
    /// (see ProjectSettings.EnumNameSuffixes) and <see cref="HasKeyAndText"/> holds. </summary>
    public static bool Looks(IReadOnlyList<ColumnModel> columns, int foreignKeyCount)
    {
        var primaryKey = columns.Where(c => c.IsPrimaryKey).ToList();
        if (primaryKey.Count != 1 || !primaryKey[0].IsIntegerColumn || foreignKeyCount > 0)
            return false;

        bool nameActive =
            columns.Any(c => c.IsStringColumn && !c.IsNullable && c.Name == "Name") &&
            columns.Any(c => c.SqlType == SqlDbType.Bit && !c.IsNullable && c.Name == "IsActive");
        if (nameActive)
            return false;

        var others = columns.Where(c => !c.IsPrimaryKey && !c.IsAuditColumn).ToList();
        return others.Count <= 4 && others.Any(c => c.IsStringColumn);
    }
}
