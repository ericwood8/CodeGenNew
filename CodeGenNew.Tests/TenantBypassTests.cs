namespace CodeGenNew.Tests;

/// <summary> What a multi-tenant API (TenantTable) does where the query filter of the context does not reach: the dashboard's raw statements, a junction's link rows, the approvals' side tables, a unique index
/// that leaves the tenant out, and the tenant of a host name. </summary>
[TestClass]
public class TenantBypassTests
{
    private static readonly string[] More =
    [
        "CREATE TABLE Tag (TagId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Name VARCHAR(40) NOT NULL, Colour VARCHAR(20) NULL, Notes VARCHAR(100) NULL, Rank INTEGER NULL)",
        "CREATE UNIQUE INDEX UX_Tag_Name ON Tag (Name)",
        "CREATE TABLE ProjectTag (ProjectId INTEGER NOT NULL REFERENCES Project (ProjectId), TagId INTEGER NOT NULL REFERENCES Tag (TagId), PRIMARY KEY (ProjectId, TagId))",
        "CREATE TABLE ProjectApproval (ProjectApprovalId INTEGER PRIMARY KEY AUTOINCREMENT, ProjectId INTEGER NOT NULL REFERENCES Project (ProjectId), ApprovalStatusId INTEGER NOT NULL DEFAULT 1, Reason VARCHAR(500) NULL, "
            + "RequestedBy VARCHAR(50) NOT NULL, RequestedOn DATETIME NOT NULL, DecidedBy VARCHAR(50) NULL, DecidedOn DATETIME NULL, DecisionNote VARCHAR(500) NULL, Revision INTEGER NOT NULL DEFAULT 1)"
    ];

    private static Task<(bool Success, List<string> Errors, Func<string, string> Read, List<string> Files)> Generate(Dictionary<string, string>? extra = null, string stack = "api") =>
        TenantPlanTests.GenerateAsync(extra, stack, More);

    [TestMethod]
    public async Task The_dashboard_reads_the_rows_of_the_callers_tenant_only_in_every_API()
    {
        var (success, errors, read, _) = await Generate(new() { ["Dashboard"] = "true" });
        Assert.IsTrue(success, string.Join(" | ", errors));
        string data = read("DashboardData.cs");
        Expect.Contains(data, "(SELECT * FROM \\\"Project\\\" WHERE \\\"TenantId\\\" = {tenant}) AS \\\"Project\\\"");
        Expect.Contains(data, "string tenant = context.CurrentTenantId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? \"NULL\";");
        Expect.Contains(data, "key = tenant + \"|\" + key;");
        Expect.Contains(data, "SqlQueryRaw<DashboardPoint>(d.Sql.Replace(\"{tenant}\", tenant))");
        Expect.Contains(data, "FROM (SELECT * FROM \\\"ProjectApproval\\\" WHERE \\\"ProjectId\\\" IN (SELECT \\\"ProjectId\\\" FROM \\\"Project\\\" WHERE \\\"TenantId\\\" = {tenant})) AS \\\"ProjectApproval\\\"");

        var (_, _, plainRead, _) = await TenantPlanTests.GenerateAsync(new() { ["Dashboard"] = "true", ["TenantTable"] = "", ["TenantMembership"] = "", ["NoTenantTables"] = "" }, "api", More);
        Expect.DoesNotContain(plainRead("DashboardData.cs"), "{tenant}");

        var (pySuccess, pyErrors, pyRead, _) = await Generate(new() { ["Dashboard"] = "true", ["Stacks"] = "python" }, "python");
        Assert.IsTrue(pySuccess, string.Join(" | ", pyErrors));
        string python = pyRead("app/routes/dashboard.py");
        Expect.Contains(python, "def load(session: Session, table: str | None, tenant: int | None) -> list[dict[str, Any]]:");
        Expect.Contains(python, "where = \"NULL\" if tenant is None else str(int(tenant))");
        Expect.Contains(python, "text(d.sql.replace(\"{tenant}\", where))");
        Expect.Contains(python, "load(session, table, user.tenant_id)");

        var (rsSuccess, rsErrors, rsRead, _) = await Generate(new() { ["Dashboard"] = "true", ["Stacks"] = "rust" }, "rust");
        Assert.IsTrue(rsSuccess, string.Join(" | ", rsErrors));
        string rust = rsRead("src/routes/dashboard.rs");
        Expect.Contains(rust, "async fn load(pool: &Pool, table: Option<&str>, tenant: Option<i32>)");
        Expect.Contains(rust, "let sql = d.sql.replace(\"{tenant}\", &tenant);");
        Expect.Contains(rust, "load(&pool, query.table.as_deref(), user.tenant_id).await?");
    }

