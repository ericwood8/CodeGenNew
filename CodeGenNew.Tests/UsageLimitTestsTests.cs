using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> The Python and Rust tests that play the plan limit, the refusal and the race of a usage ledger (PY_TestUsage, RS_TestUsage): the text they are written from, over a schema read from a SQLite file that has
/// the plan tables the subscription tests need. </summary>
[TestClass]
public class UsageLimitTestsTests
{
    private static readonly string[] Tables =
    [
        "CREATE TABLE Plan (PlanId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(40) NOT NULL, MonthlyPrice DECIMAL(10,2) NOT NULL DEFAULT 0, BillingPeriod VARCHAR(20) NOT NULL DEFAULT 'month')",
        "CREATE TABLE PlanLimit (PlanLimitId INTEGER PRIMARY KEY AUTOINCREMENT, PlanId INTEGER NOT NULL REFERENCES Plan (PlanId), LimitKey VARCHAR(40) NOT NULL, LimitValue INTEGER NULL)",
        "CREATE TABLE Subscription (SubscriptionId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), PlanId INTEGER NOT NULL REFERENCES Plan (PlanId), Status VARCHAR(20) NOT NULL, CurrentPeriodStart DATETIME NULL, CurrentPeriodEnd DATETIME NULL, GatewaySubscriptionId VARCHAR(80) NULL)",
        "CREATE TABLE UsageEvent (UsageEventId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Kind VARCHAR(40) NOT NULL, Quantity INTEGER NOT NULL, OccurredOn DATETIME NOT NULL)",
        "CREATE TABLE UsageMonthly (UsageMonthlyId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Kind VARCHAR(40) NOT NULL, UsageMonth DATE NOT NULL, Quantity INTEGER NOT NULL)",
        "CREATE TABLE PingEvent (PingEventId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Kind VARCHAR(40) NOT NULL, OccurredOn DATETIME NOT NULL)"
    ];

