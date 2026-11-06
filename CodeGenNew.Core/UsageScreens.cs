namespace CodeGenNew.Core;

/// <summary> The words of the usage page, the same in every front end. </summary>
public static class UsageScreenWords
{
    public const string MonthColumn = "Month";
    public const string KindColumn = "Kind";
    public const string QuantityColumn = "Quantity";
    public const string EventsColumn = "Events";
    public const string ChartColumn = "Chart";
    public const string OverTime = "By month";
    public const string KindFilter = "Kind";
    public const string FromFilter = "From";
    public const string BeforeFilter = "Before";
    public const string Show = "Show";
    public const string BadDate = "Dates are written year-month-day, for example 2026-11-01.";
    public const string Hint = "The units used by your tenant, added up for each month and kind, with a bar for each line and a column for each month. From is the first day shown, Before the first day left out. Months that were rolled up show their total and no events, and only when the whole month lies between From and Before.";
    public const string NoRows = "No usage has been recorded.";
    public const string LoadFailed = "Only an administrator who works in a tenant sees its usage.";
    public const string NoTenant = "Switch to a tenant to see its usage.";
}

/// <summary> The page of one usage table: its summary per month and kind, with a filter on the kind. <paramref name="Rollup"/> is the monthly total table of this usage table, which the summary adds. </summary>
public sealed record UsagePage(UsageShape Shape, UsageRollupShape? Rollup, string ApiRoute, string Path, string Label, string Name)
{
    public string PageClass => Name + "Page";
}

/// <summary> Which front ends show the usage of <c>UsageTables</c>: for the administrator, a table of the months and kinds the API sums (<c>GET</c> of the table's route) and a kind filter. A front end that signs in needs the
/// route, so it needs <c>Auth</c>, <c>ApiAuth</c> and the tenants. A table with monthly totals also lists the months that were rolled up. </summary>
public static class UsageScreens
{
    public static IReadOnlyList<UsagePage> Pages(DatabaseModel database, ProjectSettings project)
    {
        if (!project.Auth || !project.ApiAuth || !TenantPlan.IsEnabled(project) || UsagePlan.Problems(database, project).Count > 0)
            return [];
        var pages = new List<UsagePage>();
        foreach (var shape in database.Tables.Select(t => TableShapes.For(t, project).Usage).OfType<UsageShape>().OrderBy(u => u.Table.TableName, StringComparer.OrdinalIgnoreCase))
            pages.Add(new UsagePage(shape, UsagePlan.RollupFor(database, project, shape), ApiRequests.Route(shape.Table), ScreenNames.Route(shape.Table.TableName), ScreenNames.Label(shape.Table.TableName).Pluralize(),
                ScreenNames.BaseName(shape.Table.TableName) + "Summary"));
        return pages;
    }

    /// <summary> The pages of the WinUI3 app, which reads the database itself and so needs the desktop sign-in with tenants. </summary>
    public static IReadOnlyList<UsagePage> DesktopPages(DatabaseModel database, ProjectSettings project) =>
        TenantPlan.HasDesktopTenants(project) ? Pages(database, project) : [];
}
