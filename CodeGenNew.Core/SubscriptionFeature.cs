namespace CodeGenNew.Core;

/// <summary> The sentences and problem types of the subscription routes and checks. </summary>
public static class SubscriptionWords
{
    public const string InactiveType = ProblemTypes.Prefix + "subscription-inactive";
    public const string LimitType = ProblemTypes.Prefix + "plan-limit";
    public const string OnlyPlatformType = ProblemTypes.Prefix + "subscription-platform";
    public const string WebhookSignatureType = ProblemTypes.Prefix + "subscription-webhook-signature";
    public const string WebhookExpiredType = ProblemTypes.Prefix + "subscription-webhook-expired";
    public const string PlanTooSmallType = ProblemTypes.Prefix + "plan-too-small";

    /// <summary> The header the gateway's webhook carries its signature in: <c>t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c>, where the hex is the HMAC-SHA256 of <c>{t}.{body}</c> with the shared secret. </summary>
    public const string WebhookHeader = "X-Gateway-Signature";
    public const string WebhookPath = "/api/subscription/webhook";
    /// <summary> How far the signed time may be from the server's, in seconds: an older (or newer) message is a replay. </summary>
    public const int WebhookToleranceSeconds = 300;
    public const string WebhookSignatureText = "The signature of the message is missing or wrong.";
    public const string WebhookExpiredText = "The message is older than five minutes (or comes from the future), so it is refused as a replay.";
    public const string WebhookUnconfiguredText = "The webhook has no secret to check messages with.";
    public const string WebhookBodyText = "The message needs a gatewayId and a status.";
    public const string WebhookUnknownText = "No subscription has that gateway id.";

    /// <summary> {0} is the status the subscription has. </summary>
    public const string InactiveText = "The subscription of this tenant is {0}, so changes are paused until it is active again.";
    public const string TrialEndedText = "The trial of this tenant ended on {0}, so changes are paused until a plan is chosen.";
    public const string NoSubscriptionText = "This tenant has no subscription, so changes are not allowed until one is chosen.";
    /// <summary> {0} is the limit's name, {1} what the plan allows and {2} what the tenant has. </summary>
    public const string LimitText = "The plan allows {1} for {0} and this tenant already has {2}. Choose a larger plan to add more.";
    public const string OnlyAdministrator = "Only an administrator of the tenant sees its subscription.";
    public const string OnlyPlatform = "Only a platform administrator (an administrator who is not working in a tenant) changes a subscription.";
    public const string NeedsPlanAndStatus = "A tenant that has no subscription needs a planId and a status.";
    public const string UnknownPlan = "That plan does not exist.";
    public const string UnknownTenant = "That tenant does not exist.";
    public const string NeedsPlan = "The body needs a planId.";
    public const string UnknownStatus = "That status is not in the look-up table of statuses.";
    public const string NoSubscriptionToRenew = "That tenant has no subscription to renew.";
    public const string NoPeriodToRenew = "The subscription table has no period end to renew.";
    public const string RenewDays = "The days to add must be a whole number from 1 to 3660.";
    /// <summary> {0} is the limit's name, {1} what the chosen plan allows and {2} what the tenant has. </summary>
    public const string PlanTooSmallText = "The plan allows {1} for {0} and this tenant already has {2}, so it cannot move to that plan until it has less.";
}

/// <summary> The words of the subscription page that the web and desktop front ends show, the same in each. </summary>
public static class SubscriptionScreenWords
{
    public const string Menu = "Subscription";
    public const string Title = "Subscription";
    public const string Path = "/api/subscription";
    public const string PlanLabel = "Plan";
    public const string StatusLabel = "Status";
    public const string PeriodEndLabel = "Period ends";
    public const string LimitsHeading = "Limits";
    public const string LimitColumn = "Limit";
    public const string AllowedColumn = "Allowed";
    public const string InUseColumn = "In use";
    public const string Unlimited = "Unlimited";
    public const string PriceLabel = "Price";
    public const string PlansHeading = "Plans";
    public const string PlanColumn = "Plan";
    public const string PriceColumn = "Price";
    public const string AllowsColumn = "Allows";
    public const string CurrentMark = "(current)";
    public const string ChooseColumn = "Choose";
    public const string ChooseButton = "Choose";
    public const string ChooseFailed = "The plan could not be changed.";
    public const string NoSubscription = "This tenant has no subscription.";
    public const string LoadFailed = "Only an administrator who works in a tenant sees its subscription.";
    public const string NoTenant = "Switch to a tenant to see its subscription.";
    public const string PausedPrefix = "Changes are paused. ";
    public const string Active = "Changes are allowed.";
}

