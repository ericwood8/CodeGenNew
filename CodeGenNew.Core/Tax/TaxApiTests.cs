using System.Text;
using CodeGenNew.Core.Api;

namespace CodeGenNew.Core.Tax;

/// <summary> The generated ASP.NET API tests of <c>TaxLines</c>, appended to the line table's own test class by <see cref="RowShapeTests"/>: the rate is the category's and the tax worked out from it, the rate is
/// kept when the category's rate changes, an unknown category is refused, and with the validity and document parts a category outside its validity is refused and the subtotals add up. Every test removes its rows. </summary>
public static class TaxApiTests
{
    public static bool Applies(TableModel table, ProjectSettings project) =>
        TableShapes.For(table, project).TaxLine is { } s && CategoryBody(s, project) is not null;

    /// <summary> An example category row with the key, the rate, the validity and the unique text columns left for the test to fill, or null when a column needs a value this plan cannot make up. </summary>
    private static string? CategoryBody(TaxLineShape s, ProjectSettings project)
    {
        var parts = new List<string>();
        foreach (var c in ApiSampleValues.BodyColumns(s.CategoryTable))
        {
            string value;
            if (c == s.CategoryKey)
                value = "0";
            else if (c == s.CategoryRate)
                value = "1";
            else if (c == s.ValidFrom)
                value = "\"\"";
            else if (c == s.ValidTo)
                value = "null";
            else if (c.IsStringColumn && (c.IsInUniqueIndex || s.Groups.Contains(c)))
                value = "\"\"";
            else if (c.IsInUniqueIndex || s.CategoryTable.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(c.Name))))
                return null;
            else
            {
                value = c.ValidExample(project, s.CategoryTable.TableName);
                if (value == "null" && !c.IsNullable)
                    return null;
            }
            parts.Add(ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + value);
        }
        return "{ " + string.Join(", ", parts) + " }";
    }

    private static string Day(ColumnModel column, string date) => column.CSharpBase() == "DateOnly" ? date : date + "T00:00:00";

    public static string Methods(ApiTestPlan plan, TableModel table, ProjectSettings project)
    {
        if (TableShapes.For(table, project).TaxLine is not { } s || CategoryBody(s, project) is not { } body)
            return "";
        string id = plan.KeyJson, cat = JsonNames.Camel(s.Category.Name), rate = JsonNames.Camel(s.Rate.Name), taxable = JsonNames.Camel(s.Taxable.Name), tax = JsonNames.Camel(s.Tax.Name);
        string categoryKey = JsonNames.Camel(s.CategoryKey.Name), categoryRate = JsonNames.Camel(s.CategoryRate.Name), categoryRoute = ApiRequests.Route(s.CategoryTable);
        string? day = s.Day is null ? null : JsonNames.Camel(s.Day.Name);
        string? document = s.Document is null ? null : JsonNames.Camel(s.Document.Name);
        bool documentIsKey = s.Document is not null && s.Table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(s.Document.Name)));   // the document is a row of another table: the generated body names one that exists
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string Q(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        L();
        L("    private static async Task<long> AddTaxCategoryAsync(List<string> routes, double rate, string? from = null, string? to = null)");
        L("    {");
        L("        var body = (JsonObject)JsonNode.Parse(" + Q(body) + ")!;");
        L($"        body[\"{categoryRate}\"] = rate;");
        if (s.ValidFrom is not null)
            L($"        body[\"{JsonNames.Camel(s.ValidFrom.Name)}\"] = from ?? \"{Day(s.ValidFrom, "2000-01-01")}\";");
        if (s.ValidTo is not null)
            L($"        body[\"{JsonNames.Camel(s.ValidTo.Name)}\"] = to;");
        foreach (var c in TaxTestPlan.UniqueTextColumns(s))
            L($"        body[\"{JsonNames.Camel(c.Name)}\"] = Guid.NewGuid().ToString(\"N\")[..{(c.CharacterLength > 0 ? Math.Min(8, c.CharacterLength) : 8)}];");
        L($"        using var response = await ApiFixture.Client.PostAsJsonAsync(\"{categoryRoute}\", body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        var made = (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L($"        long key = made[\"{categoryKey}\"]!.GetValue<long>();");
        L($"        routes.Add($\"{categoryRoute}/{{key}}\");");
        L("        return key;");
        L("    }");
        L();
        L("    private static async Task<HttpResponseMessage> AddTaxLineAsync(List<string> routes, long? category, double amount, string? day = null)");
        L("    {");
        L("        var body = await NewBodyAsync();");
        L($"        body[\"{cat}\"] = category;");
        L($"        body[\"{taxable}\"] = amount;");
        L($"        body[\"{rate}\"] = 99;");
        L($"        body[\"{tax}\"] = 1;");
        if (day is not null)
            L($"        if (day is not null)\n            body[\"{day}\"] = day;");
        if (document is not null && !documentIsKey)
            L($"        body[\"{document}\"] = TaxDocument;");
        L("        var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
        L("        if (response.StatusCode == HttpStatusCode.Created)");
        L($"            routes.Add($\"{{Route}}/{{JsonNode.Parse(await response.Content.ReadAsStringAsync())![\"{id}\"]!.GetValue<long>()}}\");");
        L("        return response;");
        L("    }");
        L();
        if (document is not null && !documentIsKey)
        {
            L("    private static readonly long TaxDocument = 800000 + Random.Shared.Next(100000);");
            L();
        }
        L("    private static async Task RemoveTaxRowsAsync(List<string> routes)");
        L("    {");
        L("        foreach (string route in Enumerable.Reverse(routes))");
        L("            using (await ApiFixture.Client.DeleteAsync(route)) { }");
        L("    }");
        L();
        L("    private static async Task<JsonObject> ReadTaxRowAsync(HttpResponseMessage response, List<string> routes) =>");
        L("        (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(routes[^1]))!;");
        L();
        L("    [TestMethod]");
        L("    public async Task A_line_carries_the_rate_of_its_category_and_the_tax_worked_out_from_it()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var routes = new List<string>();");
        L("        try");
        L("        {");
        L("            long category = await AddTaxCategoryAsync(routes, 0.2);");
        L("            using var response = await AddTaxLineAsync(routes, category, 50);");
        L("            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("            var row = await ReadTaxRowAsync(response, routes);");
        L($"            Assert.AreEqual(0.2d, row[\"{rate}\"]!.GetValue<double>(), \"the rate is the category's, not the one sent\");");
        L($"            Assert.AreEqual(10d, row[\"{tax}\"]!.GetValue<double>(), \"the tax is the taxable amount times the rate over 100\");");
        L("        }");
        L("        finally { await RemoveTaxRowsAsync(routes); }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_line_keeps_its_rate_when_the_rate_of_its_category_changes()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var routes = new List<string>();");
        L("        try");
        L("        {");
        L("            long category = await AddTaxCategoryAsync(routes, 0.2);");
        L("            using var first = await AddTaxLineAsync(routes, category, 100);");
        L("            Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());");
        L("            string lineRoute = routes[^1];");
        L($"            var changed = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(routes[0]))!;");
        L($"            changed[\"{categoryRate}\"] = 0.25;");
        L("            using (var put = await ApiFixture.Client.PutAsJsonAsync(routes[0], changed))");
        L("                Assert.IsTrue(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());");
        L("            var line = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(lineRoute))!;");
        L($"            line[\"{taxable}\"] = 200;");
        L("            using (var put = await ApiFixture.Client.PutAsJsonAsync(lineRoute, line))");
        L("                Assert.IsTrue(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());");
        L("            line = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(lineRoute))!;");
        L($"            Assert.AreEqual(0.2d, line[\"{rate}\"]!.GetValue<double>(), \"the rate the line was written with stays\");");
        L($"            Assert.AreEqual(40d, line[\"{tax}\"]!.GetValue<double>());");
        L("            using var later = await AddTaxLineAsync(routes, category, 100);");
        L("            Assert.AreEqual(HttpStatusCode.Created, later.StatusCode, await later.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(0.25d, (await ReadTaxRowAsync(later, routes))[\"{rate}\"]!.GetValue<double>(), \"a new line takes the category's new rate\");");
        L("        }");
        L("        finally { await RemoveTaxRowsAsync(routes); }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_line_with_a_category_that_does_not_exist_is_refused()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var routes = new List<string>();");
        L("        try");
        L("        {");
        L("            using var response = await AddTaxLineAsync(routes, 2000000000, 10);");
        L("            Assert.IsTrue(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());");
        L("            StringAssert.Contains(await response.Content.ReadAsStringAsync(), \"" + TaxWords.UnknownType + "\");");
        L("        }");
        L("        finally { await RemoveTaxRowsAsync(routes); }");
        L("    }");
        if (s.ValidFrom is not null && s.Day is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task A_category_outside_its_validity_is_refused()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var routes = new List<string>();");
            L("        try");
            L("        {");
            L($"            long category = await AddTaxCategoryAsync(routes, 0.2, \"{Day(s.ValidFrom, "2024-01-01")}\", " + (s.ValidTo is null ? "null" : $"\"{Day(s.ValidTo, "2024-12-31")}\"") + ");");
            L($"            using var inside = await AddTaxLineAsync(routes, category, 10, \"{Day(s.Day, "2024-06-01")}\");");
            L("            Assert.AreEqual(HttpStatusCode.Created, inside.StatusCode, await inside.Content.ReadAsStringAsync());");
            L($"            using var before = await AddTaxLineAsync(routes, category, 10, \"{Day(s.Day, "2023-12-31")}\");");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, before.StatusCode);");
            L("            StringAssert.Contains(await before.Content.ReadAsStringAsync(), \"" + TaxWords.NotInForceType + "\");");
            if (s.ValidTo is not null)
            {
                L($"            using var after = await AddTaxLineAsync(routes, category, 10, \"{Day(s.Day, "2025-01-01")}\");");
                L("            Assert.AreEqual(HttpStatusCode.BadRequest, after.StatusCode);");
                L("            StringAssert.Contains(await after.Content.ReadAsStringAsync(), \"" + TaxWords.NotInForceType + "\");");
            }
            L("        }");
            L("        finally { await RemoveTaxRowsAsync(routes); }");
            L("    }");
        }
        if (document is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task The_subtotals_of_a_document_add_up_by_category_and_rate()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var routes = new List<string>();");
            L("        try");
            L("        {");
            L("            long twenty = await AddTaxCategoryAsync(routes, 0.2);");
            L("            long five = await AddTaxCategoryAsync(routes, 0.05);");
            L("            foreach (var (category, amount) in new[] { (twenty, 100d), (twenty, 50d), (five, 200d) })");
            L("            {");
            L("                using var response = await AddTaxLineAsync(routes, category, amount);");
            L("                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
            L("            }");
            L(documentIsKey ? $"            long taxDocument = (await NewBodyAsync())[\"{document}\"]!.GetValue<long>();" : "            long taxDocument = TaxDocument;");
            L($"            var subtotals = (await ApiFixture.Client.GetFromJsonAsync<JsonArray>($\"{{Route}}/tax-subtotals?document={{taxDocument}}\"))!;");
            if (!documentIsKey)
                L("            Assert.AreEqual(2, subtotals.Count);");
            L("            var first = subtotals.Single(x => x![\"category\"]!.GetValue<long>() == twenty)!;");
            L("            Assert.AreEqual(150d, first[\"taxable\"]!.GetValue<double>());");
            L("            Assert.AreEqual(30d, first[\"tax\"]!.GetValue<double>());");
            L("            Assert.AreEqual(2, first[\"lines\"]!.GetValue<int>());");
            L("            var second = subtotals.Single(x => x![\"category\"]!.GetValue<long>() == five)!;");
            L("            Assert.AreEqual(10d, second[\"tax\"]!.GetValue<double>());");
            L("            using var missing = await ApiFixture.Client.GetAsync($\"{Route}/tax-subtotals\");");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);");
            L("        }");
            L("        finally { await RemoveTaxRowsAsync(routes); }");
            L("    }");
        }
        return o.ToString();
    }
}
