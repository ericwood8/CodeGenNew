namespace CodeGenNew.Core;

/// <summary> The two tables feature flags are kept in: the flags (a name and whether the flag is on by default) and the overrides (a flag, the tenant or the person it is switched for, and on or off).
/// <c>Tenant</c> is null when the project has no tenants or the override table has no tenant column; <c>Person</c> is null when no person column can be matched to the token's owner claim. </summary>
public sealed record FeatureFlagPlan(
    TableModel Flag, ColumnModel FlagKey, ColumnModel FlagName, ColumnModel FlagDefault,
    TableModel Override, ColumnModel OverrideFlag, ColumnModel OverrideEnabled,
    ColumnModel? OverrideTenant, ColumnModel? OverridePerson, FeaturePlanLink? PlanLink = null);

/// <summary> The table that says which flags a plan switches on: a row per plan and flag, each side a foreign key. </summary>
public sealed record FeaturePlanLink(TableModel Table, ColumnModel Flag, ColumnModel Plan);

/// <summary> <c>FeatureTables=Feature,FeatureOverride</c>: feature flags in tables. A flag is on or off by default, a tenant's override or a person's override replaces the default, and the person's wins over the tenant's.
/// The API gets <c>FeatureFlags.EnabledAsync</c>, <c>GET /api/features</c> (every flag as the caller sees it) and <c>PUT /api/features/{name}</c> (an administrator switches a flag for the tenant or for a person). </summary>
public static class FeatureFlagTables
{
    public const string Path = "/api/features";
    public const string UnknownText = "That feature does not exist.";
    public const string OnlyAdministratorText = "Only an administrator changes a feature.";
    public const string NeedsScopeText = "A change names a tenant or a person: a tenant's administrator changes their own tenant, and the body names the person.";
    public const string UnknownPersonText = "That person does not exist.";
    public const string NoTargetText = "This project keeps no overrides for that kind of target (a tenant or a person).";
    public const string OnlyPlatformText = "Only a platform administrator changes a feature for another tenant.";

    private static readonly string[] FlagDefaultNames = ["DefaultEnabled", "DefaultOn", "IsEnabledByDefault", "IsEnabled", "Enabled", "IsOn", "Default"];
    private static readonly string[] OverrideNames = ["IsEnabled", "Enabled", "IsOn", "Value", "State"];

    public static bool IsConfigured(ProjectSettings project) => project.ApiAuth && project.FeatureTables.Length > 0;

    /// <summary> Whether a table is one of the two flag tables: they get no ordinary API, since a list and edit screen would let a tenant's administrator rewrite the flags of every tenant. </summary>
    public static bool IsFeatureTable(ProjectSettings project, string table) =>
        IsConfigured(project) && project.FeatureTables.Any(t => t.EqualsIgnoreCase(table));

    /// <summary> Whether a table is the plan link of the flags: it gets no ordinary API either. </summary>
    public static bool IsPlanLinkTable(ProjectSettings project, string table) =>
        IsConfigured(project) && project.FeaturePlanTable is { } name && name.EqualsIgnoreCase(table);

