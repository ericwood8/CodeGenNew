using CodeGenNew.Core;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Name-pattern match for audit/tracking columns, used to exclude them from DisplayColumnSelector
/// (Docs/ARCHITECTURE.md section 4). </summary>
public static class AuditColumnClassifier
{
    public static bool IsAuditColumn(this string columnName) =>
        AuditTableShape.IsCreateName(columnName) ||
        AuditTableShape.IsChangeName(columnName) ||
        columnName.StartsWithIgnoreCase("Delete") ||
        columnName.StartsWithIgnoreCase("Inactiv") ||
        columnName.StartsWithIgnoreCase("Activ") ||
        columnName.EqualsIgnoreCase("BadAddressDate");
}
