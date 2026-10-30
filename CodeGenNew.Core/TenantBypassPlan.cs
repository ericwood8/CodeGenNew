namespace CodeGenNew.Core;

/// <summary> What the generated tests of a multi-tenant API (<c>TenantTable</c>) play beyond the query filter, decided once for the ASP.NET, Python and Rust tests: the tables a dashboard widget counts, the junction
/// tables with a tenant-owned side, the row approvals of a tenant-owned table and the tenant that a host name names. Each case works in a tenant the test makes (or, in the Python and Rust tests, a tenant key no
/// row belongs to), so it needs no second tenant in the scratch database and no other test can change what it sees. </summary>
public static class TenantBypassPlan
{
    /// <summary> A tenant-owned table that dashboard widgets read, with the ids of those widgets. </summary>
    public sealed record DashboardCase(TableModel Table, IReadOnlyList<string> WidgetIds);

    /// <summary> A junction table with a tenant-owned side. </summary>
    public sealed record JunctionCase(TableModel Junction, TableModel Anchor, TableModel Target, bool AnchorOwned, bool TargetOwned);

    /// <summary> The tenant-owned tables with a plain API that the dashboard's widgets read. </summary>
    public static IReadOnlyList<DashboardCase> Dashboard(DatabaseModel database, ProjectSettings project)
    {
        if (!project.Dashboard || !TenantPlan.HasApiTenancy(project))
            return [];
        var plan = DashboardPlan.Build(database, project);
        if (plan.Problems.Count > 0)
            return [];
        var cases = new List<DashboardCase>();
        foreach (var owned in TenantTestPlan.Owned(database, project).Concat(TenantTestPlan.OwnedNamed(database, project)).OrderBy(o => o.Table.TableName, StringComparer.OrdinalIgnoreCase))
        {
            var ids = plan.Widgets.Where(w => w.Table.EqualsIgnoreCase(owned.Table.TableName)).Select(w => w.Id).ToList();
            if (ids.Count > 0)
                cases.Add(new DashboardCase(owned.Table, ids));
        }
        return cases;
    }

    /// <summary> The junction tables (both sides with an API) of which at least one side is tenant-owned. </summary>
    public static IReadOnlyList<JunctionCase> Junctions(DatabaseModel database, ProjectSettings project)
    {
        if (!TenantPlan.HasApiTenancy(project))
            return [];
        var cases = new List<JunctionCase>();
        foreach (var table in database.Tables.Where(t => t.IsJunctionTable && t.Junction is not null).OrderBy(t => t.TableName, StringComparer.OrdinalIgnoreCase))
        {
            var junction = table.Junction!;
            if (!junction.BothSidesHaveApi(project) || TenantPlan.OwnedBy(table, database, project) is not null)
                continue;
            var anchor = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(junction.AnchorRole));
            var target = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(junction.TargetRole));
            if (anchor is null || target is null)
                continue;
            bool anchorOwned = TenantPlan.OwnedBy(anchor, database, project) is not null, targetOwned = TenantPlan.OwnedBy(target, database, project) is not null;
            if (anchorOwned || targetOwned)
                cases.Add(new JunctionCase(table, anchor, target, anchorOwned, targetOwned));
        }
        return cases;
    }

    /// <summary> The seed of the approval tests when the first table of <c>RowApprovals</c> is tenant-owned and the people and the row can be made from the schema; null otherwise. </summary>
    public static (ApprovalPlan Plan, ApprovalTestSeed Seed)? Approvals(DatabaseModel database, ProjectSettings project)
    {
        if (!TenantPlan.HasApiTenancy(project) || project.RowApprovals.Length == 0)
            return null;
        var plan = ApprovalPlan.Resolve(database, project);
        if (plan.Problems.Count > 0 || plan.Tables.Count == 0 || ApprovalTestSeed.For(plan, database, project, out _) is not { TenantSql: not null } seed)
            return null;
        return (plan, seed);
    }

    /// <summary> The tenant table's column that holds the first label of a host name, or null when the project names none. </summary>
    public static ColumnModel? HostColumn(DatabaseModel database, ProjectSettings project) =>
        TenantPlan.HasApiTenancy(project) ? TenantPlan.HostColumnOf(database, project) : null;
}