    [TestMethod]
    public async Task A_junction_names_only_rows_the_caller_can_see()
    {
        var (success, errors, read, _) = await Generate(new() { ["Stacks"] = "api" });
        Assert.IsTrue(success, string.Join(" | ", errors));
        string junction = read("Apis/ProjectTagJunctionApi.cs");
        Expect.Contains(junction, "if (!await context.Set<Project>().AnyAsync(r => r.ProjectId == anchorId))");
        Expect.Contains(junction, "if (!await context.Set<Tag>().AnyAsync(r => r.TagId == request.TargetId))");
        Expect.Contains(junction, "return NotFound();");
    }

    [TestMethod]
    public async Task The_approvals_of_a_tenant_owned_row_are_the_callers_tenants_in_every_API()
    {
        var approvals = new Dictionary<string, string> { ["RowApprovals"] = "Project" };
        var (success, errors, read, _) = await Generate(approvals);
        Assert.IsTrue(success, string.Join(" | ", errors));
        string csharp = read("Apis/Approvals.cs");
        Expect.Contains(csharp, "private static IQueryable<ProjectApproval> Cycles(ShopContext context) => context.Set<ProjectApproval>().Where(a => context.Set<Project>().Any(r => r.ProjectId == a.ProjectId));");
        Expect.Contains(csharp, "await Cycles(context).AsNoTracking().Where(a => a.ProjectId == id)");
        Expect.Contains(csharp, "Cycles(context).Where(a => a.ProjectApprovalId == latest.ProjectApprovalId && a.ApprovalStatusId == 1)");

        var (_, _, py, _) = await Generate(new(approvals) { ["Stacks"] = "python" }, "python");
        string python = py("app/routes/approvals.py");
        Expect.Contains(python, "tenant=Project.tenant_id,");
        Expect.Contains(python, "def require_row(session: Session, user: CurrentUser, spec: ApprovalSpec, row_id: int) -> None:");
        Expect.Contains(python, "query = query.where(spec.col(\"link\").in_(visible(spec, user)))");
        Expect.Contains(python, "from app.routes.tenancy import only_tenant");

        var (_, _, rs, _) = await Generate(new(approvals) { ["Stacks"] = "rust" }, "rust");
        string rust = rs("src/routes/approvals.rs");
        Expect.Contains(rust, "async fn require_row_project(pool: &Pool, user: &CurrentUser, id: i32) -> ApiResult<()> {");
        Expect.Contains(rust, "AND \"ProjectId\" IN (SELECT \"ProjectId\" FROM \"Project\" WHERE \"TenantId\" = ?)");
        Expect.Contains(rust, ".bind(user.tenant_id)");

        var (_, _, plain, _) = await TenantPlanTests.GenerateAsync(new(approvals) { ["TenantTable"] = "", ["TenantMembership"] = "", ["NoTenantTables"] = "" }, "api", More);
        Expect.DoesNotContain(plain("Apis/Approvals.cs"), "Cycles(");
    }

