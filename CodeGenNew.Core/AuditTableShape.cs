namespace CodeGenNew.Core;

/// <summary> Whether a table's column names say the application keeps track of who made and who changed each row: a column that records its creation (CreateDate, CreateUser, CreatedBy)
/// and a column that records a later change (ModifiedDate, ModifiedBy, UpdatedOn, LastChangedDate). Such a table is offered the audit trail template (SP_AuditTable). The names are
/// the creation and change patterns of AuditColumnClassifier, so the right-click menu (TableSummary) and a full TableModel always agree. </summary>
public static class AuditTableShape
{
    public static bool IsCreateName(string columnName) => columnName.StartsWithIgnoreCase("Create");

    public static bool IsChangeName(string columnName) =>
        (columnName.ContainsIgnoreCase("Modif") && !columnName.EqualsIgnoreCase("IsBeingModified"))
        || columnName.ContainsIgnoreCase("Change")
        || columnName.StartsWithIgnoreCase("Update");

    public static bool IsAuditTable(IEnumerable<string> columnNames)
    {
        bool created = false, changed = false;
        foreach (string name in columnNames)
        {
            created |= IsCreateName(name);
            changed |= IsChangeName(name);
        }

        return created && changed;
    }
}
