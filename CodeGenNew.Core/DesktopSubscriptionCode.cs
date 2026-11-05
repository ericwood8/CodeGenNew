using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> Plans and subscriptions in the WinUI3 app (<c>SubscriptionTable</c> with the desktop sign-in and <c>TenantTable</c>). The app reads the database itself, so it never meets the API's check: <c>SubscriptionGuard</c> is the
/// same rule written for the app's own context (a paused subscription refuses a save, a new row past a plan limit is refused), the detail dialogs call it before they save, and the subscription page shows the plan, the status and
/// the limits. A check in the client: anyone who writes to the database directly bypasses it, and the API is where it is enforced. </summary>
public static class DesktopSubscriptionCode
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        TenantPlan.HasDesktopTenants(project) && SubscriptionFeature.IsEnabled(project) && SubscriptionFeature.Problems(database, project).Count == 0;

    /// <summary> The lines of <c>SaveAsync</c> that run the subscription check before a row is saved (eight spaces in): a paused subscription, or a new row past the plan's limit, is the dialog's error (any table: the API pauses every write of a tenant, so the app does too). Empty when the app has no
    /// subscription check. </summary>
    public static string CheckLines(ProjectSettings project, TableModel table, string context) =>
        WriteLines(project, table, context, "ErrorMessage", "return false;", "_editing is null");

    /// <summary> The same check for a change other than a save (a delete, a restore, a clone, the rows of a grid): <paramref name="error"/> is the property that takes the sentence, <paramref name="exit"/> the statement that
    /// leaves the method, <paramref name="isNew"/> the expression that says rows are added (the plan's limit is checked then; "false" checks only that the subscription is active) and <paramref name="adding"/> how many.
    /// Empty when the app has no subscription check. </summary>
    public static string WriteLines(ProjectSettings project, TableModel table, string context, string error, string exit, string isNew = "false", string adding = "1", int indent = 8) =>
        Lines(project, SubscriptionFeature.IsEnabled(project), table.TableName, context, error, exit, isNew, adding, indent);

    /// <summary> The pause check alone, for a screen that writes through its own context and is not about one tenant-owned table (a junction editor, an approval button): a paused subscription is <paramref name="error"/>.
    /// Empty when the app has no subscription check. </summary>
    public static string AnyWriteLines(ProjectSettings project, string context, string error, string exit, int indent = 8) =>
        Lines(project, SubscriptionFeature.IsEnabled(project), "", context, error, exit, "false", "1", indent);

    /// <summary> The same for the rows of a child grid: the child table is checked, not the row the dialog is about. </summary>
    public static string ChildLines(ProjectSettings project, DesktopChild child, string context, string error, string exit, string isNew = "false", string adding = "1", int indent = 8) =>
        Lines(project, SubscriptionFeature.IsEnabled(project) && TenantPlan.ChildTenantColumn(child.Grid.Link, project) is not null, child.Table, context, error, exit, isNew, adding, indent);

    private static string Lines(ProjectSettings project, bool tenantOwned, string tableName, string context, string error, string exit, string isNew, string adding, int indent)
    {
        if (!TenantPlan.HasDesktopTenants(project) || !tenantOwned)
            return "";
        string key = isNew == "false" ? "" : SubscriptionFeature.Entries(project).FirstOrDefault(e => e.Table.EqualsIgnoreCase(tableName)).Key ?? "";
        string pad = new(' ', indent);
        string extra = adding == "1" ? "" : $", {adding}";
        var o = new StringBuilder();
        o.Append($"{pad}if (await SubscriptionGuard.RefusalAsync({context}, {(key.Length == 0 ? "null" : CSharpText.Quote(key))}, {isNew}{extra}) is {{ }} paused)\n");
        o.Append($"{pad}{{\n");
        o.Append($"{pad}    {error} = paused;\n");
        o.Append($"{pad}    {exit}\n");
        o.Append($"{pad}}}\n\n");
        return o.ToString();
    }

    /// <summary> The guard class and the records the page shows, as the text of <c>Repositories/SubscriptionGuard.cs</c> (without the namespace line). </summary>
    public static string Guard(DatabaseModel database, ProjectSettings project, string contextType, string[] usings)
    {
        var subscription = SubscriptionFeature.SubscriptionOf(database, project)!;
        var limit = SubscriptionFeature.LimitTableOf(database, project);
        var limited = limit is null ? [] : SubscriptionFeature.LimitedTables(database, project);
        string planName = subscription.PlanName is null ? $"p.{subscription.PlanKey.CSharpName()}.ToString()"
            : subscription.PlanName.IsNullable ? $"p.{subscription.PlanName.CSharpName()} ?? \"\"" : $"p.{subscription.PlanName.CSharpName()}";
        string statuses = string.Join(", ", SubscriptionFeature.ActiveStatuses(project).Select(s => CSharpText.Quote(s.ToLowerInvariant())));
        string countCases = string.Join("", limited.Select(l =>
        {
            string deleted = l.Deleted is null ? "" : $" && !r.{l.Deleted.CSharpName()}";
            return $"            {CSharpText.Quote(l.LimitKey)} => context.Set<{l.Table.TableName}>().AsNoTracking().CountAsync(r => r.{l.Tenant.CSharpName()} == tenant{deleted}),\n";
        }));
        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append("using Microsoft.EntityFrameworkCore;\n\n");
        (string, string)[] names =
        [
            ("§CTX", contextType), ("§S", subscription.Table.TableName), ("§SKEY", subscription.Key.CSharpName()), ("§STENANT", subscription.Tenant.CSharpName()), ("§SPLAN", subscription.Plan.CSharpName()),
            ("§SSTATUS", subscription.Status.CSharpName()), ("§P", subscription.PlanTable.TableName), ("§PKEY", subscription.PlanKey.CSharpName()), ("§PNAME", planName),
            ("§ENDSELECT", subscription.PeriodEnd is null ? "(DateTime?)null" : $"s.{subscription.PeriodEnd.CSharpName()}"),
            ("§STATUSES", statuses), ("§REQUIRED", project.SubscriptionRequired ? "true" : "false"),
            ("§INACTIVE_TEXT", CSharpText.Escape(SubscriptionWords.InactiveText)), ("§TRIAL_TEXT", CSharpText.Escape(SubscriptionWords.TrialEndedText)), ("§NONE_TEXT", CSharpText.Escape(SubscriptionWords.NoSubscriptionText)),
            ("§LIMIT_TEXT", CSharpText.Escape(SubscriptionWords.LimitText)),
            ("§PRICESELECT", subscription.PlanPrice is null ? "(decimal?)null" : $"(decimal?)p.{subscription.PlanPrice.CSharpName()}"),
            ("§PERIODSELECT", subscription.PlanPeriod is null ? "(string?)null" : $"p.{subscription.PlanPeriod.CSharpName()}"),
            ("§LIMITT", limit?.Table.TableName ?? ""), ("§LPLAN", limit?.Plan.CSharpName() ?? ""), ("§LKEY", limit?.Key.CSharpName() ?? ""), ("§LVALUE", limit?.Value.CSharpName() ?? ""),
            ("§KEYS", string.Join(", ", limited.Select(l => CSharpText.Quote(l.LimitKey)))), ("§COUNTCASES", countCases),
            ("§PLANVALUES", limit is null ? PlanValuesNone : PlanValuesOf),
            ("§CANCHOOSE", project.SubscriptionPlanChoice ? "true" : "false"),
            ("§NOTENANT_TEXT", CSharpText.Escape(SubscriptionScreenWords.NoTenant)), ("§UNKNOWNPLAN_TEXT", CSharpText.Escape(SubscriptionWords.UnknownPlan)), ("§TOOSMALL_TEXT", CSharpText.Escape(SubscriptionWords.PlanTooSmallText))
        ];
        string startInit = subscription.PeriodStart is null ? "" : $", {subscription.PeriodStart.CSharpName()} = " + (subscription.PeriodStart.CSharpBase() == "DateTimeOffset" ? "DateTimeOffset.UtcNow" : "DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)");
        string choose = project.SubscriptionPlanChoice ? FillAll(FillAll(ChooseText, ("§TOOSMALL", limit is null ? "" : TooSmallText), ("§STARTINIT", startInit)), names) : "";
        o.Append(FillAll(limit is null ? GuardNoLimits : GuardText, names.Concat([("§CHOOSE", choose)]).ToArray()));
        return o.ToString();
    }

    public static string Xaml(string viewNamespace, string repositoryNamespace) => UnoSubscriptionCode.Xaml(viewNamespace, repositoryNamespace);

    public static string Code(string viewNamespace, string repositoryNamespace, string contextType, string[] usings) => FillAll(PageText, ("§NS", viewNamespace), ("§REPO", repositoryNamespace), ("§CTX", contextType), ("§USINGS", string.Concat(usings.Select(u => $"using {u};\n"))),
        ("§PLANLABEL", SubscriptionScreenWords.PlanLabel), ("§STATUSLABEL", SubscriptionScreenWords.StatusLabel), ("§PERIODLABEL", SubscriptionScreenWords.PeriodEndLabel),
        ("§NOSUBSCRIPTION", SubscriptionScreenWords.NoSubscription), ("§NOTENANT", SubscriptionScreenWords.NoTenant), ("§PAUSED", SubscriptionScreenWords.PausedPrefix),
        ("§ACTIVE", SubscriptionScreenWords.Active), ("§UNLIMITED", SubscriptionScreenWords.Unlimited), ("§PRICELABEL", SubscriptionScreenWords.PriceLabel));

    private const string GuardCommon = """
/// <summary> One plan limit: what the plan allows (null when it is unlimited) and what the tenant has. </summary>
public sealed class LimitRow
{
    public string Key { get; set; } = "";
    public long? Allowed { get; set; }
    public int Used { get; set; }

    public string AllowedText => Allowed is { } allowed ? allowed.ToString() : "§UNLIMITED";
}

/// <summary> What the subscription of the signed-in tenant says now. </summary>
public sealed record DesktopSubscription(bool Exists, int? PlanId, string? PlanName, decimal? PlanPrice, string? PlanPeriod, string? Status, DateTime? PeriodEnd, bool Writable, string? Why, List<LimitRow> Limits, List<PlanRow> Plans);

""";

    private const string PlanValuesOf = """
        var keys = Keys;
        var values = await context.Set<§LIMITT>().AsNoTracking().Where(l => keys.Contains(l.§LKEY)).Select(l => new { Plan = l.§LPLAN, Key = l.§LKEY, Value = (long?)l.§LVALUE }).ToListAsync();

""";

    private const string PlanValuesNone = """
        var values = new[] { new { Plan = 0, Key = "", Value = (long?)null } }.Take(0).ToList();

""";

    private const string ChooseText = """

    /// <summary> The administrator moves the tenant to a plan of the list (SubscriptionPlanChoice=true), at once and with no payment step. Answers the sentence that refuses, or null when the plan was changed. </summary>
    public static async Task<string?> ChooseAsync(§CTX context, int planId)
    {
        if (context.CurrentTenantId is not int tenant)
            return "§NOTENANT_TEXT";
        if (!await context.Set<§P>().AsNoTracking().AnyAsync(p => p.§PKEY == planId))
            return "§UNKNOWNPLAN_TEXT";
§TOOSMALL        var row = await context.Set<§S>().Where(s => s.§STENANT == tenant).OrderByDescending(s => s.§SKEY).FirstOrDefaultAsync();
        if (row is null)
            context.Add(new §S { §STENANT = tenant, §SPLAN = planId, §SSTATUS = ActiveStatuses.Contains("active") ? "active" : ActiveStatuses[0]§STARTINIT });
        else
            row.§SPLAN = planId;
        await context.SaveChangesAsync();
        return null;
    }
""";

    private const string TooSmallText = """
        foreach (string key in Keys)
        {
            int used = await UsedAsync(context, tenant, key);
            if (await AllowedAsync(context, planId, key) is { } most && used > most)
                return string.Format("§TOOSMALL_TEXT", key, most, used);
        }
""";

    private const string GuardText = """
// The check of the API (Apis/Subscriptions.cs) written for the app's own context, which reads the database itself. The context filters the tenant's rows, so the counts and the subscription are the signed-in tenant's.
public static class SubscriptionGuard
{
    public static readonly string[] ActiveStatuses = { §STATUSES };
    public const bool Required = §REQUIRED;
    public const bool CanChoose = §CANCHOOSE;
    public static readonly string[] Keys = { §KEYS };

    /// <summary> The sentence that refuses a save, or null when it may go ahead: the subscription of the tenant is paused, or <paramref name="adding"/> new rows would pass the plan's limit named <paramref name="limitKey"/>. </summary>
    public static async Task<string?> RefusalAsync(§CTX context, string? limitKey, bool isNew, int adding = 1)
    {
        if (context.CurrentTenantId is not int tenant)
            return null;
        var state = await ReadAsync(context, tenant, includeLimits: false);
        if (!state.Writable)
            return state.Why;
        if (isNew && limitKey is not null && state.PlanId is { } plan && await AllowedAsync(context, plan, limitKey) is { } allowed)
        {
            int used = await UsedAsync(context, tenant, limitKey);
            if (used + adding > allowed)
                return string.Format("§LIMIT_TEXT", limitKey, allowed, used);
        }
        return null;
    }

    public static async Task<DesktopSubscription> ReadAsync(§CTX context, int tenant, bool includeLimits = true)
    {
        var row = await context.Set<§S>().AsNoTracking().Where(s => s.§STENANT == tenant).OrderByDescending(s => s.§SKEY)
            .Select(s => new { Plan = (int?)s.§SPLAN, Status = s.§SSTATUS, End = §ENDSELECT }).FirstOrDefaultAsync();
        var limits = new List<LimitRow>();
        var plans = new List<PlanRow>();
        if (includeLimits)
        {
            foreach (string key in Keys)
                limits.Add(new LimitRow { Key = key, Allowed = row?.Plan is { } planId ? await AllowedAsync(context, planId, key) : null, Used = await UsedAsync(context, tenant, key) });
            plans = await PlansAsync(context, row?.Plan);
        }
        var current = plans.FirstOrDefault(p => p.Current);
        if (row is null)
            return new DesktopSubscription(false, null, null, null, null, null, null, !Required, Required ? string.Format("§NONE_TEXT") : null, limits, plans);
        var (writable, why) = Evaluate(row.Status, row.End, DateTime.UtcNow);
        return new DesktopSubscription(true, row.Plan, current?.Name, current?.Price, current?.Period, row.Status, row.End, writable, why, limits, plans);
    }

    /// <summary> Every plan, cheapest first (a plan with no price last), with what it allows of each limit; <paramref name="current"/> marks the tenant's own. </summary>
    public static async Task<List<PlanRow>> PlansAsync(§CTX context, int? current)
    {
        var rows = await context.Set<§P>().AsNoTracking().Select(p => new { Id = p.§PKEY, Name = §PNAME, Price = §PRICESELECT, Period = §PERIODSELECT }).ToListAsync();
§PLANVALUES        return rows.OrderBy(r => r.Price ?? decimal.MaxValue).ThenBy(r => r.Id)
            .Select(r => new PlanRow { PlanId = r.Id, Name = r.Name, Price = r.Price, Period = r.Period, Current = r.Id == current, Limits = Keys.Select(k => new PlanLimitValue { Key = k, Allowed = values.FirstOrDefault(v => v.Plan == r.Id && v.Key == k)?.Value }).ToList() }).ToList();
    }

    /// <summary> The rule: a status outside <see cref="ActiveStatuses"/> pauses writes, and a trialing subscription also stops at its period end. </summary>
    public static (bool Writable, string? Why) Evaluate(string status, DateTime? end, DateTime now)
    {
        if (!ActiveStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
            return (false, string.Format("§INACTIVE_TEXT", status));
        if (end is { } ends && ends < now && status.Trim().Equals("trialing", StringComparison.OrdinalIgnoreCase))
            return (false, string.Format("§TRIAL_TEXT", ends.ToString("yyyy-MM-dd")));
        return (true, null);
    }

    public static async Task<long?> AllowedAsync(§CTX context, int plan, string key) =>
        await context.Set<§LIMITT>().AsNoTracking().Where(l => l.§LPLAN == plan && l.§LKEY == key).Select(l => (long?)l.§LVALUE).FirstOrDefaultAsync();

    public static Task<int> UsedAsync(§CTX context, int tenant, string key) => key switch
    {
§COUNTCASES        _ => Task.FromResult(0)
    };
§CHOOSE}

""";

    private const string GuardNoLimits = """
// The check of the API (Apis/Subscriptions.cs) written for the app's own context, which reads the database itself. The context filters the tenant's rows, so the subscription is the signed-in tenant's.
public static class SubscriptionGuard
{
    public static readonly string[] ActiveStatuses = { §STATUSES };
    public const bool Required = §REQUIRED;
    public const bool CanChoose = §CANCHOOSE;

    /// <summary> The sentence that refuses a save, or null when it may go ahead: the subscription of the tenant is paused. </summary>
    public static async Task<string?> RefusalAsync(§CTX context, string? limitKey, bool isNew, int adding = 1)
    {
        if (context.CurrentTenantId is not int tenant)
            return null;
        var state = await ReadAsync(context, tenant);
        return state.Writable ? null : state.Why;
    }

    public static readonly string[] Keys = { };

    public static async Task<DesktopSubscription> ReadAsync(§CTX context, int tenant, bool includeLimits = true)
    {
        var row = await context.Set<§S>().AsNoTracking().Where(s => s.§STENANT == tenant).OrderByDescending(s => s.§SKEY)
            .Select(s => new { Plan = (int?)s.§SPLAN, Status = s.§SSTATUS, End = §ENDSELECT }).FirstOrDefaultAsync();
        var plans = includeLimits ? await PlansAsync(context, row?.Plan) : [];
        var current = plans.FirstOrDefault(p => p.Current);
        if (row is null)
            return new DesktopSubscription(false, null, null, null, null, null, null, !Required, Required ? string.Format("§NONE_TEXT") : null, [], plans);
        var (writable, why) = Evaluate(row.Status, row.End, DateTime.UtcNow);
        return new DesktopSubscription(true, row.Plan, current?.Name, current?.Price, current?.Period, row.Status, row.End, writable, why, [], plans);
    }

    /// <summary> Every plan, cheapest first (a plan with no price last); <paramref name="current"/> marks the tenant's own. </summary>
    public static async Task<List<PlanRow>> PlansAsync(§CTX context, int? current)
    {
        var rows = await context.Set<§P>().AsNoTracking().Select(p => new { Id = p.§PKEY, Name = §PNAME, Price = §PRICESELECT, Period = §PERIODSELECT }).ToListAsync();
        return rows.OrderBy(r => r.Price ?? decimal.MaxValue).ThenBy(r => r.Id)
            .Select(r => new PlanRow { PlanId = r.Id, Name = r.Name, Price = r.Price, Period = r.Period, Current = r.Id == current }).ToList();
    }

    /// <summary> The rule: a status outside <see cref="ActiveStatuses"/> pauses writes, and a trialing subscription also stops at its period end. </summary>
    public static (bool Writable, string? Why) Evaluate(string status, DateTime? end, DateTime now)
    {
        if (!ActiveStatuses.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
            return (false, string.Format("§INACTIVE_TEXT", status));
        if (end is { } ends && ends < now && status.Trim().Equals("trialing", StringComparison.OrdinalIgnoreCase))
            return (false, string.Format("§TRIAL_TEXT", ends.ToString("yyyy-MM-dd")));
        return (true, null);
    }
§CHOOSE}

""";

    private const string PageText = """
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using §REPO;
§USINGS
namespace §NS;

// A plain Page: the menu shows it to administrators. It reads the subscription of the tenant the person works in from the database.
public sealed partial class SubscriptionPage : Page
{
    private readonly §CTX _context;

    public ObservableCollection<LimitRow> Limits { get; } = [];
    public ObservableCollection<PlanRow> Plans { get; } = [];

    public SubscriptionPage(§CTX context)
    {
        _context = context;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Problem.IsOpen = false;
        Limits.Clear();
        Plans.Clear();
        if (_context.CurrentTenantId is not int tenant)
        {
            Problem.Message = "§NOTENANT";
            Problem.IsOpen = true;
            return;
        }
        var info = await SubscriptionGuard.ReadAsync(_context, tenant);
        if (!info.Exists)
        {
            PlanText.Text = "§NOSUBSCRIPTION";
            PriceLine.Text = "";
            StatusText.Text = "";
            PeriodText.Text = "";
            WritableText.Text = info.Writable ? "" : "§PAUSED" + info.Why;
        }
        else
        {
            PlanText.Text = "§PLANLABEL: " + info.PlanName;
            PriceLine.Text = info.PlanPrice is null ? "" : "§PRICELABEL: " + PriceTexts.Of(info.PlanPrice, info.PlanPeriod);
            StatusText.Text = "§STATUSLABEL: " + info.Status;
            PeriodText.Text = info.PeriodEnd is { } end ? "§PERIODLABEL: " + end.ToString("d") : "";
            WritableText.Text = info.Writable ? "§ACTIVE" : "§PAUSED" + info.Why;
        }
        foreach (var limit in info.Limits)
            Limits.Add(limit);
        LimitsHeading.Visibility = Limits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var plan in info.Plans)
        {
            plan.CanChoose = SubscriptionGuard.CanChoose && !plan.Current;
            Plans.Add(plan);
        }
        PlansHeading.Visibility = Plans.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Choose moves the tenant to the plan of the row; a refusal shows its sentence.
    private async void OnChooseClick(object sender, RoutedEventArgs e)
    {
        if (await SubscriptionGuard.ChooseAsync(_context, (int)((Button)sender).Tag) is { } refused)
        {
            Problem.Message = refused;
            Problem.IsOpen = true;
            return;
        }
        await LoadAsync();
    }
}
""";

    /// <summary> The text of <c>Repositories/SubscriptionGuard.cs</c> after its namespace line: the shared records and the guard. </summary>
    public static string GuardFile(DatabaseModel database, ProjectSettings project, string contextType, string[] usings)
    {
        string guard = Guard(database, project, contextType, usings);
        // the records come after the usings of the guard text
        int classAt = guard.IndexOf("// The check of the API", StringComparison.Ordinal);
        return guard[..classAt] + FillAll(GuardCommon, ("§UNLIMITED", SubscriptionScreenWords.Unlimited)) + UnoSubscriptionCode.PlanRowClasses + "\n" + guard[classAt..];
    }
}