    [TestMethod]
    public async Task A_unique_index_that_leaves_the_tenant_out_is_made_per_tenant_by_a_script()
    {
        var (success, errors, read, files) = await Generate();
        Assert.IsTrue(success, string.Join(" | ", errors));
        string script = read("Sql/TenantIndexes.sql");
        Expect.Contains(script, "-- Tag: UX_Tag_Name on (Name) becomes (TenantId, Name)");
        Expect.Contains(script, "DROP INDEX IF EXISTS \"UX_Tag_Name\";");
        Expect.Contains(script, "CREATE UNIQUE INDEX \"UX_Tag_TenantId_Name\" ON \"Tag\" (\"TenantId\", \"Name\");");
        Expect.DoesNotContain(script, "Slug");
        Expect.DoesNotContain(script, "ProjectTag");

        var (_, _, _, withoutTenants) = await TenantPlanTests.GenerateAsync(new() { ["TenantTable"] = "", ["TenantMembership"] = "", ["NoTenantTables"] = "" }, "api", More);
        Assert.IsFalse(withoutTenants.Any(f => f.EndsWith("TenantIndexes.sql")));
        Assert.IsTrue(files.Any(f => f.EndsWith("TenantIndexes.sql")));
    }

    [TestMethod]
    public async Task A_table_with_the_tenant_column_and_no_foreign_key_is_not_served_to_every_tenant()
    {
        string[] history = [.. More, "CREATE TABLE Project_History (AuditId INTEGER PRIMARY KEY AUTOINCREMENT, ProjectId INTEGER NOT NULL, TenantId INTEGER NOT NULL, Name VARCHAR(80) NOT NULL, Notes VARCHAR(100) NULL, Rank INTEGER NULL)"];
        var (success, errors, _, _) = await TenantPlanTests.GenerateAsync(null, "api", history);
        Assert.IsFalse(success);
        Assert.IsTrue(errors.Any(e => e.Contains("Project_History has a column TenantId but not as a NOT NULL whole-number foreign key to Tenant")), string.Join(" | ", errors));

        var (leftOut, leftOutErrors, _, _) = await TenantPlanTests.GenerateAsync(new() { ["NoApiTables"] = "Project_History" }, "api", history);
        Assert.IsTrue(leftOut, string.Join(" | ", leftOutErrors));
        var (shared, sharedErrors, _, _) = await TenantPlanTests.GenerateAsync(new() { ["NoTenantTables"] = "Country,Project_History" }, "api", history);
        Assert.IsTrue(shared, string.Join(" | ", sharedErrors));
    }

    [TestMethod]
    public async Task A_host_name_can_name_the_tenant_and_a_token_of_another_tenant_is_refused_on_it()
    {
        var (success, errors, read, _) = await Generate(new() { ["TenantHostColumn"] = "Slug" });
        Assert.IsTrue(success, string.Join(" | ", errors));
        string tenants = read("Apis/Tenants.cs");
        Expect.Contains(tenants, "public static class TenantHost");
        Expect.Contains(tenants, "t.Slug.ToLower() == label");
        Expect.Contains(tenants, "app.Use(TenantHost.InvokeAsync);");
        Expect.Contains(tenants, "public class TenantLoginClaims(ShopContext context, Microsoft.AspNetCore.Http.IHttpContextAccessor http) : ILoginClaims");
        Expect.Contains(tenants, "return new LoginClaims(Refusal: TenantProblems.OtherHost());");
        Expect.Contains(tenants, "if (await TenantHost.FindAsync(context, http, http.RequestAborted) is int host && host != wanted)");
        Expect.Contains(tenants, "type: \"/problems/tenant-host\"");

        var (_, _, plain, _) = await Generate();
        string without = plain("Apis/Tenants.cs");
        Expect.DoesNotContain(without, "TenantHost");
        Expect.DoesNotContain(without, "OtherHost");

        var (failed, problems, _, _) = await Generate(new() { ["TenantHostColumn"] = "CreatedOn" });
        Assert.IsFalse(failed);
        Assert.IsTrue(problems.Any(p => p.Contains("TenantHostColumn: CreatedOn must be a text column")), string.Join(" | ", problems));
    }

