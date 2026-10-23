using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The integration tests API_Test writes per table, the test project API_EssentialTests writes, and the setting that adds them to a plan. </summary>
[TestClass]
public class ApiTestsTemplateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerStatusId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 3),
        Sample.Column("BillingEmail", SqlDbType.VarChar, characters: 100, ordinal: 4),
        Sample.Column("Rating", SqlDbType.Int, ordinal: 5, check: new CheckRange(1, false, 5, false)),
        Sample.Column("Tier", SqlDbType.VarChar, characters: 10, ordinal: 6, choices: ["Gold", "Silver", "Bronze"]),
    ],
    [new ForeignKeyModel { ConstraintName = "FK_s", ReferencingColumns = ["CustomerStatusId"], ReferencedSchema = "dbo", ReferencedTable = "CustomerStatus", ReferencedColumns = ["CustomerStatusId"] }]);

    private static async Task<string> Render(TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Test_v1.tt"), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public async Task A_test_class_per_table_adds_reads_changes_and_removes_a_row()
    {
        string tests = await Render(Customer(), Project());

        Expect.Contains(tests, "namespace Acme.Api.Tests;\n\n[TestClass]\npublic class CustomerApiTests");
        Expect.Contains(tests, "private const string Route = \"/api/customers\";");
        Expect.Contains(tests, "[\"customerId\"] = 0,");
        Expect.Contains(tests, "[\"customerStatusId\"] = await ApiFixture.ExistingKeyAsync(\"/api/customerstatus\", \"customerStatusId\", 1, \"CustomerStatus\"),");
        Expect.Contains(tests, "[\"name\"] = ApiFixture.Unique(\"Sample name\", 50),");
        Expect.Contains(tests, "[\"billingEmail\"] = \"someone@example.com\",");
        Expect.Contains(tests, "[\"rating\"] = 1,");
        Expect.Contains(tests, "[\"tier\"] = \"Gold\",");
        Expect.Contains(tests, "string after = ApiFixture.Unique(\"Changed\", 50);\n            row[\"name\"] = after;");
        Expect.Contains(tests, "Assert.AreEqual(HttpStatusCode.NotFound, gone.StatusCode");
        Expect.Contains(tests, "A_row_that_is_not_there_answers_404");
        Expect.Contains(tests, "The_search_answers_a_page");
        Expect.DoesNotContain(tests, "is_refused");
    }

    [TestMethod]
    public async Task A_parent_a_row_points_at_is_checked_for_a_refused_delete_unless_the_key_removes_or_changes_the_row()
    {
        string refused = await Render(Customer(), Project());
        Expect.Contains(refused, "public async Task A_CustomerStatus_row_that_CustomerStatusId_points_at_cannot_be_removed()");
        Expect.Contains(refused, "var refused = await client.DeleteAsync($\"/api/customerstatus/{parentId}\");");
        Expect.Contains(refused, "Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode");

        foreach (var rule in new[] { ForeignKeyDeleteRule.Cascade, ForeignKeyDeleteRule.SetNull, ForeignKeyDeleteRule.SetDefault })
        {
            var table = Customer();
            table.ForeignKeys[0] = new ForeignKeyModel { ConstraintName = "FK_s", ReferencingColumns = ["CustomerStatusId"], ReferencedSchema = "dbo", ReferencedTable = "CustomerStatus", ReferencedColumns = ["CustomerStatusId"], DeleteRule = rule };
            Expect.DoesNotContain(await Render(table, Project()), "cannot_be_removed");
        }
    }

    [TestMethod]
    [DataRow("NO_ACTION", ForeignKeyDeleteRule.NoAction)]
    [DataRow("NO ACTION", ForeignKeyDeleteRule.NoAction)]
    [DataRow("a", ForeignKeyDeleteRule.NoAction)]
    [DataRow("RESTRICT", ForeignKeyDeleteRule.Restrict)]
    [DataRow("r", ForeignKeyDeleteRule.Restrict)]
    [DataRow("CASCADE", ForeignKeyDeleteRule.Cascade)]
    [DataRow("c", ForeignKeyDeleteRule.Cascade)]
    [DataRow("SET_NULL", ForeignKeyDeleteRule.SetNull)]
    [DataRow("SET NULL", ForeignKeyDeleteRule.SetNull)]
    [DataRow("n", ForeignKeyDeleteRule.SetNull)]
    [DataRow("SET_DEFAULT", ForeignKeyDeleteRule.SetDefault)]
    [DataRow("d", ForeignKeyDeleteRule.SetDefault)]
    [DataRow(null, ForeignKeyDeleteRule.NoAction)]
    public void Each_database_words_the_delete_rule_its_own_way(string? text, ForeignKeyDeleteRule expected)
    {
        Assert.AreEqual(expected, ForeignKeyDeleteRules.Parse(text));
        Assert.AreEqual(expected is ForeignKeyDeleteRule.NoAction or ForeignKeyDeleteRule.Restrict, expected.RefusesInUse());
    }

    private static TableModel Tag() => Sample.Table("Tag",
    [
        Sample.Column("TagId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)
    ]);

    private static TableModel CustomerTag() => Sample.Table("CustomerTag",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column("TagId", SqlDbType.Int, primaryKey: true, ordinal: 2)
    ],
    [
        Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "Name"),
        Sample.ForeignKey("TagId", "Tag", "TagId", "Name")
    ]);

    [TestMethod]
    [DataRow("PostgreSql", "Testcontainers.PostgreSql", "new PostgreSqlBuilder(\"postgres:17-alpine\").WithDatabase(\"codegen_test\").Build()", "PostgreSqlContainer")]
    [DataRow("MySql", "Testcontainers.MySql", "new MySqlBuilder(\"mysql:8.4\").WithDatabase(\"codegen_test\").Build()", "MySqlContainer")]
    [DataRow("SqlServer", "Testcontainers.MsSql", "new MsSqlBuilder(\"mcr.microsoft.com/mssql/server:2022-latest\").Build()", "MsSqlContainer")]
    public async Task A_test_container_of_the_projects_database_is_started_and_filled_when_asked(string provider, string package, string build, string type)
    {
        var plain = await Repo.Cache.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project(("ApiTests", "true"), ("DatabaseProvider", provider)));
        var asked = await Repo.Cache.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project(("ApiTests", "true"), ("ApiTestContainers", "true"), ("ApiTestSchema", "sql/create.sql,sql/functions"), ("DatabaseProvider", provider)));
        Assert.IsTrue(plain.Success && asked.Success, string.Join(" | ", plain.Errors.Concat(asked.Errors)));
        string without = plain.GeneratedText!.ReplaceLineEndings("\n"), with = asked.GeneratedText!.ReplaceLineEndings("\n");

        Expect.DoesNotContain(without, "Testcontainers");
        Expect.Contains(with, "<PackageReference Include=\"" + package + "\" Version=\"4.15.0\" />");
        Expect.Contains(with, "private static " + type + "? _container;");
        Expect.Contains(with, "_container = " + build + ";");
        Expect.Contains(with, "private static readonly string[] SchemaScripts = [\"sql/create.sql\", \"sql/functions\"];");
        Expect.Contains(with, "public static async Task Start(TestContext context)");
        Expect.Contains(with, "connection = await StartContainerAsync();");
        Expect.Contains(with, "_container.ExecScriptAsync(");
        Expect.Contains(with, "_container?.DisposeAsync()");
        Expect.Contains(with, "The test container could not be made: ");
        Expect.Contains(with, "ApiFolder()");
    }

    [TestMethod]
    public async Task SQLite_needs_no_test_container_whatever_the_setting_says()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project(("ApiTests", "true"), ("ApiTestContainers", "true"), ("DatabaseProvider", "Sqlite"), ("DatabaseName", "shop.db")));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.DoesNotContain(result.GeneratedText!, "Testcontainers");
        Expect.Contains(result.GeneratedText!, "File.Copy(source, _scratchFile);");
    }

    [TestMethod]
    public async Task A_junction_gets_a_test_class_of_its_list_link_and_unlink_and_an_ordinary_table_is_refused()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_JunctionTest_v1.tt"), CustomerTag(), Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string tests = result.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(tests, "public class CustomerTagJunctionApiTests");
        Expect.Contains(tests, "private const string Route = \"/api/customertags\";");
        Expect.Contains(tests, "await ApiFixture.ExistingKeyAsync(\"/api/customers\", \"customerId\", 1, \"Customer\")");
        Expect.Contains(tests, "A_target_can_be_linked_and_unlinked_and_either_can_be_repeated");
        Expect.Contains(tests, "The_list_names_every_target_with_its_display_columns");
        Expect.Contains(tests, "A_row_that_is_not_there_cannot_be_linked");
        Expect.Contains(tests, "targetId = int.MaxValue");
        Expect.DoesNotContain(tests, "Without_a_token");

        var signedIn = await Repo.Cache.RunAsync(Repo.Template("API_JunctionTest_v1.tt"), CustomerTag(), Project(("ApiAuth", "true"), ("WriteRoles", "Administrator,Manager")));
        Assert.IsTrue(signedIn.Success, string.Join(" | ", signedIn.Errors));
        Expect.Contains(signedIn.GeneratedText!, "Without_a_token_the_junction_answers_401");
        Expect.Contains(signedIn.GeneratedText!, "A_role_the_write_rule_does_not_list_cannot_link");

        var ordinary = await Repo.Cache.RunAsync(Repo.Template("API_JunctionTest_v1.tt"), Customer(), Project());
        Assert.IsFalse(ordinary.Success);
        Expect.Contains(string.Join(" ", ordinary.Errors), "is not one");

        var noApi = await Repo.Cache.RunAsync(Repo.Template("API_JunctionTest_v1.tt"), CustomerTag(), Project(("NoApiTables", "Tag")));
        Assert.IsFalse(noApi.Success);
        Expect.Contains(string.Join(" ", noApi.Errors), "needs an API for both sides");
    }

    [TestMethod]
    public async Task The_clone_sort_and_unique_value_checks_are_written_for_the_tables_that_have_them()
    {
        var unique = Sample.Table("Customer",
        [
            Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("AccountNumber", SqlDbType.VarChar, characters: 20, ordinal: 2, inUniqueIndex: true),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 3),
        ], indexes: [new IndexModel("UX_Customer_AccountNumber", true, ["AccountNumber"])]);
        string tests = await Render(unique, Project());

        Expect.Contains(tests, "public async Task A_row_can_be_cloned()");
        Expect.Contains(tests, "client.PostAsync($\"{Route}/{id}/clone\", null)");
        Expect.Contains(tests, "[DataRow(\"accountnumber\")]");
        Expect.Contains(tests, "[DataRow(\"name\")]");
        Expect.Contains(tests, "The_search_sorts_by_each_column_it_offers(string column)");
        Expect.Contains(tests, "A_sort_column_the_search_does_not_offer_is_ignored");
        Expect.Contains(tests, "A_second_row_with_a_value_that_must_be_unique_is_refused");
        Expect.Contains(tests, "again[\"accountNumber\"] = body[\"accountNumber\"]!.GetValue<string>();");

        string plain = await Render(Customer(), Project(("NoCloneTables", "Customer")));
        Expect.DoesNotContain(plain, "A_row_can_be_cloned");
        Expect.DoesNotContain(plain, "must_be_unique");
    }

    [TestMethod]
    public async Task The_limit_tests_are_written_only_with_ApiValidation()
    {
        string tests = await Render(Customer(), Project(("ApiValidation", "true")));

        Expect.Contains(tests, "Text_longer_than_the_column_is_refused");
        Expect.Contains(tests, "new string('x', 51)");
        Expect.Contains(tests, "A_number_past_its_limit_is_refused");
        Expect.Contains(tests, "[\"rating\"] = 6;");
    }

    [TestMethod]
    public async Task A_key_the_database_does_not_number_gets_a_random_one_and_a_parent_without_an_api_gets_the_sample()
    {
        var table = Sample.Table("Widget",
        [
            Sample.Column("WidgetId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("Active", SqlDbType.Bit, ordinal: 2)
        ]);
        string tests = await Render(table, Project());

        Expect.Contains(tests, "[\"widgetId\"] = ApiFixture.NewKey(1000000000, 2000000000),");
        Expect.Contains(tests, "bool after = !row[\"active\"]!.GetValue<bool>();");
    }

    [TestMethod]
    public async Task A_table_with_no_crud_api_is_refused_with_a_reason()
    {
        var composite = Sample.Table("Link",
        [
            Sample.Column("AId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("BId", SqlDbType.Int, primaryKey: true, ordinal: 2)
        ]);
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Test_v1.tt"), composite, Project());

        Assert.IsFalse(result.Success);
        Expect.Contains(string.Join(" ", result.Errors), "API_Test writes tests for the tables API_Crud writes an API for");
    }

    [TestMethod]
    public async Task The_test_project_is_written_only_when_asked_and_points_at_the_api()
    {
        var off = await Repo.Cache.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project(("ApiTests", "false")));
        var sqlite = await Repo.Cache.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project(("ApiTests", "true"), ("DatabaseProvider", "Sqlite"), ("DatabaseName", "shop.db")));
        var server = await Repo.Cache.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project(("ApiTests", "true")));
        Assert.IsTrue(off.Success && sqlite.Success && server.Success, string.Join(" | ", off.Errors.Concat(sqlite.Errors).Concat(server.Errors)));

        Assert.AreEqual("", off.GeneratedText!.Trim());
        string lite = sqlite.GeneratedText!.Replace("\r\n", "\n"), other = server.GeneratedText!.Replace("\r\n", "\n");
        foreach (string text in new[] { lite, other })
        {
            Expect.Contains(text, "@@@FILE Acme.Api.Tests.csproj@@@");
            Expect.Contains(text, "<ProjectReference Include=\"../Acme.Api/Acme.Api.csproj\" />");
            Expect.Contains(text, "private const string ConnectionVariable = \"ACME_TEST_CONNECTION\";");
            Expect.Contains(text, "Assert.Inconclusive(");
        }
        Expect.Contains(lite, "File.Copy(source, _scratchFile);");
        Expect.Contains(lite, "Path.Combine(ApiFolder(), \"shop.db\")");
        Expect.DoesNotContain(other, "File.Copy");
        foreach (string text in new[] { lite, other })
        {
            // a parent with no rows gets one from its own tests' body, and it is removed again when the run ends
            Expect.Contains(text, "private static async Task<JsonNode> SeedAsync(string route, string keyProperty, string? parentTests)");
            Expect.Contains(text, "Type.GetType(typeof(ApiFixture).Namespace + \".\" + parentTests + \"ApiTests\")");
            Expect.Contains(text, "using var created = await Client.PostAsJsonAsync(route, row);");
            Expect.Contains(text, "Seeded.Add((route, key!.ToString()));");
            Expect.Contains(text, "RemoveSeeded();\n        Client?.Dispose();");
            Expect.Contains(text, "and the parent has no tests to take a row from.");
        }
    }

    [TestMethod]
    public async Task A_guid_foreign_key_takes_a_parent_row_like_a_text_one_and_the_body_can_be_built_by_the_child_tests()
    {
        var item = Sample.Table("ItemInventory",
        [
            Sample.Column("Id", SqlDbType.UniqueIdentifier, primaryKey: true, ordinal: 1),
            Sample.Column("ProductId", SqlDbType.UniqueIdentifier, ordinal: 2),
            Sample.Column("Cube", SqlDbType.VarChar, characters: 50, ordinal: 3)
        ],
        [Sample.ForeignKey("ProductId", "Product", "ProductId", "Name")]);
        string tests = await Render(item, Project());

        Expect.Contains(tests, "[\"productId\"] = await ApiFixture.ExistingCodeAsync(\"/api/products\", \"productId\", \"00000000-0000-0000-0000-000000000001\", \"Product\"),");
        Expect.Contains(tests, "internal static async Task<JsonObject> NewBodyAsync() => new()");
    }

    [TestMethod]
    public async Task The_program_is_public_for_the_test_project_only_when_asked()
    {
        var plain = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(("ApiTests", "false")));
        var tests = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(("ApiTests", "true")));
        Assert.IsTrue(plain.Success && tests.Success, string.Join(" | ", plain.Errors.Concat(tests.Errors)));

        Expect.DoesNotContain(plain.GeneratedText!, "partial class Program");
        Expect.Contains(tests.GeneratedText!, "public partial class Program;");
    }

    [TestMethod]
    public void The_setting_adds_the_template_and_the_test_project_has_its_own_folder()
    {
        CollectionAssert.DoesNotContain(Project(("ApiTests", "false")).ImpliedPlanTemplates.ToArray(), "API_Test");
        CollectionAssert.IsSubsetOf(new[] { "API_Test", "API_JunctionTest" }, Project(("ApiTests", "true")).ImpliedPlanTemplates.ToArray());
        Assert.AreEqual("Acme.Api.Tests", Project().OutputFolderOf("apitests"));
        Assert.AreEqual("Tests/Api", Project(("OutputApiTests", "Tests/Api")).OutputFolderOf("apitests"));
        Assert.AreEqual("ApiTests", TemplateCatalog.Discover(Repo.TemplatesDirectory).Single(t => t.Name == "API_Test").Config.OutputRoot);
    }

    [TestMethod]
    public void The_api_test_step_runs_the_test_project()
    {
        Assert.IsNull(ProjectBuilder.CommandFor(Project(("ApiTests", "false")), "Api", "test"));
        string? command = ProjectBuilder.CommandFor(Project(("ApiTests", "true")), "Api", "test");
        Expect.Contains(command!, "dotnet test");
        Expect.Contains(command!, "Acme.Api.Tests");
    }
}