/// <summary> The subscription table: one row per tenant (the newest row counts) with the plan it is on and its status. <c>PeriodStart</c> is stamped when the row is made; <c>PeriodEnd</c> ends a trial. </summary>
public sealed record SubscriptionModel(TableModel Table, ColumnModel Key, ColumnModel Tenant, ColumnModel Plan, ColumnModel Status, ColumnModel? PeriodStart, ColumnModel? PeriodEnd, ColumnModel? Gateway,
    TableModel PlanTable, ColumnModel PlanKey, ColumnModel? PlanName, ColumnModel? PlanPrice, ColumnModel? PlanPeriod, StatusLookup? Lookup = null);

/// <summary> A status held in a look-up table: the subscription's status column is a whole-number foreign key to <c>Table</c>, whose <c>Name</c> column holds the words (trialing, active, past_due, cancelled). </summary>
public sealed record StatusLookup(TableModel Table, ColumnModel Key, ColumnModel Name);

/// <summary> The plan limit table: a row per plan and limit name, with the most the plan allows (NULL or no row means unlimited). </summary>
public sealed record PlanLimitModel(TableModel Table, ColumnModel Plan, ColumnModel Key, ColumnModel Value);

/// <summary> A table whose new rows are counted against a limit. <c>Tenant</c> is the column that says whose row it is, <c>Deleted</c> the soft-delete flag (a flagged row is not counted), <c>Routes</c> the create path
/// (and its clone and import) that the check guards. </summary>
public sealed record LimitedTable(TableModel Table, string LimitKey, ColumnModel Tenant, ColumnModel? Deleted, string CreatePath, bool IsMembership);

/// <summary> Plans, subscriptions and entitlements for SaaS billing (<c>SubscriptionTable</c>, <c>PlanLimitTable</c>, <c>PlanLimits</c>), on top of the tenants (<c>TenantTable</c>). A tenant's writes stop while its subscription is not in
/// <c>SubscriptionActiveStatuses</c> (default trialing and active; a trial also ends on its period end date), a create in a limited table is refused when the tenant already has what the plan allows, and
/// <c>GET /api/subscription</c> and <c>PUT /api/subscription/{tenantId}</c> read and change the subscription. The payment gateway is outside the generator: the code holds only the ids and the status it reports. </summary>
public static class SubscriptionFeature
{
    public static readonly string[] DefaultActiveStatuses = ["trialing", "active"];
    public const string MembersPath = "/api/auth/members";

    public static bool IsEnabled(ProjectSettings project) => project.SubscriptionTable is not null;

    /// <summary> Whether the web and Uno front ends get the subscription page: they sign in, the API serves <c>GET /api/subscription</c> and the settings can be served. </summary>
    public static bool HasPage(DatabaseModel database, ProjectSettings project) =>
        project.Auth && project.ApiAuth && IsEnabled(project) && TenantPlan.IsEnabled(project) && Problems(database, project).Count == 0;

    /// <summary> Whether any of the settings is present, so a project that names only a part is told what is missing. </summary>
    public static bool AnySetting(ProjectSettings project) =>
        project.SubscriptionTable is not null || project.PlanTable is not null || project.PlanLimitTable is not null || project.PlanLimits.Length > 0 || project.SubscriptionRequired || project.SubscriptionActiveStatusesOrNull is not null || project.SubscriptionWebhookSecret is not null;

