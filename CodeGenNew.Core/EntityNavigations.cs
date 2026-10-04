namespace CodeGenNew.Core;

/// <summary> A navigation property the entity class has for a single-column foreign key: <c>Customer.Status</c> for <c>StatusId</c>. </summary>
public sealed record EntityNavigation(ColumnModel Column, string Role, string Type, ForeignKeyModel ForeignKey);

/// <summary> Which foreign keys get a navigation property and what it is called: the one rule CS_Entity writes the property by and the code that sorts or searches through it reads. </summary>
public static class EntityNavigations
{
    /// <param name="skip"> True for a foreign key whose parent gets no navigation (an enum or lookup table with no entity class). </param>
    public static List<EntityNavigation> Of(TableModel model, Func<ForeignKeyModel, bool> skip)
    {
        var foreignKeyByColumn = new Dictionary<string, ForeignKeyModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in model.ForeignKeys.Where(f => f.ReferencingColumns.Count == 1))
            foreignKeyByColumn[fk.ReferencingColumns[0]] = fk;

        var navigations = new List<EntityNavigation>();
        var takenNames = new HashSet<string>(model.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase) { model.TableName };
        foreach (var column in model.Columns.Where(c => foreignKeyByColumn.ContainsKey(c.Name) && !c.IsPrimaryKey))
        {
            var fk = foreignKeyByColumn[column.Name];
            if (skip(fk))
                continue;

            string role = column.Name.Length > 2 && column.Name.EndsWith("Id", StringComparison.Ordinal) ? column.Name[..^2] : column.Name + "Ref";
            // a role that repeats another name (or the table's own) cannot be a second property: both stay plain columns
            if (role.Length == 0 || !takenNames.Add(role))
                continue;
            navigations.Add(new EntityNavigation(column, role, fk.ReferencedTable, fk));
        }

        return navigations;
    }
}
