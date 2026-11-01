using System.Text;

namespace CodeGenNew.Core;

/// <summary> MSTest tests of <c>SubscriptionGuard</c> in the WinUI3 app (<c>SubscriptionGuardTests.cs</c>): the guard runs in process over an in-memory SQLite database made from the generated context, with a tenant, a plan, its
/// limits and a subscription. A paused subscription refuses a change, a new row past a plan limit is refused, a trial ends at its period end, a tenant with no subscription and a caller in no tenant. </summary>
public static class DesktopSubscriptionTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.DesktopTests && DesktopSubscriptionCode.Applies(database, project);

    public static string File(DatabaseModel database, ProjectSettings project, string testNamespace, IEnumerable<string> usings, string contextType)
    {
        var subscription = SubscriptionPlan.SubscriptionOf(database, project)!;
        var limit = SubscriptionPlan.LimitTableOf(database, project);
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        // the context keeps foreign keys unchecked, so any limited table with a key the database makes can hold the rows
        var limited = limit is null ? null : SubscriptionPlan.LimitedTables(database, project).FirstOrDefault(l => !l.IsMembership && l.Table.PrimaryKeyColumns is [{ IsIdentity: true }]);
        if (limited is null)
            limit = null;
        bool trialing = SubscriptionPlan.ActiveStatuses(project).Contains("trialing", StringComparer.OrdinalIgnoreCase);
        string Json(string text) => ApiSampleValues.Json(text);
        string Row(TableModel table, params (ColumnModel Column, string Value)[] overrides) =>
            DesktopApprovalTests.SampleJson(table, project, overrides.ToDictionary(o => o.Column.Name, o => o.Value));
        string planJson = Row(subscription.PlanTable);
        string SubscriptionJson(string status) => Row(subscription.Table, (subscription.Tenant, "1"), (subscription.Plan, "1"), (subscription.Status, Json(status)));
        string statusCases = string.Concat(new[] { "active", "cancelled" }.Select(s => "        " + CSharpText.Quote(s) + " => \"\"\"" + SubscriptionJson(s) + "\"\"\",\n"));

        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append("using Microsoft.Data.Sqlite;\nusing Microsoft.EntityFrameworkCore;\n");
        o.Append($$""""

namespace {{testNamespace}};

/// <summary> The subscription rules of the app, run in process by <c>SubscriptionGuard</c>: a paused subscription refuses a change, a new row past a plan limit is refused, and the rules of a trial, of a tenant with no
/// subscription and of a caller in no tenant. </summary>
[TestClass]
public class SubscriptionGuardTests
{
    private const string TenantJson = """{{Row(tenantTable)}}""";
    private const string PlanJson = """{{planJson}}""";

    private sealed class Scope(int? tenant) : ITenantScope
    {
        public int? TenantId => tenant;
    }

    private static T Make<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json, TestJson.Options)!;

    private static {{contextType}} In(TestDb db, int? tenant) =>
        new(new DbContextOptionsBuilder<{{contextType}}>().UseSqlite(db.Connection).Options, new Scope(tenant));

    /// <summary> A tenant (key 1), a plan (key 1){{(limit is null ? "" : ", the plan's limit when one is given")}} and the subscription the test names (none for null). </summary>
    private static async Task<TestDb> OpenAsync(string? status{{(limit is null ? "" : ", long? allowed = null")}})
    {
        var db = await TestDb.CreateAsync();
        db.Context.Add(Make<{{tenantTable.TableName}}>(TenantJson));
        db.Context.Add(Make<{{subscription.PlanTable.TableName}}>(PlanJson));
        await db.Context.SaveChangesAsync();
{{LimitRow(limit, limited, project)}}        if (status is not null)
            db.Context.Add(Make<{{subscription.Table.TableName}}>(SubscriptionJson(status)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return db;
    }

    private static string SubscriptionJson(string status) => (status switch
    {
{{statusCases}}        _ => throw new ArgumentException(status)
    });

    [TestMethod]
    public async Task A_change_is_refused_while_the_subscription_is_not_active_and_allowed_when_it_is()
    {
        await using var db = await OpenAsync("cancelled");
        await using var paused = In(db, 1);
        Assert.AreEqual(string.Format("{{CSharpText.Escape(SubscriptionWords.InactiveText)}}", "cancelled"), await SubscriptionGuard.RefusalAsync(paused, null, false));
        await using var running = await OpenAsync("active");
        await using var allowed = In(running, 1);
        Assert.IsNull(await SubscriptionGuard.RefusalAsync(allowed, null, false));
    }

    [TestMethod]
    public async Task A_caller_in_no_tenant_is_not_checked()
    {
        await using var db = await OpenAsync("cancelled");
        await using var platform = In(db, null);
        Assert.IsNull(await SubscriptionGuard.RefusalAsync(platform, null, true));
    }
{{ChooseTests(project, limited)}}
    [TestMethod]
    public async Task {{(project.SubscriptionRequired ? "A_tenant_with_no_subscription_is_refused_when_a_subscription_is_required" : "A_tenant_with_no_subscription_is_on_no_plan_and_may_change_rows")}}()
    {
        await using var db = await OpenAsync(null);
        await using var context = In(db, 1);
        var refusal = await SubscriptionGuard.RefusalAsync(context, null, false);
        {{(project.SubscriptionRequired ? $"Assert.AreEqual(\"{CSharpText.Escape(SubscriptionWords.NoSubscriptionText)}\", refusal);" : "Assert.IsNull(refusal);")}}
    }

    [TestMethod]
    public async Task The_subscription_is_read_with_its_plan_and_status()
    {
        await using var db = await OpenAsync("active");
        await using var context = In(db, 1);
        var info = await SubscriptionGuard.ReadAsync(context, 1);
        Assert.IsTrue(info.Exists);
        Assert.AreEqual("active", info.Status);
        Assert.AreEqual(1, info.PlanId);
        Assert.IsTrue(info.Writable);
    }
{{TrialTest(subscription, trialing)}}{{LimitTests(limit, limited, project)}}}
"""");
        return o.ToString();
    }

    /// <summary> The test of <c>SubscriptionGuard.ChooseAsync</c> (with <c>SubscriptionPlanChoice=true</c>): an unknown plan and a caller in no tenant are refused, a plan that allows less than the tenant has is refused, and a plan is chosen. </summary>
    private static string ChooseTests(ProjectSettings project, LimitedTable? limited)
    {
        if (!project.SubscriptionPlanChoice)
            return "";
        string tooSmall = limited is null ? "" : $$"""
        await using var full = await OpenAsync("active", 1);
        await AddRowsAsync(full, 2);
        await using var over = In(full, 1);
        Assert.AreEqual(string.Format("{{CSharpText.Escape(SubscriptionWords.PlanTooSmallText)}}", {{CSharpText.Quote(limited.LimitKey)}}, 1, 2), await SubscriptionGuard.ChooseAsync(over, 1), "the plan allows one and the tenant has two");

""";
        return $$"""

    [TestMethod]
    public async Task A_tenant_chooses_a_plan_of_the_list_unless_it_is_unknown_or_allows_less_than_the_tenant_has()
    {
        await using var db = await OpenAsync("active");
        await using var context = In(db, 1);
        Assert.AreEqual("{{CSharpText.Escape(SubscriptionWords.UnknownPlan)}}", await SubscriptionGuard.ChooseAsync(context, 99999));
        await using var none = In(db, null);
        Assert.AreEqual("{{CSharpText.Escape(SubscriptionScreenWords.NoTenant)}}", await SubscriptionGuard.ChooseAsync(none, 1));
{{tooSmall}}        Assert.IsNull(await SubscriptionGuard.ChooseAsync(context, 1));
    }

""";
    }

    private static string LimitRow(PlanLimitModel? limit, LimitedTable? limited, ProjectSettings project)
    {
        if (limit is null || limited is null)
            return "";
        string json = DesktopApprovalTests.SampleJson(limit.Table, project, new Dictionary<string, string>
        {
            [limit.Plan.Name] = "1", [limit.Key.Name] = ApiSampleValues.Json(limited.LimitKey), [limit.Value.Name] = "999999"
        });
        // the allowed number is spliced into the JSON text; a value column the sample leaves out is added
        if (!json.Contains("999999", StringComparison.Ordinal))
            json = json[..json.LastIndexOf('}')].TrimEnd() + $", {ApiSampleValues.Json(JsonNames.Camel(limit.Value.Name))}: 999999 }}";
        int at = json.IndexOf("999999", StringComparison.Ordinal);
        string before = json[..at], after = json[(at + "999999".Length)..];
        return $"        if (allowed is not null)\n            db.Context.Add(Make<{limit.Table.TableName}>(\"\"\"{before}\"\"\" + allowed + \"\"\"{after}\"\"\"));\n";
    }

    private static string TrialTest(SubscriptionModel subscription, bool trialing) => subscription.PeriodEnd is null || !trialing ? "" : """

    [TestMethod]
    public void A_trial_ends_at_its_period_end()
    {
        var now = DateTime.UtcNow;
        Assert.IsFalse(SubscriptionGuard.Evaluate("trialing", now.AddDays(-1), now).Writable, "an ended trial pauses changes");
        Assert.IsTrue(SubscriptionGuard.Evaluate("trialing", now.AddDays(14), now).Writable, "a running trial does not");
        Assert.IsTrue(SubscriptionGuard.Evaluate("trialing", null, now).Writable, "a trial with no end does not");
    }

""";

    private static string LimitTests(PlanLimitModel? limit, LimitedTable? limited, ProjectSettings project)
    {
        if (limit is null || limited is null)
            return "";
        string key = CSharpText.Quote(limited.LimitKey);
        string tenantColumn = limited.Tenant.Name;
        string rowJson = DesktopApprovalTests.SampleJson(limited.Table, project, new Dictionary<string, string> { [tenantColumn] = "1" });
        return $$""""

    private static async Task AddRowsAsync(TestDb db, int count)
    {
        for (int i = 0; i < count; i++)
            db.Context.Add(Make<{{limited.Table.TableName}}>(RowJson));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    private const string RowJson = """{{rowJson}}""";

    [TestMethod]
    public async Task A_new_row_past_the_plan_limit_is_refused_until_the_limit_is_raised()
    {
        await using var db = await OpenAsync("active", 1);
        await AddRowsAsync(db, 1);
        await using var context = In(db, 1);
        Assert.AreEqual(string.Format("{{CSharpText.Escape(SubscriptionWords.LimitText)}}", {{key}}, 1, 1), await SubscriptionGuard.RefusalAsync(context, {{key}}, true));
        Assert.IsNull(await SubscriptionGuard.RefusalAsync(context, {{key}}, false), "a change of a row that exists is not a new row");
        Assert.IsNotNull(await SubscriptionGuard.RefusalAsync(context, {{key}}, true, 2), "two new rows are more than one");

        await using var larger = await OpenAsync("active", 3);
        await AddRowsAsync(larger, 1);
        await using var roomy = In(larger, 1);
        Assert.IsNull(await SubscriptionGuard.RefusalAsync(roomy, {{key}}, true));
        Assert.IsNull(await SubscriptionGuard.RefusalAsync(roomy, {{key}}, true, 2), "two more rows fit under three");
        Assert.IsNotNull(await SubscriptionGuard.RefusalAsync(roomy, {{key}}, true, 3), "three more rows do not");
    }

    [TestMethod]
    public async Task A_plan_without_a_limit_row_is_unlimited_and_the_view_shows_what_is_in_use()
    {
        await using var db = await OpenAsync("active");
        await AddRowsAsync(db, 2);
        await using var context = In(db, 1);
        Assert.IsNull(await SubscriptionGuard.RefusalAsync(context, {{key}}, true));
        var limit = (await SubscriptionGuard.ReadAsync(context, 1)).Limits.Single(l => l.Key == {{key}});
        Assert.IsNull(limit.Allowed);
        Assert.AreEqual(2, limit.Used);
    }

"""";
    }
}
