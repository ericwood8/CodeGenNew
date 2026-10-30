using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated ASP.NET tests of what the query filter of a multi-tenant API does not reach (<c>TenantBypassApiTests.cs</c>): the dashboard, a junction table with a tenant-owned side, the row approvals of a
/// tenant-owned table and the tenant a host name names. Every case works in a tenant the test makes with a platform token, so the scratch database needs no second tenant; <see cref="TenantBypassPlan"/> decides the cases. </summary>
public static class ApiTenantBypassTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.ApiTests && project.ApiAuth && TenantPlan.IsEnabled(project) && TenantPlan.TenantTableOf(database, project) is not null
        && (TenantBypassPlan.Dashboard(database, project).Count > 0 || TenantBypassPlan.Junctions(database, project).Count > 0 || TenantBypassPlan.Approvals(database, project) is not null || TenantBypassPlan.HostColumn(database, project) is not null);

    /// <summary> The route and the key property of a table's API as its own test class uses them. </summary>
    private static (string Route, string Key) RouteOf(TableModel table, ProjectSettings project)
    {
        if (!TenantTests.HasClass(table, project) && ApiTestPlan.For(table, project) is { } plan)
            return (plan.Route, plan.KeyJson);
        return (ApiRequests.Route(table), JsonNames.Camel(table.PrimaryKeyColumns[0].Name));
    }

    private static string Literal(string text) => "\"" + CSharpText.Escape(text) + "\"";

    public static string ClassText(DatabaseModel database, ProjectSettings project)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var (tenantRoute, tenantKey) = RouteOf(tenantTable, project);
        string admin = project.AuthAdminRole, apis = project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis";
        string activeLine = tenantTable.IsActiveColumn is { } flag ? $"        body[\"{JsonNames.Camel(flag.Name)}\"] = true;\n" : "";
        var host = TenantBypassPlan.HostColumn(database, project);
        var dashboard = TenantBypassPlan.Dashboard(database, project);
        var junctions = TenantBypassPlan.Junctions(database, project);
        var approvals = TenantBypassPlan.Approvals(database, project);

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L("/// <summary> What the query filter of the context does not reach: the dashboard, a junction table, the row approvals and the host name of a tenant. Each test makes a tenant of its own, so it sees only what it put there. </summary>");
        L("[TestClass]");
        L("public class TenantBypassApiTests");
        L("{");
        L($"    private const string TenantRoute = \"{tenantRoute}\";");
        L();
        L("    /// <summary> Adds a tenant with a platform token and answers its key; the test removes it again. </summary>");
        L("    private static async Task<int> MakeTenantAsync(string? slug = null)");
        L("    {");
        L($"        var body = await {tenantTable.TableName}ApiTests.NewBodyAsync();");
        o.Append(activeLine);
        if (host is not null)
        {
            L("        if (slug is not null)");
            L($"            body[\"{JsonNames.Camel(host.Name)}\"] = slug;");
        }
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        using var made = await platform.PostAsJsonAsync(TenantRoute, body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, made.StatusCode, await made.Content.ReadAsStringAsync());");
        L($"        return (await made.Content.ReadFromJsonAsync<JsonObject>())![\"{tenantKey}\"]!.GetValue<int>();");
        L("    }");
        L();
        L("    private static Task RemoveTenantAsync(int key) => " + apis + ".TenantTestSupport.RemoveAsync(ApiFixture.Services, key);");

        if (dashboard.Count > 0)
        {
            L();
            L("    /// <summary> Every point of the widget is zero (or empty) and it lists no row. </summary>");
            L("    private static async Task AssertNothingShownAsync(HttpClient client, string[] widgetIds, string table)");
            L("    {");
            L("        using var reply = await client.GetAsync(\"/api/dashboard\");");
            L("        Assert.AreEqual(HttpStatusCode.OK, reply.StatusCode, await reply.Content.ReadAsStringAsync());");
            L("        var widgets = (await reply.Content.ReadFromJsonAsync<JsonObject>())![\"widgets\"]!.AsArray();");
            L("        foreach (string id in widgetIds)");
            L("        {");
            L("            var widget = widgets.FirstOrDefault(w => w![\"id\"]!.GetValue<string>() == id);");
            L("            if (widget is null)");
            L("                continue;");
            L("            foreach (var point in widget[\"points\"]!.AsArray())");
            L("                Assert.IsTrue(point![\"value\"] is null || point[\"value\"]!.GetValue<double>() == 0, $\"{id}: a tenant without {table} rows is shown {point}\");");
            L("            Assert.IsEmpty(widget[\"rows\"]!.AsArray(), $\"{id}: a tenant without {table} rows is shown rows\");");
            L("        }");
            L("    }");
            foreach (var d in dashboard)
            {
                var (route, key) = RouteOf(d.Table, project);
                string ids = string.Join(", ", d.WidgetIds.Select(Literal));
                L();
                L("    [TestMethod]");
                L($"    public async Task The_dashboard_shows_a_tenant_without_rows_none_of_{d.Table.TableName}()");
                L("    {");
                L("        ApiFixture.RequireDatabase();");
                L("        if (ApiFixture.TenantId is null)");
                L("        {");
                L("            Assert.Inconclusive(\"the database needs an active tenant\");");
                L("            return;");
                L("        }");
                L($"        using var created = await ApiFixture.Client.PostAsJsonAsync(\"{route}\", await {d.Table.TableName}ApiTests.NewBodyAsync());");
                L("        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());");
                L($"        string row = (await created.Content.ReadFromJsonAsync<JsonObject>())![\"{key}\"]!.ToString();");
                L("        int empty = await MakeTenantAsync();");
                L("        try");
                L("        {");
                L($"            using var client = ApiFixture.ClientInTenant(empty, \"{admin}\");");
                L($"            await AssertNothingShownAsync(client, [{ids}], \"{d.Table.TableName}\");");
                L("            using var platform = ApiFixture.PlatformClient();");
                L($"            await AssertNothingShownAsync(platform, [{ids}], \"{d.Table.TableName}\");");
                L("        }");
                L("        finally");
                L("        {");
                L($"            using (await ApiFixture.Client.DeleteAsync($\"{route}/{{row}}\")) {{ }}");
                L("            await RemoveTenantAsync(empty);");
                L("        }");
                L("    }");
            }
        }

        foreach (var j in junctions)
        {
            var junction = j.Junction.Junction!;
            string route = BlazorNames.ApiRoute(j.Junction.TableName);
            string anchorKey = ApiSampleValues.Json(JsonNames.Camel(junction.AnchorForeignKey.ReferencedColumns[0])), targetKey = ApiSampleValues.Json(JsonNames.Camel(junction.TargetForeignKey.ReferencedColumns[0]));
            string Existing(string role, string keyJson, bool text) =>
                $"await ApiFixture.{(text ? "ExistingCodeAsync" : "ExistingKeyAsync")}({ApiSampleValues.Json(BlazorNames.ApiRoute(role))}, {keyJson}, {(text ? "\"\"" : "1")}, {ApiSampleValues.Json(role)})";
            string who = j.AnchorOwned ? "what it does not see" : "a target it does not see";
            L();
            L("    [TestMethod]");
            L($"    public async Task {j.Junction.TableName}_another_tenant_cannot_list_link_or_unlink_{who.Replace(' ', '_')}()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        if (ApiFixture.TenantId is null)");
            L("        {");
            L("            Assert.Inconclusive(\"the database needs an active tenant\");");
            L("            return;");
            L("        }");
            L($"        var anchor = {Existing(junction.AnchorRole, anchorKey, junction.AnchorKeyIsText)};");
            L($"        var target = {Existing(junction.TargetRole, targetKey, junction.TargetKeyIsText)};");
            L("        var link = new { anchorId = anchor, targetId = target };");
            L($"        using var linked = await ApiFixture.Client.PostAsJsonAsync(\"{route}/junction/link\", link);");
            L("        Assert.AreEqual(HttpStatusCode.OK, linked.StatusCode, await linked.Content.ReadAsStringAsync());");
            L("        int other = await MakeTenantAsync();");
            L("        try");
            L("        {");
            L($"            using var rival = ApiFixture.ClientInTenant(other, \"{admin}\");");
            L($"            using var list = await rival.GetAsync($\"{route}/junction/{{Uri.EscapeDataString(anchor.ToString()!)}}\");");
            if (j.AnchorOwned)
                L("            Assert.AreEqual(HttpStatusCode.NotFound, list.StatusCode, \"another tenant is not shown the links of a row it does not see\");");
            else
            {
                L("            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, await list.Content.ReadAsStringAsync());");
                L("            var items = (await list.Content.ReadFromJsonAsync<JsonArray>())!;");
                L("            Assert.IsFalse(items.Any(i => i![\"targetId\"]!.ToString() == target.ToString()), \"another tenant is not offered a target it does not see\");");
            }
            L($"            using var link2 = await rival.PostAsJsonAsync(\"{route}/junction/link\", link);");
            L("            Assert.AreEqual(HttpStatusCode.NotFound, link2.StatusCode, \"another tenant cannot link\");");
            L($"            using var unlink = await rival.PostAsJsonAsync(\"{route}/junction/unlink\", link);");
            L("            Assert.AreEqual(HttpStatusCode.NotFound, unlink.StatusCode, \"another tenant cannot unlink\");");
            L($"            using var mine = await ApiFixture.Client.GetAsync($\"{route}/junction/{{Uri.EscapeDataString(anchor.ToString()!)}}\");");
            L("            var own = (await mine.Content.ReadFromJsonAsync<JsonArray>())!;");
            L("            Assert.IsTrue(own.Any(i => i![\"targetId\"]!.ToString() == target.ToString() && i[\"isSelected\"]!.GetValue<bool>()), \"the link is still there\");");
            L("        }");
            L("        finally");
            L("        {");
            L($"            using (await ApiFixture.Client.PostAsJsonAsync(\"{route}/junction/unlink\", link)) {{ }}");
            L("            await RemoveTenantAsync(other);");
            L("        }");
            L("    }");
        }

        if (approvals is { } a)
        {
            var (route, _) = RouteOf(a.Plan.Tables[0].Table, project);
            string row = a.Seed.RowKey.ToString();
            L();
            L("    private static readonly string[] ApprovalSetup =");
            L("    [");
            foreach (string sql in a.Seed.Setup)
                L("        " + Literal(sql) + ",");
            L("    ];");
            L();
            L("    private static readonly string[] ApprovalCleanup =");
            L("    [");
            foreach (string sql in a.Seed.Cleanup)
                L("        " + Literal(sql) + ",");
            L("    ];");
            L();
            L("    private static async Task RunAsync(string[] statements)");
            L("    {");
            L("        foreach (string sql in statements)");
            L($"            await {apis}.TenantTestSupport.RunAsync(ApiFixture.Services, sql);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task Another_tenant_cannot_ask_decide_withdraw_or_read_the_approvals_of_a_row()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        if (ApiFixture.TenantId is not int mine)");
            L("        {");
            L("            Assert.Inconclusive(\"the database needs an active tenant\");");
            L("            return;");
            L("        }");
            L("        await RunAsync(ApprovalCleanup);");
            L("        await RunAsync(ApprovalSetup);");
            L("        int other = await MakeTenantAsync();");
            L("        try");
            L("        {");
            L($"            string route = \"{route}/{row}\";");
            L($"            using var requester = ApiFixture.ClientInTenant(mine, \"{admin}\", \"{ApprovalTestSeed.Requester}\");");
            L($"            using var approver = ApiFixture.ClientInTenant(mine, \"{admin}\", \"{ApprovalTestSeed.Approver}\");");
            L("            using var asked = await requester.PostAsJsonAsync(route + \"/request\", new { reason = \"please\" });");
            L("            Assert.AreEqual(HttpStatusCode.OK, asked.StatusCode, await asked.Content.ReadAsStringAsync());");
            L();
            L($"            using var rivalRequester = ApiFixture.ClientInTenant(other, \"{admin}\", \"{ApprovalTestSeed.Requester}\");");
            L($"            using var rivalApprover = ApiFixture.ClientInTenant(other, \"{admin}\", \"{ApprovalTestSeed.Approver}\");");
            L("            using var request = await rivalRequester.PostAsJsonAsync(route + \"/request\", new { reason = \"please\" });");
            if (TenantPlan.PeopleOf(database, project) is not null)
                L("            Assert.IsTrue(request.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, \"another tenant's row cannot be asked for (a person of the first tenant is no person of this one)\");");
            else
                L("            Assert.AreEqual(HttpStatusCode.NotFound, request.StatusCode, \"another tenant's row cannot be asked for\");");
            L("            using var approve = await rivalApprover.PostAsJsonAsync(route + \"/approve\", new { note = \"fine\" });");
            L("            Assert.AreNotEqual(HttpStatusCode.OK, approve.StatusCode, \"another tenant's cycle cannot be approved\");");
            L("            using var withdraw = await rivalRequester.PostAsync(route + \"/withdraw\", null);");
            L("            Assert.AreNotEqual(HttpStatusCode.OK, withdraw.StatusCode, \"another tenant's cycle cannot be withdrawn\");");
            L("            using var history = await rivalRequester.GetAsync(route + \"/approvals\");");
            L("            Assert.IsTrue(history.StatusCode == HttpStatusCode.NotFound || (history.StatusCode == HttpStatusCode.OK && (await history.Content.ReadAsStringAsync()).Trim() == \"[]\"), \"another tenant is shown no history\");");
            L($"            using var waiting = await rivalApprover.GetAsync(\"{route}/toapprove\");");
            L("            if (waiting.StatusCode == HttpStatusCode.OK)");
            L($"                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(await waiting.Content.ReadAsStringAsync(), \"\\\"rowId\\\":{row}(?!\\\\d)\"), \"another tenant's waiting request is not listed\");");
            L();
            L($"            using var ownWaiting = await approver.GetAsync(\"{route}/toapprove\");");
            L("            Assert.AreEqual(HttpStatusCode.OK, ownWaiting.StatusCode, await ownWaiting.Content.ReadAsStringAsync());");
            L($"            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(await ownWaiting.Content.ReadAsStringAsync(), \"\\\"rowId\\\":{row}(?!\\\\d)\"), \"the tenant's own approver sees the request\");");
            L("            using var decided = await approver.PostAsJsonAsync(route + \"/approve\", new { note = \"fine\" });");
            L("            Assert.AreEqual(HttpStatusCode.OK, decided.StatusCode, await decided.Content.ReadAsStringAsync());");
            L("        }");
            L("        finally");
            L("        {");
            L("            await RunAsync(ApprovalCleanup);");
            L("            await RemoveTenantAsync(other);");
            L("        }");
            L("    }");
        }

        if (host is not null)
        {
            L();
            L("    /// <summary> A GET of the tenants route on this host name (the TenantHostColumn rule reads the first label). </summary>");
            L("    private static HttpRequestMessage OnHost(string host) => new(HttpMethod.Get, \"http://\" + host + TenantRoute);");
            L();
            L("    [TestMethod]");
            L("    public async Task A_token_is_refused_on_the_host_of_another_tenant_and_not_restricted_on_a_host_that_names_none()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        if (ApiFixture.TenantId is null)");
            L("        {");
            L("            Assert.Inconclusive(\"the database needs an active tenant\");");
            L("            return;");
            L("        }");
            L("        string label = \"t\" + Guid.NewGuid().ToString(\"N\")[..10];");
            L("        int other = await MakeTenantAsync(label);");
            L("        try");
            L("        {");
            L("            using var refused = await ApiFixture.Client.SendAsync(OnHost(label + \".example.test\"));");
            L("            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, await refused.Content.ReadAsStringAsync());");
            L($"            Assert.AreEqual(\"{TenantWords.HostType}\", (await refused.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
            L($"            using var own = ApiFixture.ClientInTenant(other, \"{admin}\");");
            L("            using var allowed = await own.SendAsync(OnHost(label + \".example.test\"));");
            L("            Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode, \"a token works on its own tenant's host: \" + await allowed.Content.ReadAsStringAsync());");
            L("            using var platform = ApiFixture.PlatformClient();");
            L("            using var platformReply = await platform.SendAsync(OnHost(label + \".example.test\"));");
            L("            Assert.AreEqual(HttpStatusCode.OK, platformReply.StatusCode, \"a platform token is not restricted on any host\");");
            L("            using var unknown = await ApiFixture.Client.SendAsync(OnHost(\"nobody\" + label + \".example.test\"));");
            L("            Assert.AreEqual(HttpStatusCode.OK, unknown.StatusCode, \"a host that names no tenant is not restricted\");");
            L("            using var plain = await ApiFixture.Client.SendAsync(OnHost(\"localhost\"));");
            L("            Assert.AreEqual(HttpStatusCode.OK, plain.StatusCode, \"localhost names no tenant\");");
            L("        }");
            L("        finally");
            L("        {");
            L("            await RemoveTenantAsync(other);");
            L("        }");
            L("    }");
        }
        L("}");
        return o.ToString();
    }
}
