using CodeGenNew.Core;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Name-pattern match for audit/tracking columns, used to exclude them from DisplayColumnSelector
/// (Docs/specs.md section 6). </summary>
public static class AuditColumnClassifier
{
    public static bool IsAuditColumn(this string columnName) =>
        columnName.StartsWithIgnoreCase("Create") ||
        (columnName.ContainsIgnoreCase("Modif") && !columnName.EqualsIgnoreCase("IsBeingModified")) ||
        columnName.ContainsIgnoreCase("Change") ||
        columnName.StartsWithIgnoreCase("Delete") ||
        columnName.StartsWithIgnoreCase("Update") ||
        columnName.StartsWithIgnoreCase("Inactiv") ||
        columnName.StartsWithIgnoreCase("Activ") ||
        columnName.EqualsIgnoreCase("BadAddressDate");
}
