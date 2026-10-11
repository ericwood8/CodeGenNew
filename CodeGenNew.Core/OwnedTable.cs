namespace CodeGenNew.Core;

/// <summary> A table whose rows belong to the owner of one parent row: <c>E_TimeSheetDetail</c> through its <c>TimeSheetId</c> to <c>E_TimeSheet.EmployeeId</c>. </summary>
public sealed record OwnerLink(string ParentTable, string ParentKey, string ForeignKey, string ParentOwner);

/// <summary> Which tables the API scopes to the signed-in person, and how. A table named in <c>OwnedTables</c> is scoped by its own owner column (<c>Table</c> uses the project's <c>OwnerColumn</c>,
/// <c>Table:Column</c> names another one); a table with a NOT NULL foreign key to an owned table is scoped through that parent unless <c>OwnedChildren=false</c>. Every template that writes an endpoint
/// for a table asks this one rule. </summary>
public static class OwnedTable
{
    /// <summary> The column that holds the owner of a row, or null when the table has no column of its own to scope by (no <c>ApiAuth</c>, not listed, or no NOT NULL int column of that name). </summary>
    public static ColumnModel? OwnerColumn(TableModel table, ProjectSettings project) =>
        project.ApiAuth && ColumnNameOf(table.TableName, project) is { } name
            ? table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name) && c.IsInt32Column && !c.IsNullable)
            : null;

    /// <summary> The parent that decides who owns a row of a table that has no owner column of its own: the first NOT NULL single-column foreign key (in column order) to a table that <c>OwnedTables</c> names.
    /// Null for a table scoped by its own column, with <c>OwnedChildren=false</c>, or without <c>ApiAuth</c>. The parent is taken to have its owner column; it says so itself when it has not. </summary>
    public static OwnerLink? Through(TableModel table, ProjectSettings project)
    {
        if (!project.ApiAuth || !project.OwnedChildren || OwnerColumn(table, project) is not null)
            return null;
        foreach (var column in table.Columns.OrderBy(c => c.OrdinalPosition))
        {
            if (column.IsNullable || !table.ForeignKeyByColumn.TryGetValue(column.Name, out var fk) || fk.ReferencedColumns.Count != 1)
                continue;
            if (fk.ReferencedTable.EqualsIgnoreCase(table.TableName) || ColumnNameOf(fk.ReferencedTable, project) is not { } owner)
                continue;
            return new OwnerLink(fk.ReferencedTable, fk.ReferencedColumns[0], column.Name, owner);
        }
        return null;
    }

    /// <summary> The table is scoped to the signed-in person, by its own column or through its parent. </summary>
    public static bool IsScoped(TableModel table, ProjectSettings project) => OwnerColumn(table, project) is not null || Through(table, project) is not null;

    /// <summary> The table is named in <c>OwnedTables</c>, whether or not it can be scoped. </summary>
    public static bool IsListed(TableModel table, ProjectSettings project) => ColumnNameOf(table.TableName, project) is not null;

    /// <summary> A listed table that cannot be scoped; the generated class says so in a comment instead of leaving every row open without a word. </summary>
    public static bool IsListedButUnusable(TableModel table, ProjectSettings project) =>
        project.ApiAuth && IsListed(table, project) && !IsScoped(table, project);

    /// <summary> The owner column name <c>OwnedTables</c> gives a table (its <c>Table:Column</c> entry, else the project's <c>OwnerColumn</c>); null when it is not listed. </summary>
    public static string? ColumnNameOf(string table, ProjectSettings project)
    {
        foreach (string entry in project.OwnedTables)
        {
            int colon = entry.IndexOf(':');
            if (!(colon < 0 ? entry : entry[..colon]).Trim().EqualsIgnoreCase(table))
                continue;
            return colon < 0 || colon == entry.Length - 1 ? project.OwnerColumn : entry[(colon + 1)..].Trim();
        }
        return null;
    }
}
