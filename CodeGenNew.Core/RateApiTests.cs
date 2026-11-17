using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated ASP.NET API tests of <c>RateTables</c>, <c>RateInverse</c> and <c>RateDocuments</c>, appended to the table's own test class by <see cref="RowShapeTests"/>. The rate table's class plays the
/// rule and the lookup with a pair of currencies no other run uses; the document table's class adds rates of the base currency through the rate table's route and plays the conversion. Every test removes its rows. </summary>
public static class RateApiTests
{
    public static bool Applies(TableModel table, ProjectSettings project) =>
        RatePlan.For(table, project) is { } rate && RateTestPlan.Fits(rate.From) && RateTestPlan.Fits(rate.To) && RateTestPlan.BodyOf(rate, project) is not null
        || RateDocumentPlan.For(table, project) is { } document && RateTestPlan.Fits(document.Currency) && RateTestPlan.BodyOf(document.Rates, project) is not null;

    public static string Methods(ApiTestPlan plan, TableModel table, ProjectSettings project)
    {
        var o = new StringBuilder();
        if (RatePlan.For(table, project) is { } rate && RateTestPlan.Fits(rate.From) && RateTestPlan.Fits(rate.To) && RateTestPlan.BodyOf(rate, project) is not null)
            o.Append(RateMethods(plan, rate, project));
        if (RateDocumentPlan.For(table, project) is { } document && RateTestPlan.Fits(document.Currency) && RateTestPlan.BodyOf(document.Rates, project) is { } rateBody)
            o.Append(DocumentMethods(plan, document, rateBody, project));
        return o.ToString();
    }

    private static string Day(ColumnModel column, string date) => column.CSharpBase() == "DateOnly" ? date : date + "T00:00:00";

