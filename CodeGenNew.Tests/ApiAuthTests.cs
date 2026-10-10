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
        Expect.Contains(auth, "Results.Problem(statusCode: 401, title: \"Wrong user name or password\")");
        Expect.Contains(auth, "Results.Problem(statusCode: 429, title: \"Too many attempts\"");
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
        Expect.Contains(with, "Results.Problem(statusCode: 403, title: \"Not your row\"");
        Expect.Contains(with, "var key = Key;");
        Expect.Contains(with, "var column = Owner!;");
        // every route that reaches one row asks first
        Assert.AreEqual(4, CountOf(with, "if (await RefuseOtherOwnersAsync(context, id) is { } refused)"), "get, delete, clone and update");
        Expect.Contains(with, "if (await VisibleOwnersAsync() is { } owners && !owners.Contains(OwnerOf(updatedRow)))");
        Expect.Contains(with, "if (OwnerOf(newRow) == 0 && owners.Length == 1)");
        Expect.Contains(with, "Scope(context.Set<TEntity>().AsNoTracking(), owners)");

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
        Expect.Contains(routines, "if (owners is not null)\n            return Results.Problem(statusCode: 403, title: \"Search is not scoped\"");
        Expect.DoesNotContain(routines, "owners.Contains");

        var unlisted = await Generate("API_Search", ("AccessMode", "Ef"), ("OwnedTables", "Employee"));
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
    public void A_setting_that_belongs_to_the_sign_in_is_on_the_API_tab()
    {
        foreach (string key in new[] { "ApiAuth", "OwnedTables", "OwnerColumn", "AuthRoleClaim", "AuthOwnerClaim", "AuthAdminRole" })
            Assert.AreEqual("API", ProjectSettingGroups.TabOf(key), key);
    }
}
