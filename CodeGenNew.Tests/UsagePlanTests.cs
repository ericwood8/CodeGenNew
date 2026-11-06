using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> UsageTables (the usage ledger: an append-only table of billable events, summed per tenant, kind and month; no ordinary API, the recorder and the summary route) and UsageLimits (plan limits counted from the month's events). </summary>
[TestClass]
public class UsagePlanTests
{
    private static ColumnModel C(string name, SqlDbType type, bool nullable = false, bool primaryKey = false, int ordinal = 0, int? characters = null, bool identity = false) =>
        Sample.Column(name, type, nullable: nullable, primaryKey: primaryKey, ordinal: ordinal, characters: characters, identity: identity);

    private static ForeignKeyModel TenantKey() => new() { ConstraintName = "FK_u", ReferencingColumns = ["TenantId"], ReferencedSchema = "dbo", ReferencedTable = "Tenant", ReferencedColumns = ["TenantId"] };

    private static TableModel Events(bool quantity = true, bool twoStamps = false, int kindLength = 40) => Sample.Table("UsageEvent",
        new List<ColumnModel>
        {
            C("UsageEventId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: kindLength),
            C("OccurredOn", SqlDbType.DateTime, ordinal: 5)
        }.Concat(quantity ? [C("Quantity", SqlDbType.Int, ordinal: 4)] : []).Concat(twoStamps ? [C("OccurredAt", SqlDbType.DateTime, ordinal: 6)] : []).ToList(), [TenantKey()]);

    private static TableModel Tenant() => Sample.Table("Tenant", [C("TenantId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("Name", SqlDbType.VarChar, ordinal: 2, characters: 40)]);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.SqlServer, Tables = tables.ToList() };

    private static ProjectSettings Project(string usage = "UsageEvent", Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string>
        {
            ["AccessMode"] = "Ef", ["DatabaseProvider"] = "SqlServer", ["ApiAuth"] = "true", ["TenantTable"] = "Tenant", ["UsageTables"] = usage
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public void The_columns_are_found_by_name_and_the_tenant_by_its_foreign_key()
    {
        var usage = TableShapes.For(Events(), Project()).Usage!;

        Assert.AreEqual("Kind", usage.Kind.Name);
        Assert.AreEqual("Quantity", usage.Quantity!.Name);
        Assert.AreEqual("OccurredOn", usage.Occurred.Name);
        Assert.AreEqual("TenantId", usage.Tenant.Name);
        Assert.AreEqual("Tenant", usage.TenantTable);
        Assert.IsNull(TableShapes.For(Events(quantity: false), Project()).Usage!.Quantity, "without a quantity each event is one unit");
        Assert.AreEqual("(long)e.Quantity", usage.UnitsOf("e"));
        Assert.AreEqual("1L", TableShapes.For(Events(quantity: false), Project()).Usage!.UnitsOf());
    }

    [TestMethod]
    public void A_table_that_lacks_something_is_named_with_what_it_lacks()
    {
        Assert.Contains("ApiAuth", UsagePlan.Problem(Events(), Project(extra: new() { ["ApiAuth"] = "false" }))!);
        Assert.Contains("AccessMode=Ef", UsagePlan.Problem(Events(), Project(extra: new() { ["AccessMode"] = "Routines" }))!);
        Assert.Contains("tenant", UsagePlan.Problem(Events(), Project(extra: new() { ["TenantTable"] = "Other" }))!);
        Assert.Contains("Missing (a NOT NULL date and time column", UsagePlan.Problem(Events(twoStamps: true), Project("UsageEvent:Kind:Quantity:Missing"))!, "a named column that does not exist");
        Assert.Contains("an occurred-on column", UsagePlan.Problem(Events(twoStamps: true), Project("UsageEvent"))!.Replace("OccurredOn", ""), "two stamps are ambiguous and must be named");
        Assert.Contains("Missing", UsagePlan.Problem(Events(), Project("UsageEvent:Missing"))!);
        Assert.IsNull(UsagePlan.Problem(Events(), Project()));
        Assert.IsNull(UsagePlan.Problem(Tenant(), Project()), "a table the project does not list is not asked");
        Assert.Contains("UsageLimits needs SubscriptionTable", UsagePlan.Problems(Database(Tenant(), Events()), Project(extra: new() { ["UsageLimits"] = "api_calls" })).Single());
        Assert.Contains("UsageTables: Missing is not a table", UsagePlan.Problems(Database(Tenant(), Events()), Project("UsageEvent,Missing")).Single());
    }

    [TestMethod]
    public void The_table_gets_no_ordinary_api_and_the_age_of_an_event_is_when_it_occurred()
    {
        var project = Project();

        Assert.IsTrue(project.NoApi("UsageEvent", Events().LookupShape) == true);
        Assert.IsFalse(Events().HasCrudApi(project), "no list and edit API for a ledger");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "API_Usage");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "PY_Usage");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "RS_Usage");
        CollectionAssert.DoesNotContain(project.ImpliedPlanTemplates.ToList(), "API_TestUsage", "the generated tests need ApiTests");
        CollectionAssert.Contains(Project(extra: new() { ["ApiTests"] = "true" }).ImpliedPlanTemplates.ToList(), "API_TestUsage");
        Assert.IsTrue(ProjectSettingsHints.All.ContainsKey("UsageTables") && ProjectSettingsHints.All.ContainsKey("UsageLimits"));
        Assert.AreEqual(ProjectSettingGroups.TabOf("SubscriptionTable"), ProjectSettingGroups.TabOf("UsageTables"), "the usage settings sit on the Plans tab, beside the limits they feed");
    }

    [TestMethod]
    public void The_recorder_is_the_only_writer_and_the_summary_adds_per_month_and_kind()
    {
        var database = Database(Tenant(), Events());
        var shape = TableShapes.For(Events(), Project()).Usage!;
        string code = UsageWriter.File([shape], database, Project(), "ShopContext", []);

        Assert.Contains("class UsageEventUsage", code);
        Assert.Contains("public static async Task<Recorded> RecordAsync", code);
        Assert.Contains("SumAsync(x => (long)x.Quantity", code);
        Assert.Contains("GroupBy(x => new { x.OccurredOn.Year, x.OccurredOn.Month, x.Kind })", code);
        Assert.Contains("app.MapGet(\"/api/usageevents\"", code);
        Assert.Contains("app.MapPost(\"/api/usageevents\"", code);
        Assert.Contains("RecordRouteAsync", code);
        Assert.IsFalse(code.Contains("MapPut") || code.Contains("MapDelete") || code.Contains("MapPatch"), "nothing edits or deletes an event");
        Assert.Contains("LimitedKinds = { }", code, "no limited kind without UsageLimits");
        Assert.IsFalse(code.Contains('§'), "every token is filled");
        string counted = UsageWriter.File([TableShapes.For(Events(quantity: false), Project()).Usage!], database, Project(), "ShopContext", []);
        Assert.Contains("LongCountAsync(cancellationToken)", counted);
        Assert.Contains("g.LongCount()", counted);
    }

    [TestMethod]
    public void A_limited_kind_asks_the_plan_and_the_subscription_counts_the_months_usage()
    {
        var project = Project(extra: new()
        {
            ["UsageLimits"] = "api_calls,emails", ["SubscriptionTable"] = "Subscription", ["PlanLimitTable"] = "PlanLimit"
        });
        var database = Database(Tenant(), Events());
        string code = UsageWriter.File([TableShapes.For(Events(), project).Usage!], database, project, "ShopContext", []);

        Assert.Contains("LimitedKinds = { \"api_calls\", \"emails\" }", code);
        Assert.Contains("SubscriptionLimits.AllowedAsync(context, plan, kind, cancellationToken)", code);
        Assert.IsFalse(code.Contains('§'), "every token is filled");
        Assert.Contains("UsageLimits needs SubscriptionTable", UsagePlan.Problems(database, Project(extra: new() { ["UsageLimits"] = "api_calls" }))[0]);
    }

    [TestMethod]
    public void The_python_and_rust_routes_are_written_with_the_same_sentences()
    {
        var database = Database(Tenant(), Events());
        var shape = TableShapes.For(Events(), Project()).Usage!;
        string python = PythonUsageWriter.File([shape], database, Project());
        string rust = RustUsageWriter.File([shape], database, Project(), SqlDialect.Sqlite);

        Assert.Contains("def record_usage(", python);
        Assert.Contains("def used_this_month(", python);
        Assert.Contains("func.extract(\"year\", occurred)", python);
        Assert.Contains(UsageWords.PlatformDetail, python);
        Assert.Contains("pub async fn record_usage(", rust);
        Assert.Contains("pub async fn used_this_month(", rust);
        Assert.Contains("GROUP BY CAST(strftime('%Y'", rust);
        Assert.Contains(UsageWords.PlatformDetail, rust);
        Assert.IsFalse(python.Contains('§') || rust.Contains('§'), "every token is filled");
        string mysql = RustUsageWriter.File([shape], database, Project(), SqlDialect.MySql);
        Assert.Contains("last_insert_id()", mysql);
        Assert.IsFalse(mysql.Contains("RETURNING"), "MySQL has no RETURNING");
        Assert.Contains("RETURNING", rust);
    }

    private static TableModel Decimals() => Sample.Table("UsageEvent",
        new List<ColumnModel>
        {
            C("UsageEventId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: 40),
            C("Quantity", SqlDbType.Decimal, ordinal: 4), C("OccurredOn", SqlDbType.DateTime, ordinal: 5)
        }, [TenantKey()]);

    private static TableModel Monthly(SqlDbType month = SqlDbType.Date, bool extraRequired = false) => Sample.Table("UsageMonthly",
        new List<ColumnModel>
        {
            C("UsageMonthlyId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: 40),
            C("UsageMonth", month, ordinal: 4), C("Quantity", SqlDbType.Decimal, ordinal: 5)
        }.Concat(extraRequired ? [C("Note", SqlDbType.VarChar, ordinal: 6, characters: 20)] : []).ToList(), [TenantKey()]);

    [TestMethod]
    public void A_decimal_quantity_is_summed_as_a_decimal_and_a_whole_ledger_refuses_a_fraction()
    {
        var database = Database(Tenant(), Decimals());
        var shape = TableShapes.For(Decimals(), Project()).Usage!;
        string code = UsageWriter.File([shape], database, Project(), "ShopContext", []);
        string whole = UsageWriter.File([TableShapes.For(Events(), Project()).Usage!], Database(Tenant(), Events()), Project(), "ShopContext", []);

        Assert.IsTrue(shape.QuantityIsDecimal);
        Assert.Contains("await query.SumAsync(x => x.Quantity, cancellationToken)", code);
        Assert.IsFalse(code.Contains("quantity != decimal.Truncate"), "a decimal ledger takes fractions");
        Assert.Contains("(decimal)await query.SumAsync(x => (long)x.Quantity, cancellationToken)", whole);
        Assert.Contains("quantity != decimal.Truncate(quantity)", whole, "a whole ledger refuses a fraction");
        var sqlite = new DatabaseModel { DatabaseName = "Shop", SchemaName = "main", Dialect = SqlDialect.Sqlite, Tables = [Tenant(), Decimals()] };
        Assert.Contains(".ToListAsync(cancellationToken)).Sum()", UsageWriter.File([shape], sqlite, Project(), "ShopContext", []), "SQLite has no decimal aggregate: the rows are added here");
        string python = PythonUsageWriter.File([shape], database, Project());
        string rust = RustUsageWriter.File([shape], database, Project(), SqlDialect.PostgreSql);
        Assert.Contains("Column(\"Quantity\", Numeric)", python);
        Assert.Contains("AS DOUBLE PRECISION", rust);
        Assert.IsFalse(python.Contains('§') || rust.Contains('§') || code.Contains('§'), "every token is filled");
    }

    [TestMethod]
    public void The_monthly_total_table_is_found_checked_and_left_out_of_the_ordinary_api()
    {
        var project = Project(extra: new() { ["UsageRollup"] = "UsageMonthly", ["UsageRollupDays"] = "45" });
        var database = Database(Tenant(), Decimals(), Monthly());
        var rollup = UsagePlan.RollupOf(database, project)!;

        Assert.AreEqual("UsageMonth", rollup.Month.Name);
        Assert.AreEqual("Quantity", rollup.Quantity.Name);
        Assert.IsFalse(rollup.MonthIsDateOnly, "a date column is read as a DateTime");
        Assert.IsEmpty(UsagePlan.Problems(database, project));
        Assert.IsTrue(project.NoApi("UsageMonthly", Monthly().LookupShape) == true);
        Assert.IsFalse(Monthly().HasCrudApi(project));
        Assert.AreEqual(45, project.UsageRollupDays);
        Assert.AreEqual(60, Project().UsageRollupDays, "the default");
        Assert.Contains("needs UsageTables", UsagePlan.Problems(Database(Tenant(), Monthly()), ProjectSettings.FromValues(new Dictionary<string, string> { ["ApiAuth"] = "true", ["TenantTable"] = "Tenant", ["UsageRollup"] = "UsageMonthly" })).Single());
        Assert.Contains("Missing is not a table", UsagePlan.Problems(Database(Tenant(), Decimals()), Project(extra: new() { ["UsageRollup"] = "Missing" })).Single());
        Assert.Contains("other column that cannot be NULL", UsagePlan.Problems(Database(Tenant(), Decimals(), Monthly(extraRequired: true)), project).Single());
        Assert.Contains("decimal column like the ledger's", UsagePlan.Problems(Database(Tenant(), Decimals(), Sample.Table("UsageMonthly",
            [C("UsageMonthlyId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: 40), C("UsageMonth", SqlDbType.Date, ordinal: 4), C("Quantity", SqlDbType.Int, ordinal: 5)],
            [TenantKey()])), project).Single());
    }

    private static TableModel Calls() => Sample.Table("ApiCall",
        [C("ApiCallId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: 40),
         C("Quantity", SqlDbType.Decimal, ordinal: 4), C("OccurredOn", SqlDbType.DateTime, ordinal: 5)], [TenantKey()]);

    private static TableModel CallMonths() => Sample.Table("ApiCallMonthly",
        [C("ApiCallMonthlyId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: 40),
         C("UsageMonth", SqlDbType.Date, ordinal: 4), C("Quantity", SqlDbType.Decimal, ordinal: 5)], [TenantKey()]);

    [TestMethod]
    public void Every_usage_table_gets_its_own_monthly_totals_by_position_or_by_name()
    {
        var database = Database(Tenant(), Calls(), CallMonths(), Decimals(), Monthly());
        var byPosition = Project("UsageEvent,ApiCall", new() { ["UsageRollup"] = "UsageMonthly,ApiCallMonthly" });
        var ledgers = UsagePlan.Ledgers(database, byPosition);

        Assert.AreEqual("ApiCall", ledgers[0].Table.TableName, "the code is written in the order of the table names");
        Assert.AreEqual("ApiCallMonthly", UsagePlan.RollupFor(database, byPosition, ledgers[0])!.Table.TableName, "the second table listed gets the second entry");
        Assert.AreEqual("UsageMonthly", UsagePlan.RollupFor(database, byPosition, ledgers[1])!.Table.TableName, "the first table listed gets the first entry");
        Assert.IsEmpty(UsagePlan.Problems(database, byPosition));

        var named = Project("UsageEvent,ApiCall", new() { ["UsageRollup"] = "UsageMonthly:UsageMonth:Quantity:UsageEvent" });
        Assert.IsNull(UsagePlan.RollupFor(database, named, UsagePlan.Ledgers(database, named)[0]), "ApiCall has none");
        Assert.AreEqual("UsageMonthly", UsagePlan.RollupFor(database, named, UsagePlan.Ledgers(database, named)[1])!.Table.TableName);
        Assert.IsEmpty(UsagePlan.Problems(database, named));

        Assert.Contains("belongs to no usage table", UsagePlan.Problems(database, Project("UsageEvent", new() { ["UsageRollup"] = "UsageMonthly,ApiCallMonthly" })).Single());
        Assert.Contains("two usage tables", UsagePlan.Problems(database, Project("UsageEvent,ApiCall", new() { ["UsageRollup"] = "UsageMonthly,UsageMonthly" })).Single());

        string code = UsageWriter.File(ledgers, database, byPosition, "ShopContext", []);
        Assert.Contains("Set<ApiCallMonthly>()", code);
        Assert.Contains("Set<UsageMonthly>()", code);
        Assert.Contains("app.MapPost(\"/api/apicalls/roll-up\"", code);
        Assert.Contains("app.MapPost(\"/api/usageevents/roll-up\"", code);
        string python = PythonUsageWriter.File(ledgers, database, byPosition);
        Assert.AreEqual(2, python.Split("RollupSpec(table=").Length - 1);
        string rust = RustUsageWriter.File(ledgers, database, byPosition, SqlDialect.Sqlite);
        Assert.Contains("pub async fn roll_up(", rust);
        Assert.Contains("pub async fn roll_up_usage_event(", rust);
        Assert.Contains("fn router_usage_event()", rust);
        Assert.IsFalse(code.Contains('§') || python.Contains('§') || rust.Contains('§'), "every token is filled");
    }

    private static DatabaseModel TwoLedgers(SqlDialect dialect = SqlDialect.SqlServer) => new() { DatabaseName = "Shop", SchemaName = "dbo", Dialect = dialect, Tables = [Tenant(), Calls(), Decimals()] };

    [TestMethod]
    public void The_python_and_rust_tests_of_a_ledger_are_written_for_Python_on_every_database_but_SQL_Server()
    {
        var settings = new Dictionary<string, string> { ["PythonTests"] = "true", ["RustTests"] = "true", ["UsageRollup"] = "UsageMonthly,ApiCallMonthly" };
        var project = Project("UsageEvent,ApiCall", settings);
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "PY_TestUsage");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "RS_TestUsage");
        CollectionAssert.DoesNotContain(Project("UsageEvent").ImpliedPlanTemplates.ToList(), "PY_TestUsage", "the generated tests need PythonTests");
        Assert.IsFalse(PythonUsageTests.Applies(TwoLedgers(), project), "the Python tests need a scratch copy of the database, which SQL Server has not");
        foreach (var dialect in new[] { SqlDialect.PostgreSql, SqlDialect.MySql, SqlDialect.Sqlite })
        {
            var database = TwoLedgers(dialect);
            Assert.IsTrue(PythonUsageTests.Applies(database, project) && RustUsageTests.Applies(database, project), dialect.ToString());
            foreach (var ledger in UsageTestPlan.Ledgers(database, project))
            {
                var rollup = UsagePlan.RollupFor(database, project, ledger);
                string python = PythonUsageTests.File(ledger, database, project, rollup), rust = RustUsageTests.File(ledger, database, project, rollup);
                Assert.IsFalse(python.Contains('§') || rust.Contains('§'), "every token is filled: " + ledger.Table.TableName);
                Assert.Contains("test_without_a_token_the_routes_answer_401", python);
                Assert.Contains("async fn without_a_token_the_routes_answer_401", rust);
                Assert.Contains("test_nothing_edits_or_deletes_an_event", python);
                Assert.AreEqual(rollup is not null && ledger.Quantity is not null, python.Contains("test_rolling_up_totals"), "the roll-up case follows the monthly table");
                Assert.AreEqual(rollup is not null && ledger.Quantity is not null, rust.Contains("async fn rolling_up_totals"));
                Assert.AreEqual(ledger.QuantityIsDecimal, python.Contains("\"quantity\": 1.5") && rust.Contains("\"quantity\": 1.5") && !python.Contains("refuses a fraction"), "a decimal ledger takes a fraction, a whole-number ledger refuses one");
                Assert.Contains("DELETE FROM", python);
            }
        }
    }

    private static readonly Dictionary<string, string> PlanSettings = new() { ["SubscriptionTable"] = "Subscription", ["PlanLimitTable"] = "PlanLimit" };

    [TestMethod]
    public void A_plan_limit_is_counted_from_the_usage_table_that_owns_its_kind()
    {
        var database = TwoLedgers();
        var project = Project("UsageEvent,ApiCall", new(PlanSettings) { ["UsageLimits"] = "api_calls,hits:ApiCall,emails:UsageEvent" });
        var ledgers = UsagePlan.Ledgers(database, project);
        var calls = ledgers.Single(l => l.Table.TableName == "ApiCall");
        var events = ledgers.Single(l => l.Table.TableName == "UsageEvent");

        CollectionAssert.AreEqual(new[] { "api_calls", "hits", "emails" }, project.UsageLimits, "the names only");
        Assert.AreEqual("UsageEvent", UsagePlan.LedgerOfLimit(database, project, "api_calls")!.Table.TableName, "a kind with no table belongs to the first table listed");
        Assert.AreEqual("ApiCall", UsagePlan.LedgerOfLimit(database, project, "hits")!.Table.TableName);
        CollectionAssert.AreEqual(new[] { "hits" }, UsagePlan.KeysOf(database, project, calls).ToArray());
        CollectionAssert.AreEqual(new[] { "api_calls", "emails" }, UsagePlan.KeysOf(database, project, events).ToArray());
        Assert.IsEmpty(UsagePlan.Problems(database, project));
        Assert.Contains("hits:Missing names a table that is not in UsageTables", UsagePlan.Problems(database, Project("UsageEvent,ApiCall", new(PlanSettings) { ["UsageLimits"] = "hits:Missing" })).Single());

        string code = UsageWriter.File(ledgers, database, project, "ShopContext", []);
        Assert.Contains("public static readonly string[] LimitedKinds = { \"hits\" };", code);
        Assert.Contains("public static readonly string[] LimitedKinds = { \"api_calls\", \"emails\" };", code);
        string python = PythonUsageWriter.File(ledgers, database, project);
        Assert.Contains("limited=(\"hits\",)", python);
        Assert.Contains("limited=(\"api_calls\", \"emails\")", python);
        string rust = RustUsageWriter.File(ledgers, database, project, SqlDialect.Sqlite);
        Assert.Contains("const LIMITED_KINDS: &[&str] = &[\"hits\"];", rust, "the first table by name keeps the plain names");
        Assert.Contains("const LIMITED_KINDS_usage_event: &[&str] = &[\"api_calls\", \"emails\"];", rust);
        Assert.IsFalse(code.Contains('§') || python.Contains('§') || rust.Contains('§'), "every token is filled");
    }

    [TestMethod]
    public void A_refusing_record_holds_a_lock_from_the_check_to_the_write()
    {
        var limits = new Dictionary<string, string>(PlanSettings) { ["UsageLimits"] = "api_calls", ["UsageRefuse"] = "true" };
        var lenient = new Dictionary<string, string>(PlanSettings) { ["UsageLimits"] = "api_calls" };
        foreach (var (dialect, aspLock, pythonLock, rustLock) in new[]
        {
            (SqlDialect.SqlServer, "WITH (UPDLOCK, ROWLOCK)", "with_hint(spec.tenants, \"WITH (UPDLOCK, ROWLOCK)\", \"mssql\")", null),
            (SqlDialect.PostgreSql, "FOR UPDATE", "with_for_update()", "pg_advisory_lock(5821, $1)"),
            (SqlDialect.MySql, "FOR UPDATE", "with_for_update()", "GET_LOCK(?, 60)"),
            (SqlDialect.Sqlite, "Local.WaitAsync", "_GATE = threading.Lock()", "USAGE_GATE.acquire()")
        })
        {
            var database = TwoLedgers(dialect);
            var shape = UsagePlan.Ledgers(database, Project("UsageEvent", new(limits)))[^1];
            var refusing = Project("UsageEvent", new(limits));
            string code = UsageWriter.File([shape], database, refusing, "ShopContext", []);
            string python = PythonUsageWriter.File([shape], database, refusing);
            Assert.Contains("await using var gate = await UsageGate.EnterAsync(context, tenant, cancellationToken);", code, dialect.ToString());
            Assert.Contains("await gate.CommitAsync(cancellationToken);", code);
            Assert.AreEqual(dialect == SqlDialect.Sqlite, code.Contains("UsageGate.UseWriteAheadLog(app);"), "only SQLite starts in write-ahead mode");
            Assert.AreEqual(dialect == SqlDialect.Sqlite, code.Contains("PRAGMA journal_mode=WAL"), dialect.ToString());
            Assert.Contains(aspLock, code, dialect.ToString());
            Assert.Contains(pythonLock, python, dialect.ToString());
            Assert.Contains("with _gate(session, spec, tenant):", python);
            Assert.IsFalse(code.Contains('§') || python.Contains('§'), "every token is filled");
            if (dialect != SqlDialect.SqlServer)
            {
                string rust = RustUsageWriter.File([shape], database, refusing, dialect);
                Assert.Contains(rustLock!, rust, dialect.ToString());
                Assert.Contains("record_usage_locked", rust);
                Assert.IsFalse(rust.Contains('§'), "every token is filled");
                Assert.IsFalse(RustUsageWriter.File([shape], database, Project("UsageEvent", new(lenient)), dialect).Contains("USAGE_GATE"), "no gate without UsageRefuse");
            }
            var plain = Project("UsageEvent", new(lenient));
            Assert.IsFalse(UsageWriter.File([shape], database, plain, "ShopContext", []).Contains("UsageGate"), "no gate without UsageRefuse");
            Assert.IsFalse(PythonUsageWriter.File([shape], database, plain).Contains("_gate"));
        }
    }

    [TestMethod]
    public void The_roll_up_runs_on_its_own_only_when_the_hours_are_set()
    {
        var database = Database(Tenant(), Decimals(), Monthly());
        var shape = TableShapes.For(Decimals(), Project()).Usage!;
        var plain = Project(extra: new() { ["UsageRollup"] = "UsageMonthly" });
        var timed = Project(extra: new() { ["UsageRollup"] = "UsageMonthly", ["UsageRollupHours"] = "12" });

        Assert.IsFalse(UsagePlan.HasSchedule(database, plain));
        Assert.IsTrue(UsagePlan.HasSchedule(database, timed));
        Assert.IsFalse(UsageWriter.File([shape], database, plain, "ShopContext", []).Contains("UsageRollupSchedule"));
        Assert.IsFalse(PythonUsageWriter.File([shape], database, plain).Contains("roll_up_every"));
        Assert.IsFalse(RustUsageWriter.File([shape], database, plain, SqlDialect.Sqlite).Contains("start_roll_up"));
        string code = UsageWriter.File([shape], database, timed, "ShopContext", []);
        Assert.Contains("new PeriodicTimer(TimeSpan.FromHours(12))", code);
        Assert.Contains("await UsageEventUsage.RollUpAsync(context, null, stop);", code);
        Assert.Contains("ROLLUP_EVERY_HOURS = 12", PythonUsageWriter.File([shape], database, timed));
        Assert.Contains("const ROLLUP_EVERY_HOURS: u64 = 12;", RustUsageWriter.File([shape], database, timed, SqlDialect.Sqlite));
        Assert.Contains("UsageRollupHours needs UsageRollup", UsagePlan.Problems(Database(Tenant(), Decimals()), Project(extra: new() { ["UsageRollupHours"] = "6" })).Single());
    }

    [TestMethod]
    public void A_project_that_refuses_past_the_limit_does_not_write_the_event_and_answers_402()
    {
        var database = Database(Tenant(), Decimals());
        var shape = TableShapes.For(Decimals(), Project()).Usage!;
        var limits = new Dictionary<string, string> { ["SubscriptionTable"] = "Subscription", ["PlanLimitTable"] = "PlanLimit", ["UsageLimits"] = "api_calls" };
        var refusing = Project(extra: new(limits) { ["UsageRefuse"] = "true" });
        var lenient = Project(extra: limits);

        string code = UsageWriter.File([shape], database, refusing, "ShopContext", []);
        string python = PythonUsageWriter.File([shape], database, refusing);
        string rust = RustUsageWriter.File([shape], database, refusing, SqlDialect.Sqlite);
        Assert.Contains("return new Recorded(default, had, cap, true, true);", code);
        Assert.Contains("statusCode: 402", code);
        Assert.Contains("\"refused\": True", python);
        Assert.Contains("ApiError(402, \"Plan limit reached\"", python);
        Assert.Contains("\"refused\": true", rust);
        Assert.Contains("Err(ApiError::Problem(402", rust);
        Assert.IsFalse(code.Contains('§') || python.Contains('§') || rust.Contains('§'), "every token is filled");
        Assert.IsFalse(UsageWriter.File([shape], database, lenient, "ShopContext", []).Contains("bool Refused"), "the event is always written by default");
        Assert.IsFalse(PythonUsageWriter.File([shape], database, lenient).Contains("\"refused\": True"));
        Assert.IsFalse(RustUsageWriter.File([shape], database, lenient, SqlDialect.Sqlite).Contains("\"refused\": true"));
        Assert.Contains("UsageRefuse needs UsageLimits", UsagePlan.Problems(database, Project(extra: new() { ["UsageRefuse"] = "true" })).Single());
    }

    [TestMethod]
    public void A_roll_up_totals_the_old_events_in_batches_and_the_summary_reads_both_tables()
    {
        var project = Project(extra: new() { ["UsageRollup"] = "UsageMonthly" });
        var database = Database(Tenant(), Decimals(), Monthly());
        var shape = TableShapes.For(Decimals(), project).Usage!;
        string code = UsageWriter.File([shape], database, project, "ShopContext", []);
        string python = PythonUsageWriter.File([shape], database, project);
        string rust = RustUsageWriter.File([shape], database, project, SqlDialect.Sqlite);

        Assert.Contains("public static async Task<RolledUp> RollUpAsync", code);
        Assert.Contains("Take(1000)", code);
        Assert.Contains("BeginTransactionAsync", code);
        Assert.Contains("ids.Contains(x.UsageEventId)", code, "an event is removed only when it was counted");
        Assert.Contains("new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified)", code);
        Assert.Contains("app.MapPost(\"/api/usageevents/roll-up\"", code);
        Assert.Contains("context.Set<UsageMonthly>().IgnoreQueryFilters().AsNoTracking()", code, "the summary reads the monthly totals");
        Assert.Contains("def roll_up(", python);
        Assert.Contains("/roll-up", python);
        Assert.Contains("pub async fn roll_up(", rust);
        Assert.Contains("transaction.commit()", rust);
        Assert.Contains("/roll-up", rust);
        Assert.IsFalse(code.Contains('§') || python.Contains('§') || rust.Contains('§'), "every token is filled");
        Assert.IsFalse(UsageWriter.File([shape], database, Project(), "ShopContext", []).Contains("RollUpAsync"), "no roll-up without UsageRollup");
    }

    [TestMethod]
    public void A_table_with_a_required_column_the_generator_cannot_fill_gets_no_record_route()
    {
        var withParent = Sample.Table("UsageEvent",
            new List<ColumnModel>
            {
                C("UsageEventId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: 40),
                C("OccurredOn", SqlDbType.DateTime, ordinal: 4), C("ProductId", SqlDbType.Int, ordinal: 5)
            },
            [TenantKey(), new ForeignKeyModel { ConstraintName = "FK_p", ReferencingColumns = ["ProductId"], ReferencedSchema = "dbo", ReferencedTable = "Product", ReferencedColumns = ["ProductId"] }]);
        var shape = TableShapes.For(withParent, Project()).Usage!;
        string code = UsageWriter.File([shape], Database(Tenant(), withParent), Project(), "ShopContext", []);

        Assert.IsFalse(UsageWriter.HasRecordRoute(shape));
        Assert.IsFalse(code.Contains("MapPost"), "the recorder stays, the route is not written");
        Assert.Contains("RecordAsync", code);
        Assert.IsNull(UsagePlan.FillColumns(shape));
    }
}