    /// <summary> Whether a write to the table can be answered 402 (the subscription is not active, or a plan limit is reached): the project has a subscription table and the table is tenant-owned. The screens show the API's sentence
    /// for it instead of a general failure. </summary>
    public static bool PausesWrites(TableModel table, ProjectSettings project) => IsEnabled(project) && TableShapes.For(table, project).Tenant is not null;

    /// <summary> Whether the APIs serve the gateway's webhook: a secret is named and the subscription table has a gateway id column to find the subscription by. </summary>
    public static bool HasWebhook(DatabaseModel database, ProjectSettings project) =>
        project.SubscriptionWebhookSecret is not null && SubscriptionOf(database, project)?.Gateway is not null;

    /// <summary> The sentence a screen shows when the API's 402 carries none. </summary>
    public const string PausedFallback = "The plan of this tenant does not allow this change.";

    /// <summary> The statuses in which a tenant may write (compared without regard to case). </summary>
    public static string[] ActiveStatuses(ProjectSettings project) => project.SubscriptionActiveStatusesOrNull ?? DefaultActiveStatuses;

    private static string[] Parts(string setting) => setting.Split(':', StringSplitOptions.TrimEntries);

    private static ColumnModel? Pick(TableModel table, string[] parts, int index, params string[] names)
    {
        if (parts.Length > index && parts[index].Length > 0)
            return table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(parts[index]));
        foreach (string name in names)
            if (table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name)) is { } found)
                return found;
        return null;
    }

    private static bool Named(string[] parts, int index) => parts.Length > index && parts[index].Length > 0;

    private static TableModel? TableOf(DatabaseModel database, string name) => database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name));

    private static bool PointsAt(TableModel table, ColumnModel column, TableModel target) =>
        table.ForeignKeys.Any(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase(column.Name) && f.ReferencedTable.EqualsIgnoreCase(target.TableName));

    /// <summary> The columns of the plan table that hold its price and its billing period, found by name; the view shows them beside the plan. </summary>
    private static readonly string[] PriceNames = ["Price", "MonthlyPrice", "PriceMonthly", "Amount"];
    private static readonly string[] PeriodNames = ["BillingPeriod", "BillingInterval", "Period", "Interval"];
    private static readonly string[] StatusNameColumns = ["Name", "Title", "Description", "Code"];

    /// <summary> Why a stack that reads the status as text cannot serve a project whose status is held in a look-up table, or null when it can: only the ASP.NET API reads and writes the look-up table. </summary>
    public static string? LookupProblem(DatabaseModel database, ProjectSettings project, string stack) =>
        SubscriptionOf(database, project)?.Lookup is { } lookup ? $"{stack}: the subscription status is held in the look-up table {lookup.Table.TableName}, which only the ASP.NET API reads and writes. Hold the status as text, or leave {stack} out." : null;

    /// <summary> The subscription table, or null when it is not named or cannot be used (see <see cref="Problems"/>). </summary>
    public static SubscriptionModel? SubscriptionOf(DatabaseModel database, ProjectSettings project) => Resolve(database, project, out _);

    private static SubscriptionModel? Resolve(DatabaseModel database, ProjectSettings project, out string? problem)
    {
        problem = null;
        if (project.SubscriptionTable is not { } setting)
            return null;
        string[] parts = Parts(setting);
        string prefix = $"SubscriptionTable: {parts[0]}";
        var table = TableOf(database, parts[0]);
        if (table is null)
        {
            problem = $"{prefix} is not a table of the database.";
            return null;
        }
        if (TenantPlan.OwnedBy(table, database, project) is not { } owned)
        {
            problem = $"{prefix} must be a tenant-owned table (a NOT NULL whole-number {project.TenantColumn} column that is a foreign key to the tenant table).";
            return null;
        }
        if (table.PrimaryKeyColumns is not [{ IsInt32Column: true } key])
        {
            problem = $"{prefix} needs a single whole-number key.";
            return null;
        }
        var plan = Pick(table, parts, 1, "PlanId");
        if (plan is not { IsInt32Column: true, IsNullable: false })
        {
            problem = $"{prefix} needs a NOT NULL whole-number plan column ({(Named(parts, 1) ? parts[1] : "PlanId")}) that is a foreign key to the plan table (SubscriptionTable=Subscription:PlanId:Status:PeriodEnd:GatewayId, with PlanTable=Plan).";
            return null;
        }
        var planTable = project.PlanTable is { } planName ? TableOf(database, planName) : null;
        if (planTable is null)
        {
            problem = $"{prefix}: PlanTable must name the table of plans (PlanTable=Plan){(project.PlanTable is null ? "" : $"; {project.PlanTable} is not a table of the database")}.";
            return null;
        }
        if (!PointsAt(table, plan, planTable) || planTable.PrimaryKeyColumns is not [{ IsInt32Column: true } planKey])
        {
            problem = $"{prefix}: {plan.Name} must be a foreign key to {planTable.TableName}, which needs a single whole-number key.";
            return null;
        }
        var status = Pick(table, parts, 2, "Status", "StatusId", "SubscriptionStatusId");
        StatusLookup? lookup = null;
        if (status is { IsInt32Column: true, IsNullable: false } && table.ForeignKeys.FirstOrDefault(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase(status.Name)) is { } statusKey
            && TableOf(database, statusKey.ReferencedTable) is { PrimaryKeyColumns: [{ IsInt32Column: true } lookupKey] } lookupTable
            && lookupTable.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && !c.IsLongTextColumn && StatusNameColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase)) is { } lookupName)
            lookup = new StatusLookup(lookupTable, lookupKey, lookupName);
        else if (status is not { IsStringColumn: true, IsNullable: false } || status.IsLongTextColumn)
        {
            problem = $"{prefix} needs a NOT NULL text status column ({(Named(parts, 2) ? parts[2] : "Status")}) holding trialing, active, past_due or cancelled, or a NOT NULL whole-number foreign key to a look-up table with a Name column.";
            return null;
        }
        var end = Pick(table, parts, 3, "CurrentPeriodEnd", "PeriodEnd", "TrialEndsOn", "EndsOn");
        if (Named(parts, 3) && end is null)
        {
            problem = $"{prefix}: the period end column {parts[3]} is not a column of the table.";
            return null;
        }
        if (end is not null && !LogTablePlan.HasTimeOfDay(end))
        {
            problem = $"{prefix}: the period end column {end.Name} must be a date-and-time column.";
            return null;
        }
        var gateway = Pick(table, parts, 4, "GatewaySubscriptionId", "GatewayId", "ExternalId");
        if (Named(parts, 4) && gateway is null)
        {
            problem = $"{prefix}: the gateway column {parts[4]} is not a column of the table.";
            return null;
        }
        if (gateway is not null && (!gateway.IsStringColumn || gateway.IsLongTextColumn))
        {
            problem = $"{prefix}: the gateway column {gateway.Name} must be a text column (the id the payment gateway gives the subscription; never a card number).";
            return null;
        }
        var start = table.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase("CurrentPeriodStart") || c.Name.EqualsIgnoreCase("PeriodStart"));
        if (start is not null && !LogTablePlan.HasTimeOfDay(start))
            start = null;
        var required = table.Columns.Where(c => c != key && c != owned.Column && c != plan && c != status && c != start && !c.IsNullable && !c.IsIdentity && !c.IsComputed && !c.IsRowVersionColumn && c.DatabaseDefaultSql is null
            && !c.IsInsertStampColumn).Select(c => c.Name).ToList();
        if (required.Count > 0)
        {
            problem = $"{prefix}: {string.Join(", ", required)} cannot be NULL and has no default, so the platform route cannot make a subscription row. Give the column a default or let it allow NULL.";
            return null;
        }
        var name = planTable.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && (c.Name.EqualsIgnoreCase("Name") || c.Name.EqualsIgnoreCase("Title")));
        var price = planTable.Columns.FirstOrDefault(c => (c.IsNumericColumn || c.IsIntegerColumn) && !c.IsPrimaryKey && PriceNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
        var billing = planTable.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsLongTextColumn && PeriodNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
        return new SubscriptionModel(table, key, owned.Column, plan, status!, start, end, gateway, planTable, planKey, name, price, billing, lookup);
    }

    /// <summary> The plan limit table, or null when it is not named or cannot be used. </summary>
    public static PlanLimitModel? LimitTableOf(DatabaseModel database, ProjectSettings project) => ResolveLimitTable(database, project, out _);

    private static PlanLimitModel? ResolveLimitTable(DatabaseModel database, ProjectSettings project, out string? problem)
    {
        problem = null;
        if (project.PlanLimitTable is not { } setting || Resolve(database, project, out _) is not { } subscription)
            return null;
        string[] parts = Parts(setting);
        string prefix = $"PlanLimitTable: {parts[0]}";
        var table = TableOf(database, parts[0]);
        if (table is null)
        {
            problem = $"{prefix} is not a table of the database.";
            return null;
        }
        var plan = Pick(table, parts, 1, subscription.Plan.Name, "PlanId");
        if (plan is not { IsInt32Column: true } || !PointsAt(table, plan, subscription.PlanTable))
        {
            problem = $"{prefix} needs a whole-number column that is a foreign key to {subscription.PlanTable.TableName} (PlanLimitTable=PlanLimit:PlanId:LimitKey:LimitValue).";
            return null;
        }
        var key = Pick(table, parts, 2, "LimitKey", "Key", "Kind", "Name");
        if (key is not { IsStringColumn: true } || key.IsLongTextColumn)
        {
            problem = $"{prefix} needs a text column that names the limit ({(Named(parts, 2) ? parts[2] : "LimitKey")}).";
            return null;
        }
        var value = Pick(table, parts, 3, "LimitValue", "Value", "MaxValue", "Limit", "Quantity");
        if (value is not { IsIntegerColumn: true })
        {
            problem = $"{prefix} needs a whole-number column that holds what the plan allows ({(Named(parts, 3) ? parts[3] : "LimitValue")}); NULL means unlimited.";
            return null;
        }
        return new PlanLimitModel(table, plan, key, value);
    }

    /// <summary> The entries of <c>PlanLimits</c> (<c>Project:projects,Employee:seats</c>: a table and the limit's name), in the order written. </summary>
    public static IReadOnlyList<(string Table, string Key)> Entries(ProjectSettings project) =>
        project.PlanLimits.Select(item =>
        {
            string[] parts = Parts(item);
            return (parts[0], parts.Length > 1 ? parts[1] : "");
        }).ToList();

    /// <summary> The tables that are counted against a limit, resolved against the schema; entries that cannot be used are left out (see <see cref="Problems"/>). </summary>
    public static IReadOnlyList<LimitedTable> LimitedTables(DatabaseModel database, ProjectSettings project)
    {
        var list = new List<LimitedTable>();
        foreach (var (name, key) in Entries(project))
            if (Limited(database, project, name, key, out _) is { } limited)
                list.Add(limited);
        return list;
    }

    private static LimitedTable? Limited(DatabaseModel database, ProjectSettings project, string name, string key, out string? problem)
    {
        problem = null;
        string prefix = $"PlanLimits: {name}";
        var table = TableOf(database, name);
        if (table is null)
        {
            problem = $"{prefix} is not a table of the database.";
            return null;
        }
        if (key.Length == 0)
        {
            problem = $"{prefix} needs the limit's name after a colon (PlanLimits=Project:projects).";
            return null;
        }
        var soft = TableShapes.For(table, project).SoftDelete?.Flag;
        if (TenantPlan.MembershipOf(database, project) is { } membership && membership.Table == table)
            return new LimitedTable(table, key, membership.Tenant, soft, MembersPath, true);
        if (TenantPlan.OwnedBy(table, database, project) is not { } owned)
        {
            problem = $"{prefix} must be a tenant-owned table or the TenantMembership table (a limit counts a tenant's rows).";
            return null;
        }
        if (!table.HasCrudApiOfAnyKey(project))
        {
            problem = $"{prefix} has no API of its own, so there is no create route to guard.";
            return null;
        }
        return new LimitedTable(table, key, owned.Column, soft, ApiRequests.Route(table), false);
    }

    /// <summary> The tables whose tenant rows the generated tests may put in a tenant of their own (the limited tables with an API, and the table the tests write to), so they can take them out again. </summary>
    public static IReadOnlyList<LimitedTable> ClearedTables(DatabaseModel database, ProjectSettings project)
    {
        var list = LimitedTables(database, project).Where(l => !l.IsMembership).ToList();
        if (TestWriteTable(database, project) is { } owned && !list.Any(l => l.Table == owned.Table))
            list.Add(new LimitedTable(owned.Table, "", owned.Column, null, ApiRequests.Route(owned.Table), false));
        return list;
    }

    /// <summary> The table the generated tests write to in a tenant of their own: the first limited table with an API, else a tenant-owned table with an API whose only foreign key is the tenant. Null when there is none. </summary>
    public static TenantOwnedShape? TestWriteTable(DatabaseModel database, ProjectSettings project)
    {
        bool Simple(TenantOwnedShape o) => o.Table.HasCrudApiOfAnyKey(project) && o.Table.ForeignKeys.All(f => f.ReferencingColumns.Count == 1 && f.ReferencingColumns[0].EqualsIgnoreCase(o.Column.Name))
            && o.Table.PrimaryKeyColumns is [{ IsIdentity: true }];
        var simpleLimited = LimitedTables(database, project).Where(l => !l.IsMembership).Select(l => TenantPlan.OwnedBy(l.Table, database, project)).OfType<TenantOwnedShape>().FirstOrDefault(Simple);
        return simpleLimited ?? TenantPlan.OwnedTables(database, project).FirstOrDefault(o => Simple(o) && o.Table != SubscriptionOf(database, project)?.Table);
    }

    /// <summary> Every reason the subscription settings cannot be served, empty when they can (or are not set). </summary>
    public static List<string> Problems(DatabaseModel database, ProjectSettings project)
    {
        var problems = new List<string>();
        if (!AnySetting(project))
            return problems;
        if (project.SubscriptionTable is null)
        {
            problems.Add("PlanTable, PlanLimitTable, PlanLimits, SubscriptionActiveStatuses and SubscriptionRequired need SubscriptionTable.");
            return problems;
        }
        if (!TenantPlan.IsEnabled(project) || !project.ApiAuth)
        {
            problems.Add("SubscriptionTable needs TenantTable and ApiAuth=true (a subscription belongs to a tenant, and the tenant is a claim of the token).");
            return problems;
        }
        if (Resolve(database, project, out string? problem) is not { } subscription)
        {
            problems.Add(problem!);
            return problems;
        }
        if (project.PlanLimitTable is not null)
        {
            ResolveLimitTable(database, project, out string? limitProblem);
            if (limitProblem is not null)
                problems.Add(limitProblem);
        }
        if (project.PlanLimits.Length > 0 && project.PlanLimitTable is null)
            problems.Add("PlanLimits needs PlanLimitTable (the table that says what each plan allows).");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, key) in Entries(project))
        {
            Limited(database, project, name, key, out string? limited);
            if (limited is not null)
                problems.Add(limited);
            else if (!keys.Add(key))
                problems.Add($"PlanLimits: the limit name {key} is used twice.");
        }
        if (ActiveStatuses(project).Length == 0)
            problems.Add("SubscriptionActiveStatuses must name at least one status.");
        if (project.SubscriptionWebhookSecret is not null && subscription.Gateway is null)
            problems.Add("SubscriptionWebhookSecret needs a gateway id column on the subscription table (GatewaySubscriptionId, GatewayId or ExternalId): the webhook finds the subscription by it.");
        return problems;
    }
}
