using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> TenantTable, TenantColumn, NoTenantTables and TenantMembership: shared-schema multi-tenancy (a query filter on the context, the tenant in the token, the tenant column written by the server). </summary>
[TestClass]
public class TenantPlanTests
{
    internal static string CreateDatabase(string[]? more = null)
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_tenants_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        string[] statements =
        [
            "CREATE TABLE Tenant (TenantId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(80) NOT NULL, Slug VARCHAR(40) NOT NULL UNIQUE, IsActive BOOLEAN NOT NULL DEFAULT 1, CreatedOn DATETIME NOT NULL)",
            "CREATE TABLE Employee (EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT, FullName VARCHAR(40) NOT NULL, Login VARCHAR(30) NOT NULL, PasswordHash VARCHAR(200) NOT NULL DEFAULT 'x', IsManager BOOLEAN NOT NULL DEFAULT 0, IsAdministrator BOOLEAN NOT NULL DEFAULT 0)",
            "CREATE TABLE EmployeeTenant (EmployeeTenantId INTEGER PRIMARY KEY AUTOINCREMENT, EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId), TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Role VARCHAR(30) NULL)",
            "CREATE TABLE Organization (OrganizationId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Name VARCHAR(80) NOT NULL, City VARCHAR(60) NULL, IsActive BOOLEAN NOT NULL DEFAULT 1)",
            "CREATE TABLE Project (ProjectId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Name VARCHAR(80) NOT NULL, Description VARCHAR(200) NULL, Budget DECIMAL(12,2) NOT NULL DEFAULT 0, StartedOn DATE NULL, IsDeleted BIT NOT NULL DEFAULT 0)",
            "CREATE TABLE ProjectTask (ProjectTaskId INTEGER PRIMARY KEY AUTOINCREMENT, ProjectId INTEGER NOT NULL REFERENCES Project (ProjectId), TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Title VARCHAR(80) NOT NULL, Hours DECIMAL(6,2) NOT NULL DEFAULT 0, Notes VARCHAR(100) NULL)",
            "CREATE TABLE Country (CountryId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Name VARCHAR(80) NOT NULL, Code VARCHAR(3) NOT NULL, Notes VARCHAR(100) NULL)"
        ];
        foreach (string statement in statements.Concat(more ?? []))
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    internal static Dictionary<string, string> Values(Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "api", ["Dashboard"] = "false", ["AccessMode"] = "Ef",
            ["ApiAuth"] = "true", ["TenantTable"] = "Tenant", ["TenantMembership"] = "EmployeeTenant:EmployeeId:TenantId:Role", ["UserTable"] = "Employee", ["UserNameColumn"] = "Login", ["UserRoleColumn"] = "IsManager",
            ["UserAdminColumn"] = "IsAdministrator", ["SoftDeleteTables"] = "Project", ["NoTenantTables"] = "Country", ["ApiTests"] = "true", ["ApiCsv"] = "true", ["ApiHttp"] = "true"
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return values;
    }