    private static async Task<DatabaseModel> ReadAsync()
    {
        string database = TenantPlanTests.CreateDatabase(Tables);
        try
        {
            var request = new CodeGenNew.Connections.ConnectionRequest { Provider = CodeGenNew.Connections.DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            return await provider.BuildAsync(database, "main");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    private static ProjectSettings Project(bool refuse, string usage = "UsageEvent", Dictionary<string, string>? extra = null)
    {
        var values = TenantPlanTests.Values(new Dictionary<string, string>
        {
            ["SubscriptionTable"] = "Subscription", ["PlanTable"] = "Plan", ["PlanLimitTable"] = "PlanLimit", ["PythonTests"] = "true", ["RustTests"] = "true", ["Stacks"] = "api,python,rust",
            ["UsageTables"] = usage, ["UsageRollup"] = "UsageMonthly", ["UsageLimits"] = "api_calls,emails", ["UsageRefuse"] = refuse ? "true" : "false"
        });
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    private static (string Python, string Rust) Texts(DatabaseModel database, ProjectSettings project)
    {
        var ledger = UsageTestPlan.Ledgers(database, project).Single(t => t.Table.TableName == "UsageEvent");
        var rollup = UsagePlan.RollupFor(database, project, ledger);
        return (PythonUsageTests.File(ledger, database, project, rollup), RustUsageTests.File(ledger, database, project, rollup));
    }

    [TestMethod]
    public async Task A_refusing_project_plays_the_limit_the_refusal_and_the_race_in_both_stacks()
    {
        var database = await ReadAsync();
        var (python, rust) = Texts(database, Project(refuse: true));

        Assert.IsFalse(python.Contains('§') || rust.Contains('§'), "every token is filled");
        Assert.Contains("KEY = \"api_calls\"", python, "the first kind the plan limits in this ledger");
        Assert.Contains("const KEY: &str = \"api_calls\";", rust);
        Assert.Contains("def test_a_request_past_the_plan_is_refused_and_writes_nothing", python);
        Assert.Contains("async fn a_request_past_the_plan_is_refused_and_writes_nothing", rust);
        Assert.Contains("def test_requests_at_the_same_moment_cannot_both_pass_the_limit", python);
        Assert.Contains("async fn requests_at_the_same_moment_cannot_both_pass_the_limit", rust);
        Assert.Contains("assert refused.status_code == 402", python);
        Assert.Contains("StatusCode::PAYMENT_REQUIRED", rust);
        Assert.Contains("/problems/plan-limit", python);
        Assert.Contains("/problems/plan-limit", rust);
        Assert.IsFalse(python.Contains("passes_its_plan") || rust.Contains("passes_its_plan"), "the over flag case is for a project that does not refuse");
        Assert.Contains("'api_calls'", python, "the plan limit rows are set with SQL for the kind");
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(python, "INSERT INTO [^ ]*PlanLimit[^ ]* "), "a PlanLimit row is inserted for the kind");
        Assert.Contains("run(engine, [", python);
        Assert.Contains("run(&api, &[", rust);
        Assert.Contains("forget_state(tenant)", python);
        Assert.Contains("::forget_state(tenant);", rust);
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(python, "REMOVE_TENANT_EVENTS = \"DELETE FROM [^ ]*UsageEvent[^ ]* WHERE [^ ]*TenantId[^ ]* = \""), "the tenant's events are taken out before the tenant");
        Assert.Contains("REMOVE_TENANT_MONTHS = ", python);
        Assert.Contains("statements.insert(0, REMOVE_TENANT_MONTHS + str(tenant))", python);
        Assert.Contains("flavor = \"multi_thread\"", rust, "the race runs on several threads");
        Assert.Contains("ThreadPoolExecutor", python);
    }

    [TestMethod]
    public async Task A_project_that_does_not_refuse_plays_the_over_flag_and_not_the_refusal_or_the_race()
    {
        var database = await ReadAsync();
        var (python, rust) = Texts(database, Project(refuse: false));

        Assert.IsFalse(python.Contains('§') || rust.Contains('§'), "every token is filled");
        Assert.Contains("def test_the_answer_says_when_a_tenant_passes_its_plan_and_the_subscription_shows_it", python);
        Assert.Contains("async fn the_answer_says_when_a_tenant_passes_its_plan_and_the_subscription_shows_it", rust);
        Assert.Contains("second[\"over\"] is True", python);
        Assert.Contains("second[\"over\"], json!(true)", rust);
        foreach (string text in new[] { python, rust })
        {
            Assert.IsFalse(text.Contains("refused_and_writes_nothing") || text.Contains("same_moment"), "no refusal and no race without UsageRefuse");
            Assert.IsFalse(text.Contains("plan-limit"));
        }
    }

    [TestMethod]
    public async Task A_ledger_with_no_units_or_no_limited_kind_or_no_plan_tables_gets_no_limit_tests()
    {
        var database = await ReadAsync();
        var noUnits = UsageTestPlan.Ledgers(database, Project(refuse: true, "PingEvent")).Single(t => t.Table.TableName == "PingEvent");
        Assert.IsNull(noUnits.Quantity);
        string python = PythonUsageTests.File(noUnits, database, Project(refuse: true, "PingEvent"), null);
        Assert.IsFalse(python.Contains("PLAN =") || python.Contains("billed"), "a ledger with no units column has no limit to play");

        var unlimited = Project(refuse: false, extra: new() { ["UsageLimits"] = "", ["UsageRefuse"] = "false" });
        var ledger = UsageTestPlan.Ledgers(database, unlimited).Single(t => t.Table.TableName == "UsageEvent");
        Assert.IsNull(UsageTestPlan.LimitCaseOf(database, unlimited, ledger, true, ""), "no kind is limited");
        Assert.IsNull(UsageTestPlan.LimitCaseOf(database, Project(refuse: true), ledger, false, ""), "the stack has no subscriptions");
        Assert.IsNotNull(UsageTestPlan.LimitCaseOf(database, Project(refuse: true), ledger, true, ""));

        var sqlServer = new DatabaseModel { DatabaseName = database.DatabaseName, SchemaName = database.SchemaName, Dialect = SqlDialect.SqlServer, Tables = database.Tables };
        Assert.IsNull(UsageTestPlan.LimitCaseOf(sqlServer, Project(refuse: true), ledger, true, ""), "SQL Server cannot give the plan a key with SQL");
    }
}
