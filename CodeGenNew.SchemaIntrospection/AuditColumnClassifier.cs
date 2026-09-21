namespace CodeGenNew.SchemaIntrospection;

/// <summary> Ported from Avatar.CodeGen.SqlServer.DataLayer.ColumnTools.IsAuditColumn. </summary>
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