    internal static async Task<(bool Success, List<string> Errors, Func<string, string> Read, List<string> Files)> GenerateAsync(Dictionary<string, string>? extra = null, string stack = "api", string[]? more = null)
    {
        string database = CreateDatabase(more);
        string output = Path.Combine(Path.GetTempPath(), "codegen_tenants_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(Values(extra)), Stacks = [stack], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            var contents = files.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(output, f)).Replace("\r\n", "\n"));
            return (report.Success, report.Errors.ToList(), name => contents[files.Single(f => f.EndsWith(name))], files);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<DatabaseModel> ReadAsync()
    {
        string database = CreateDatabase();
        try
        {
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            return await provider.BuildAsync(database, "main");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    [TestMethod]
    public async Task A_table_is_tenant_owned_when_it_has_the_tenant_column_as_a_foreign_key_to_the_tenant_table()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values());

        CollectionAssert.AreEqual(new[] { "EmployeeTenant", "Organization", "Project", "ProjectTask" }, TenantPlan.OwnedTables(database, project).Select(o => o.Table.TableName).ToArray(),
            "Country is left out by NoTenantTables, Employee has no tenant column and the tenant table is not owned by itself");
        Assert.IsNull(TenantPlan.OwnedBy(database.Tables.Single(t => t.TableName == "Tenant"), database, project));
        Assert.IsNull(TenantPlan.OwnedBy(database.Tables.Single(t => t.TableName == "Employee"), database, project));
        Assert.IsEmpty(TenantPlan.Problems(database, project));
        Assert.IsEmpty(TenantPlan.OwnedTables(database, ProjectSettings.FromValues(Values(new() { ["ApiAuth"] = "false" }))), "without sign-in there is no tenant claim");
        var membership = TenantPlan.MembershipOf(database, project)!;
        Assert.AreEqual("EmployeeId", membership.User.Name);
        Assert.AreEqual("Role", membership.Role!.Name);
    }

    [TestMethod]
    public async Task A_filtered_unique_index_is_made_per_tenant_with_its_row_condition_and_included_columns()
    {
        var database = await ReadAsync();
        var project = ProjectSettings.FromValues(Values());
        int at = database.Tables.FindIndex(t => t.TableName == "Project");
        var old = database.Tables[at];
        database.Tables[at] = Sample.Table(old.TableName, old.Columns, foreignKeys: old.ForeignKeys, indexes: [.. old.Indexes], dialect: SqlDialect.Sqlite,
            partialIndexes:
            [
                new PartialIndexModel("UX_Project_Live", true, ["Name"], ["Budget"], "IsActive = 1"),
                new PartialIndexModel("UX_Project_Tenant", true, ["TenantId", "Name"], [], "IsActive = 1"),
                new PartialIndexModel("IX_Project_Plain", false, ["Name"], [], "IsActive = 1")
            ]);

        string script = TenantIndexPlan.Script(database, project);

        Expect.Contains(script, "-- Project: UX_Project_Live on (Name) becomes (TenantId, Name)");
        Expect.Contains(script, "CREATE UNIQUE INDEX \"UX_Project_TenantId_Name\" ON \"Project\" (\"TenantId\", \"Name\") INCLUDE (\"Budget\") WHERE IsActive = 1;");
        Expect.DoesNotContain(script, "UX_Project_Tenant on");
        Expect.DoesNotContain(script, "IX_Project_Plain");
    }

    [TestMethod]
    public async Task A_setting_that_cannot_be_served_is_named_with_the_reason()
    {
        var database = await ReadAsync();
        string Problems(Dictionary<string, string> extra) => string.Join(" | ", TenantPlan.Problems(database, ProjectSettings.FromValues(Values(extra))));

        Assert.Contains("needs ApiAuth=true", Problems(new() { ["ApiAuth"] = "false" }));
        Assert.Contains("MissingTable is not a table", Problems(new() { ["TenantTable"] = "MissingTable" }));
        Assert.Contains("no other table has a NOT NULL whole-number column Owner", Problems(new() { ["TenantColumn"] = "Owner" }));
        Assert.Contains("NoTenantTables: Nope is not a table", Problems(new() { ["NoTenantTables"] = "Nope" }));
        Assert.Contains("TenantMembership: Bad:X must name a table", Problems(new() { ["TenantMembership"] = "Bad:X" }));
        Assert.Contains("TenantMembership needs UserTable", Problems(new() { ["UserTable"] = "" }));
        Assert.Contains("need TenantTable", string.Join(" | ", TenantPlan.Problems(database, ProjectSettings.FromValues(new Dictionary<string, string> { ["TenantMembership"] = "A:B:C", ["AccessMode"] = "Ef", ["DatabaseProvider"] = "Sqlite" }))));
        var sqlServer = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.SqlServer, Tables = database.Tables };
        Assert.Contains("needs AccessMode=Ef", string.Join(" | ", TenantPlan.Problems(sqlServer, ProjectSettings.FromValues(Values(new() { ["AccessMode"] = "Routines", ["DatabaseProvider"] = "SqlServer" })))));
    }

    [TestMethod]
    public void The_settings_add_the_templates_and_have_a_hint_and_a_tab()
    {
        var project = ProjectSettings.FromValues(Values());

        Assert.Contains("API_Tenants", project.ImpliedPlanTemplates.ToList());
        Assert.Contains("API_TestTenants", project.ImpliedPlanTemplates.ToList());
        Assert.DoesNotContain("API_Tenants", ProjectSettings.FromValues(Values(new() { ["TenantTable"] = "" })).ImpliedPlanTemplates.ToList());
        Assert.AreEqual("Tenant", project.TenantTable);
        Assert.AreEqual("TenantId", project.TenantColumn);
        Assert.IsTrue(project.NoTenant("country"));
        foreach (string key in new[] { "TenantTable", "TenantColumn", "NoTenantTables", "TenantMembership" })
        {
            Assert.AreEqual("Table shapes", ProjectSettingGroups.TabOf(key), key);
            Assert.IsTrue(ProjectSettingsHints.All.ContainsKey(key), key);
        }
    }

    [TestMethod]
    public async Task Every_API_lists_the_tenants_a_person_can_sign_in_to_without_a_sign_in()
    {
        var (success, errors, read, _) = await GenerateAsync();
        Assert.IsTrue(success, string.Join(" | ", errors));
        string tenants = read("Apis/Tenants.cs");
        Assert.Contains("public static class TenantSignIn", tenants);
        Assert.Contains("context.Set<Tenant>().IgnoreQueryFilters().AsNoTracking().Where(t => t.IsActive)", tenants, "a switched-off tenant is left out");
        Assert.Contains("app.MapGet(\"" + TenantWords.SignInListPath + "\", TenantSignIn.ListAsync).AllowAnonymous();", tenants);

        var (pythonSuccess, pythonErrors, python, _) = await GenerateAsync(stack: "python");
        Assert.IsTrue(pythonSuccess, string.Join(" | ", pythonErrors));
        string tenancy = python("app/routes/tenancy.py");
        Assert.Contains("@public_router.get(\"" + TenantWords.SignInListPath + "\")", tenancy);
        Assert.Contains(".where(tenants.c[TENANT_ACTIVE].is_(True))", tenancy);
        Assert.Contains("app.include_router(tenancy_public_router)", python("app/main.py"));

        var (rustSuccess, rustErrors, rust, _) = await GenerateAsync(stack: "rust");
        Assert.IsTrue(rustSuccess, string.Join(" | ", rustErrors));
        Assert.Contains(".route(\"" + TenantWords.SignInListPath + "\", get(sign_in_tenants))", rust("src/tenancy.rs"));
        Assert.Contains(".merge(crate::tenancy::public_router())", rust("src/auth.rs"));
    }

    [TestMethod]
    public async Task The_context_filters_every_tenant_owned_table_by_the_tenant_in_the_token_and_soft_delete_shares_the_filter()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string context = read("Data/ShopContext.cs");
        Assert.Contains("public ShopContext(DbContextOptions<ShopContext> options, ITenantScope tenant)", context);
        Assert.Contains("public int? CurrentTenantId => _tenant?.TenantId;", context);
        Assert.Contains("modelBuilder.Entity<Organization>().HasQueryFilter(e => e.TenantId == CurrentTenantId);", context);
        Assert.Contains("modelBuilder.Entity<Project>().HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);", context, "one filter per entity: soft delete and tenant together");
        Assert.Contains("modelBuilder.Entity<Tenant>().HasQueryFilter(e => (e.TenantId == CurrentTenantId || IsPlatform));", context, "the tenant table shows the caller's own tenant, and a platform administrator every tenant");
        Assert.DoesNotContain("Entity<Country>().HasQueryFilter", context, "a shared table has no filter");
        Assert.Contains("Entity<Employee>().HasQueryFilter(e => (IsPlatform || Set<EmployeeTenant>()", context, "the people of the user table are the members of the tenant");
        Assert.DoesNotContain("ClaimsTenantScope", context, "the API's scope lives in Tenants.cs, so the WinUI3 context compiles without ASP.NET types");
        Assert.Contains("public class ClaimsTenantScope", read("Apis/Tenants.cs"));
        Assert.Contains("services.AddTenants();", read("Apis/ApiRegistration.cs"));
        Assert.Contains("TenantRoutes.Register(app);", read("Apis/ApiRegistration.cs"));
    }

