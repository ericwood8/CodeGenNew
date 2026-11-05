using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> SubscriptionTable, PlanTable, PlanLimitTable, PlanLimits, SubscriptionActiveStatuses and SubscriptionRequired: plans, subscriptions and entitlements on top of the tenants. </summary>
[TestClass]
public class SubscriptionFeatureTests
{
    private static readonly string[] PlanTables =
    [
        "CREATE TABLE Plan (PlanId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(40) NOT NULL, MonthlyPrice DECIMAL(10,2) NOT NULL DEFAULT 0, BillingPeriod VARCHAR(20) NOT NULL DEFAULT 'month')",
        "CREATE TABLE PlanLimit (PlanLimitId INTEGER PRIMARY KEY AUTOINCREMENT, PlanId INTEGER NOT NULL REFERENCES Plan (PlanId), LimitKey VARCHAR(40) NOT NULL, LimitValue INTEGER NULL)",
        "CREATE TABLE Subscription (SubscriptionId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), PlanId INTEGER NOT NULL REFERENCES Plan (PlanId), Status VARCHAR(20) NOT NULL, CurrentPeriodStart DATETIME NULL, CurrentPeriodEnd DATETIME NULL, GatewaySubscriptionId VARCHAR(80) NULL)"
    ];

    private static Dictionary<string, string> Values(Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string>
        {
            ["SubscriptionTable"] = "Subscription", ["PlanTable"] = "Plan", ["PlanLimitTable"] = "PlanLimit", ["PlanLimits"] = "Project:projects,EmployeeTenant:seats"
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return TenantPlanTests.Values(values);
    }

    private static async Task<DatabaseModel> ReadAsync()
    {
        string database = TenantPlanTests.CreateDatabase(PlanTables);
        try
        {
            var request = new CodeGenNew.Connections.ConnectionRequest { Provider = CodeGenNew.Connections.DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = CodeGenNew.SchemaIntrospection.SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            return await provider.BuildAsync(database, "main");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    [TestMethod]
    public async Task The_three_tables_are_read_from_the_schema_and_the_limited_tables_are_the_tenant_rows_and_the_seats()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values());

        Assert.IsEmpty(SubscriptionFeature.Problems(database, project));
        var subscription = SubscriptionFeature.SubscriptionOf(database, project)!;
        Assert.AreEqual("PlanId", subscription.Plan.Name);
        Assert.AreEqual("Status", subscription.Status.Name);
        Assert.AreEqual("CurrentPeriodEnd", subscription.PeriodEnd!.Name);
        Assert.AreEqual("CurrentPeriodStart", subscription.PeriodStart!.Name);
        Assert.AreEqual("GatewaySubscriptionId", subscription.Gateway!.Name);
        Assert.AreEqual("Name", subscription.PlanName!.Name);
        var limit = SubscriptionFeature.LimitTableOf(database, project)!;
        Assert.AreEqual("LimitKey", limit.Key.Name);
        Assert.AreEqual("LimitValue", limit.Value.Name);
        var limited = SubscriptionFeature.LimitedTables(database, project);
        CollectionAssert.AreEqual(new[] { "Project", "EmployeeTenant" }, limited.Select(l => l.Table.TableName).ToArray());
        Assert.IsFalse(limited[0].IsMembership);
        Assert.AreEqual("IsDeleted", limited[0].Deleted!.Name, "a soft-deleted project is not counted");
        Assert.IsTrue(limited[1].IsMembership);
        Assert.AreEqual("/api/auth/members", limited[1].CreatePath);
        Assert.AreEqual("Project", SubscriptionFeature.TestWriteTable(database, project)!.Table.TableName, "the first limited table whose only foreign key is the tenant");
        Assert.AreEqual("Project", SubscriptionFeature.TestWriteTable(database, ProjectSettings.FromValues(Values(new() { ["PlanLimits"] = "ProjectTask:tasks" })))!.Table.TableName, "a limited table with another foreign key is not used; the next simple one is");
    }

    [TestMethod]
    public async Task The_plan_tables_get_no_ordinary_api()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values());

        foreach (string name in new[] { "Plan", "PlanLimit", "Subscription" })
        {
            var table = database.Tables.Single(t => t.TableName == name);
            Assert.IsTrue(project.IsSubscriptionManaged(name), name);
            Assert.IsFalse(table.HasCrudApi(project), name + " would let a tenant's administrator change a plan");
            Assert.IsTrue(project.NoApiFor(name, table.LookupShape), name);
        }
        Assert.IsTrue(database.Tables.Single(t => t.TableName == "Project").HasCrudApi(project));
        Assert.IsTrue(database.Tables.Single(t => t.TableName == "Plan").HasCrudApi(ProjectSettings.FromValues(TenantPlanTests.Values())), "without the settings it is an ordinary table");
    }

