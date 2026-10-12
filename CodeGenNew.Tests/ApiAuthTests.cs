using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The API half of the sign-in (ApiAuth=true): the Jwt group, the middleware in Program.cs, the owner scope in CrudApi and in the classes of an owned table, and the tests, requests and
/// documents that carry the token. Run live in a scratch project over SQLite (login, 401, 403, scoped lists, create, update, clone, delete, CSV, throttle). </summary>
[TestClass]
public class ApiAuthTests
{
    private static readonly (string, string) On = ("ApiAuth", "true");
    private static readonly (string, string) Owned = ("OwnedTables", "TimeSheet");

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<string> RenderProject(string template, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static async Task<string> RenderTable(string template, TableModel table, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static TableModel TimeSheet(bool ownerNullable = false, string ownerName = "EmployeeId") => Sample.Table("TimeSheet",
    [
        Sample.Column("TimeSheetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column(ownerName, SqlDbType.Int, nullable: ownerNullable, ordinal: 2),
        Sample.Column("Notes", SqlDbType.NVarChar, characters: 100, ordinal: 3)
    ]);

    [TestMethod]
    public void The_settings_default_to_off_and_to_the_usual_claim_names()
    {
        var plain = Project();
        Assert.IsFalse(plain.ApiAuth);
        Assert.IsEmpty(plain.OwnedTables);
        Assert.AreEqual("EmployeeId", plain.OwnerColumn);
        Assert.IsTrue(plain.OwnedChildren);
        Assert.IsFalse(Project(("OwnedChildren", "false")).OwnedChildren);
        Assert.AreEqual("role", plain.AuthRoleClaim);
        Assert.AreEqual("employeeId", plain.AuthOwnerClaim);
        Assert.AreEqual("Administrator", plain.AuthAdminRole);

        var set = Project(On, ("OwnedTables", "TimeSheet, Expense"), ("OwnerColumn", "PersonId"), ("AuthRoleClaim", "roles"), ("AuthOwnerClaim", "personId"), ("AuthAdminRole", "Boss"));
        CollectionAssert.AreEqual(new[] { "TimeSheet", "Expense" }, set.OwnedTables);
        Assert.AreEqual("PersonId", set.OwnerColumn);
        Assert.AreEqual("roles", set.AuthRoleClaim);
        Assert.AreEqual("personId", set.AuthOwnerClaim);
        Assert.AreEqual("Boss", set.AuthAdminRole);
        Assert.Contains("ApiAuth", ProjectSettingsHints.BooleanKeys);
    }

    [TestMethod]
    public void ApiAuth_turns_on_the_Jwt_group_and_nothing_else()
    {
        CollectionAssert.AreEqual(new[] { "Jwt" }, Project(On).ImpliedEssentialsGroups.ToArray());
        var group = EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Api").Single(g => g.Name == "Jwt");
        Assert.IsFalse(group.DefaultOn);
        Assert.IsFalse(EssentialsCatalog.IsOnFor(group, Project()));
        Assert.IsTrue(EssentialsCatalog.IsOnFor(group, Project(On)));
    }

    [TestMethod]
    public void A_table_is_owned_when_listed_and_it_has_a_NOT_NULL_whole_number_owner_column()
    {
        var project = Project(On, Owned);
        Assert.AreEqual("EmployeeId", OwnedTable.OwnerColumn(TimeSheet(), project)!.Name);
        Assert.IsNull(OwnedTable.OwnerColumn(TimeSheet(), Project(Owned)), "nothing is scoped without ApiAuth");
        Assert.IsNull(OwnedTable.OwnerColumn(Sample.Employee(), project), "not listed");
        Assert.IsNull(OwnedTable.OwnerColumn(TimeSheet(ownerNullable: true), project));
        Assert.IsTrue(OwnedTable.IsListedButUnusable(TimeSheet(ownerNullable: true), project));
        Assert.IsTrue(OwnedTable.IsListedButUnusable(TimeSheet(ownerName: "PersonId"), project));
        Assert.IsFalse(OwnedTable.IsListedButUnusable(TimeSheet(), project));
        Assert.AreEqual("PersonId", OwnedTable.OwnerColumn(TimeSheet(ownerName: "PersonId"), Project(On, Owned, ("OwnerColumn", "PersonId")))!.Name);
    }

    [TestMethod]
    public async Task The_Jwt_group_writes_the_login_the_token_wiring_and_the_owner_scope()
    {
        string text = await RenderProject("API_EssentialJwt_v1.tt", On);
        var files = GeneratedFiles.Split(text).ToDictionary(f => f.RelativePath, f => f.Content.ReplaceLineEndings("\n"));
        CollectionAssert.AreEquivalent(new[] { "Apis/Auth.cs", "Apis/OwnerScope.cs" }, files.Keys.ToArray());

        string auth = files["Apis/Auth.cs"];
        Expect.Contains(auth, "namespace Acme.ApiService.Apis;");
        Expect.Contains(auth, "app.MapPost(\"/api/auth/login\", LoginAsync)");
        Expect.Contains(auth, ".AllowAnonymous()");
        Expect.Contains(auth, "public record LoginResponse(string Token, DateTime ExpiresUtc, AuthUser User);");
        Expect.Contains(auth, "Results.Problem(statusCode: 401, type: \"" + ProblemTypes.WrongCredentials + "\", title: \"Wrong user name or password\")");
        Expect.Contains(auth, "Results.Problem(statusCode: 429, type: \"" + ProblemTypes.TooManyAttempts + "\", title: \"Too many attempts\"");
        Expect.Contains(auth, "options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();");
        Expect.Contains(auth, "options.MapInboundClaims = false;");
        Expect.Contains(auth, "public const string RoleClaim = \"role\";");
        Expect.Contains(auth, "public const string OwnerClaim = \"employeeId\";");
        Expect.Contains(auth, "public const string AdminRole = \"Administrator\";");
        Expect.Contains(auth, "services.TryAddScoped<IUserDirectory, BootstrapDirectory>();");
        Expect.Contains(auth, "services.TryAddScoped<IOwnerScope, ClaimsOwnerScope>();");
        Expect.Contains(auth, "Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes)");
        Expect.Contains(auth, "CryptographicOperations.FixedTimeEquals");
        Expect.Contains(auth, "Bootstrap:AdminPassword");
        Expect.Contains(auth, "Set the environment variable Jwt__Key; do not write the key into appsettings.json.");
        Expect.DoesNotContain(auth, "Jwt:Key\": \"");

        string scope = files["Apis/OwnerScope.cs"];
        Expect.Contains(scope, "public interface IOwnerScope");
        Expect.Contains(scope, "user.IsInRole(AuthNames.AdminRole)");
    }

    [TestMethod]
    public async Task The_claim_names_and_the_administrator_role_come_from_the_settings()
    {
        string auth = await RenderProject("API_EssentialJwt_v1.tt", On, ("AuthRoleClaim", "roles"), ("AuthOwnerClaim", "personId"), ("AuthAdminRole", "Boss"));
        Expect.Contains(auth, "public const string RoleClaim = \"roles\";");
        Expect.Contains(auth, "public const string OwnerClaim = \"personId\";");
        Expect.Contains(auth, "public const string AdminRole = \"Boss\";");
    }

    [TestMethod]
    public async Task Program_adds_the_package_the_setup_the_middleware_and_the_login_route_only_with_ApiAuth()
    {
        string with = await RenderProject("API_EssentialProgram_v1.tt", On, ("ApiProduction", "true"), ("ApiDocs", "true"));
        Expect.Contains(with, "<PackageReference Include=\"Microsoft.AspNetCore.Authentication.JwtBearer\" Version=\"10.0.0\" />");
        Expect.Contains(with, "builder.AddGeneratedAuth();");
        Expect.Contains(with, "app.UseAuthentication();");
        Expect.Contains(with, "app.UseAuthorization();");
        Expect.Contains(with, "app.MapAuthEndpoints();");
        Expect.Contains(with, "\"/health/live\", new HealthCheckOptions { Predicate = _ => false }).ExcludeFromDescription().AllowAnonymous();");
        Expect.Contains(with, "app.MapGet(\"/docs\", () => Results.Content(Page, \"text/html\")).ExcludeFromDescription().AllowAnonymous();");
        Assert.IsLessThan(with.IndexOf("app.MapAuthEndpoints();", StringComparison.Ordinal), with.IndexOf("app.UseAuthorization();", StringComparison.Ordinal));
        Assert.IsGreaterThan(with.IndexOf("app.UseCors();", StringComparison.Ordinal), with.IndexOf("app.UseAuthentication();", StringComparison.Ordinal));

        string without = await RenderProject("API_EssentialProgram_v1.tt", ("ApiProduction", "true"), ("ApiDocs", "true"));
        foreach (string text in new[] { "JwtBearer", "AddGeneratedAuth", "UseAuthentication", "UseAuthorization", "MapAuthEndpoints", "AllowAnonymous" })
            Expect.DoesNotContain(without, text);
    }

    [TestMethod]
    public async Task CrudApi_checks_the_owner_only_with_ApiAuth_and_answers_403_for_another_owners_row()
    {
        string with = await CrudApiTests.Essential(("ApiAuth", "true"));
        Expect.Contains(with, "protected virtual Expression<Func<TEntity, int>>? Owner => null;");
        Expect.Contains(with, "GetRequiredService<IOwnerScope>().VisibleOwnersAsync(Http.User, Http.RequestAborted)");
        Expect.Contains(with, "private async Task<IResult?> RefuseOtherOwnersAsync(AcmeContext context, TKey id)");
        Expect.Contains(with, "Results.Problem(statusCode: 403, type: \"" + ProblemTypes.NotYours + "\", title: \"Not your row\"");
        Expect.Contains(with, "var key = Key;");
        Expect.Contains(with, "var column = Owner!;");
        // every route that reaches one row asks first
        Assert.AreEqual(4, CountOf(with, "if (await RefuseOtherOwnersAsync(context, id) is { } refused)"), "get, delete, clone and update");
        Expect.Contains(with, "int? newOwner = OwnedThroughParent ? await ParentOwnerAsync(context, updatedRow) : OwnerOf(updatedRow);");
        Expect.Contains(with, "if (OwnerOf(newRow) == 0 && owners.Length == 1)");
        Expect.Contains(with, "Scope(context, context.Set<TEntity>().AsNoTracking(), owners)");

        string without = await CrudApiTests.Essential();
        foreach (string text in new[] { "IOwnerScope", "Owner", "NotYours", "403" })
            Expect.DoesNotContain(without, text);
    }

    private static int CountOf(string text, string part)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(part, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += part.Length;
        }
        return count;
    }

    [TestMethod]
    public async Task An_owned_table_names_its_owner_column_and_one_that_cannot_be_scoped_says_so()
    {
        string owned = await RenderTable("API_Crud_v1.tt", TimeSheet(), On, Owned);
        Expect.Contains(owned, "protected override Expression<Func<TimeSheet, int>>? Owner => c => c.EmployeeId;");

        Expect.DoesNotContain(await RenderTable("API_Crud_v1.tt", TimeSheet(), On), "Owner");
        Expect.DoesNotContain(await RenderTable("API_Crud_v1.tt", TimeSheet(), Owned), "Owner");   // no ApiAuth: nothing is scoped
        Expect.DoesNotContain(await RenderTable("API_Crud_v1.tt", Sample.Employee(), On, Owned), "Owner");

        string unusable = await RenderTable("API_Crud_v1.tt", TimeSheet(ownerNullable: true), On, Owned);
        Expect.DoesNotContain(unusable, "Owner =>");
        Expect.Contains(unusable, "// OwnedTables names this table, but it has no NOT NULL whole-number column named EmployeeId (OwnerColumn), so its rows are not scoped to the signed-in person.");
    }

    private static TableModel Detail(bool parentIdNullable = false, string parent = "TimeSheet") => Sample.Table("TimeSheetDetail",
    [
        Sample.Column("TimeSheetDetailId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("TimeSheetId", SqlDbType.Int, nullable: parentIdNullable, ordinal: 2),
        Sample.Column("Notes", SqlDbType.NVarChar, characters: 100, ordinal: 3)
    ],
    [Sample.ForeignKey("TimeSheetId", parent, "TimeSheetId")]);

    private static TableModel DonateLeave() => Sample.Table("DonateLeave",
    [
        Sample.Column("DonateLeaveId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("DonateFrom_EmployeeId", SqlDbType.Int, ordinal: 2),
        Sample.Column("DonateTo_EmployeeId", SqlDbType.Int, ordinal: 3)
    ]);

    [TestMethod]
    public async Task An_entry_may_name_its_own_owner_column()
    {
        var project = Project(On, ("OwnedTables", "DonateLeave:DonateFrom_EmployeeId, TimeSheet"));
        Assert.AreEqual("DonateFrom_EmployeeId", OwnedTable.OwnerColumn(DonateLeave(), project)!.Name);
        Assert.AreEqual("EmployeeId", OwnedTable.OwnerColumn(TimeSheet(), project)!.Name, "an entry without a column uses OwnerColumn");
        Assert.IsTrue(OwnedTable.IsListed(DonateLeave(), project));
        Assert.IsNull(OwnedTable.OwnerColumn(DonateLeave(), Project(On, ("OwnedTables", "DonateLeave"))), "without the entry's column the default column is not there");

        string api = await RenderTable("API_Crud_v1.tt", DonateLeave(), On, ("OwnedTables", "DonateLeave:DonateFrom_EmployeeId"));
        Expect.Contains(api, "protected override Expression<Func<DonateLeave, int>>? Owner => c => c.DonateFrom_EmployeeId;");
    }

    [TestMethod]
    public void A_table_under_an_owned_table_belongs_to_the_owner_of_its_parent()
    {
        var project = Project(On, Owned);
        var link = OwnedTable.Through(Detail(), project)!;
        Assert.AreEqual(new OwnerLink("TimeSheet", "TimeSheetId", "TimeSheetId", "EmployeeId"), link);
        Assert.IsNull(OwnedTable.OwnerColumn(Detail(), project), "it has no owner column of its own");
        Assert.IsTrue(OwnedTable.IsScoped(Detail(), project));
        Assert.IsFalse(OwnedTable.IsListedButUnusable(Detail(), Project(On, ("OwnedTables", "TimeSheet, TimeSheetDetail"))));

        Assert.IsNull(OwnedTable.Through(Detail(), Project(On, Owned, ("OwnedChildren", "false"))));
        Assert.IsNull(OwnedTable.Through(Detail(parentIdNullable: true), project), "a nullable link leaves rows with no owner");
        Assert.IsNull(OwnedTable.Through(Detail(parent: "Other"), project), "the parent is not owned");
        Assert.IsNull(OwnedTable.Through(Detail(), Project(Owned)), "nothing is scoped without ApiAuth");
        Assert.IsNull(OwnedTable.Through(TimeSheet(), project), "a table with its own owner column keeps it");
        Assert.AreEqual("PersonId", OwnedTable.Through(Detail(), Project(On, ("OwnedTables", "TimeSheet:PersonId")))!.ParentOwner);
    }

    [TestMethod]
    public async Task The_class_of_a_child_table_scopes_its_rows_through_the_parent()
    {
        string child = await RenderTable("API_Crud_v1.tt", Detail(), On, Owned);
        Expect.Contains(child, "using Microsoft.EntityFrameworkCore;");
        Expect.Contains(child, "protected override bool OwnedThroughParent => true;");
        Expect.Contains(child, "rows.Where(c => context.Set<TimeSheet>().Any(p => p.TimeSheetId == c.TimeSheetId && owners.Contains(p.EmployeeId)));");
        Expect.Contains(child, "await context.Set<TimeSheet>().Where(p => p.TimeSheetId == row.TimeSheetId).Select(p => (int?)p.EmployeeId).FirstOrDefaultAsync();");
        Expect.DoesNotContain(child, "Owner =>");
        Expect.DoesNotContain(child, "OwnedTables names this table");

        foreach (var off in new[] { new[] { On, Owned, ("OwnedChildren", "false") }, new[] { Owned }, new[] { On } })
        {
            string plain = await RenderTable("API_Crud_v1.tt", Detail(), off);
            Expect.DoesNotContain(plain, "OwnedThroughParent");
            Expect.DoesNotContain(plain, "Microsoft.EntityFrameworkCore");
        }

        string search = await RenderTable("API_Search_v1.tt", Detail(), On, Owned, ("AccessMode", "Ef"));
        Expect.Contains(search, "query = query.Where(row => context.Set<TimeSheet>().Any(p => p.TimeSheetId == row.TimeSheetId && owners.Contains(p.EmployeeId)));");
        string routines = await RenderTable("API_Search_v1.tt", Detail(), On, Owned);
        Expect.Contains(routines, "type: \"" + ProblemTypes.SearchNotScoped + "\", title: \"Search is not scoped\"");
    }

    [TestMethod]
    public async Task CrudApi_scopes_a_child_table_through_its_parent_and_checks_before_it_saves_child_rows()
    {
        string cs = await CrudApiTests.Essential(On, ("EditableChildren", "Department.DepartmentTeam"));
        Expect.Contains(cs, "protected virtual bool OwnedThroughParent => false;");
        Expect.Contains(cs, "protected virtual IQueryable<TEntity> ScopeThroughParent(AcmeContext context, IQueryable<TEntity> rows, int[] owners) => rows.Where(_ => false);");
        Expect.Contains(cs, "if (Owner is null && !OwnedThroughParent)");
        Expect.Contains(cs, "return ScopeThroughParent(context, rows, owners);");
        Expect.Contains(cs, "if (await ParentOwnerAsync(context, newRow) is not { } parentOwner || !owners.Contains(parentOwner))");
        Expect.Contains(cs, "int? newOwner = OwnedThroughParent ? await ParentOwnerAsync(context, updatedRow) : OwnerOf(updatedRow);");
        Expect.Contains(cs, "return await ScopeThroughParent(context, row, owners).AnyAsync() ? null : NotYours();");

        // the lists of an editable child are saved after the parent's own owner check, and each row is tied to that parent
        Assert.IsLessThan(cs.IndexOf("await ChildRowsSupport.SaveAsync(context, EditableChildren, KeyOf(newRow)", StringComparison.Ordinal), cs.IndexOf("return NotYours();", StringComparison.Ordinal));
        Assert.IsLessThan(cs.IndexOf("await ChildRowsSupport.SaveAsync(context, EditableChildren, id, children)", StringComparison.Ordinal), cs.IndexOf("RefuseOtherOwnersAsync(context, id) is { } refused", cs.IndexOf("private async Task<IResult> UpdateAsync(", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task The_generated_tests_sign_in_and_the_requests_and_documents_carry_the_token()
    {
        string fixture = await RenderProject("API_EssentialTests_v1.tt", On, ("ApiTests", "true"));
        var files = GeneratedFiles.Split(fixture).ToDictionary(f => f.RelativePath, f => f.Content.ReplaceLineEndings("\n"));
        Expect.Contains(files["ApiFixture.cs"], "public static async Task Start(TestContext context)");
        Expect.Contains(files["ApiFixture.cs"], "Environment.SetEnvironmentVariable(\"Jwt__Key\"");
        Expect.Contains(files["ApiFixture.cs"], "Client.DefaultRequestHeaders.Authorization = new(\"Bearer\", token);");
        Expect.Contains(files["ApiFixture.cs"], "public static HttpClient Anonymous()");
        Expect.Contains(files["AuthApiTests.cs"], "A_wrong_password_answers_401");
        Expect.DoesNotContain(files["ApiFixture.cs"], "AdminPassword\", \"");

        string plainFixture = await RenderProject("API_EssentialTests_v1.tt", ("ApiTests", "true"));
        Expect.Contains(plainFixture, "public static void Start(TestContext context)");
        Expect.DoesNotContain(plainFixture, "Jwt__Key");
        Expect.DoesNotContain(plainFixture, "AuthApiTests");

        string test = await RenderTable("API_Test_v1.tt", TimeSheet(), On, ("ApiTests", "true"));
        Expect.Contains(test, "Without_a_token_the_list_answers_401");
        Expect.DoesNotContain(await RenderTable("API_Test_v1.tt", TimeSheet(), ("ApiTests", "true")), "Without_a_token");

        string http = await RenderTable("API_Http_v1.tt", TimeSheet(), On);
        Assert.StartsWith("@token = ", http);
        Expect.Contains(http, "GET {{host}}/api/timesheets\nAuthorization: Bearer {{token}}\nAccept: application/json");
        Expect.DoesNotContain(await RenderTable("API_Http_v1.tt", TimeSheet()), "Authorization");
    }

    private static string DatabaseFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_auth_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Employee (EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(50) NOT NULL)",
            "CREATE TABLE Department (DepartmentId INTEGER PRIMARY KEY AUTOINCREMENT, Title VARCHAR(50) NOT NULL)",
            "CREATE TABLE TimeSheet (TimeSheetId INTEGER PRIMARY KEY AUTOINCREMENT, EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId), Notes VARCHAR(100) NOT NULL)"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static string Pick(Dictionary<string, string> files, string suffix) =>
        files.Where(f => f.Key.EndsWith(suffix, StringComparison.Ordinal)).Select(f => f.Value).SingleOrDefault()
        ?? throw new AssertFailedException($"no generated file ends with {suffix}; the files are: {string.Join(", ", files.Keys)}");

    private static async Task<Dictionary<string, string>> Generate(string only, params (string Key, string Value)[] extra)
    {
        string database = DatabaseFile();
        string output = Path.Combine(Path.GetTempPath(), "codegen_auth_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api", ["ContextName"] = "ShopContext",
                ["ContextNamespace"] = "Shop.Data", ["EntityNamespace"] = "Shop.Entities", ["ApiAuth"] = "true", ["OwnedTables"] = "TimeSheet"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = ["Api"], OutputDirectory = output, DatabaseName = database, Schema = "main", OnlyTemplates = only.Split(',')
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [TestMethod]
    public async Task The_search_of_an_owned_table_is_limited_to_the_owners_in_Ef_mode_and_refused_in_routine_mode()
    {
        var ef = await Generate("API_Search", ("AccessMode", "Ef"));
        string search = Pick(ef, "TimeSheetSearchApi.cs");
        Expect.Contains(search, "HttpContext http,");
        Expect.Contains(search, "int[]? owners = (await http.RequestServices.GetRequiredService<IOwnerScope>().VisibleOwnersAsync(http.User, http.RequestAborted))?.ToArray();");
        Expect.Contains(search, "query = query.Where(row => owners.Contains(row.EmployeeId));");
        Expect.DoesNotContain(search, "Search is not scoped");

        string routines = await RenderTable("API_Search_v1.tt", TimeSheet(), On, Owned);
        Expect.Contains(routines, "if (owners is not null)\n            return Results.Problem(statusCode: 403, type: \"" + ProblemTypes.SearchNotScoped + "\", title: \"Search is not scoped\"");
        Expect.DoesNotContain(routines, "owners.Contains");

        var unlisted = await Generate("API_Search", ("AccessMode", "Ef"), ("OwnedTables", "Employee"), ("OwnedChildren", "false"));
        var underEmployee = await Generate("API_Search", ("AccessMode", "Ef"), ("OwnedTables", "Employee"));
        Expect.Contains(Pick(underEmployee, "TimeSheetSearchApi.cs"), "context.Set<Employee>().Any(p => p.EmployeeId == row.EmployeeId && owners.Contains(p.EmployeeId))");
        Expect.DoesNotContain(Pick(unlisted, "TimeSheetSearchApi.cs"), "IOwnerScope");
        Expect.DoesNotContain(Pick(unlisted, "TimeSheetSearchApi.cs"), "HttpContext");

        var plainEf = await Generate("API_Search", ("AccessMode", "Ef"), ("ApiAuth", "false"));
        Expect.DoesNotContain(Pick(plainEf, "TimeSheetSearchApi.cs"), "IOwnerScope");
    }

    [TestMethod]
    public async Task The_CSV_routes_of_an_owned_table_are_for_the_administrator_only()
    {
        var files = await Generate("API_Csv", ("ApiCsv", "true"));
        string csv = Pick(files, "Csv.cs");
        Expect.Contains(csv, "MapTable<TimeSheet, ShopContext>(app, ");
        Expect.Contains(csv, ", true);");
        Expect.Contains(csv, ", false);");
        Expect.Contains(csv, "string file, CsvColumn[] columns, bool adminOnly)");
        Expect.Contains(csv, ".WithName(\"Export\" + file + \"Csv\").AdminOnly(adminOnly);");
        Expect.Contains(csv, ".WithName(\"Import\" + file + \"Csv\").AdminOnly(adminOnly);");
        Expect.Contains(csv, "builder.RequireAuthorization(AuthNames.AdministratorPolicy)");

        var plain = await Generate("API_Csv", ("ApiCsv", "true"), ("ApiAuth", "false"));
        Expect.DoesNotContain(Pick(plain, "Csv.cs"), "adminOnly");
    }

    [TestMethod]
    public async Task The_OpenAPI_document_declares_the_bearer_scheme_and_the_login_route()
    {
        var with = await Generate("API_OpenApi", ("ApiDocs", "true"));
        string yaml = Pick(with, "openapi.yaml");
        Expect.Contains(yaml, "security:\n  - bearerAuth: []\npaths:\n  /api/auth/login:");
        Expect.Contains(yaml, "      security: []\n");
        Expect.Contains(yaml, "  securitySchemes:\n    bearerAuth:\n      type: http\n      scheme: bearer\n      bearerFormat: JWT");
        Expect.Contains(yaml, "    LoginResponse:");

        var without = await Generate("API_OpenApi", ("ApiDocs", "true"), ("ApiAuth", "false"));
        string plain = Pick(without, "openapi.yaml");
        Expect.DoesNotContain(plain, "bearerAuth");
        Expect.DoesNotContain(plain, "/api/auth/login");
    }

    [TestMethod]
    public void A_setting_that_belongs_to_the_sign_in_is_on_the_Sign_in_tab()
    {
        foreach (string key in new[] { "ApiAuth", "OwnedTables", "OwnerColumn", "OwnedChildren", "AuthRoleClaim", "AuthOwnerClaim", "AuthAdminRole" })
            Assert.AreEqual("Sign-in", ProjectSettingGroups.TabOf(key), key);
    }
}
