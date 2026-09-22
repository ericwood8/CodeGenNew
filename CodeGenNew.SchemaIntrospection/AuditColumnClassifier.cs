namespace CodeGenNew.SchemaIntrospection;

/// <summary> Name-pattern match for audit/tracking columns, used to exclude them from DisplayColumnSelector
/// (Docs/specs.md section 6). </summary>
public static class AuditColumnClassifier
{
    public static bool IsAuditColumn(string columnName) =>
        columnName.StartsWith("Create", StringComparison.OrdinalIgnoreCase) ||
        (columnName.Contains("Modif", StringComparison.OrdinalIgnoreCase) && !columnName.Equals("IsBeingModified", StringComparison.OrdinalIgnoreCase)) ||
        columnName.Contains("Change", StringComparison.OrdinalIgnoreCase) ||
        columnName.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
        columnName.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
        columnName.StartsWith("Inactiv", StringComparison.OrdinalIgnoreCase) ||
        columnName.StartsWith("Activ", StringComparison.OrdinalIgnoreCase) ||
        columnName.Equals("BadAddressDate", StringComparison.OrdinalIgnoreCase);
}