/// <summary> The Dockerfiles the web front ends can have. </summary>
[TestClass]
public class FrontEndDockerTemplateTests
{
    private static ProjectSettings Project() => ProjectSettings.FromValues([new("ProjectName", "Acme")]);

    [TestMethod]
    public async Task Each_front_end_builds_with_node_and_is_served_by_nginx_with_the_api_forwarded()
    {
        foreach ((string template, string dist, string proxy) in new[] { ("TSX_EssentialDocker_v1.tt", "/app/dist /usr/share/nginx/html", "proxy_pass http://api_backend;"), ("TS_EssentialDocker_v1.tt", "/app/dist/frontend/browser /usr/share/nginx/html", "proxy_pass http://api_backend;") })
        {
            var result = await Repo.Cache.RunAsync(Repo.Template(template), Project());
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            string text = result.GeneratedText!.Replace("\r\n", "\n");

            Expect.Contains(text, "@@@FILE Dockerfile@@@\nFROM node:24-alpine AS build");
            Expect.Contains(text, "RUN npm run build");
            Expect.Contains(text, "COPY --from=build " + dist);
            Expect.Contains(text, "ENV API_UPSTREAM=api:8080");
            Expect.Contains(text, "@@@FILE nginx.conf.template@@@");
            Expect.Contains(text, proxy);
            Expect.Contains(text, "try_files $uri $uri/ /index.html;");
            Expect.Contains(text, "@@@FILE .dockerignore@@@\nnode_modules/");
        }
    }

