namespace CodeGenNew.Core;

/// <summary> What an entry of a front end's menu opens. </summary>
public enum ScreenMenuKind { Dashboard, Screen, OData, Junction, Approvals }

/// <summary> One entry of a front end's menu: what it opens, the table (null for the dashboard), its label and the roles that may read it (none listed: any signed-in person, or nobody is asked). </summary>
public sealed record ScreenMenuEntry(ScreenMenuKind Kind, TableModel? Table, string Label, IReadOnlyList<string> Roles);

/// <summary> The menu of a front end that lists its screens from the database (Blazor and Uno), in menu order: the dashboard (<c>Dashboard=true</c>), the screens in the order of the project's <c>Screens</c> setting,
/// then the OData pages of the served tables unless <c>ODataScreens=Replace</c> (the OData page then takes the REST page's place and has no entry of its own), then the page of each junction table (<see cref="JunctionPlan"/>) and, for a project with <c>RowApprovals</c>, the approvals page. The stack's template turns an entry into its own
/// route or tag. </summary>
public static class ScreenMenu
{
    /// <summary> The roles that may read the table's screen: the project's read rules with <c>Auth=true</c>, else none are asked. </summary>
    public static IReadOnlyList<string> RolesOf(ProjectSettings project, TableModel table) => project.Auth ? WriteRolePlan.ReadFor(project, table) : [];

    public static List<ScreenMenuEntry> For(ProjectSettings project, IEnumerable<TableModel> screens, IEnumerable<TableModel> odataScreens, IEnumerable<TableModel>? junctions = null, bool approvals = false)
    {
        var entries = new List<ScreenMenuEntry>();
        if (project.Dashboard)
            entries.Add(new ScreenMenuEntry(ScreenMenuKind.Dashboard, null, "Dashboard", []));
        foreach (var t in screens)
            entries.Add(new ScreenMenuEntry(ScreenMenuKind.Screen, t, ScreenNames.Label(t.TableName), RolesOf(project, t)));
        if (!project.ODataReplacesScreens)
            foreach (var t in odataScreens)
                entries.Add(new ScreenMenuEntry(ScreenMenuKind.OData, t, AngularNames.ODataLabel(t.TableName), RolesOf(project, t)));
        foreach (var t in junctions ?? [])
            entries.Add(new ScreenMenuEntry(ScreenMenuKind.Junction, t, JunctionPlan.Label(t.TableName), JunctionPlan.Roles(project, t)));
        if (approvals)
            entries.Add(new ScreenMenuEntry(ScreenMenuKind.Approvals, null, ApprovalWords.Title, []));
        return entries;
    }
}