    public static (FeatureFlagPlan? Plan, string? Problem) Resolve(DatabaseModel database, ProjectSettings project)
    {
        if (project.FeatureTables.Length == 0)
            return (null, null);
        if (!project.ApiAuth)
            return (null, "FeatureTables needs ApiAuth=true: the flags are read for the signed-in tenant and person.");
        if (project.FeatureTables.Length != 2)
            return (null, "FeatureTables names two tables: FeatureTables=Feature,FeatureOverride.");

        var tables = new TableModel[2];
        for (int i = 0; i < 2; i++)
        {
            string name = project.FeatureTables[i];
            tables[i] = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name))!;
            if (tables[i] is null)
                return (null, $"FeatureTables names {name}, which is not a table of the database.");
        }
        var (flag, over) = (tables[0], tables[1]);

        if (flag.PrimaryKeyColumns is not [{ IsInt32Column: true } flagKey])
            return (null, $"FeatureTables: {flag.TableName} needs a primary key of one whole-number (int) column.");
        if (flag.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase("Name") && c.IsStringColumn) is not { } flagName)
            return (null, $"FeatureTables: {flag.TableName} needs a text column called Name.");
        if (Flag(flag, FlagDefaultNames) is not { } flagDefault)
            return (null, $"FeatureTables: {flag.TableName} needs a NOT NULL yes / no column for the default (DefaultEnabled, IsEnabled or Enabled).");

        var links = over.ForeignKeys.Where(fk => fk.ReferencedTable.EqualsIgnoreCase(flag.TableName) && fk.ReferencingColumns.Count == 1).ToList();
        if (links.Count != 1 || over.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(links[0].ReferencingColumns[0])) is not { IsInt32Column: true } overFlag)
            return (null, $"FeatureTables: {over.TableName} needs exactly one whole-number foreign key to {flag.TableName}.");
        if (Flag(over, OverrideNames) is not { } overEnabled)
            return (null, $"FeatureTables: {over.TableName} needs a NOT NULL yes / no column for on or off (IsEnabled or Enabled).");

        ColumnModel? tenant = null;
        if (TenantPlan.IsEnabled(project) && TenantPlan.TenantTableOf(database, project) is { } tenantTable)
        {
            var column = over.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(project.TenantColumn));
            if (column is { IsInt32Column: true } && PointsAt(over, column, tenantTable))
                tenant = column;
        }

        ColumnModel? person = null;
        var (user, _) = project.UserTable is null ? (null, null) : UserTable.Resolve(database, project);
        if (user is { Owner: { } owner } && user.Table.PrimaryKeyColumns is [{ } userKey] && userKey.Name.EqualsIgnoreCase(owner.Name))
        {
            var candidates = over.ForeignKeys.Where(fk => fk.ReferencedTable.EqualsIgnoreCase(user.Table.TableName) && fk.ReferencingColumns.Count == 1).ToList();
            if (candidates.Count == 1 && over.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(candidates[0].ReferencingColumns[0])) is { IsInt32Column: true } found)
                person = found;
        }

        if (tenant is null && person is null)
            return (null, $"FeatureTables: {over.TableName} needs a {project.TenantColumn} column that is a foreign key to the tenant table (TenantTable), or a foreign key to the user table whose key is the owner column (UserTable, UserOwnerColumn), to say who the override is for.");

        if (tenant is not null && person is not null && (!tenant.IsNullable || !person.IsNullable))
            return (null, $"FeatureTables: {over.TableName} has both a {tenant.Name} and a {person.Name} column, so both must allow NULL: an override names a tenant or a person, not both.");

        foreach (var column in over.Columns)
            if (column != overFlag && column != overEnabled && column != tenant && column != person && !over.PrimaryKeyColumns.Contains(column) && !column.IsNullable && !column.IsIdentity && column.DatabaseDefaultSql is null && !column.IsComputed)
                return (null, $"FeatureTables: {over.TableName}.{column.Name} is NOT NULL with no default, so the API cannot add an override row. Make it nullable or give it a default.");

        FeaturePlanLink? link = null;
        if (project.FeaturePlanTable is { } linkName)
        {
            var subscription = SubscriptionFeature.Problems(database, project).Count == 0 ? SubscriptionFeature.SubscriptionOf(database, project) : null;
            if (subscription is null)
                return (null, "FeaturePlanTable needs SubscriptionTable (and PlanTable): the plan of the tenant says which flags are on.");
            var linkTable = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(linkName));
            if (linkTable is null)
                return (null, $"FeaturePlanTable names {linkName}, which is not a table of the database.");
            var toFlag = linkTable.ForeignKeys.Where(fk => fk.ReferencedTable.EqualsIgnoreCase(flag.TableName) && fk.ReferencingColumns.Count == 1).ToList();
            var toPlan = linkTable.ForeignKeys.Where(fk => fk.ReferencedTable.EqualsIgnoreCase(subscription.PlanTable.TableName) && fk.ReferencingColumns.Count == 1).ToList();
            if (toFlag.Count != 1 || toPlan.Count != 1
                || linkTable.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(toFlag[0].ReferencingColumns[0])) is not { IsInt32Column: true } linkFlag
                || linkTable.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(toPlan[0].ReferencingColumns[0])) is not { IsInt32Column: true } linkPlan)
                return (null, $"FeaturePlanTable: {linkTable.TableName} needs exactly one whole-number foreign key to {flag.TableName} and one to {subscription.PlanTable.TableName}.");
            foreach (var column in linkTable.Columns)
                if (column != linkFlag && column != linkPlan && !linkTable.PrimaryKeyColumns.Contains(column) && !column.IsNullable && !column.IsIdentity && column.DatabaseDefaultSql is null && !column.IsComputed)
                    return (null, $"FeaturePlanTable: {linkTable.TableName}.{column.Name} is NOT NULL with no default.");
            link = new FeaturePlanLink(linkTable, linkFlag, linkPlan);
        }

        return (new FeatureFlagPlan(flag, flagKey, flagName, flagDefault, over, overFlag, overEnabled, tenant, person, link), null);
    }

    /// <summary> Whether a row of the flag table can be made from a key, a name and a default alone (every other column may be NULL or has a default), as the generated tests of the routes do. </summary>
    public static bool FlagRowsAreSimple(FeatureFlagPlan plan) =>
        plan.Flag.Columns.All(c => c == plan.FlagKey || c == plan.FlagName || c == plan.FlagDefault || c.IsNullable || c.IsIdentity || c.DatabaseDefaultSql is not null || c.IsComputed);

    private static ColumnModel? Flag(TableModel table, string[] names)
    {
        var booleans = table.Columns.Where(c => c.IsBooleanColumn && !c.IsNullable).ToList();
        foreach (string name in names)
            if (booleans.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name)) is { } found)
                return found;
        return booleans.Count == 1 ? booleans[0] : null;
    }

    private static bool PointsAt(TableModel table, ColumnModel column, TableModel target) =>
        table.ForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase(column.Name) && f.ReferencedTable.EqualsIgnoreCase(target.TableName));
}

