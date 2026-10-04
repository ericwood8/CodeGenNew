using System.Data;

namespace CodeGenNew.Core;

/// <summary> Which tables a grid offers a Clone button for, and what a clone of one needs: the same rules SP_Clone follows when it writes <c>&lt;Table&gt;_Clone</c> (the
/// primary key, audit columns and unique values are handled deliberately), seen from the code that calls it. </summary>
public static class CloneShape
{
    private static bool IsTextLob(ColumnModel c) => c.SqlType is SqlDbType.Text or SqlDbType.NText;

    // SQL Server's timestamp is a row version the database sets; in PostgreSQL, MySQL and SQLite a timestamp is an ordinary date and time
    private static bool IsRowVersion(TableModel m, ColumnModel c) =>
        m.Dialect == SqlDialect.SqlServer
        && (c.SqlTypeDeclaration.Equals("timestamp", StringComparison.OrdinalIgnoreCase) || c.SqlTypeDeclaration.Equals("rowversion", StringComparison.OrdinalIgnoreCase));

    // A column the clone sets by rule (a create date, the active flag, the deleted flag ...), not by copying or overriding the source's value.
    private static bool IsRuleColumn(TableModel m, ColumnModel c)
    {
        var active = m.HasActiveInactivePair ? m.ActiveColumn : null;
        var inactiveDate = m.HasActiveInactivePair ? m.InactiveDateColumn : null;
        return c.IsCreateDateColumn || c.IsLastChangedDateColumn || c.IsModifiedDateColumn || c.IsModifiedUserColumn || c.IsCreateUserColumn
            || c.IsInactiveReasonColumn || c.IsAdminFlagColumn || c == active || c == inactiveDate
            || (m.HasSoftDelete && (c == m.IsDeletedColumn || c == m.DeletedDateColumn));
    }

    /// <summary> The columns a clone writes: everything except computed, row version and identity columns. </summary>
    public static IEnumerable<ColumnModel> Copyable(TableModel m) => m.Columns.Where(c => !c.IsComputed && !IsRowVersion(m, c) && !c.IsIdentity);

    /// <summary> The columns that record who created the row: a parameter of the clone routine (who is cloning it). </summary>
    public static List<ColumnModel> CreateUserColumns(TableModel m) => Copyable(m).Where(c => c.IsCreateUserColumn).ToList();

    /// <summary> The columns in a unique index (other than the key) that the clone routine takes an override for: a copy that kept the source's value could not be saved. </summary>
    public static List<ColumnModel> OverrideColumns(TableModel m) => Copyable(m)
        .Where(c => c.IsInUniqueIndex && !c.IsPrimaryKey && !IsRuleColumn(m, c) && !IsTextLob(c)).ToList();

    /// <summary> A grid offers Clone for a table with a single database-assigned int key (the new key has to come from the routine), plain-repository shape (no name/active
    /// table), unique values that are all text (the repository suggests a free one: "A100" gives "A1002"), and not listed in the project's <c>NoCloneTables</c>. </summary>
    public static bool CanClone(TableModel m, ProjectSettings project) =>
        m.PrimaryKeyColumns.Count == 1
        && m.PrimaryKeyColumns[0].IsIdentity
        && m.PrimaryKeyColumns[0].SqlType == SqlDbType.Int
        && !m.IsNameActiveTable
        && OverrideColumns(m).All(c => c.IsStringColumn && c.DbEnumType is null)
        && !project.NoClone(m.TableName);

    /// <summary> Why CanClone is false, for a template's header comment or an error; null when it is true. </summary>
    public static string? WhyNot(TableModel m, ProjectSettings project)
    {
        if (CanClone(m, project)) return null;
        if (project.NoClone(m.TableName)) return "it is listed in the project's NoCloneTables";
        if (m.PrimaryKeyColumns.Count != 1 || !m.PrimaryKeyColumns[0].IsIdentity || m.PrimaryKeyColumns[0].SqlType != SqlDbType.Int)
            return "its key is not a single database-assigned int (a clone needs a new key from the routine)";
        if (m.IsNameActiveTable) return "it is a name/active table (hand-maintained repository)";
        return "a unique column that is not text cannot be given a suggested new value";
    }
}
