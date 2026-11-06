using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated ASP.NET tests of the usage routes (<c>&lt;Table&gt;UsageApiTests.cs</c>): a call without a sign-in is a 401, a role that is not the administrator's is a 403, the summary adds the events of a
/// tenant per month and kind (and can be limited by kind and by dates), another tenant's events are not in it, a platform administrator names the tenant and is the one who records usage, a recorded event is in the
/// summary, and nothing edits or deletes an event. Each test adds events of its own through the test support and removes them again. </summary>
public static class ApiUsageTests
{
    public static IReadOnlyList<UsageShape> Tables(DatabaseModel database, ProjectSettings project) =>
        !project.ApiTests || !project.ApiAuth ? [] :
        database.Tables.Select(t => TableShapes.For(t, project).Usage).OfType<UsageShape>().Where(t => KindLength(t) >= 4).OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> How many characters of a made-up kind the ledger can hold (up to 12): a kind column too short for a unique test kind gets no generated tests. </summary>
    internal static int KindLength(UsageShape t) => t.Kind.CharacterLength is > 0 and var length ? Math.Min(12, length - 1) : 12;

    public static string ClassText(UsageShape t, ProjectSettings project, UsageRollupShape? rollup = null)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string apis = project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis", admin = project.AuthAdminRole;
        string table = t.Table.TableName, support = $"{apis}.{table}UsageTestSupport", route = ApiRequests.Route(t.Table);
        int length = KindLength(t);
        string key = JsonNames.Camel(t.Key.Name);
        bool record = UsageWriter.HasRecordRoute(t);
        string q1 = t.QuantityIsDecimal ? "3.25m" : "3", q2 = t.QuantityIsDecimal ? "4.5m" : "4", q3 = t.QuantityIsDecimal ? "5.75m" : "5", sum1 = t.QuantityIsDecimal ? "7.75m" : "7m", sum3 = t.QuantityIsDecimal ? "5.75m" : "5m";

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L($"/// <summary> The usage ledger {table}. Each test adds events of its own and removes them again. </summary>");
        L("[TestClass]");
        L($"public class {table}UsageApiTests");
        L("{");
        L($"    private const string Route = \"{route}\";");
        L();
        L($"    private static string NewKind() => \"t\" + Guid.NewGuid().ToString(\"N\")[..{length - 1}];");
        L();
        L("    private static int Tenant()");
        L("    {");
        L("        if (ApiFixture.TenantId is null)");
        L("            Assert.Inconclusive(\"The database has no tenant to bill usage to.\");");
        L("        return ApiFixture.TenantId!.Value;");
        L("    }");
        L();
        L("    private static HttpClient Admin(int tenant) => ApiFixture.ClientInTenant(tenant, \"" + admin + "\");");
        L();
        L("    private static async Task<JsonArray> ReadAsync(HttpClient client, string query = \"\")");
        L("    {");
        L("        using var response = await client.GetAsync(Route + query);");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        return (await response.Content.ReadFromJsonAsync<JsonArray>())!;");
        L("    }");
        L();
        L("    private static JsonNode? Line(JsonArray lines, string kind, string month) => lines.FirstOrDefault(l => l![\"kind\"]!.GetValue<string>() == kind && l[\"month\"]!.GetValue<string>() == month);");
        L();
        L("    private static string ThisMonth() => DateTime.UtcNow.ToString(\"yyyy-MM\");");
        L();
        L("    [TestMethod]");
        L("    public async Task Without_a_sign_in_the_routes_answer_401()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var nobody = ApiFixture.Anonymous();");
        L("        using var list = await nobody.GetAsync(Route);");
        L("        Assert.AreEqual(HttpStatusCode.Unauthorized, list.StatusCode);");
        if (record)
        {
            L("        using var post = await nobody.PostAsJsonAsync(Route, new { tenantId = 1, kind = NewKind() });");
            L("        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);");
        }
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_role_that_is_not_the_administrators_cannot_read_the_usage()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var other = ApiFixture.ClientInTenant(Tenant(), \"NoSuchRole\");");
        L("        using var list = await other.GetAsync(Route);");
        L("        Assert.AreEqual(HttpStatusCode.Forbidden, list.StatusCode);");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task The_summary_adds_the_events_per_month_and_kind()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        int tenant = Tenant();");
        L("        string one = NewKind(), two = NewKind();");
        L("        var ids = new[]");
        L("        {");
        L($"            await {support}.AddAsync(ApiFixture.Services, tenant, one, {q1}),");
        L($"            await {support}.AddAsync(ApiFixture.Services, tenant, one, {q2}),");
        L($"            await {support}.AddAsync(ApiFixture.Services, tenant, two, {q3}, new DateTime(2020, 1, 15, 12, 0, 0))");
        L("        };");
        L("        try");
        L("        {");
        L("            using var client = Admin(tenant);");
        L("            var all = await ReadAsync(client);");
        if (t.Quantity is not null)
        {
            L($"            Assert.AreEqual({sum1}, Line(all, one, ThisMonth())![\"quantity\"]!.GetValue<decimal>(), \"the units of the two events of this month\");");
            L($"            Assert.AreEqual({sum3}, Line(all, two, \"2020-01\")![\"quantity\"]!.GetValue<decimal>());");
        }
        else
        {
            L("            Assert.AreEqual(2m, Line(all, one, ThisMonth())![\"quantity\"]!.GetValue<decimal>(), \"each event is one unit\");");
            L("            Assert.AreEqual(1m, Line(all, two, \"2020-01\")![\"quantity\"]!.GetValue<decimal>());");
        }
        L("            Assert.AreEqual(2, Line(all, one, ThisMonth())![\"events\"]!.GetValue<int>());");
        L("            var byKind = await ReadAsync(client, $\"?kind={one}\");");
        L("            Assert.IsTrue(byKind.All(l => l![\"kind\"]!.GetValue<string>() == one), \"the kind filter\");");
        L("            var before = await ReadAsync(client, \"?from=2019-01-01&to=2020-06-01\");");
        L("            Assert.IsNotNull(Line(before, two, \"2020-01\"), \"the date filter keeps the old event\");");
        L("            Assert.IsNull(Line(before, one, ThisMonth()), \"and leaves this month out\");");
        L("            var months = all.Select(l => l![\"month\"]!.GetValue<string>()).ToList();");
        L("            CollectionAssert.AreEqual(months.OrderByDescending(m => m, StringComparer.Ordinal).ToList(), months, \"newest month first\");");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, ids);");
        L("        }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task Another_tenants_events_are_not_in_the_summary()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        int tenant = Tenant();");
        L("        if (ApiFixture.OtherTenantId is null)");
        L("            Assert.Inconclusive(\"The database has only one tenant.\");");
        L("        string kind = NewKind();");
        L($"        var id = await {support}.AddAsync(ApiFixture.Services, ApiFixture.OtherTenantId!.Value, kind, 2);");
        L("        try");
        L("        {");
        L("            using var client = Admin(tenant);");
        L("            Assert.IsNull(Line(await ReadAsync(client), kind, ThisMonth()), \"a tenant never reads another tenant's usage\");");
        L("            using var elsewhere = Admin(ApiFixture.OtherTenantId!.Value);");
        L("            Assert.IsNotNull(Line(await ReadAsync(elsewhere), kind, ThisMonth()));");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, id);");
        L("        }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_platform_administrator_names_the_tenant_to_read()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        int tenant = Tenant();");
        L("        string kind = NewKind();");
        L($"        var id = await {support}.AddAsync(ApiFixture.Services, tenant, kind, 1);");
        L("        try");
        L("        {");
        L("            using var platform = ApiFixture.PlatformClient();");
        L("            using var unnamed = await platform.GetAsync(Route);");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, unnamed.StatusCode);");
        L("            Assert.IsNotNull(Line(await ReadAsync(platform, $\"?tenantId={tenant}\"), kind, ThisMonth()));");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, id);");
        L("        }");
        L("    }");
        if (record)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task Only_a_platform_administrator_records_usage_and_it_reaches_the_summary()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        int tenant = Tenant();");
            L("        string kind = NewKind();");
            L("        using var inside = Admin(tenant);");
            L("        using var refused = await inside.PostAsJsonAsync(Route, new { tenantId = tenant, kind });");
            L("        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);");
            L("        using var platform = ApiFixture.PlatformClient();");
            L("        using var noKind = await platform.PostAsJsonAsync(Route, new { tenantId = tenant, kind = \" \" });");
            L("        Assert.AreEqual(HttpStatusCode.BadRequest, noKind.StatusCode);");
            L("        using var noTenant = await platform.PostAsJsonAsync(Route, new { kind });");
            L("        Assert.AreEqual(HttpStatusCode.BadRequest, noTenant.StatusCode);");
            L("        using var zero = await platform.PostAsJsonAsync(Route, new { tenantId = tenant, kind, quantity = 0 });");
            L("        Assert.AreEqual(HttpStatusCode.BadRequest, zero.StatusCode);");
            L("        using var unknown = await platform.PostAsJsonAsync(Route, new { tenantId = 2000000000, kind });");
            L("        Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);");
            if (t.Quantity is not null && !t.QuantityIsDecimal)
            {
                L("        using var fraction = await platform.PostAsJsonAsync(Route, new { tenantId = tenant, kind, quantity = 1.5 });");
                L("        Assert.AreEqual(HttpStatusCode.BadRequest, fraction.StatusCode, \"a whole-number ledger refuses a fraction\");");
            }
            L("        using var made = await platform.PostAsJsonAsync(Route, new { tenantId = tenant, kind, quantity = 6 });");
            L("        Assert.AreEqual(HttpStatusCode.Created, made.StatusCode, await made.Content.ReadAsStringAsync());");
            L("        var recorded = (await made.Content.ReadFromJsonAsync<JsonObject>())!;");
            L($"        var id = recorded[\"id\"]!.GetValue<{t.Key.CSharpBase()}>();");
            L("        try");
            L("        {");
            L("            Assert.IsFalse(recorded[\"over\"]!.GetValue<bool>(), \"a kind no plan limits is never over\");");
            if (t.Quantity is not null)
                L("            Assert.AreEqual(6m, recorded[\"used\"]!.GetValue<decimal>());");
            if (t.QuantityIsDecimal)
            {
                L("            using var half = await platform.PostAsJsonAsync(Route, new { tenantId = tenant, kind, quantity = 1.5 });");
                L("            Assert.AreEqual(HttpStatusCode.Created, half.StatusCode, await half.Content.ReadAsStringAsync());");
                L($"            var halfId = (await half.Content.ReadFromJsonAsync<JsonObject>())![\"id\"]!.GetValue<{t.Key.CSharpBase()}>();");
                L($"            await {support}.RemoveAsync(ApiFixture.Services, halfId);");
            }
            L("            var line = Line(await ReadAsync(inside), kind, ThisMonth());");
            if (t.Quantity is not null)
                L("            Assert.AreEqual(6m, line![\"quantity\"]!.GetValue<decimal>());");
            else
                L("            Assert.IsNotNull(line);");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, id);");
            L("        }");
            L("    }");
        }
        if (rollup is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task Rolling_up_totals_old_events_into_the_months_and_the_summary_still_adds_them()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        int tenant = Tenant();");
            L("        string kind = NewKind();");
            L($"        await {support}.AddAsync(ApiFixture.Services, tenant, kind, 2, new DateTime(2019, 3, 10, 12, 0, 0));");
            L($"        await {support}.AddAsync(ApiFixture.Services, tenant, kind, 3, new DateTime(2019, 3, 20, 12, 0, 0));");
            L($"        var recent = await {support}.AddAsync(ApiFixture.Services, tenant, kind, 4);");
            L("        try");
            L("        {");
            L("            using var platform = ApiFixture.PlatformClient();");
            L("            using var inside = Admin(tenant);");
            L("            using var refused = await inside.PostAsync($\"{Route}/roll-up?before=2019-04-01\", null);");
            L("            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, \"only a platform administrator rolls up\");");
            L("            using var rolled = await platform.PostAsync($\"{Route}/roll-up?before=2019-04-01\", null);");
            L("            Assert.AreEqual(HttpStatusCode.OK, rolled.StatusCode, await rolled.Content.ReadAsStringAsync());");
            L("            Assert.IsTrue((await rolled.Content.ReadFromJsonAsync<JsonObject>())![\"events\"]!.GetValue<int>() >= 2, \"the two old events were totalled\");");
            L("            var lines = await ReadAsync(inside);");
            L("            var march = Line(lines, kind, \"2019-03\");");
            L("            Assert.IsNotNull(march, \"the rolled month is still in the summary\");");
            L("            Assert.AreEqual(5m, march![\"quantity\"]!.GetValue<decimal>(), \"2 and 3 units\");");
            L("            Assert.AreEqual(0, march[\"events\"]!.GetValue<int>(), \"no event of that month is left\");");
            L("            Assert.AreEqual(4m, Line(lines, kind, ThisMonth())![\"quantity\"]!.GetValue<decimal>(), \"this month was not rolled\");");
            L("            Assert.IsNull(Line(await ReadAsync(inside, \"?from=2019-03-10\"), kind, \"2019-03\"), \"a rolled month is left out when From cuts into it\");");
            L("            Assert.IsNull(Line(await ReadAsync(inside, \"?to=2019-03-20\"), kind, \"2019-03\"), \"a rolled month is left out when Before cuts into it\");");
            L("            Assert.IsNotNull(Line(await ReadAsync(inside, \"?from=2019-03-01&to=2019-04-01\"), kind, \"2019-03\"), \"a rolled month is kept when the whole month is inside\");");
            L($"            await {support}.AddAsync(ApiFixture.Services, tenant, kind, 6, new DateTime(2019, 3, 5, 12, 0, 0));");
            L("            using var again = await platform.PostAsync($\"{Route}/roll-up?before=2019-04-01\", null);");
            L("            Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);");
            L("            Assert.AreEqual(11m, Line(await ReadAsync(inside), kind, \"2019-03\")![\"quantity\"]!.GetValue<decimal>(), \"a second roll-up adds to the month\");");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, recent);");
            L($"            await {support}.RemoveMonthlyAsync(ApiFixture.Services, tenant, kind);");
            L("        }");
            L("    }");
        }
        L();
        L("    [TestMethod]");
        L("    public async Task Nothing_edits_or_deletes_an_event()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var client = ApiFixture.PlatformClient();");
        L("        foreach (var response in new[] { await client.PutAsJsonAsync($\"{Route}/1\", new { }), await client.DeleteAsync($\"{Route}/1\"), await client.PatchAsJsonAsync($\"{Route}/1\", new { }) })");
        L("        {");
        L("            using (response)");
        L("                Assert.IsTrue(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, \"the ledger has no edit or delete route\");");
        L("        }");
        L("    }");
        L("}");
        return o.ToString();
    }
}
