using System.Text;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Allowance;

/// <summary> The generated ASP.NET API tests of <c>AllowanceCharges</c>, appended to the table's own test class by <see cref="RowShapeTests"/>: the amount is worked out from the base and the percentage, the percentage from
/// the base and the amount, a negative value, a percentage with no base and a row with nothing to work from are refused, and with the document part the totals add up. Every test removes its rows. </summary>
public static class AllowanceApiTests
{
    public static bool Applies(TableModel table, ProjectSettings project) =>
        AllowanceChargePlan.For(table, project) is { } s && (s.Document is null || s.Document.CSharpBase() is "long" or "int" or "short");

    public static string Methods(ApiTestPlan plan, TableModel table, ProjectSettings project)
    {
        if (!Applies(table, project) || AllowanceChargePlan.For(table, project) is not { } s)
            return "";
        string id = plan.KeyJson, flag = JsonNames.Camel(s.Flag.Name), baseColumn = JsonNames.Camel(s.Base.Name), percent = JsonNames.Camel(s.Percent.Name), amount = JsonNames.Camel(s.Amount.Name);
        string? document = s.Document is null ? null : JsonNames.Camel(s.Document.Name);
        string? lineColumn = s.Line is not null && !s.Table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(s.Line.Name))) ? JsonNames.Camel(s.Line.Name) : null;   // a line column that points at another table needs a row there, which this test cannot make
        bool documentIsKey = s.Document is not null && s.Table.ForeignKeys.Any(f => f.ReferencingColumns.Any(r => r.EqualsIgnoreCase(s.Document.Name)));
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');

        L();
        L("    private static async Task<HttpResponseMessage> AddAllowanceAsync(List<string> routes, bool charge, double baseAmount, double percentage, double amount, long? document = null" + (lineColumn is null ? "" : ", long? line = null") + ")");
        L("    {");
        L("        var body = await NewBodyAsync();");
        L($"        body[\"{flag}\"] = charge;");
        L($"        body[\"{baseColumn}\"] = baseAmount;");
        L($"        body[\"{percent}\"] = percentage;");
        L($"        body[\"{amount}\"] = amount;");
        if (document is not null)
            L($"        if (document is not null)\n            body[\"{document}\"] = document;");
        if (lineColumn is not null)
            L($"        if (line is not null)\n            body[\"{lineColumn}\"] = line;");
        L("        var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
        L("        if (response.StatusCode == HttpStatusCode.Created)");
        L($"            routes.Add($\"{{Route}}/{{JsonNode.Parse(await response.Content.ReadAsStringAsync())![\"{id}\"]!.GetValue<long>()}}\");");
        L("        return response;");
        L("    }");
        L();
        L("    private static async Task RemoveAllowanceRowsAsync(List<string> routes)");
        L("    {");
        L("        foreach (string route in Enumerable.Reverse(routes))");
        L("            using (await ApiFixture.Client.DeleteAsync(route)) { }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task The_amount_is_worked_out_from_the_base_and_the_percentage()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var routes = new List<string>();");
        L("        try");
        L("        {");
        L("            using var response = await AddAllowanceAsync(routes, false, 200, 10, 1);");
        L("            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("            var row = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(routes[^1]))!;");
        L($"            Assert.AreEqual(20d, row[\"{amount}\"]!.GetValue<double>(), 0.001, \"the amount sent is ignored when a percentage is given\");");
        L("        }");
        L("        finally { await RemoveAllowanceRowsAsync(routes); }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task The_percentage_is_worked_out_from_the_base_and_the_amount()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var routes = new List<string>();");
        L("        try");
        L("        {");
        L("            using var response = await AddAllowanceAsync(routes, true, 200, 0, 50);");
        L("            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("            var row = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(routes[^1]))!;");
        L($"            Assert.AreEqual(25d, row[\"{percent}\"]!.GetValue<double>(), 0.001);");
        L($"            Assert.AreEqual(50d, row[\"{amount}\"]!.GetValue<double>(), 0.001);");
        L($"            Assert.IsTrue(row[\"{flag}\"]!.GetValue<bool>(), \"the amount stays positive, the flag says it is a charge\");");
        L("        }");
        L("        finally { await RemoveAllowanceRowsAsync(routes); }");
        L("    }");
        foreach (var (name, args) in new[]
        {
            ("A_negative_value_is_refused", "false, -5, 10, 1"),
            ("A_percentage_without_a_base_is_refused", "false, 0, 10, 0"),
            ("A_row_with_nothing_to_work_an_amount_from_is_refused", "false, 0, 0, 0"),
        })
        {
            L();
            L("    [TestMethod]");
            L($"    public async Task {name}()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var routes = new List<string>();");
            L("        try");
            L("        {");
            L($"            using var response = await AddAllowanceAsync(routes, {args});");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);");
            L($"            StringAssert.Contains(await response.Content.ReadAsStringAsync(), \"{AllowanceChargeWords.InvalidType}\");");
            L("        }");
            L("        finally { await RemoveAllowanceRowsAsync(routes); }");
            L("    }");
        }
        if (document is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task The_totals_of_a_document_add_up_the_allowances_and_the_charges()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var routes = new List<string>();");
            L("        try");
            L("        {");
            L(documentIsKey ? $"            long allowanceDocument = (await NewBodyAsync())[\"{document}\"]!.GetValue<long>();" : "            long allowanceDocument = 800000 + Random.Shared.Next(100000);");
            L($"            string totalsRoute = $\"{{Route}}/{AllowanceChargeWords.TotalsRoute}?document={{allowanceDocument}}\";");
            L("            var before = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(totalsRoute))!;");
            L("            foreach (var (charge, baseAmount, percentage, amount) in new[] { (false, 200d, 10d, 0d), (false, 0d, 0d, 5d), (true, 0d, 0d, 60d) })");
            L("            {");
            L("                using var response = await AddAllowanceAsync(routes, charge, baseAmount, percentage, amount, allowanceDocument);");
            L("                Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
            L("            }");
            L("            var totals = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(totalsRoute))!;");
            L("            Assert.AreEqual(25d, totals[\"allowances\"]!.GetValue<double>() - before[\"allowances\"]!.GetValue<double>(), 0.001);");
            L("            Assert.AreEqual(60d, totals[\"charges\"]!.GetValue<double>() - before[\"charges\"]!.GetValue<double>(), 0.001);");
            L("            Assert.AreEqual(35d, totals[\"net\"]!.GetValue<double>() - before[\"net\"]!.GetValue<double>(), 0.001);");
            L("            Assert.AreEqual(3, totals[\"rows\"]!.GetValue<int>() - before[\"rows\"]!.GetValue<int>());");
            L($"            using var missing = await ApiFixture.Client.GetAsync($\"{{Route}}/{AllowanceChargeWords.TotalsRoute}\");");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);");
            L("        }");
            L("        finally { await RemoveAllowanceRowsAsync(routes); }");
            L("    }");
            if (lineColumn is not null)
            {
                L();
                L("    [TestMethod]");
                L("    public async Task The_totals_of_one_line_count_only_the_rows_of_that_line()");
                L("    {");
                L("        ApiFixture.RequireDatabase();");
                L("        var routes = new List<string>();");
                L("        try");
                L("        {");
                L(documentIsKey ? $"            long allowanceDocument = (await NewBodyAsync())[\"{document}\"]!.GetValue<long>();" : "            long allowanceDocument = 800000 + Random.Shared.Next(100000);");
                L("            long firstLine = 900000 + Random.Shared.Next(50000), secondLine = firstLine + 1;");
                L($"            string totalsRoute = $\"{{Route}}/{AllowanceChargeWords.TotalsRoute}?document={{allowanceDocument}}&line={{firstLine}}\";");
                L("            using (var first = await AddAllowanceAsync(routes, false, 200, 10, 0, allowanceDocument, firstLine))");
                L("                Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());");
                L("            using (var second = await AddAllowanceAsync(routes, true, 0, 0, 60, allowanceDocument, secondLine))");
                L("                Assert.AreEqual(HttpStatusCode.Created, second.StatusCode, await second.Content.ReadAsStringAsync());");
                L("            var totals = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(totalsRoute))!;");
                L("            Assert.AreEqual(20d, totals[\"allowances\"]!.GetValue<double>(), 0.001);");
                L("            Assert.AreEqual(0d, totals[\"charges\"]!.GetValue<double>(), 0.001);");
                L("            Assert.AreEqual(1, totals[\"rows\"]!.GetValue<int>());");
                L("        }");
                L("        finally { await RemoveAllowanceRowsAsync(routes); }");
                L("    }");
            }
        }
        return o.ToString();
    }
}