    [TestMethod]
    public async Task Each_front_end_nginx_config_compresses_caches_hashed_files_and_keeps_the_api_connection_open()
    {
        // Vite writes its hashed files under /assets/; Angular puts an 8 character hash in the file name
        foreach ((string template, string hashedFiles) in new[] { ("TSX_EssentialDocker_v1.tt", "location /assets/ {"), ("TS_EssentialDocker_v1.tt", "location ~ \"-[A-Z0-9]{8}\\.(?:js|css|woff2?)$\" {") })
        {
            var result = await Repo.Cache.RunAsync(Repo.Template(template), Project());
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            string text = result.GeneratedText!.Replace("\r\n", "\n");

            Expect.Contains(text, "gzip on;");
            Expect.Contains(text, "gzip_proxied any;");
            Expect.Contains(text, "gzip_types application/javascript");
            Expect.Contains(text, hashedFiles + "\n        add_header Cache-Control \"public, max-age=31536000, immutable\";");
            Expect.Contains(text, "location = /index.html {\n        add_header Cache-Control \"no-cache\";");
            Expect.Contains(text, "upstream api_backend {\n    server ${API_UPSTREAM};\n    keepalive 16;");
            Expect.Contains(text, "proxy_http_version 1.1;\n        proxy_set_header Connection \"\";");
        }
    }

    [TestMethod]
    public void The_front_end_docker_groups_are_optional()
    {
        var groups = EssentialsCatalog.All(Repo.TemplatesDirectory).Where(g => g.Name == "Docker").ToList();

        CollectionAssert.AreEquivalent(new[] { "Angular", "React" }, groups.Select(g => g.Stack).ToArray());
        Assert.IsTrue(groups.All(g => !g.DefaultOn));
    }

    [TestMethod]
    public async Task The_react_build_files_include_the_vite_types_the_fetch_helper_needs()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("TSX_EssentialBuild_v1.tt"), Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));

        Expect.Contains(result.GeneratedText!.Replace("\r\n", "\n"), "@@@FILE src/vite-env.d.ts@@@\n/// <reference types=\"vite/client\" />");
    }
}