    [TestMethod]
    public async Task A_setting_that_cannot_be_served_is_named_with_the_reason()
    {
        var database = await ReadAsync();
        string Problems(Dictionary<string, string> extra) => string.Join(" | ", SubscriptionFeature.Problems(database, ProjectSettings.FromValues(Values(extra))));

        Assert.Contains("need SubscriptionTable", Problems(new() { ["SubscriptionTable"] = "" }));
        Assert.Contains("needs TenantTable and ApiAuth", Problems(new() { ["ApiAuth"] = "false" }));
        Assert.Contains("Nope is not a table", Problems(new() { ["SubscriptionTable"] = "Nope" }));
        Assert.Contains("must be a tenant-owned table", Problems(new() { ["SubscriptionTable"] = "Plan" }));
        Assert.Contains("PlanTable must name the table of plans", Problems(new() { ["PlanTable"] = "" }));
        Assert.Contains("must be a foreign key to Tenant", Problems(new() { ["PlanTable"] = "Tenant" }), "the plan column points at the plan table, not another table");
        Assert.Contains("needs a NOT NULL text status column (Missing)", Problems(new() { ["SubscriptionTable"] = "Subscription:PlanId:Missing" }));
        Assert.Contains("the period end column Missing is not a column", Problems(new() { ["SubscriptionTable"] = "Subscription:PlanId:Status:Missing" }));
        Assert.Contains("PlanLimits needs PlanLimitTable", Problems(new() { ["PlanLimitTable"] = "", ["PlanLimits"] = "Project:projects" }));
        Assert.Contains("PlanLimits: Country must be a tenant-owned table", Problems(new() { ["PlanLimits"] = "Country:countries" }), "Country is shared by every tenant");
        Assert.Contains("PlanLimits: Project needs the limit's name", Problems(new() { ["PlanLimits"] = "Project" }));
        Assert.Contains("the limit name projects is used twice", Problems(new() { ["PlanLimits"] = "Project:projects,ProjectTask:projects" }));
        Assert.Contains("PlanLimits: Nope is not a table", Problems(new() { ["PlanLimits"] = "Nope:things" }));
        Assert.Contains("PlanLimitTable: PlanLimit needs a text column that names the limit (Missing)", Problems(new() { ["PlanLimitTable"] = "PlanLimit:PlanId:Missing" }));
        Assert.IsEmpty(SubscriptionFeature.Problems(database, ProjectSettings.FromValues(TenantPlanTests.Values())), "no setting, no problem");
    }