    [TestMethod]
    public async Task The_API_writes_the_callers_tenant_and_the_tenant_table_is_neither_created_nor_deleted()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.Contains("c => c.TenantId;", read("Apis/ProjectApi.cs"));
        Assert.Contains("Expression<Func<Project, int>>? Tenant", read("Apis/ProjectApi.cs"));
        Assert.Contains("Expression<Func<Organization, int>>? Tenant", read("Apis/OrganizationApi.cs"));
        Assert.DoesNotContain("Tenant =>", read("Apis/CountryApi.cs"));
        string tenantApi = read("Apis/TenantApi.cs");
        Assert.Contains("/problems/tenant-onboarding", tenantApi);
        Assert.Contains("BeforeDeleteAsync", tenantApi);
        string crud = read("Apis/CrudApi.cs");
        Assert.Contains("private IResult? StampTenant(TEntity row)", crud);
        Assert.Contains("if (StampTenant(newRow) is { } noTenant)", crud);
        Assert.Contains("if (StampTenant(updatedRow) is { } noTenant)", crud);
        Assert.Contains("/problems/tenant-required", crud);
    }

    [TestMethod]
    public async Task The_sign_in_puts_the_tenant_in_the_token_and_a_person_in_several_tenants_can_switch()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string jwt = read("Apis/Auth.cs");
        Assert.Contains("public record LoginRequest(string? UserName, string? Password, int? TenantId = null);", jwt);
        Assert.Contains("GetServices<ILoginClaims>()", jwt);
        string tenants = read("Apis/Tenants.cs");
        foreach (string text in new[] { "class TenantLoginClaims", "\"/api/auth/tenants\"", "\"/api/auth/switch-tenant\"", "\"/api/tenants/onboard\"", "TenantMemberships.OfAsync", "IgnoreQueryFilters()", "OnlyPlatform()",
            "context.Add(new EmployeeTenant { EmployeeId = admin, TenantId = tenant.TenantId, Role = AuthNames.AdminRole });", "TenantTestSupport" })
            Assert.Contains(text, tenants);
        Assert.Contains("Set<Employee>().IgnoreQueryFilters().AsNoTracking()", read("Apis/TableUserDirectory.cs"), "nobody works in a tenant when they sign in");
    }

    [TestMethod]
    public async Task Soft_delete_routes_keep_the_tenant_filter_when_they_ignore_the_others()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string shapes = read("Apis/RowShapes.cs");
        Assert.Contains("IgnoreQueryFilters().AsNoTracking().Where(x => x.IsDeleted).Where(x => x.TenantId == context.CurrentTenantId)", shapes);
        Assert.Contains("&& x.IsDeleted && x.TenantId == context.CurrentTenantId)", shapes);
    }

    [TestMethod]
    public async Task Editable_child_rows_of_a_tenant_owned_table_are_stamped_with_the_callers_tenant()
    {
        var (success, errors, read, _) = await GenerateAsync(new() { ["EditableChildren"] = "Project.ProjectTask" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.Contains("new ChildRows<int, ProjectTask>(\"projectTasks\", c => c.ProjectTaskId, c => c.ProjectId, c => c.TenantId),", read("/ProjectApi.cs"));
        string support = read("/CrudApi.cs");
        Assert.Contains("setTenant(row, tenant!.Value);", support);
        Assert.Contains("GetRequiredService<ITenantScope>().TenantId), null);", support);
        Assert.Contains("await list.ApplyAsync(context, parentId, rows, body.Options, body.Tenant);", support);
    }

    [TestMethod]
    public async Task A_platform_administrator_creates_tenants_and_a_token_of_a_switched_off_tenant_is_refused()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tenantApi = read("/TenantApi.cs");
        Assert.Contains("isNew && !Http.RequestServices.GetRequiredService<ITenantScope>().IsPlatform", tenantApi);
        Assert.Contains("TenantCreator.AttachAsync(context, Http, row.TenantId)", tenantApi, "the creator becomes the first administrator");
        string tenants = read("Apis/Tenants.cs");
        Assert.Contains("public static class TenantCreator", tenants);
        Assert.Contains("app.Use(TenantActiveCheck.InvokeAsync);", tenants);
        Assert.Contains("type: \"/problems/tenant-inactive\"", tenants);
        Assert.Contains("public static async Task DeactivateAsync(", tenants);
        Assert.Contains("A_token_of_a_tenant_that_was_switched_off_is_refused", read("/TenantApiTests.cs"));
    }

    [TestMethod]
    public async Task The_Python_API_filters_and_stamps_by_the_callers_tenant_and_signs_in_to_a_tenant()
    {
        var (success, errors, read, _) = await GenerateAsync(new() { ["Stacks"] = "python" }, "python");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tenancy = read("app/routes/tenancy.py");
        Assert.Contains("def only_tenant(statement, tenant_column, user: CurrentUser):", tenancy);
        Assert.Contains("def login_tenant(session: Session, user: DirectoryUser, requested: int | None, host: str | None = None)", tenancy);
        Assert.Contains("@router.post(\"/api/auth/switch-tenant\")", tenancy);
        Assert.Contains("def ensure_active(tenant_id: int) -> None:", tenancy);
        Assert.Contains("PROBLEM_INACTIVE = \"/problems/tenant-inactive\"", tenancy);
        string auth = read("app/auth.py");
        Assert.Contains("TENANT_CLAIM = \"tenant\"", auth);
        Assert.Contains("tenant_id, tenant_role = login_tenant(session, user, body.tenant_id)", auth);
        string project = read("app/routes/project.py");
        Assert.Contains("statement = only_tenant(statement, Project.tenant_id, user)", project);
        Assert.Contains("row.tenant_id = user.tenant_id   # a row never moves to another tenant", project);
        Assert.Contains("refuse_other_tenant(row.tenant_id, user)", project);
        Assert.Contains("from app.routes import tenancy", read("app/routes/__init__.py"));
        Assert.DoesNotContain("only_tenant", read("app/routes/employee.py"), "a table that is not tenant-owned has no tenant column to filter");
        Assert.Contains("only_members(", read("app/routes/employee.py"));
        string organization = read("app/routes/organization.py");
        Assert.Contains("@router.get(\"/api/organizations/active\"", organization);
        Assert.Contains("@router.get(\"/api/organizations/{id:int}\"", organization);
        Assert.Contains("@router.get(\"/api/organizations/{name}\"", organization, "find by name comes after the routes with a key");
        Assert.Contains("def check_name(session: Session, body: OrganizationSchema, key=None, user: CurrentUser = None) -> None:", organization);
        Assert.Contains("raise ApiError(409, \"Duplicate name\", problem_type=\"/problems/duplicate-name\", detail=f\"An active row named '{body.name}' already exists.\")", organization);
        Assert.Contains("statement = only_tenant(statement, Organization.tenant_id, user)", organization, "the duplicate rule compares within the caller's tenant");
        string tenant = read("app/routes/tenant.py");
        Assert.Contains("raise only_platform()", tenant);
        Assert.Contains("raise never_deleted()", tenant);
        Assert.Contains("attach_creator(session, user, row.tenant_id)", tenant);
        Assert.DoesNotContain("project", read("app/routes/csv_files.py"), "a tenant-owned table has no CSV routes here");
        Assert.Contains("employee", read("app/routes/csv_files.py"));
    }

    [TestMethod]
    public async Task The_Python_tests_sign_in_to_a_tenant_and_play_the_isolation_of_its_tables()
    {
        var (success, errors, read, files) = await GenerateAsync(new() { ["Stacks"] = "python", ["PythonTests"] = "true" }, "python");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.IsTrue(files.Any(f => f.EndsWith("tests/conftest.py")), "the shared conftest is written");
        string tests = read("tests/test_tenants.py");
        Assert.Contains("app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"tenant-test\", role=ADMIN_ROLE, owner_id=None, tenant_id=who[\"tenant\"])", tests);
        Assert.Contains("(\"/api/projects\", \"projectId\", \"tenantId\", project_body),", tests);
        Assert.Contains("(\"/api/organizations\", \"organizationId\", \"name\", \"isActive\", 80, organization_body),", tests);
        Assert.Contains("def test_another_tenant_cannot_read_change_delete_or_find_a_row(", tests);
        Assert.Contains("assert created.json()[\"type\"] == \"/problems/tenant-required\"", tests);
        Assert.Contains("assert duplicate.json()[\"detail\"] == f\"An active row named '{text}' already exists.\"", tests);
        Assert.Contains("assert refused.json()[\"type\"] == \"/problems/tenant-onboarding\"", tests);
        Assert.Contains("def test_a_token_of_a_tenant_that_was_switched_off_is_refused(call, tenants, application):", tests);
        Assert.Contains("SWITCH_OFF_TENANT = \"UPDATE \\\"Tenant\\\" SET \\\"IsActive\\\" = 0 WHERE \\\"TenantId\\\" = \"", tests);
        Assert.Contains("assert after.json()[\"type\"] == \"/problems/tenant-inactive\"", tests);

        var (_, _, _, withoutTests) = await GenerateAsync(new() { ["Stacks"] = "python" }, "python");
        Assert.IsFalse(withoutTests.Any(f => f.EndsWith("tests/test_tenants.py")));
    }

    [TestMethod]
    public async Task The_Rust_tests_work_in_a_tenant_and_play_the_isolation_of_its_tables()
    {
        var (success, errors, read, files) = await GenerateAsync(new() { ["Stacks"] = "rust", ["RustTests"] = "true" }, "rust");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string support = read("tests/api/test_support.rs");
        Assert.Contains("const TENANT_CLAIM: &str = \"tenant\";", support);
        Assert.Contains("pub fn in_tenant(&self, tenant: i32, role: &str) -> Client {", support);
        Assert.Contains("pub fn platform(&self) -> Client {", support);
        Assert.Contains("crate::tenant_support::first_tenants(&self.pool).await", support);
        Assert.DoesNotContain("_TEST_USER", support, "the people who sign in are rows of the database, so the tests make their tokens");
        Assert.Contains("pub async fn first_tenants(pool: &Pool) -> Vec<i32> {", read("tests/api/tenant_support.rs"));
        string main = read("tests/api/main.rs");
        Assert.Contains("mod tenant_routes;", main);
        Assert.Contains("mod tenant_support;", main);
        string project = read("tests/api/project.rs");
        Assert.Contains("async fn another_tenant_cannot_read_change_delete_or_find_a_row() {", project);
        Assert.Contains("async fn a_new_row_belongs_to_the_callers_tenant_whatever_the_body_says() {", project);
        Assert.Contains("assert_eq!(created.json()[\"type\"], \"/problems/tenant-required\");", project);
        Assert.DoesNotContain("tenant", read("tests/api/employee.rs"), "a table that is not tenant-owned is untouched");
        string routes = read("tests/api/tenant_routes.rs");
        Assert.Contains("async fn a_tenant_is_created_by_a_platform_administrator_only_and_never_deleted() {", routes);
        Assert.Contains("async fn organization_names_are_trimmed_and_unique_among_the_active_rows_of_a_tenant() {", routes);
        Assert.Contains("\"/problems/duplicate-name\"", routes);
        Assert.Contains("async fn a_token_of_a_tenant_that_was_switched_off_is_refused() {", routes);
        Assert.Contains("::forget_tenant(tenant);", routes);
        Assert.Contains("assert_eq!(after.json()[\"type\"], \"/problems/tenant-inactive\");", routes);
        Assert.Contains("pub use tenancy::forget_tenant;", read("src/lib.rs"));

        var (_, _, _, withoutTests) = await GenerateAsync(new() { ["Stacks"] = "rust" }, "rust");
        Assert.IsFalse(withoutTests.Any(f => f.EndsWith("tests/api/tenant_routes.rs")));
    }

    [TestMethod]
    public async Task The_Rust_API_filters_and_stamps_by_the_callers_tenant_and_signs_in_to_a_tenant()
    {
        var (success, errors, read, _) = await GenerateAsync(new() { ["Stacks"] = "rust" }, "rust");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tenancy = read("src/tenancy.rs");
        Assert.Contains("pub enum Tenancy {", tenancy);
        Assert.Contains("pub async fn login_tenant(pool: &Pool, user: &DirectoryUser, requested: Option<i32>, _host: Option<&str>)", tenancy);
        Assert.Contains(".route(\"/api/auth/switch-tenant\", post(switch_tenant))", tenancy);
        Assert.Contains("pub async fn ensure_active(pool: &Pool, tenant: i32)", tenancy);
        Assert.Contains("PROBLEM_INACTIVE: &str = \"/problems/tenant-inactive\"", tenancy);
        string auth = read("src/auth.rs");
        Assert.Contains("pub const TENANT_CLAIM: &str = \"tenant\";", auth);
        Assert.Contains("crate::tenancy::login_tenant(&pool, &user, requested_tenant, None).await?", auth);
        Assert.Contains("pub fn session_response(user: &DirectoryUser, tenant: Option<i32>)", auth);
        string project = read("src/routes/project.rs");
        Assert.Contains("repo::get_all_scoped(&pool, &Tenancy::of(&user))", project);
        Assert.Contains("row.tenant_id = require_tenant(&user)?;", project);
        Assert.Contains("row.tenant_id = current.tenant_id; // a row never moves to another tenant", project);
        Assert.Contains("tenancy.apply(&mut builder, r#\"t.\"TenantId\"\"#);", read("src/repos/project.rs"));
        Assert.Contains(".merge(crate::tenancy::router())", read("src/routes/mod.rs"));
        Assert.Contains("from_fn_with_state(pool.clone(), auth::require_user)", read("src/lib.rs"));
        Assert.DoesNotContain("Tenancy::of(&user)", read("src/routes/employee.rs"), "a table that is not tenant-owned has no tenant column to filter");
        Assert.Contains("Tenancy::of_members(&user)", read("src/routes/employee.rs"));
        string organization = read("src/routes/organization.rs");
        Assert.Contains(".route(\"/api/organizations/active\", get(get_all_active))", organization);
        Assert.Contains("async fn get_by_key(State(pool): State<Pool>, Path(key): Path<String>", organization);
        Assert.Contains("async fn check_name(pool: &Pool, row: &mut Organization, key: Option<i32>, user: &CurrentUser)", organization);
        Assert.Contains("ApiError::Rejected(409, \"/problems/duplicate-name\", \"Duplicate name\"", organization);
        Assert.Contains("pub async fn name_taken(pool: &Pool, name: &str, key: Option<i32>, tenancy: &Tenancy)", read("src/repos/organization.rs"));
        string tenant = read("src/routes/tenant.rs");
        Assert.Contains("return Err(only_platform());", tenant);
        Assert.Contains("Err(never_deleted())", tenant);
        Assert.Contains("attach_creator(&pool, &user, row.tenant_id).await?;", tenant);
        Assert.DoesNotContain("project", read("src/routes/csv_files.rs"), "a tenant-owned table has no CSV routes here");
        Assert.Contains("employee", read("src/routes/csv_files.rs"));
    }

    [TestMethod]
    public async Task The_tests_prove_one_tenant_cannot_reach_another_and_the_fixture_works_in_the_first_tenant()
    {
        var (success, errors, read, files) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string project = read("ProjectApiTests.cs");
        foreach (string name in new[] { "Another_tenant_cannot_read_change_delete_or_find_a_row", "A_new_row_belongs_to_the_callers_tenant_whatever_the_body_says", "A_caller_in_no_tenant_sees_no_row_and_cannot_create_one", "A_caller_in_no_tenant_exports_no_row" })
            Assert.Contains(name, project);
        Assert.IsTrue(files.Any(f => f.EndsWith("OrganizationApiTests.cs")), "a Name and IsActive table has no API_Test class, so API_TestTenants writes one");
        Assert.Contains("A_caller_reads_their_own_tenant_and_no_other", read("/TenantApiTests.cs"));
        Assert.Contains("Only_a_platform_administrator_onboards_a_tenant", read("/TenantApiTests.cs"));
        Assert.DoesNotContain("Another_tenant_cannot", read("CountryApiTests.cs"), "a shared table has no tenant tests");
        string fixture = read("ApiFixture.cs");
        foreach (string text in new[] { "public static HttpClient ClientInTenant(int tenantId, string role, string name = \"tenant-test\")", "public static HttpClient PlatformClient()", "TenantTestSupport.FirstTenantsAsync" })
            Assert.Contains(text, fixture);
        Assert.DoesNotContain("/api/auth/login", fixture, "the people are rows of the database, so the tests make their tokens");
    }
}
