using CodeGenNew.Core;

namespace CodeGenNew.Generation;

/// <summary> What a whole-database run leaves out and says nothing about: tables no template of the chosen stacks runs for, and child tables a master-detail screen links to that have no screen. </summary>
public static class TableShapeNotes
{
    public static readonly IReadOnlyList<string> UiStacks = ["WinUI3", "React", "Angular", "Blazor", "Uno"];

    public static List<string> Of(IReadOnlyList<PlanStep> steps, DatabaseModel database, ProjectSettings project, IReadOnlyCollection<string> stacks)
    {
        var notes = new List<string>();
        var covered = steps.Where(s => !s.IsDatabaseLevel).SelectMany(s => s.TableNames).ToHashSet(StringComparer.Ordinal);
        foreach (var table in database.Tables.Where(t => !covered.Contains(t.TableName)).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
            notes.Add($"{table.TableName}: no template of the chosen technologies fits its shape ({Shape(table, project)}), so it is skipped.");

        string[] uiStacks = [.. stacks.Where(s => UiStacks.Contains(s, StringComparer.OrdinalIgnoreCase))];
        if (uiStacks.Length == 0)
            return notes;
        var screens = database.ScreenTables(project, uiStacks);
        var screenNames = screens.Select(s => s.TableName).ToHashSet(StringComparer.Ordinal);
        foreach (var master in screens.Where(t => DatabaseModel.IsDetailMaster(t, project)))
            foreach (var grid in ChildGrid.ForWeb(master, project).Where(g => !screenNames.Contains(g.Table)).DistinctBy(g => g.Table))
                notes.Add($"{grid.Table}: a child of {master.TableName}, no screen.");
        return notes;
    }

    public static string Shape(TableModel table, ProjectSettings project) => table.PrimaryKeyShape switch
    {
        PrimaryKeyShape.None => "no primary key",
        _ when project.IsEnumTable(table) == true => "enum table",
        PrimaryKeyShape.Composite when table.IsJunctionTable => "junction table",
        PrimaryKeyShape.Composite => "composite primary key",
        PrimaryKeyShape.SingleUniqueIdentifier => "uniqueidentifier key",
        PrimaryKeyShape.SingleText => "text key",
        PrimaryKeyShape.SingleOther => "key of another type",
        _ => table.IsNameActiveTable ? "name and active table" : "integer key"
    };
}
