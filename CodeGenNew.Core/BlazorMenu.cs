namespace CodeGenNew.Core;

/// <summary> One link of the Blazor menu: where it goes, the words on it and the roles that may see it (none: every signed-in person, or everyone without sign-in). </summary>
public sealed record BlazorMenuEntry(string Route, string Label, IReadOnlyList<string> Roles);

/// <summary> The links of the Blazor menu in menu order, so the menu BLZ_Screens writes and the menu test BLZ_TestMenu writes read one list: the dashboard, the REST screens, the OData screens that take no
/// REST screen's place, then a page for each junction table. </summary>
public static class BlazorMenu
{
    public static List<BlazorMenuEntry> Entries(DatabaseModel database, ProjectSettings project)
    {
        var screens = database.ScreenTables(project, "Blazor");
        var odataScreens = ODataPlan.BlazorScreenTables(database, project);
        var entries = ScreenMenu.For(project, screens, odataScreens, JunctionPlan.Tables(database, project), ApprovalScreens.For(database, project).Count > 0)
            .Select(m => new BlazorMenuEntry(m.Kind switch
            {
                ScreenMenuKind.Dashboard => "dashboard",
                ScreenMenuKind.Approvals => "approvals",
                ScreenMenuKind.Screen => ScreenNames.Route(m.Table!.TableName),
                ScreenMenuKind.Junction => JunctionPlan.Route(m.Table!.TableName),
                _ => AngularNames.ODataRoute(m.Table!.TableName)
            }, m.Label, m.Roles))
            .ToList();
        return entries;
    }
}
