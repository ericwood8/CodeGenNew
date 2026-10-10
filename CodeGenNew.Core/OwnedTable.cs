namespace CodeGenNew.Core;

/// <summary> Which tables the API scopes to the signed-in person: the ones <c>OwnedTables</c> names that have the owner column. Every template that writes an endpoint for a table asks this one rule. </summary>
public static class OwnedTable
{
    /// <summary> The column that holds the owner of a row, or null when the table is not scoped (no <c>ApiAuth</c>, not listed, or no NOT NULL int column of that name). </summary>
    public static ColumnModel? OwnerColumn(TableModel table, ProjectSettings project) =>
        project.ApiAuth && IsListed(table, project)
            ? table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(project.OwnerColumn) && c.IsInt32Column && !c.IsNullable)
            : null;

    /// <summary> The table is named in <c>OwnedTables</c>, whether or not it can be scoped. </summary>
    public static bool IsListed(TableModel table, ProjectSettings project) =>
        project.OwnedTables.Any(t => t.EqualsIgnoreCase(table.TableName));

    /// <summary> A listed table that cannot be scoped; the generated class says so in a comment instead of leaving every row open without a word. </summary>
    public static bool IsListedButUnusable(TableModel table, ProjectSettings project) =>
        project.ApiAuth && IsListed(table, project) && OwnerColumn(table, project) is null;
}
