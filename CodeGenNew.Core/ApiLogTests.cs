using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated API tests of a log table (LogTables): it is only read, so the tests do not add, change or remove rows. They check that no route changes rows, that the search answers a page and
/// takes the date range, that an empty range is an empty page, that a bad date is refused, and that the export takes the range. </summary>
public static class ApiLogTests
{
    public static string Text(ApiTestPlan plan, ProjectSettings project)
    {
        string route = plan.Route;
        string absent = plan.AbsentKey;
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        string from = LogTable.FromParameter, to = LogTable.ToParameter;

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L("[TestClass]");
        L($"public class {plan.Singular}ApiTests");
        L("{");
        L($"    private const string Route = \"{route}\";");
        L();
        L("    [TestMethod]");
        L("    public async Task A_row_that_is_not_there_answers_404()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L($"        using var response = await ApiFixture.Client.GetAsync($\"{{Route}}/{{{absent}}}\");");
        L("        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);");
        L("    }");
        if (project.ApiAuth)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task Without_a_token_the_list_answers_401()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        using var anonymous = ApiFixture.Anonymous();");
            L("        using var response = await anonymous.GetAsync(Route);");
            L("        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);");
            L("    }");
        }
        L();
        L("    [TestMethod]");
        L("    public async Task No_route_changes_a_row_of_a_log_table()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var client = ApiFixture.Client;");
        L("        using var created = await client.PostAsJsonAsync(Route, new JsonObject());");
        L("        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, created.StatusCode, \"there is no POST\");");
        L($"        using var updated = await client.PutAsJsonAsync($\"{{Route}}/{{{absent}}}\", new JsonObject());");
        L("        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, updated.StatusCode, \"there is no PUT\");");
        L($"        using var removed = await client.DeleteAsync($\"{{Route}}/{{{absent}}}\");");
        L("        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, removed.StatusCode, \"there is no DELETE\");");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task The_search_answers_a_page()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var response = await ApiFixture.Client.GetAsync($\"{Route}/search?pageNumber=1&pageSize=5\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        Assert.IsFalse(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync()));");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_date_range_in_the_far_future_lists_no_rows()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L($"        using var response = await ApiFixture.Client.GetAsync($\"{{Route}}/search?{from}=2999-01-01&{to}=2999-12-31&pageNumber=1&pageSize=5\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        var page = (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("        Assert.AreEqual(0, page[\"totalCount\"]!.GetValue<int>());");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_date_that_is_not_a_date_is_refused()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L($"        using var response = await ApiFixture.Client.GetAsync($\"{{Route}}/search?{from}=not-a-date&pageNumber=1&pageSize=5\");");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);");
        L("    }");
        if (plan.SortNames.Count > 0)
        {
            L();
            L("    [TestMethod]");
            foreach (string sortName in plan.SortNames)
                L($"    [DataRow({ApiSampleValues.Json(sortName)})]");
            L("    public async Task The_search_sorts_by_each_column_it_offers(string column)");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        foreach (string direction in new[] { \"asc\", \"desc\" })");
            L("        {");
            L("            using var response = await ApiFixture.Client.GetAsync($\"{Route}/search?pageNumber=1&pageSize=5&sortBy={Uri.EscapeDataString(column)}&sortDir={direction}\");");
            L("            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, column + \" \" + direction + \": \" + await response.Content.ReadAsStringAsync());");
            L("        }");
            L("    }");
        }
        if (project.ApiCsv)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task The_export_takes_the_date_range_and_has_no_import()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L($"        using var export = await ApiFixture.Client.GetAsync($\"{{Route}}/export.csv?{from}=2999-01-01\");");
            L("        Assert.AreEqual(HttpStatusCode.OK, export.StatusCode, await export.Content.ReadAsStringAsync());");
            L("        string[] lines = (await export.Content.ReadAsStringAsync()).Split('\\n', StringSplitOptions.RemoveEmptyEntries);");
            L("        Assert.HasCount(1, lines, \"only the header line\");");
            L("        using var import = await ApiFixture.Client.PostAsync($\"{Route}/import\", new StringContent(\"\"));");
            L("        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, import.StatusCode);");
            L("    }");
        }
        L("}");
        return o.ToString();
    }
}