/// <summary> The words of the page that switches a flag for a tenant and the name of the call a front end reads the flags with, the same in every front end. </summary>
public static class FeatureScreenWords
{
    public const string Menu = "Features";
    public const string Title = "Features";
    public const string Path = FeatureFlagTables.Path;
    public const string FeatureColumn = "Feature";
    public const string OnColumn = "On";
    public const string ResetColumn = "Default";
    public const string ResetButton = "Use default";
    public const string NoFeatures = "There are no features to switch.";
    public const string LoadFailed = "The features could not be read.";
    public const string SaveFailed = "The feature could not be changed.";
    public const string NoTenant = "Switch to a tenant to change its features.";
    public const string Hint = "A change applies to this tenant only.";
}

/// <summary> Which front ends read the flags and where they show: the screens a flag hides, and the page that switches the flags of a tenant. </summary>
public static class FeatureScreens
{
    /// <summary> The flag that hides the screen of a table, or null. </summary>
    public static string? FlagOf(ProjectSettings project, string table)
    {
        if (!FeatureFlagTables.IsConfigured(project))
            return null;
        foreach (string entry in project.FeatureScreens)
        {
            int colon = entry.IndexOf(':');
            if (colon > 0 && entry[..colon].Trim().EqualsIgnoreCase(table) && entry[(colon + 1)..].Trim() is { Length: > 0 } flag)
                return flag;
        }
        return null;
    }

    /// <summary> The flag that hides a field of the edit form of a table (<c>FeatureFields=Invoice.Notes:beta-notes</c>), or null. </summary>
    public static string? FieldFlagOf(ProjectSettings project, string table, string column)
    {
        if (!FeatureFlagTables.IsConfigured(project))
            return null;
        foreach (string entry in project.FeatureFields)
            if (ParseField(entry) is { } parsed && parsed.Table.EqualsIgnoreCase(table) && parsed.Column.EqualsIgnoreCase(column))
                return parsed.Flag;
        return null;
    }

    private static (string Table, string Column, string Flag)? ParseField(string entry)
    {
        int colon = entry.IndexOf(':');
        if (colon <= 0 || entry[(colon + 1)..].Trim() is not { Length: > 0 } flag)
            return null;
        string target = entry[..colon].Trim();
        int dot = target.LastIndexOf('.');
        return dot > 0 && dot < target.Length - 1 ? (target[..dot].Trim(), target[(dot + 1)..].Trim(), flag) : null;
    }

    /// <summary> Whether a signed-in front end reads the flags (to hide screens) and offers the page that switches them: the flags are served and the front end signs in. </summary>
    public static bool Reads(ProjectSettings project) => project.Auth && FeatureFlagTables.IsConfigured(project);

    /// <summary> Whether the page that switches a flag for the tenant is generated: the override table has a tenant column, so a tenant's administrator has something to switch. </summary>
    public static bool HasPage(DatabaseModel database, ProjectSettings project) =>
        Reads(project) && FeatureFlagTables.Resolve(database, project).Plan is { OverrideTenant: not null };
}
