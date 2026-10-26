using CodeGenNew.Core;

namespace CodeGenNew.Generation;

/// <summary> What a whole-database run leaves out and says nothing about: tables no template of the chosen stacks runs for, and child tables a master-detail screen links to that have no screen. </summary>
public static class TableShapeNotes
{
    public static readonly IReadOnlyList<string> UiStacks = ["WinUI3", "React", "Angular", "Blazor", "Uno"];

    public static List<string> Of(IReadOnlyList<PlanStep> steps, DatabaseModel database, ProjectSettings project, IReadOnlyCollection<string> stacks)
    {
        var notes = new List<string>();
        foreach (var table in database.Tables.Where(t => t.KeySource is not null).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
            notes.Add($"{table.TableName}: the database declares no primary key; {string.Join(", ", table.PrimaryKeyColumns.Select(c => c.DbName))} from {table.KeySource} is its key. A real primary key in the database would be better.");
        foreach (var table in database.Tables.Where(t => t.KeySource is not null && t.KeyAdvice is not null).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
            notes.Add($"{table.TableName}: {table.KeyAdvice}, so {table.KeySource} was used instead.");
        var covered = steps.Where(s => !s.IsDatabaseLevel).SelectMany(s => s.TableNames).ToHashSet(StringComparer.Ordinal);
        foreach (var table in database.Tables.Where(t => !covered.Contains(t.TableName)).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
            notes.Add($"{table.TableName}: no template of the chosen technologies fits its shape ({Shape(table, project)}), so it is skipped.");

        string[] uiStacks = [.. stacks.Where(s => UiStacks.Contains(s, StringComparer.OrdinalIgnoreCase))];
        if (uiStacks.Length == 0)
            return notes;
        foreach (var table in database.Tables.Where(t => LooksSingleRow(t, project)).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
            notes.Add($"{table.TableName}: holds one row; if the application keeps only one, list it in SingleRowTables to get a form instead of a list.");
        var screens = database.ScreenTables(project, uiStacks);
        var screenNames = screens.Select(s => s.TableName).ToHashSet(StringComparer.Ordinal);
        foreach (var table in database.EntityTables.Where(t => t.HasNameActiveApi(project)).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (!screenNames.Contains(table.TableName))
            {
                notes.Add($"{table.TableName}: has a Name and an IsActive column, so it gets a screen only when the Screens setting lists it.");
                continue;
            }
            string[] withoutODataPage = [.. uiStacks.Where(s => s.Equals("Blazor", StringComparison.OrdinalIgnoreCase) || s.Equals("Uno", StringComparison.OrdinalIgnoreCase))];
            if (ODataPlan.Serves(table, project) && withoutODataPage.Length > 0)
                notes.Add($"{table.TableName}: has a Name and an IsActive column, so {string.Join(" and ", withoutODataPage)} write only its REST screen, not an OData page.");
        }
        foreach (var master in screens.Where(t => DatabaseModel.IsDetailMaster(t, project)))
            foreach (var grid in ChildGrid.ForWeb(master, project).Where(g => !screenNames.Contains(g.Table)).DistinctBy(g => g.Table))
                notes.Add($"{grid.Table}: a child of {master.TableName}, no screen.");
        return notes;
    }

    /// <summary> A table with a key, four or more ordinary columns and one row at the moment, which the project has not called a single-row table or an enum: a company's settings look like this. </summary>
    private static bool LooksSingleRow(TableModel table, ProjectSettings project) =>
        table.LookupShape.RowCount == 1 && table.HasPrimaryKey && !project.IsSingleRow(table.TableName) && project.IsEnumTable(table) != true
        && table.Columns.Count(c => !c.IsPrimaryKey && !c.IsAuditColumn) >= 4;

    public static string Shape(TableModel table, ProjectSettings project) => table.PrimaryKeyShape switch
    {
        PrimaryKeyShape.None => table.KeyAdvice ?? "no primary key",
        _ when project.IsSingleRow(table.TableName) => "single-row table",
        _ when project.IsEnumTable(table) == true => "enum table",
        PrimaryKeyShape.Composite when table.IsJunctionTable => "junction table",
        PrimaryKeyShape.Composite => "composite primary key",
        PrimaryKeyShape.SingleUniqueIdentifier => "uniqueidentifier key",
        PrimaryKeyShape.SingleText => "text key",
        PrimaryKeyShape.SingleOther => "key of another type",
        _ => table.IsNameActiveTable ? "name and active table" : "integer key"
    };
}
