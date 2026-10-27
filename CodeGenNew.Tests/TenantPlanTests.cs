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
    private static string CreateDatabase()
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
            "CREATE TABLE Country (CountryId INTEGER PRIMARY KEY AUTOINCREMENT, TenantId INTEGER NOT NULL REFERENCES Tenant (TenantId), Name VARCHAR(80) NOT NULL, Code VARCHAR(3) NOT NULL, Notes VARCHAR(100) NULL)"
        ];
        foreach (string statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static Dictionary<string, string> Values(Dictionary<string, string>? extra = null)
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

    private static async Task<(bool Success, List<string> Errors, Func<string, string> Read, List<string> Files)> GenerateAsync(Dictionary<string, string>? extra = null)
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_tenants_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(Values(extra)), Stacks = ["api"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
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

        CollectionAssert.AreEqual(new[] { "EmployeeTenant", "Organization", "Project" }, TenantPlan.OwnedTables(database, project).Select(o => o.Table.TableName).ToArray(),
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
    public async Task The_context_filters_every_tenant_owned_table_by_the_tenant_in_the_token_and_soft_delete_shares_the_filter()
    {
        var (success, errors, read, _) = await GenerateAsync();

        Assert.IsTrue(success, string.Join(" | ", errors));
        string context = read("Data/ShopContext.cs");
        Assert.Contains("public ShopContext(DbContextOptions<ShopContext> options, ITenantScope tenant)", context);
        Assert.Contains("public int? CurrentTenantId => _tenant?.TenantId;", context);
        Assert.Contains("modelBuilder.Entity<Organization>().HasQueryFilter(e => e.TenantId == CurrentTenantId);", context);
        Assert.Contains("modelBuilder.Entity<Project>().HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);", context, "one filter per entity: soft delete and tenant together");
        Assert.Contains("modelBuilder.Entity<Tenant>().HasQueryFilter(e => e.TenantId == CurrentTenantId);", context, "the tenant table shows only the caller's own tenant");
        Assert.DoesNotContain("Entity<Country>().HasQueryFilter", context, "a shared table has no filter");
        Assert.DoesNotContain("Entity<Employee>().HasQueryFilter", context);
        Assert.Contains("public class ClaimsTenantScope", context);
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
        foreach (string text in new[] { "public static HttpClient ClientInTenant(int tenantId, string role)", "public static HttpClient PlatformClient()", "TenantTestSupport.FirstTenantsAsync" })
            Assert.Contains(text, fixture);
        Assert.DoesNotContain("/api/auth/login", fixture, "the people are rows of the database, so the tests make their tokens");
    }
}
