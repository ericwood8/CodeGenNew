namespace CodeGenNew.Core;

/// <summary> A table that adds columns to one row of another table: its one-column primary key is also its foreign key to that table (a customer's profile beside the customer). </summary>
public sealed record ExtensionShape(TableModel Table, ColumnModel Key, ForeignKeyModel Base);

/// <summary> Works out whether a table is a one-to-one extension of another. Nothing in the settings is needed: the shape is the key. </summary>
public static class ExtensionPlan
{
    public static ExtensionShape? For(TableModel table, ProjectSettings project)
    {
        if (table.PrimaryKeyColumns.Count != 1 || table.IsJunctionTable)
            return null;
        var key = table.PrimaryKeyColumns[0];
        var baseKey = table.ForeignKeys.FirstOrDefault(fk => fk.ReferencesPrimaryKey && fk.ReferencingColumns.Count == 1
            && fk.ReferencingColumns[0].EqualsIgnoreCase(key.Name) && !fk.ReferencedTable.EqualsIgnoreCase(table.TableName));
        return baseKey is null ? null : new ExtensionShape(table, key, baseKey);
    }
}
