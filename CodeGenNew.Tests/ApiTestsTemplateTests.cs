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
        var result = await TemplateRunner.RunAsync(Repo.Template("API_Test_v1.tt"), table, project);
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
        Expect.Contains(tests, "[\"customerStatusId\"] = await ApiFixture.ExistingKeyAsync(\"/api/customerstatus\", \"customerStatusId\", 1),");
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
        var result = await TemplateRunner.RunAsync(Repo.Template("API_Test_v1.tt"), composite, Project());

        Assert.IsFalse(result.Success);
        Expect.Contains(string.Join(" ", result.Errors), "API_Test writes tests for the tables API_Crud writes an API for");
    }

    [TestMethod]
    public async Task The_test_project_is_written_only_when_asked_and_points_at_the_api()
    {
        var off = await TemplateRunner.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project());
        var sqlite = await TemplateRunner.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project(("ApiTests", "true"), ("DatabaseProvider", "Sqlite"), ("DatabaseName", "shop.db")));
        var server = await TemplateRunner.RunAsync(Repo.Template("API_EssentialTests_v1.tt"), Project(("ApiTests", "true")));
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
    }

    [TestMethod]
    public async Task The_program_is_public_for_the_test_project_only_when_asked()
    {
        var plain = await TemplateRunner.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project());
        var tests = await TemplateRunner.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(("ApiTests", "true")));
        Assert.IsTrue(plain.Success && tests.Success, string.Join(" | ", plain.Errors.Concat(tests.Errors)));

        Expect.DoesNotContain(plain.GeneratedText!, "partial class Program");
        Expect.Contains(tests.GeneratedText!, "public partial class Program;");
    }

    [TestMethod]
    public void The_setting_adds_the_template_and_the_test_project_has_its_own_folder()
    {
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToArray(), "API_Test");
        CollectionAssert.Contains(Project(("ApiTests", "true")).ImpliedPlanTemplates.ToArray(), "API_Test");
        Assert.AreEqual("Acme.Api.Tests", Project().OutputFolderOf("apitests"));
        Assert.AreEqual("Tests/Api", Project(("OutputApiTests", "Tests/Api")).OutputFolderOf("apitests"));
        Assert.AreEqual("ApiTests", TemplateCatalog.Discover(Repo.TemplatesDirectory).Single(t => t.Name == "API_Test").Config.OutputRoot);
    }

    [TestMethod]
    public void The_api_test_step_runs_the_test_project()
    {
        Assert.IsNull(ProjectBuilder.CommandFor(Project(), "Api", "test"));
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
        foreach ((string template, string dist) in new[] { ("TSX_EssentialDocker_v1.tt", "/app/dist /usr/share/nginx/html"), ("TS_EssentialDocker_v1.tt", "/app/dist/frontend/browser /usr/share/nginx/html") })
        {
            var result = await TemplateRunner.RunAsync(Repo.Template(template), Project());
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            string text = result.GeneratedText!.Replace("\r\n", "\n");

            Expect.Contains(text, "@@@FILE Dockerfile@@@\nFROM node:24-alpine AS build");
            Expect.Contains(text, "RUN npm run build");
            Expect.Contains(text, "COPY --from=build " + dist);
            Expect.Contains(text, "ENV API_UPSTREAM=api:8080");
            Expect.Contains(text, "@@@FILE nginx.conf.template@@@");
            Expect.Contains(text, "proxy_pass http://${API_UPSTREAM};");
            Expect.Contains(text, "try_files $uri $uri/ /index.html;");
            Expect.Contains(text, "@@@FILE .dockerignore@@@\nnode_modules/");
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
        var result = await TemplateRunner.RunAsync(Repo.Template("TSX_EssentialBuild_v1.tt"), Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));

        Expect.Contains(result.GeneratedText!.Replace("\r\n", "\n"), "@@@FILE src/vite-env.d.ts@@@\n/// <reference types=\"vite/client\" />");
    }
}
