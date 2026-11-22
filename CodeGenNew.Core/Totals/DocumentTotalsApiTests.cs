using System.Text;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Totals;

/// <summary> The generated ASP.NET API tests of <c>DocumentTotals</c>, appended to the header table's own test class by <see cref="RowShapeTests"/>: the totals a client writes into a new header are replaced by the
/// worked-out ones, the check route finds the header in order, and a change to the header leaves its totals worked out from its rows. Every test removes its rows. </summary>
public static class DocumentTotalsApiTests
{
    public static bool Applies(TableModel table, ProjectSettings project) => DocumentTotalsPlan.For(table, project) is not null;

    public static string Methods(ApiTestPlan plan, TableModel table, ProjectSettings project)
    {
        if (DocumentTotalsPlan.For(table, project) is not { } s)
            return "";
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string id = plan.KeyJson;
        var owned = s.Columns.Owned().Select(p => JsonNames.Camel(p.Column.Name)).ToList();
        string? prepaid = s.Columns.Prepaid is null ? null : JsonNames.Camel(s.Columns.Prepaid.Name);
        L();
        L("    private static async Task<JsonObject> NewTotalsBodyAsync(double wrong)");
        L("    {");
        L("        var body = await NewBodyAsync();");
        foreach (string column in owned)
            L($"        body[\"{column}\"] = wrong;");
        if (prepaid is not null)
            L($"        body[\"{prepaid}\"] = 0;");
        L("        return body;");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task The_totals_a_client_writes_are_replaced_by_the_worked_out_ones()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var response = await ApiFixture.Client.PostAsJsonAsync(Route, await NewTotalsBodyAsync(999));");
        L("        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L($"        string route = $\"{{Route}}/{{JsonNode.Parse(await response.Content.ReadAsStringAsync())![\"{id}\"]!.GetValue<long>()}}\";");
        L("        try");
        L("        {");
        L("            var row = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(route))!;");
        foreach (string column in owned)
            L($"            Assert.AreEqual(0d, row[\"{column}\"]!.GetValue<double>(), 0.001, \"{column} is worked out from the rows, not taken from the client\");");
        L("        }");
        L("        finally { using (await ApiFixture.Client.DeleteAsync(route)) { } }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_header_whose_totals_follow_its_rows_is_not_listed_by_the_check()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var response = await ApiFixture.Client.PostAsJsonAsync(Route, await NewTotalsBodyAsync(5));");
        L("        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L($"        long key = JsonNode.Parse(await response.Content.ReadAsStringAsync())![\"{id}\"]!.GetValue<long>();");
        L("        string route = $\"{Route}/{key}\";");
        L("        try");
        L("        {");
        L($"            var check = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>($\"{{Route}}/{DocumentTotalsWords.CheckRoute}?id={{key}}\"))!;");
        L("            Assert.AreEqual(1, check[\"checked\"]!.GetValue<int>());");
        L("            Assert.AreEqual(0, check[\"differing\"]!.GetValue<int>(), check.ToJsonString());");
        L("        }");
        L("        finally { using (await ApiFixture.Client.DeleteAsync(route)) { } }");
        L("    }");
        return o.ToString();
    }
}