    [TestMethod]
    public async Task The_ASP_NET_tests_play_the_dashboard_junction_approval_and_host_cases_in_a_tenant_they_make()
    {
        var (success, errors, read, files) = await Generate(new() { ["ApiTests"] = "true", ["Dashboard"] = "true", ["RowApprovals"] = "Project", ["TenantHostColumn"] = "Slug" });
        Assert.IsTrue(success, string.Join(" | ", errors));
        string tests = read(files.First(f => f.EndsWith("TenantBypassApiTests.cs")));
        Expect.Contains(tests, "public async Task The_dashboard_shows_a_tenant_without_rows_none_of_Project()");
        Expect.Contains(tests, "await ProjectApiTests.NewBodyAsync()");
        Expect.Contains(tests, "public async Task ProjectTag_another_tenant_cannot_list_link_or_unlink_what_it_does_not_see()");
        Expect.Contains(tests, "Assert.AreEqual(HttpStatusCode.NotFound, list.StatusCode, \"another tenant is not shown the links of a row it does not see\");");
        Expect.Contains(tests, "public async Task Another_tenant_cannot_ask_decide_withdraw_or_read_the_approvals_of_a_row()");
        Expect.Contains(tests, "ApiFixture.ClientInTenant(other, \"Administrator\", \"zz_ann\")");
        Expect.Contains(tests, "TenantTestSupport.RunAsync(ApiFixture.Services, sql)");
        Expect.Contains(tests, "public async Task A_token_is_refused_on_the_host_of_another_tenant_and_not_restricted_on_a_host_that_names_none()");
        Expect.Contains(tests, "Assert.AreEqual(\"/problems/tenant-host\"");

        var (_, _, plainRead, plainFiles) = await Generate(new() { ["ApiTests"] = "true" });
        string plain = plainRead(plainFiles.First(f => f.EndsWith("TenantBypassApiTests.cs")));
        Expect.DoesNotContain(plain, "host_of_another_tenant");
        Expect.DoesNotContain(plain, "approvals_of_a_row");

        var (_, _, _, withoutTenants) = await TenantPlanTests.GenerateAsync(new() { ["ApiTests"] = "true", ["TenantTable"] = "", ["TenantMembership"] = "", ["NoTenantTables"] = "" }, "api", More);
        Assert.IsFalse(withoutTenants.Any(f => f.EndsWith("TenantBypassApiTests.cs")));
    }

    [TestMethod]
    public async Task The_Python_and_Rust_APIs_onboard_a_tenant_and_read_the_host_name()
    {
        var host = new Dictionary<string, string> { ["TenantHostColumn"] = "Slug" };
        var (pySuccess, pyErrors, pyRead, _) = await Generate(new(host) { ["Stacks"] = "python", ["PythonTests"] = "true" }, "python");
        Assert.IsTrue(pySuccess, string.Join(" | ", pyErrors));
        string route = pyRead("app/routes/tenant.py");
        Expect.Contains(route, "@router.post(\"/api/tenants/onboard\", response_model=TenantSchema, status_code=201)");
        Expect.Contains(route, "attach_admin(session, request.admin_id, row.tenant_id)");
        string tenancy = pyRead("app/routes/tenancy.py");
        Expect.Contains(tenancy, "def host_tenant(host: str | None) -> int | None:");
        Expect.Contains(tenancy, "def login_tenant(session: Session, user: DirectoryUser, requested: int | None, host: str | None = None)");
        Expect.Contains(tenancy, "def check_host(request: Request, tenant_id: int) -> None:");
        string auth = pyRead("app/auth.py");
        Expect.Contains(auth, "check_host(request, tenant)");
        Expect.Contains(auth, "login_tenant(session, user, body.tenant_id, request.url.hostname)");
        string pyTests = pyRead("tests/test_tenants.py");
        Expect.Contains(pyTests, "def test_only_a_platform_administrator_onboards_a_tenant(");
        Expect.Contains(pyTests, "def test_a_token_is_refused_on_the_host_of_another_tenant_and_not_restricted_on_a_host_that_names_none(");

        var (rsSuccess, rsErrors, rsRead, _) = await Generate(new(host) { ["Stacks"] = "rust", ["RustTests"] = "true" }, "rust");
        Assert.IsTrue(rsSuccess, string.Join(" | ", rsErrors));
        string rsRoute = rsRead("src/routes/tenant.rs");
        Expect.Contains(rsRoute, ".route(\"/api/tenants/onboard\", axum::routing::post(onboard))");
        Expect.Contains(rsRoute, "require_person(&pool, request.admin_id).await?;");
        string rsTenancy = rsRead("src/tenancy.rs");
        Expect.Contains(rsTenancy, "pub async fn host_tenant(pool: &Pool, host: Option<&str>) -> ApiResult<Option<i32>> {");
        Expect.Contains(rsTenancy, "pub async fn check_host(pool: &Pool, headers: &HeaderMap, tenant: i32) -> ApiResult<()> {");
        Expect.Contains(rsRead("src/auth.rs"), "crate::tenancy::check_host(&pool, request.headers(), tenant).await?;");
        string rsTests = rsRead("tests/api/tenant_routes.rs");
        Expect.Contains(rsTests, "async fn only_a_platform_administrator_onboards_a_tenant() {");
        Expect.Contains(rsTests, "api.on_host(&host).get(TENANTS).await");

        var (_, _, plainPython, _) = await Generate(new() { ["Stacks"] = "python", ["PythonTests"] = "true" }, "python");
        Expect.DoesNotContain(plainPython("app/routes/tenancy.py"), "host_tenant");
        Expect.DoesNotContain(plainPython("app/auth.py"), "check_host");
        Expect.DoesNotContain(plainPython("tests/test_tenants.py"), "host_of_another_tenant");
        var (_, _, plainRust, _) = await Generate(new() { ["Stacks"] = "rust", ["RustTests"] = "true" }, "rust");
        Expect.DoesNotContain(plainRust("src/tenancy.rs"), "host_tenant");
    }