    private static string RateMethods(ApiTestPlan plan, RateShape s, ProjectSettings project)
    {
        string id = plan.KeyJson, from = JsonNames.Camel(s.From.Name), to = JsonNames.Camel(s.To.Name), rate = JsonNames.Camel(s.Rate.Name), day = JsonNames.Camel(s.Day.Name);
        string d1 = Day(s.Day, "2024-01-01"), d2 = Day(s.Day, "2024-03-01"), d3 = Day(s.Day, "2024-03-15");
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string W(string text) => text.Replace("§FROM", from).Replace("§TO", to).Replace("§RATE", rate).Replace("§DAY", day).Replace("§ID", id).Replace("§D1", d1).Replace("§D2", d2).Replace("§D3", d3)
            .Replace("§TAKEN", RateWords.TakenType).Replace("§INVALID", RateWords.InvalidType).Replace("§MISSING", RateWords.MissingType).Replace("§HEADER", RateWords.InvertedHeader);

        L();
        L("    private static string NewCurrency(char lead) => lead + Guid.NewGuid().ToString(\"N\")[..2].ToUpperInvariant();");
        L();
        L("    private static async Task<HttpResponseMessage> SaveRateAsync(List<JsonObject> made, string from, string to, double rate, string day)");
        L("    {");
        L("        var body = await NewBodyAsync();");
        L(W("        body[\"§FROM\"] = from;"));
        L(W("        body[\"§TO\"] = to;"));
        L(W("        body[\"§RATE\"] = rate;"));
        L(W("        body[\"§DAY\"] = day;"));
        L("        var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
        L("        if (response.StatusCode == HttpStatusCode.Created)");
        L("            made.Add((await response.Content.ReadFromJsonAsync<JsonObject>())!);");
        L("        return response;");
        L("    }");
        L();
        L("    private static async Task<HttpResponseMessage> RateOnAsync(string from, string to, string? day = null) =>");
        L("        await ApiFixture.Client.GetAsync($\"{Route}/rate?from={from}&to={to}\" + (day is null ? \"\" : $\"&on={day}\"));");
        L();
        L("    private static async Task<string> ProblemTypeAsync(HttpResponseMessage response) =>");
        L("        (await response.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>();");
        L();
        L("    [TestMethod]");
        L("    public async Task A_rate_is_kept_trimmed_in_upper_case_and_once_per_pair_and_day()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var made = new List<JsonObject>();");
        L("        string source = NewCurrency('T'), target = NewCurrency('Y');");
        L("        using var first = await SaveRateAsync(made, \"  \" + source.ToLowerInvariant() + \" \", target, 1.5, \"§D1\");".Replace("§D1", d1));
        L("        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());");
        L(W("        Assert.AreEqual(source, made[0][\"§FROM\"]!.GetValue<string>(), \"the currency is trimmed and upper case\");"));
        L("        using var same = await SaveRateAsync(made, source.ToLowerInvariant(), target, 1.6, \"" + d1 + "\");");
        L("        Assert.AreEqual(HttpStatusCode.Conflict, same.StatusCode, await same.Content.ReadAsStringAsync());");
        L("        Assert.AreEqual(\"" + RateWords.TakenType + "\", await ProblemTypeAsync(same));");
        L("        using var opposite = await SaveRateAsync(made, target, source, 0.66, \"" + d1 + "\");");
        L("        Assert.AreEqual(HttpStatusCode.Created, opposite.StatusCode, \"the opposite pair is another rate\");");
        L("        using var later = await SaveRateAsync(made, source, target, 1.7, \"" + d2 + "\");");
        L("        Assert.AreEqual(HttpStatusCode.Created, later.StatusCode, await later.Content.ReadAsStringAsync());");
        L(W("        using var own = await PutAsync(made[2], b => b[\"§RATE\"] = 1.8);"));
        L("        Assert.IsTrue(own.IsSuccessStatusCode, \"a row keeps its own pair and day\");");
        L(W("        using var clash = await PutAsync(made[2], b => b[\"§DAY\"] = \"§D1\");"));
        L("        Assert.AreEqual(HttpStatusCode.Conflict, clash.StatusCode, await clash.Content.ReadAsStringAsync());");
        L("        Assert.AreEqual(\"" + RateWords.TakenType + "\", await ProblemTypeAsync(clash));");
        L("        await RemoveAsync(made.ToArray());");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_rate_that_is_not_valid_is_refused()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var made = new List<JsonObject>();");
        L("        string source = NewCurrency('T'), target = NewCurrency('Y');");
        L("        foreach (var (from, to, rate) in new[] { (source, source, 1.0), (source, target, 0.0), (source, target, -2.0), (\"  \", target, 1.0) })");
        L("        {");
        L("            using var refused = await SaveRateAsync(made, from, to, rate, \"" + d1 + "\");");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode, await refused.Content.ReadAsStringAsync());");
        L("            Assert.AreEqual(\"" + RateWords.InvalidType + "\", await ProblemTypeAsync(refused));");
        L("        }");
        L("        Assert.IsEmpty(made);");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task The_rate_in_force_is_the_latest_one_on_or_before_the_day()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var made = new List<JsonObject>();");
        L("        string source = NewCurrency('T'), target = NewCurrency('Y');");
        L("        (await SaveRateAsync(made, source, target, 1.5, \"" + d1 + "\")).Dispose();");
        L("        (await SaveRateAsync(made, source, target, 1.7, \"" + d2 + "\")).Dispose();");
        L("        using var early = await RateOnAsync(source.ToLowerInvariant(), target, \"2023-12-31\");");
        L("        Assert.AreEqual(HttpStatusCode.NotFound, early.StatusCode, \"no rate before the first one\");");
        L("        Assert.AreEqual(\"" + RateWords.MissingType + "\", await ProblemTypeAsync(early));");
        L(W("        async Task<double> RateAsync(string? day) => (await (await RateOnAsync(source, target, day)).Content.ReadFromJsonAsync<JsonObject>())![\"§RATE\"]!.GetValue<double>();"));
        L("        Assert.AreEqual(1.5, await RateAsync(\"2024-02-15\"), \"a day between two rates gets the earlier one\");");
        L("        Assert.AreEqual(1.7, await RateAsync(\"2024-03-01\"), \"the day a rate starts gets that rate\");");
        L("        Assert.AreEqual(1.7, await RateAsync(null), \"no day means today\");");
        L("        using var unknown = await RateOnAsync(source, \"XXQ\", \"2024-06-01\");");
        L("        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);");
        L("        using var twice = await RateOnAsync(source, source);");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, twice.StatusCode);");
        L("        using var lonely = await ApiFixture.Client.GetAsync($\"{Route}/rate?from={source}\");");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, lonely.StatusCode);");
        L("        await RemoveAsync(made.ToArray());");
        L("    }");
        if (project.RateInverse && RatePlan.InvertibleRate(s.Rate))
        {
            L();
            L("    [TestMethod]");
            L("    public async Task A_newer_rate_of_the_opposite_pair_answers_as_one_divided_by_it()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var made = new List<JsonObject>();");
            L("        string source = NewCurrency('T'), target = NewCurrency('Y');");
            L("        (await SaveRateAsync(made, source, target, 1.1, \"" + d1 + "\")).Dispose();");
            L("        (await SaveRateAsync(made, target, source, 0.8, \"" + d1 + "\")).Dispose();");
            L("        using var direct = await RateOnAsync(source, target, \"2024-02-01\");");
            L(W("        Assert.AreEqual(1.1, (await direct.Content.ReadFromJsonAsync<JsonObject>())![\"§RATE\"]!.GetValue<double>(), \"on the same day the pair's own row wins\");"));
            L(W("        Assert.IsFalse(direct.Headers.Contains(\"§HEADER\"));"));
            L("        (await SaveRateAsync(made, target, source, 0.5, \"" + d3 + "\")).Dispose();");
            L("        using var inverse = await RateOnAsync(source, target, \"2024-04-01\");");
            L("        var answer = (await inverse.Content.ReadFromJsonAsync<JsonObject>())!;");
            L(W("        Assert.AreEqual(2.0, answer[\"§RATE\"]!.GetValue<double>(), \"one divided by the newer opposite rate\");"));
            L(W("        Assert.AreEqual(source, answer[\"§FROM\"]!.GetValue<string>());"));
            L(W("        Assert.AreEqual(target, answer[\"§TO\"]!.GetValue<string>());"));
            L(W("        Assert.IsTrue(inverse.Headers.Contains(\"§HEADER\"), \"the answer says it was worked out\");"));
            L("        await RemoveAsync(made.ToArray());");
            L("    }");
        }
        return o.ToString();
    }

    private static string DocumentMethods(ApiTestPlan plan, RateDocumentShape s, string rateBody, ProjectSettings project)
    {
        string id = plan.KeyJson, cur = JsonNames.Camel(s.Currency.Name), rate = JsonNames.Camel(s.Rate.Name), amount = JsonNames.Camel(s.Amount.Name), baseAmount = JsonNames.Camel(s.BaseAmount.Name);
        string? day = s.Day is null ? null : JsonNames.Camel(s.Day.Name);
        var r = s.Rates;
        string rid = JsonNames.Camel(r.Key.Name), rfrom = JsonNames.Camel(r.From.Name), rto = JsonNames.Camel(r.To.Name), rrate = JsonNames.Camel(r.Rate.Name), rday = JsonNames.Camel(r.Day.Name);
        string rateRoute = ApiRequests.Route(r.Table);
        string d1 = s.Day is null ? "2024-01-01" : Day(s.Day, "2024-01-01"), d2 = s.Day is null ? "2024-03-01" : Day(s.Day, "2024-03-01");
        string rd1 = Day(r.Day, "2024-01-01"), rd2 = Day(r.Day, "2024-03-01");
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string Q(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        L();
        L("    private static string NewCurrency(char lead) => lead + Guid.NewGuid().ToString(\"N\")[..2].ToUpperInvariant();");
        L();
        L("    private static async Task<JsonObject> AddRateAsync(List<string> routes, string from, string to, double rate, string day)");
        L("    {");
        L("        var body = (JsonObject)JsonNode.Parse(" + Q(rateBody) + ")!;");
        L($"        body[\"{rfrom}\"] = from;");
        L($"        body[\"{rto}\"] = to;");
        L($"        body[\"{rrate}\"] = rate;");
        L($"        body[\"{rday}\"] = day;");
        L($"        using var response = await ApiFixture.Client.PostAsJsonAsync(\"{rateRoute}\", body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        var made = (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L($"        routes.Add($\"{rateRoute}/{{made[\"{rid}\"]!.GetValue<long>()}}\");");
        L("        return made;");
        L("    }");
        L();
        L("    private static async Task<HttpResponseMessage> AddDocumentAsync(List<string> routes, string currency, double amount, string? day)");
        L("    {");
        L("        var body = await NewBodyAsync();");
        L($"        body[\"{cur}\"] = currency;");
        L($"        body[\"{amount}\"] = amount;");
        L($"        body[\"{rate}\"] = 99;");
        L($"        body[\"{baseAmount}\"] = 1;");
        if (day is not null)
            L($"        if (day is not null)\n            body[\"{day}\"] = day;");
        L("        var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
        L("        if (response.StatusCode == HttpStatusCode.Created)");
        L($"            routes.Add($\"{{Route}}/{{JsonNode.Parse(await response.Content.ReadAsStringAsync())![\"{id}\"]!.GetValue<long>()}}\");");
        L("        return response;");
        L("    }");
        L();
        L("    private static async Task RemoveRoutesAsync(List<string> routes)");
        L("    {");
        L("        foreach (string route in Enumerable.Reverse(routes))");
        L("            using (await ApiFixture.Client.DeleteAsync(route)) { }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_document_is_written_with_the_rate_in_force_and_keeps_it()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var routes = new List<string>();");
        L("        string currency = NewCurrency('T');");
        L($"        await AddRateAsync(routes, currency, \"{s.BaseCurrency}\", 1.1, \"{rd1}\");");
        L($"        await AddRateAsync(routes, currency, \"{s.BaseCurrency}\", 1.2, \"{rd2}\");");
        L($"        using var first = await AddDocumentAsync(routes, \" \" + currency.ToLowerInvariant() + \" \", 100, {(day is null ? "null" : Q(d1))});");
        L("        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());");
        L("        var written = (JsonObject)JsonNode.Parse(await first.Content.ReadAsStringAsync())!;");
        L($"        Assert.AreEqual(currency, written[\"{cur}\"]!.GetValue<string>(), \"the currency is trimmed and upper case\");");
        if (day is not null)
        {
            L($"        Assert.AreEqual(1.1, written[\"{rate}\"]!.GetValue<double>(), \"the rate in force on the date\");");
            L($"        Assert.AreEqual(110.0, written[\"{baseAmount}\"]!.GetValue<double>(), \"the amount in the base currency\");");
            L("        var movedRow = (JsonObject)written.DeepClone();");
            L($"        movedRow[\"{day}\"] = {Q(d2)};");
            L("        using var moved = await ApiFixture.Client.PutAsJsonAsync(routes[^1], movedRow);");
            L("        Assert.IsTrue(moved.IsSuccessStatusCode, await moved.Content.ReadAsStringAsync());");
            L("        var again = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(routes[^1]))!;");
            L($"        Assert.AreEqual(1.2, again[\"{rate}\"]!.GetValue<double>(), \"a new date takes the rate of that date\");");
            L($"        Assert.AreEqual(120.0, again[\"{baseAmount}\"]!.GetValue<double>());");
        }
        else
        {
            L($"        Assert.AreEqual(100.0 * written[\"{rate}\"]!.GetValue<double>(), written[\"{baseAmount}\"]!.GetValue<double>(), 0.005, \"the amount times the rate\");");
        }
        L($"        var changed = (JsonObject)written.DeepClone();");
        L($"        changed[\"{amount}\"] = 200;");
        L($"        changed[\"{rate}\"] = 5;");
        L($"        using var edit = await ApiFixture.Client.PutAsJsonAsync(routes[^1], changed);");
        L("        Assert.IsTrue(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());");
        L("        var kept = (await ApiFixture.Client.GetFromJsonAsync<JsonObject>(routes[^1]))!;");
        L($"        Assert.AreNotEqual(5.0, kept[\"{rate}\"]!.GetValue<double>(), \"the rate is not the caller's\");");
        L($"        Assert.AreEqual(200.0 * kept[\"{rate}\"]!.GetValue<double>(), kept[\"{baseAmount}\"]!.GetValue<double>(), 0.005);");
        L($"        using var home = await AddDocumentAsync(routes, \"{s.BaseCurrency.ToLowerInvariant()}\", 50, {(day is null ? "null" : Q(d1))});");
        L("        Assert.AreEqual(HttpStatusCode.Created, home.StatusCode, await home.Content.ReadAsStringAsync());");
        L("        var local = (JsonObject)JsonNode.Parse(await home.Content.ReadAsStringAsync())!;");
        L($"        Assert.AreEqual(1.0, local[\"{rate}\"]!.GetValue<double>(), \"the base currency has rate 1\");");
        L($"        Assert.AreEqual(50.0, local[\"{baseAmount}\"]!.GetValue<double>());");
        L("        await RemoveRoutesAsync(routes);");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_document_in_a_currency_with_no_rate_is_refused()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var routes = new List<string>();");
        L($"        using var none = await AddDocumentAsync(routes, NewCurrency('T'), 100, {(day is null ? "null" : Q(d1))});");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, none.StatusCode, await none.Content.ReadAsStringAsync());");
        L("        Assert.AreEqual(\"" + RateWords.NeededType + "\", (await none.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
        L($"        using var blank = await AddDocumentAsync(routes, \"  \", 100, {(day is null ? "null" : Q(d1))});");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, blank.StatusCode, await blank.Content.ReadAsStringAsync());");
        L("        Assert.IsEmpty(routes, \"nothing was kept\");");
        L("    }");
        return o.ToString();
    }
}