    [TestMethod]
    public async Task The_subscription_table_cannot_have_a_required_column_the_platform_route_does_not_fill()
    {
        string database = TenantPlanTests.CreateDatabase(
        [
            PlanTables[0], PlanTables[1],
            "CREATE TABLE Subscription (SubscriptionId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), PlanId INTEGER NOT NULL REFERENCES Plan (PlanId), Status VARCHAR(20) NOT NULL, SeatsBought INTEGER NOT NULL)"
        ]);
        try
        {
            var request = new CodeGenNew.Connections.ConnectionRequest { Provider = CodeGenNew.Connections.DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var model = await CodeGenNew.SchemaIntrospection.SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config")).BuildAsync(database, "main");
            Assert.Contains("SeatsBought cannot be NULL and has no default", string.Join(" | ", SubscriptionFeature.Problems(model, ProjectSettings.FromValues(Values()))));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    [TestMethod]
    public void The_statuses_a_tenant_may_write_in_default_to_trialing_and_active()
    {
        CollectionAssert.AreEqual(new[] { "trialing", "active" }, SubscriptionFeature.ActiveStatuses(ProjectSettings.FromValues(Values())));
        CollectionAssert.AreEqual(new[] { "paid", "grace" }, SubscriptionFeature.ActiveStatuses(ProjectSettings.FromValues(Values(new() { ["SubscriptionActiveStatuses"] = "paid,grace" }))));
        Assert.IsFalse(ProjectSettings.FromValues(Values()).SubscriptionRequired);
        Assert.IsTrue(ProjectSettings.FromValues(Values(new() { ["SubscriptionRequired"] = "true" })).SubscriptionRequired);
    }

    [TestMethod]
    public void The_plan_adds_the_subscription_templates_only_with_a_tenant_and_a_sign_in()
    {
        var project = ProjectSettings.FromValues(Values());
        CollectionAssert.IsSubsetOf(new[] { "API_Subscriptions", "API_TestSubscriptions" }, project.ImpliedPlanTemplates.ToList());
        Assert.DoesNotContain("API_Subscriptions", ProjectSettings.FromValues(TenantPlanTests.Values()).ImpliedPlanTemplates.ToList());
        Assert.DoesNotContain("API_TestSubscriptions", ProjectSettings.FromValues(Values(new() { ["ApiTests"] = "false" })).ImpliedPlanTemplates.ToList());
    }

    [TestMethod]
    public async Task The_generated_code_pauses_writes_counts_the_limited_tables_and_registers_its_routes()
    {
        var (success, errors, read, files) = await TenantPlanTests.GenerateAsync(Values(), more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("Apis/Subscriptions.cs");
        Expect.Contains(code, "public static class SubscriptionCheck");
        Expect.Contains(code, "app.Use(SubscriptionCheck.InvokeAsync);");
        Expect.Contains(code, "app.MapGet(\"/api/subscription\", SubscriptionEndpoints.GetAsync);");
        Expect.Contains(code, "app.MapPut(\"/api/subscription/{tenantId:int}\", SubscriptionEndpoints.PutAsync);");
        Expect.Contains(code, "ActiveStatuses = { \"trialing\", \"active\" }");
        Expect.Contains(code, "public const bool Required = false;");
        Expect.Contains(code, "Keys = { \"projects\", \"seats\" }");
        Expect.Contains(code, "if (IsCreate(path, \"/api/projects\"))");
        Expect.Contains(code, "if (path.Equals(\"/api/auth/members\", StringComparison.OrdinalIgnoreCase))");
        Expect.Contains(code, "\"projects\" => context.Set<Project>().IgnoreQueryFilters().AsNoTracking().CountAsync(r => r.TenantId == tenant && !r.IsDeleted, cancellationToken)");
        Expect.Contains(code, "\"seats\" => context.Set<EmployeeTenant>().IgnoreQueryFilters().AsNoTracking().CountAsync(r => r.TenantId == tenant, cancellationToken)");
        Expect.Contains(code, "statusCode: 402");
        Expect.Contains(code, "public static class SubscriptionGateway");
        Expect.Contains(code, "row.CurrentPeriodEnd = SubscriptionState.Stored(end);");
        Expect.Contains(code, "row = new Subscription { TenantId = tenantId, PlanId = plan, Status = request.Status.Trim(), CurrentPeriodStart = SubscriptionState.Stored(DateTime.UtcNow) };");
        Expect.Contains(read("Apis/ApiRegistration.cs"), "SubscriptionRoutes.Register(app);");
        foreach (string table in new[] { "Plan", "PlanLimit", "Subscription" })
            Assert.IsFalse(files.Any(f => f.EndsWith($"Apis/{table}Api.cs")), table + " has an ordinary API");
        Expect.Contains(read("SubscriptionApiTests.cs"), "public class SubscriptionApiTests");
        Expect.Contains(read("SubscriptionApiTests.cs"), "A_create_past_the_plan_limit_is_refused_until_the_limit_is_raised");
        Expect.Contains(read("SubscriptionApiTests.cs"), "A_trial_ends_at_its_period_end");
    }

    [TestMethod]
    public async Task An_import_counts_every_row_against_the_plan_limit()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Values(new Dictionary<string, string> { ["ApiCsv"] = "true" }), more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Apis/Subscriptions.cs"), "public static async Task<IResult?> ImportRefusalAsync(HttpContext http, int rows)");
        Expect.Contains(read("Apis/Subscriptions.cs"), "return used + rows > allowed ?");
        Expect.Contains(read("Apis/Csv.cs"), "SubscriptionLimits.ImportRefusalAsync(request.HttpContext, import.Rows.Count)");
    }

    [TestMethod]
    public async Task A_renewal_route_is_always_written_and_the_plan_choice_only_when_the_setting_is_on()
    {
        var off = await TenantPlanTests.GenerateAsync(Values(), more: PlanTables);
        Assert.IsTrue(off.Success, string.Join(" | ", off.Errors));
        string without = off.Read("Apis/Subscriptions.cs");
        Expect.Contains(without, "app.MapPost(\"/api/subscription/{tenantId:int}/renew\", SubscriptionEndpoints.RenewAsync);");
        Expect.Contains(without, "int days = request?.Days ?? 30;");
        Expect.DoesNotContain(without, "ChooseAsync");
        Expect.Contains(without, "List<PlanInfo> Plans, bool CanChoose)");

        var on = await TenantPlanTests.GenerateAsync(Values(new Dictionary<string, string> { ["SubscriptionPlanChoice"] = "true", ["SubscriptionPeriodDays"] = "14" }), more: PlanTables);
        Assert.IsTrue(on.Success, string.Join(" | ", on.Errors));
        string with = on.Read("Apis/Subscriptions.cs");
        Expect.Contains(with, "app.MapPut(\"/api/subscription/plan\", SubscriptionEndpoints.ChooseAsync);");
        Expect.Contains(with, "int days = request?.Days ?? 14;");
        Expect.Contains(with, "return Results.Problem(statusCode: 409");
        Expect.Contains(on.Read("SubscriptionApiTests.cs"), "An_administrator_chooses_a_plan_from_the_list_unless_it_allows_less_than_the_tenant_has");
        Expect.Contains(on.Read("SubscriptionApiTests.cs"), "A_renewal_adds_a_period_that_starts_when_the_old_one_ends");

        var python = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["SubscriptionPlanChoice"] = "true" }), stack: "python", more: PlanTables);
        Assert.IsTrue(python.Success, string.Join(" | ", python.Errors));
        Expect.Contains(python.Read("subscriptions.py"), "@router.put(\"/api/subscription/plan\")");
        Expect.Contains(python.Read("subscriptions.py"), "@router.post(\"/api/subscription/{tenant_id}/renew\")");
        Assert.IsTrue(python.Read("subscriptions.py").IndexOf("/api/subscription/plan", StringComparison.Ordinal) < python.Read("subscriptions.py").IndexOf("@router.put(\"/api/subscription/{tenant_id}\")", StringComparison.Ordinal), "the plan route comes before the one that reads a tenant key");

        var rust = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["SubscriptionPlanChoice"] = "true" }), stack: "rust", more: PlanTables);
        Assert.IsTrue(rust.Success, string.Join(" | ", rust.Errors));
        Expect.Contains(rust.Read("subscriptions.rs"), ".route(\"/api/subscription/plan\", put(choose_plan))");
        Expect.Contains(rust.Read("subscriptions.rs"), "axum::routing::post(renew_subscription)");
    }

    [TestMethod]
    public async Task A_status_held_in_a_look_up_table_is_read_and_written_by_the_aspnet_api_and_refused_by_the_other_stacks()
    {
        string[] more =
        [
            PlanTables[0], PlanTables[1],
            "CREATE TABLE SubscriptionStatus (SubscriptionStatusId INTEGER PRIMARY KEY, Name VARCHAR(20) NOT NULL)",
            "CREATE TABLE Subscription (SubscriptionId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), PlanId INTEGER NOT NULL REFERENCES Plan (PlanId), StatusId INTEGER NOT NULL REFERENCES SubscriptionStatus (SubscriptionStatusId), CurrentPeriodStart DATETIME NULL, CurrentPeriodEnd DATETIME NULL, GatewaySubscriptionId VARCHAR(80) NULL)"
        ];
        var api = await TenantPlanTests.GenerateAsync(Values(), more: more);
        Assert.IsTrue(api.Success, string.Join(" | ", api.Errors));
        string code = api.Read("Apis/Subscriptions.cs");
        Expect.Contains(code, "Status = context.Set<SubscriptionStatus>().IgnoreQueryFilters().Where(l => l.SubscriptionStatusId == s.StatusId).Select(l => l.Name).FirstOrDefault() ?? \"\"");
        Expect.Contains(code, "public static async Task<int?> StatusIdAsync(");
        Expect.Contains(code, "row = new Subscription { TenantId = tenantId, PlanId = plan, StatusId = statusId!.Value");
        Expect.Contains(code, "row.StatusId = statusId!.Value;");
        Expect.Contains(code, "That status is not in the look-up table of statuses.");

        foreach (string stack in new[] { "python", "rust" })
        {
            var other = await TenantPlanTests.GenerateAsync(WithSignIn(), stack: stack, more: more);
            Assert.IsFalse(other.Success, stack + " served a look-up status");
            StringAssert.Contains(string.Join(" | ", other.Errors), "look-up table SubscriptionStatus");
        }
    }

    [TestMethod]
    public async Task An_import_is_not_counted_without_a_plan_limit()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Values(new Dictionary<string, string> { ["ApiCsv"] = "true", ["PlanLimits"] = "" }), more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.IsFalse(read("Apis/Csv.cs").Contains("ImportRefusalAsync"));
    }

    [TestMethod]
    public async Task A_required_subscription_and_other_statuses_change_the_generated_rule()
    {
        var (success, errors, read, files) = await TenantPlanTests.GenerateAsync(Values(new() { ["SubscriptionRequired"] = "true", ["SubscriptionActiveStatuses"] = "paid,Grace", ["PlanLimits"] = "ProjectTask:tasks" }), more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("Apis/Subscriptions.cs");
        Expect.Contains(code, "ActiveStatuses = { \"paid\", \"grace\" }");
        Expect.Contains(code, "public const bool Required = true;");
        Expect.DoesNotContain(code, "\"seats\"");
        Expect.Contains(read("SubscriptionApiTests.cs"), "A_tenant_with_no_subscription_cannot_write_when_a_subscription_is_required");
        Expect.DoesNotContain(read("SubscriptionApiTests.cs"), "A_trial_ends_at_its_period_end");
    }

    [TestMethod]
    public async Task A_project_without_a_limit_table_still_checks_the_status()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(Values(new() { ["PlanLimitTable"] = "", ["PlanLimits"] = "" }), more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("Apis/Subscriptions.cs");
        Expect.Contains(code, "public static readonly string[] Keys = { };");
        Expect.DoesNotContain(code, "SetLimitAsync");
        Expect.Contains(code, "public static class SubscriptionCheck");
    }

    [TestMethod]
    public async Task A_setting_that_cannot_be_served_stops_the_template_with_the_reason()
    {
        var (success, errors, _, _) = await TenantPlanTests.GenerateAsync(Values(new() { ["PlanLimits"] = "Country:countries" }), more: PlanTables);

        Assert.IsFalse(success);
        Assert.Contains("PlanLimits: Country must be a tenant-owned table", string.Join(" | ", errors));
    }

    private static Dictionary<string, string> WithSignIn(Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string> { ["Auth"] = "true", ["BlazorTests"] = "true" };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return Values(values);
    }

    [TestMethod]
    public void The_plan_adds_the_pages_and_the_other_apis_only_with_a_sign_in()
    {
        var all = ProjectSettings.FromValues(WithSignIn()).ImpliedPlanTemplates.ToList();
        CollectionAssert.IsSubsetOf(new[] { "TSX_Subscription", "TS_Subscription", "BLZ_Subscription", "BLZ_SubscriptionTest", "UNO_Subscription", "PY_Subscriptions", "RS_Subscriptions" }, all);
        Assert.DoesNotContain("TSX_Subscription", ProjectSettings.FromValues(Values()).ImpliedPlanTemplates.ToList(), "the pages need Auth");
        Assert.DoesNotContain("PY_Subscriptions", ProjectSettings.FromValues(TenantPlanTests.Values()).ImpliedPlanTemplates.ToList());
    }

    [TestMethod]
    public async Task The_react_app_gets_the_subscription_page_and_shows_the_sentence_of_a_402()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(), stack: "react", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("SubscriptionPage.tsx"), "export function SubscriptionPage()");
        Expect.Contains(read("subscription.ts"), "request<SubscriptionInfo>('/api/subscription')");
        Expect.Contains(read("screens.tsx"), "{ path: 'subscription', label: 'Subscription', element: <SubscriptionPage />, roles: ['Administrator'] }");
        Expect.Contains(read("ProjectDetailMasterPage.tsx"), "const planDetail = (error: unknown): string =>");
        Expect.Contains(read("ProjectDetailMasterPage.tsx"), "else if (err instanceof ApiError && err.status === 402) { setError(planDetail(err)); }");
        Expect.Contains(read("MembersPage.tsx"), "error.status === 402");
    }

    [TestMethod]
    public async Task The_angular_app_gets_the_subscription_screen()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(), stack: "angular", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("subscription.service.ts"), "this.http.get<SubscriptionInfo>('api/subscription')");
        Expect.Contains(read("subscription.component.ts"), "export class SubscriptionComponent");
        Expect.Contains(read("app.routes.ts"), "path: 'subscription'");
        Expect.Contains(read("members.component.ts"), "error.status === 402");
    }

    [TestMethod]
    public async Task The_blazor_app_gets_the_subscription_page_and_reads_the_detail_of_a_402()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(), stack: "blazor", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Subscription.razor"), "@page \"/subscription\"");
        Expect.Contains(read("SubscriptionTests.cs"), "Says_why_changes_are_paused");
        Expect.Contains(read("ApiSupport.cs"), "402 => Message,");
        Expect.Contains(read("ApiSupport.cs"), "ProblemDetailAsync");
        Expect.Contains(read("Members.razor"), "ApiSupport.ProblemDetailAsync(response)");
    }

    [TestMethod]
    public async Task The_uno_app_gets_the_subscription_page()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(), stack: "uno", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("SubscriptionPage.xaml.cs"), "GetFromJsonAsync<SubscriptionInfo>(\"/api/subscription\")");
        Expect.Contains(read("SubscriptionPage.xaml"), "x:DataType=\"rows:LimitRow\"");
        Expect.Contains(read("MainPage.Screens.cs"), "\"Subscription\" => new SubscriptionPage(_api)");
        Expect.Contains(read("ApiSupport.cs"), "402 => Message,");
        Expect.Contains(read("MembersPage.xaml.cs"), "ApiSupport.ProblemDetailAsync(response)");
    }

    [TestMethod]
    public async Task The_python_api_pauses_writes_in_require_user_and_serves_the_subscription_routes()
    {
        var (success, errors, read, files) = await TenantPlanTests.GenerateAsync(WithSignIn(), stack: "python", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("subscriptions.py");
        Expect.Contains(code, "ACTIVE_STATUSES = (\"trialing\", \"active\",)");
        Expect.Contains(code, "@router.get(\"/api/subscription\")");
        Expect.Contains(code, "@router.put(\"/api/subscription/{tenant_id}\")");
        Expect.Contains(code, "(\"/api/projects\", \"projects\", False)");
        Expect.Contains(code, "(\"/api/auth/members\", \"seats\", True)");
        Expect.Contains(read("app/auth.py"), "check_write(request, tenant)");
        Expect.Contains(read("routes/__init__.py"), "subscriptions");
        foreach (string table in new[] { "plan", "plan_limit", "subscription" })
            Assert.IsFalse(files.Any(f => f.EndsWith($"routes/{table}.py")), table + " has an ordinary router");
    }

    [TestMethod]
    public async Task The_rust_api_pauses_writes_in_require_user_and_serves_the_subscription_routes()
    {
        var (success, errors, read, files) = await TenantPlanTests.GenerateAsync(WithSignIn(), stack: "rust", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("subscriptions.rs");
        Expect.Contains(code, "const ACTIVE_STATUSES: &[&str] = &[\"trialing\", \"active\"];");
        Expect.Contains(code, "route(\"/api/subscription/{tenant_id}\", put(put_subscription))");
        Expect.Contains(code, "pub const LIMIT_KEYS: &[&str] = &[\"projects\", \"seats\"];");
        Expect.Contains(read("src/auth.rs"), "crate::subscriptions::check_write(&pool, request.method(), request.uri().path(), tenant).await?;");
        Expect.Contains(read("routes/mod.rs"), ".merge(crate::subscriptions::router())");
        Assert.IsFalse(files.Any(f => f.EndsWith("routes/plan.rs")), "Plan has an ordinary router");
    }

    [TestMethod]
    public async Task The_winui3_app_checks_the_subscription_before_a_save_and_before_adding_a_member()
    {
        var (success, errors, read, files) = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["Stacks"] = "winui3" }), stack: "winui3", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string guard = read("SubscriptionGuard.cs");
        Expect.Contains(guard, "public static class SubscriptionGuard");
        Expect.Contains(guard, "\"projects\" => context.Set<Project>().AsNoTracking().CountAsync(r => r.TenantId == tenant && !r.IsDeleted)");
        Expect.Contains(read("ProjectDetailMasterViewModel.cs"), "SubscriptionGuard.RefusalAsync(_context, \"projects\", _editing is null)");
        Expect.Contains(read("MembersPage.xaml.cs"), "SubscriptionGuard.RefusalAsync(_context, \"seats\", true)");
        Expect.Contains(read("MainWindow.Screens.cs"), "\"Subscription\" => new SubscriptionPage(_context)");
        Expect.Contains(read("SubscriptionPage.xaml"), "x:DataType=\"rows:LimitRow\"");
    }

    private const string Secret = "GATEWAY_WEBHOOK_SECRET";

    [TestMethod]
    public async Task A_webhook_secret_needs_a_gateway_column()
    {
        var database = await ReadAsync();
        Assert.IsEmpty(SubscriptionFeature.Problems(database, ProjectSettings.FromValues(Values(new() { ["SubscriptionWebhookSecret"] = Secret }))));
        Assert.IsTrue(SubscriptionFeature.HasWebhook(database, ProjectSettings.FromValues(Values(new() { ["SubscriptionWebhookSecret"] = Secret }))));
        Assert.IsFalse(SubscriptionFeature.HasWebhook(database, ProjectSettings.FromValues(Values())), "no secret named, no webhook");
        string[] noGateway = [PlanTables[0], PlanTables[1], PlanTables[2].Replace(", GatewaySubscriptionId VARCHAR(80) NULL", "")];
        string path = TenantPlanTests.CreateDatabase(noGateway);
        try
        {
            var request = new CodeGenNew.Connections.ConnectionRequest { Provider = CodeGenNew.Connections.DatabaseProvider.Sqlite, ServerName = "", DatabaseName = path };
            var provider = CodeGenNew.SchemaIntrospection.SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var bare = await provider.BuildAsync(path, "main");
            var problems = SubscriptionFeature.Problems(bare, ProjectSettings.FromValues(Values(new() { ["SubscriptionWebhookSecret"] = Secret })));
            Expect.Contains(string.Join(" | ", problems), "SubscriptionWebhookSecret needs a gateway id column");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task The_aspnet_api_serves_the_plan_list_and_a_signed_open_webhook()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["SubscriptionWebhookSecret"] = Secret, ["ApiTests"] = "true" }), stack: "api", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("Subscriptions.cs");
        Expect.Contains(code, "public static async Task<List<PlanInfo>> PlansAsync(");
        Expect.Contains(code, "(decimal?)p.MonthlyPrice");
        Expect.Contains(code, "app.MapPost(\"/api/subscription/webhook\", SubscriptionWebhook.PostAsync).AllowAnonymous();");
        Expect.Contains(code, "configuration[\"GATEWAY_WEBHOOK_SECRET\"]");
        Expect.Contains(code, "CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature))");
        Expect.Contains(read("SubscriptionApiTests.cs"), "The_webhook_sets_the_status_of_the_subscription_the_gateway_names_and_refuses_what_is_not_signed");
        Expect.Contains(read("SubscriptionApiTests.cs"), "The_view_lists_every_plan_with_what_it_allows_and_marks_the_tenants_own");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Each_stack_writes_the_scenarios_the_list_gives_it_and_no_others(bool required)
    {
        var extra = new Dictionary<string, string> { ["SubscriptionWebhookSecret"] = Secret, ["ApiTests"] = "true", ["PythonTests"] = "true", ["RustTests"] = "true", ["SubscriptionRequired"] = required ? "true" : "false", ["SubscriptionPlanChoice"] = "true" };
        var project = ProjectSettings.FromValues(WithSignIn(extra));
        var scenarios = SubscriptionScenario.For(project);
        var files = new Dictionary<string, (string Stack, string File, Func<SubscriptionScenario, string> Name)>
        {
            ["api"] = (SubscriptionScenario.Api, "SubscriptionApiTests.cs", s => s.CSharpName + "()"),
            ["python"] = (SubscriptionScenario.Python, "tests/test_subscriptions.py", s => "def " + s.PythonName + "("),
            ["rust"] = (SubscriptionScenario.Rust, "subscription_routes.rs", s => "async fn " + s.SnakeName + "()")
        };

        foreach (var (stack, (name, file, named)) in files)
        {
            var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(extra), stack: stack, more: PlanTables);
            Assert.IsTrue(success, string.Join(" | ", errors));
            string text = read(file);
            string marker = stack == "api" ? "[TestMethod]" : stack == "python" ? "\ndef test_" : "#[tokio::test]";
            int written = text.Split(marker).Length - 1;
            Assert.AreEqual(scenarios.Count(s => s.Stacks.Contains(name)), written, $"{name}: a test is written that the scenario list does not name");
            foreach (var scenario in scenarios)
            {
                bool served = scenario.Stacks.Contains(name);
                Assert.AreEqual(served, text.Contains(named(scenario), StringComparison.Ordinal), $"{name}: {scenario.Key} should {(served ? "" : "not ")}be written ({named(scenario)})");
            }
        }
    }

    [TestMethod]
    public async Task The_aspnet_api_has_no_webhook_without_a_secret()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["ApiTests"] = "true" }), stack: "api", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.DoesNotContain("SubscriptionWebhook", read("Subscriptions.cs"));
        Assert.DoesNotContain("webhook", read("SubscriptionApiTests.cs"));
    }

    [TestMethod]
    public async Task The_python_api_serves_the_webhook_outside_the_sign_in_and_has_generated_tests()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["SubscriptionWebhookSecret"] = Secret, ["PythonTests"] = "true" }), stack: "python", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("routes/subscriptions.py");
        Expect.Contains(code, "@public_router.post(\"/api/subscription/webhook\")");
        Expect.Contains(code, "def plans_of(session: Session, current: int | None)");
        Expect.Contains(read("app/main.py"), "app.include_router(subscription_public_router)");
        string tests = read("tests/test_subscriptions.py");
        Expect.Contains(tests, "def test_writes_are_paused_while_the_subscription_is_not_active_and_reads_go_on(world, tenant):");
        Expect.Contains(tests, "def test_a_create_past_the_plan_limit_is_refused_until_the_limit_is_raised(world, tenant):");
        Expect.Contains(tests, "def test_the_webhook_sets_the_status_of_the_subscription_the_gateway_names_and_refuses_what_is_not_signed(");
        Expect.Contains(tests, "monkeypatch.setenv(\"GATEWAY_WEBHOOK_SECRET\", secret)");
        Expect.Contains(tests, "from test_tenants import");
    }

    [TestMethod]
    public async Task The_rust_api_serves_the_webhook_outside_the_sign_in_and_has_generated_tests()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["SubscriptionWebhookSecret"] = Secret, ["RustTests"] = "true" }), stack: "rust", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string code = read("subscriptions.rs");
        Expect.Contains(code, ".route(\"/api/subscription/webhook\", axum::routing::post(webhook))");
        Expect.Contains(code, "async fn plans_of(pool: &Pool, current: Option<i32>)");
        Expect.Contains(code, "fn parse_end(text: &str) -> Option<String>");
        Expect.Contains(read("src/lib.rs"), "subscriptions::public_router()");
        string tests = read("subscription_routes.rs");
        Expect.Contains(tests, "async fn writes_are_paused_while_the_subscription_is_not_active_and_reads_go_on()");
        Expect.Contains(tests, "async fn the_webhook_sets_the_status_of_the_subscription_the_gateway_names_and_refuses_what_is_not_signed()");
        Expect.Contains(read("tests/api/main.rs"), "mod subscription_routes;");
        Expect.Contains(read("test_support.rs"), "pub fn with_header(&self, name: &str, value: &str) -> Client");
    }

    [TestMethod]
    public async Task The_winui3_app_checks_deletes_and_grid_rows_and_has_a_generated_guard_test()
    {
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["Stacks"] = "winui3", ["DesktopTests"] = "true" }), stack: "winui3", more: PlanTables);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tests = read("SubscriptionGuardTests.cs");
        Expect.Contains(tests, "public class SubscriptionGuardTests");
        Expect.Contains(tests, "A_new_row_past_the_plan_limit_is_refused_until_the_limit_is_raised");
        Expect.Contains(tests, "SubscriptionGuard.RefusalAsync(context, \"projects\", true, 2)");
        Expect.Contains(read("TestDb.cs"), "public SqliteConnection Connection => _connection;");
        Expect.Contains(read("SubscriptionGuard.cs"), "public static async Task<List<PlanRow>> PlansAsync(");
        Expect.Contains(read("SubscriptionGuard.cs"), "int adding = 1");
        Expect.Contains(read("SubscriptionPage.xaml"), "x:DataType=\"rows:PlanRow\"");
    }

    [TestMethod]
    public async Task The_winui3_app_checks_a_junction_editor_the_approval_service_and_a_table_with_no_tenant()
    {
        string[] more =
        [
            .. PlanTables,
            "CREATE TABLE Tag (TagId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(40) NOT NULL)",
            "CREATE TABLE ProjectTag (ProjectId INTEGER NOT NULL REFERENCES Project (ProjectId), TagId INTEGER NOT NULL REFERENCES Tag (TagId), PRIMARY KEY (ProjectId, TagId))",
            "CREATE TABLE ProjectApproval (ProjectApprovalId INTEGER PRIMARY KEY, ProjectId INTEGER NOT NULL REFERENCES Project (ProjectId), ApprovalStatusId INTEGER NOT NULL, Reason VARCHAR(500) NULL, RequestedBy VARCHAR(50) NOT NULL, RequestedOn DATETIME NOT NULL, DecidedBy VARCHAR(50) NULL, DecidedOn DATETIME NULL, DecisionNote VARCHAR(500) NULL, Revision INTEGER NOT NULL)"
        ];
        var (success, errors, read, _) = await TenantPlanTests.GenerateAsync(WithSignIn(new() { ["Stacks"] = "winui3", ["RowApprovals"] = "Project" }), stack: "winui3", more: more);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string junction = read("ProjectTagJunctionEditorViewModel.cs");
        Expect.Contains(junction, "if (await SubscriptionGuard.RefusalAsync(_context, null, false) is { } paused)");
        Expect.Contains(junction, "ErrorMessage = paused;");
        Expect.Contains(read("ProjectTagJunctionEditor.xaml"), "ViewModel.ErrorMessage");
        string service = read("ApprovalService.cs");
        Expect.Contains(service, "throw new ApprovalRefused(402, paused);");
        Expect.Contains(service, "public async Task DecideAsync(string table, int id, bool approve, string? note)");
        Expect.Contains(read("CountryDetailViewModel.cs"), "SubscriptionGuard.RefusalAsync(_context, null, _editing is null)");
    }

    [TestMethod]
    public async Task A_table_named_like_a_system_type_gets_a_global_using_alias()
    {
        var (success, errors, read, files) = await TenantPlanTests.GenerateAsync(null, stack: "api", more: [.. PlanTables, "CREATE TABLE Attribute (AttributeId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(40) NOT NULL)"]);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("EntityAliases.cs"), "global using Attribute = ");
        Expect.Contains(read("EntityAliases.cs"), ".Entities.Attribute;");
        Assert.IsFalse(read("EntityAliases.cs").Contains("= ") && read("EntityAliases.cs").Contains("global using Plan ="), "only the clashing names are aliased");
    }
}