    [TestMethod]
    public async Task The_Python_and_Rust_tests_play_the_dashboard_junction_and_approval_cases_in_a_tenant_that_holds_nothing()
    {
        var settings = new Dictionary<string, string> { ["RowApprovals"] = "Project", ["Dashboard"] = "true" };
        var (pySuccess, pyErrors, pyRead, _) = await Generate(new(settings) { ["Stacks"] = "python", ["PythonTests"] = "true" }, "python");
        Assert.IsTrue(pySuccess, string.Join(" | ", pyErrors));
        string python = pyRead("tests/test_tenants.py");
        Expect.Contains(python, "def test_the_dashboard_shows_a_tenant_without_rows_none_of_the_rows(call, tenants, route, key, make, widget_ids):");
        Expect.Contains(python, "def test_another_tenant_cannot_list_link_or_unlink_what_it_does_not_see(");
        Expect.Contains(python, "(\"/api/projecttags\", \"/api/projects\", \"projectId\", project_body, \"/api/tags\", \"tagId\", tag_body, True),");
        string approvals = pyRead("tests/test_approvals.py");
        Expect.Contains(approvals, "ELSEWHERE = 2147483000");
        Expect.Contains(approvals, "def test_another_tenant_cannot_ask_decide_withdraw_or_read_the_approvals_of_a_row(call):");

        var (rsSuccess, rsErrors, rsRead, _) = await Generate(new(settings) { ["Stacks"] = "rust", ["RustTests"] = "true" }, "rust");
        Assert.IsTrue(rsSuccess, string.Join(" | ", rsErrors));
        string rust = rsRead("tests/api/tenant_routes.rs");
        Expect.Contains(rust, "async fn the_dashboard_shows_a_tenant_without_rows_none_of_project() {");
        Expect.Contains(rust, "async fn project_tag_another_tenant_cannot_list_link_or_unlink_what_it_does_not_see() {");
        Expect.Contains(rust, "pub async fn make_tenant(api: &Client) -> i32 {");
        string rustApprovals = rsRead("tests/api/approval_routes.rs");
        Expect.Contains(rustApprovals, "crate::tenant_routes::make_tenant(&api).await");

        var (_, _, plainPython, _) = await TenantPlanTests.GenerateAsync(new(settings) { ["Stacks"] = "python", ["PythonTests"] = "true", ["TenantTable"] = "", ["TenantMembership"] = "", ["NoTenantTables"] = "" }, "python", More);
        Expect.DoesNotContain(plainPython("tests/test_approvals.py"), "ELSEWHERE");
    }
}
