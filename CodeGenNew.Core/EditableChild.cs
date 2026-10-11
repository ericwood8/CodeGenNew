namespace CodeGenNew.Core;

/// <summary> Which child tables a parent's form edits in place and saves in the same request as the parent (<c>EditableChildren=DepartmentTeam</c>). The API (the child list a create or update
/// reconciles) and the front ends (the editable grid under the form) both ask this one rule, so the name of the list in the request and the tables that have one cannot differ. </summary>
public static class EditableChild
{
    /// <summary> The child tables of <paramref name="parent"/> the project lists and that can be edited this way. Needs the child's columns (a template config with <c>NeedsReferencedDisplayColumns=true</c>). </summary>
    public static IReadOnlyList<ChildForeignKeyModel> Of(TableModel parent, ProjectSettings project) =>
        project.EditableChildren.Length == 0 ? [] : parent.ChildForeignKeys.Where(fk => IsEditable(parent, fk, project)).ToList();

    /// <summary> The table is listed, hangs off the parent by one NOT NULL column of the parent's key type, has a single identity whole-number key of its own, and is the parent's only link to that table.
    /// A list entry is the child's name (<c>DepartmentTeam</c>), which counts only for a child that belongs to one table, or <c>Parent.Child</c> (<c>Department.DepartmentTeam</c>), which names the parent
    /// whose form edits the rows: a team also points at its region, and a region's form is not where teams are edited. </summary>
    public static bool IsEditable(TableModel parent, ChildForeignKeyModel fk, ProjectSettings project)
    {
        if (!project.EditableChildren.Any(entry => Names(entry, parent, fk)))
            return false;
        if (parent.PrimaryKeyType is not { } parentKey || fk.ReferencingColumns.Count != 1 || fk.ReferencedColumns.Count != 1
            || parent.PrimaryKeyColumns.Count != 1 || !parent.PrimaryKeyColumns[0].Name.EqualsIgnoreCase(fk.ReferencedColumns[0]))
            return false;
        if (parent.ChildForeignKeys.Count(o => o.ReferencingTable.EqualsIgnoreCase(fk.ReferencingTable)) != 1)
            return false;
        if (fk.ReferencingPrimaryKeyColumns.Count != 1)
            return false;

        var key = fk.ReferencingTableColumns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(fk.ReferencingPrimaryKeyColumns[0]));
        var link = fk.ReferencingTableColumns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(fk.ReferencingColumns[0]));
        return key is { IsInt32Column: true, IsIdentity: true } && link is { IsNullable: false } && KeyType.Of(link)?.CSharp == parentKey.CSharp;
    }

    private static bool Names(string entry, TableModel parent, ChildForeignKeyModel fk)
    {
        int dot = entry.IndexOf('.');
        if (dot >= 0)
            return entry[..dot].Trim().EqualsIgnoreCase(parent.TableName) && entry[(dot + 1)..].Trim().EqualsIgnoreCase(fk.ReferencingTable);
        return entry.EqualsIgnoreCase(fk.ReferencingTable)
               && fk.ReferencingTableForeignKeys.Select(o => o.ReferencedTable).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1;
    }

    /// <summary> The property of the request body that holds the child rows: the child's name in the plural, camel cased (<c>departmentTeams</c>). </summary>
    public static string JsonName(ChildForeignKeyModel fk) => JsonNames.Camel(fk.ReferencingTable.Pluralize());

    /// <summary> The most columns an editable child shows as one table of inputs: with more, a table is too narrow for its boxes, so each row is a block of labelled fields that wrap. </summary>
    public const int MaxTableColumns = 6;

    public static bool UsesCards(ChildForeignKeyModel fk) => Columns(fk).Count > MaxTableColumns;

    /// <summary> The columns of the child the editable grid shows, in the table's order: not its key, not the link to the parent, nothing computed, identity or an audit column. </summary>
    public static List<ColumnModel> Columns(ChildForeignKeyModel fk) => fk.ReferencingTableColumns
        .Where(c => !c.IsPrimaryKey && !c.IsComputed && !c.IsIdentity && !c.IsAuditColumn && !c.Name.EqualsIgnoreCase(fk.ReferencingColumns[0])
                    && !fk.ReferencingPrimaryKeyColumns.Any(k => k.EqualsIgnoreCase(c.Name)))
        .OrderBy(c => c.OrdinalPosition).ToList();
}
