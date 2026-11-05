using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated ASP.NET tests of plans, subscriptions and limits (<c>SubscriptionApiTests.cs</c>): writes pause while the subscription is not active and reads go on, a tenant without a subscription, a create past
/// a plan limit, a trial that ends, who changes a subscription, and what the administrator sees. Each test makes a tenant and a plan of its own with a platform token and removes them again. </summary>
public static class ApiSubscriptionTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.ApiTests && project.ApiAuth && TenantPlan.IsEnabled(project) && SubscriptionPlan.IsEnabled(project) && SubscriptionPlan.Problems(database, project).Count == 0;

    public static string ClassText(DatabaseModel database, ProjectSettings project)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string Name(string key) => SubscriptionScenario.Get(project, key).CSharpName;
        var subscription = SubscriptionPlan.SubscriptionOf(database, project)!;
        var limit = SubscriptionPlan.LimitTableOf(database, project);
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var tenantKey = JsonNames.Camel(tenantTable.PrimaryKeyColumns[0].Name);
        string tenantRoute = ApiRequests.Route(tenantTable), admin = project.AuthAdminRole, apis = project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis";
        string activeLine = tenantTable.IsActiveColumn is { } flag ? $"        body[\"{JsonNames.Camel(flag.Name)}\"] = true;\n" : "";
        var write = SubscriptionPlan.TestWriteTable(database, project);
        string writeRoute = write is null ? "" : ApiRequests.Route(write.Table);
        var limited = write is null || limit is null ? null : SubscriptionPlan.LimitedTables(database, project).FirstOrDefault(l => l.Table == write.Table && !l.IsMembership);
        bool trialing = SubscriptionPlan.ActiveStatuses(project).Contains("trialing", StringComparer.OrdinalIgnoreCase);

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L("/// <summary> Plans, subscriptions and limits. Each test makes a tenant and a plan of its own with a platform token and removes them again. </summary>");
        L("[TestClass]");
        L("public class SubscriptionApiTests");
        L("{");
        L($"    private const string TenantRoute = \"{tenantRoute}\";");
        if (write is not null)
            L($"    private const string WriteRoute = \"{writeRoute}\";");
        L();
        L("    private static async Task<int> MakeTenantAsync()");
        L("    {");
        L($"        var body = await {tenantTable.TableName}ApiTests.NewBodyAsync();");
        o.Append(activeLine);
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        using var made = await platform.PostAsJsonAsync(TenantRoute, body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, made.StatusCode, await made.Content.ReadAsStringAsync());");
        L($"        return (await made.Content.ReadFromJsonAsync<JsonObject>())![\"{tenantKey}\"]!.GetValue<int>();");
        L("    }");
        L();
        L("    private static Task<int> MakePlanAsync() => " + apis + ".SubscriptionTestSupport.AddPlanAsync(ApiFixture.Services, new JsonObject");
        L("    {");
        foreach (var c in ApiSampleValues.BodyColumns(subscription.PlanTable).Where(c => c != subscription.PlanKey))
            L($"        [{ApiSampleValues.Json(JsonNames.Camel(c.Name))}] = {TenantTests.Value(subscription.PlanTable, c, project)},");
        L("    }.ToJsonString());");
        L();
        L("    private static async Task RemoveAsync(int tenant, int plan)");
        L("    {");
        L($"        await {apis}.SubscriptionTestSupport.ClearAsync(ApiFixture.Services, tenant);");
        L($"        await {apis}.SubscriptionTestSupport.RemovePlanAsync(ApiFixture.Services, plan, tenant);");
        L($"        await {apis}.TenantTestSupport.RemoveAsync(ApiFixture.Services, tenant);");
        L("    }");
        L();
        L("    private static async Task SetAsync(int tenant, object change)");
        L("    {");
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        using var set = await platform.PutAsJsonAsync($\"/api/subscription/{tenant}\", change);");
        L("        Assert.AreEqual(HttpStatusCode.OK, set.StatusCode, await set.Content.ReadAsStringAsync());");
        L("    }");
        if (write is not null)
        {
            L();
            L("    private static async Task AssertProblemAsync(HttpResponseMessage response, string type)");
            L("    {");
            L("        Assert.AreEqual(HttpStatusCode.PaymentRequired, response.StatusCode, await response.Content.ReadAsStringAsync());");
            L("        var problem = (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
            L("        StringAssert.Contains(problem[\"type\"]!.GetValue<string>(), type);");
            L("    }");
            L();
            L("    [TestMethod]");
            L($"    public async Task {Name("Paused")}()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        int tenant = await MakeTenantAsync();");
            L("        int plan = await MakePlanAsync();");
            L("        try");
            L("        {");
            L("            await SetAsync(tenant, new { planId = plan, status = \"cancelled\" });");
            L($"            using var client = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
            L($"            using var refused = await client.PostAsJsonAsync(WriteRoute, await {write.Table.TableName}ApiTests.NewBodyAsync());");
            L($"            await AssertProblemAsync(refused, \"{SubscriptionWords.InactiveType.Split('/').Last()}\");");
            L("            using var read = await client.GetAsync(WriteRoute);");
            L("            Assert.AreEqual(HttpStatusCode.OK, read.StatusCode, \"reads are never paused\");");
            L("            await SetAsync(tenant, new { status = \"active\" });");
            L($"            using var created = await client.PostAsJsonAsync(WriteRoute, await {write.Table.TableName}ApiTests.NewBodyAsync());");
            L("            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());");
            L("        }");
            L("        finally");
            L("        {");
            L("            await RemoveAsync(tenant, plan);");
            L("        }");
            L("    }");
            L();
            L("    [TestMethod]");
            L($"    public async Task {Name("NoSubscription")}()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        int tenant = await MakeTenantAsync();");
            L("        try");
            L("        {");
            L($"            using var client = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
            L($"            using var response = await client.PostAsJsonAsync(WriteRoute, await {write.Table.TableName}ApiTests.NewBodyAsync());");
            if (project.SubscriptionRequired)
                L($"            await AssertProblemAsync(response, \"{SubscriptionWords.InactiveType.Split('/').Last()}\");");
            else
                L("            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {apis}.SubscriptionTestSupport.ClearAsync(ApiFixture.Services, tenant);");
            L($"            await {apis}.TenantTestSupport.RemoveAsync(ApiFixture.Services, tenant);");
            L("        }");
            L("    }");
            if (limited is not null)
            {
                string key = CSharpText.Quote(limited.LimitKey);
                L();
                L("    [TestMethod]");
                L($"    public async Task {Name("Limit")}()");
                L("    {");
                L("        ApiFixture.RequireDatabase();");
                L("        int tenant = await MakeTenantAsync();");
                L("        int plan = await MakePlanAsync();");
                L("        try");
                L("        {");
                L($"            await {apis}.SubscriptionTestSupport.SetLimitAsync(ApiFixture.Services, plan, {key}, 1);");
                L("            await SetAsync(tenant, new { planId = plan, status = \"active\" });");
                L($"            using var client = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
                L($"            using var first = await client.PostAsJsonAsync(WriteRoute, await {write!.Table.TableName}ApiTests.NewBodyAsync());");
                L("            Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());");
                L($"            using var second = await client.PostAsJsonAsync(WriteRoute, await {write.Table.TableName}ApiTests.NewBodyAsync());");
                L($"            await AssertProblemAsync(second, \"{SubscriptionWords.LimitType.Split('/').Last()}\");");
                L("            using var shown = await client.GetAsync(\"/api/subscription\");");
                L("            Assert.AreEqual(HttpStatusCode.OK, shown.StatusCode, await shown.Content.ReadAsStringAsync());");
                L("            var info = (await shown.Content.ReadFromJsonAsync<JsonObject>())!;");
                L($"            var row = info[\"limits\"]!.AsArray().Single(l => l![\"key\"]!.GetValue<string>() == {key})!;");
                L("            Assert.AreEqual(1, row[\"allowed\"]!.GetValue<int>());");
                L("            Assert.AreEqual(1, row[\"used\"]!.GetValue<int>());");
                L($"            await {apis}.SubscriptionTestSupport.SetLimitAsync(ApiFixture.Services, plan, {key}, 2);");
                L($"            using var third = await client.PostAsJsonAsync(WriteRoute, await {write.Table.TableName}ApiTests.NewBodyAsync());");
                L("            Assert.AreEqual(HttpStatusCode.Created, third.StatusCode, await third.Content.ReadAsStringAsync());");
                L("        }");
                L("        finally");
                L("        {");
                L("            await RemoveAsync(tenant, plan);");
                L("        }");
                L("    }");
                L();
                L("    [TestMethod]");
                L($"    public async Task {Name("Concurrent")}()");
                L("    {");
                L("        ApiFixture.RequireDatabase();");
                L("        int tenant = await MakeTenantAsync();");
                L("        int plan = await MakePlanAsync();");
                L("        try");
                L("        {");
                L($"            await {apis}.SubscriptionTestSupport.SetLimitAsync(ApiFixture.Services, plan, {key}, 1);");
                L("            await SetAsync(tenant, new { planId = plan, status = \"active\" });");
                L($"            using var client = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
                L($"            var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ => (await client.PostAsJsonAsync(WriteRoute, await {write!.Table.TableName}ApiTests.NewBodyAsync())).StatusCode));");
                L("            Assert.AreEqual(1, answers.Count(a => a == HttpStatusCode.Created), string.Join(\", \", answers));");
                L("            Assert.AreEqual(3, answers.Count(a => a == HttpStatusCode.PaymentRequired), string.Join(\", \", answers));");
                L("        }");
                L("        finally");
                L("        {");
                L("            await RemoveAsync(tenant, plan);");
                L("        }");
                L("    }");
            }
            if (subscription.PeriodEnd is not null && trialing)
            {
                L();
                L("    [TestMethod]");
                L($"    public async Task {Name("Trial")}()");
                L("    {");
                L("        ApiFixture.RequireDatabase();");
                L("        int tenant = await MakeTenantAsync();");
                L("        int plan = await MakePlanAsync();");
                L("        try");
                L("        {");
                L("            await SetAsync(tenant, new { planId = plan, status = \"trialing\", periodEnd = DateTime.UtcNow.AddDays(-1) });");
                L($"            using var client = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
                L($"            using var ended = await client.PostAsJsonAsync(WriteRoute, await {write.Table.TableName}ApiTests.NewBodyAsync());");
                L($"            await AssertProblemAsync(ended, \"{SubscriptionWords.InactiveType.Split('/').Last()}\");");
                L("            await SetAsync(tenant, new { periodEnd = DateTime.UtcNow.AddDays(14) });");
                L($"            using var running = await client.PostAsJsonAsync(WriteRoute, await {write.Table.TableName}ApiTests.NewBodyAsync());");
                L("            Assert.AreEqual(HttpStatusCode.Created, running.StatusCode, await running.Content.ReadAsStringAsync());");
                L("        }");
                L("        finally");
                L("        {");
                L("            await RemoveAsync(tenant, plan);");
                L("        }");
                L("    }");
            }
        }
        L();
        L("    [TestMethod]");
        L($"    public async Task {Name("PlatformAdmin")}()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        int tenant = await MakeTenantAsync();");
        L("        int plan = await MakePlanAsync();");
        L("        try");
        L("        {");
        L($"            using var inside = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
        L("            using var refused = await inside.PutAsJsonAsync($\"/api/subscription/{tenant}\", new { planId = plan, status = \"active\" });");
        L("            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, \"an administrator inside a tenant cannot give themselves a plan\");");
        L("            using var platform = ApiFixture.PlatformClient();");
        L("            using var nobody = await platform.PutAsJsonAsync($\"/api/subscription/{int.MaxValue}\", new { planId = plan, status = \"active\" });");
        L("            Assert.AreEqual(HttpStatusCode.NotFound, nobody.StatusCode);");
        L("            using var incomplete = await platform.PutAsJsonAsync($\"/api/subscription/{tenant}\", new { status = \"active\" });");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, incomplete.StatusCode, \"a tenant with no subscription needs a plan and a status\");");
        L("            using var unknown = await platform.PutAsJsonAsync($\"/api/subscription/{tenant}\", new { planId = int.MaxValue, status = \"active\" });");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode, \"an unknown plan is refused\");");
        L("        }");
        L("        finally");
        L("        {");
        L("            await RemoveAsync(tenant, plan);");
        L("        }");
        L("    }");
        if (subscription.PeriodEnd is not null)
        {
            L();
            L("    [TestMethod]");
            L($"    public async Task {Name("Renewal")}()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        int tenant = await MakeTenantAsync();");
            L("        int plan = await MakePlanAsync();");
            L("        try");
            L("        {");
            L("            await SetAsync(tenant, new { planId = plan, status = \"active\", periodEnd = DateTime.UtcNow.AddDays(5) });");
            L($"            using var inside = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
            L("            using var refused = await inside.PostAsJsonAsync($\"/api/subscription/{tenant}/renew\", new { days = 10 });");
            L("            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, \"a tenant's administrator cannot renew their own period\");");
            L("            using var platform = ApiFixture.PlatformClient();");
            L("            using var renewed = await platform.PostAsJsonAsync($\"/api/subscription/{tenant}/renew\", new { days = 10 });");
            L("            Assert.AreEqual(HttpStatusCode.OK, renewed.StatusCode, await renewed.Content.ReadAsStringAsync());");
            L("            var end = (await renewed.Content.ReadFromJsonAsync<JsonObject>())![\"periodEnd\"]!.GetValue<DateTime>();");
            L("            Assert.IsTrue(Math.Abs((end - DateTime.UtcNow.AddDays(15)).TotalMinutes) < 5, \"the new period starts at the old end (in 5 days) and lasts 10 days, but ends \" + end);");
            L("            using var none = await platform.PostAsJsonAsync($\"/api/subscription/{tenant}/renew\", new { days = 0 });");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, none.StatusCode);");
            L("            using var nobody = await platform.PostAsJsonAsync($\"/api/subscription/{int.MaxValue}/renew\", new { days = 1 });");
            L("            Assert.AreEqual(HttpStatusCode.NotFound, nobody.StatusCode);");
            L("        }");
            L("        finally");
            L("        {");
            L("            await RemoveAsync(tenant, plan);");
            L("        }");
            L("    }");
        }
        if (project.SubscriptionPlanChoice)
        {
            L();
            L("    [TestMethod]");
            L($"    public async Task {Name("PlanChoice")}()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        int tenant = await MakeTenantAsync();");
            L("        int plan = await MakePlanAsync();");
            L("        int other = await MakePlanAsync();");
            L("        try");
            L("        {");
            L("            await SetAsync(tenant, new { planId = plan, status = \"active\" });");
            L($"            using var client = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
            L("            using var shown = await client.GetAsync(\"/api/subscription\");");
            L("            Assert.IsTrue((await shown.Content.ReadFromJsonAsync<JsonObject>())![\"canChoose\"]!.GetValue<bool>());");
            L("            using var unknown = await client.PutAsJsonAsync(\"/api/subscription/plan\", new { planId = int.MaxValue });");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);");
            if (limited is not null)
            {
                string key = CSharpText.Quote(limited.LimitKey);
                L($"            await {apis}.SubscriptionTestSupport.SetLimitAsync(ApiFixture.Services, plan, {key}, 5);");
                L($"            await {apis}.SubscriptionTestSupport.SetLimitAsync(ApiFixture.Services, other, {key}, 0);");
                L($"            using var made = await client.PostAsJsonAsync(WriteRoute, await {write!.Table.TableName}ApiTests.NewBodyAsync());");
                L("            Assert.AreEqual(HttpStatusCode.Created, made.StatusCode, await made.Content.ReadAsStringAsync());");
                L("            using var tooSmall = await client.PutAsJsonAsync(\"/api/subscription/plan\", new { planId = other });");
                L("            Assert.AreEqual(HttpStatusCode.Conflict, tooSmall.StatusCode, await tooSmall.Content.ReadAsStringAsync());");
                L($"            await {apis}.SubscriptionTestSupport.SetLimitAsync(ApiFixture.Services, other, {key}, 1);");
            }
            L("            using var chosen = await client.PutAsJsonAsync(\"/api/subscription/plan\", new { planId = other });");
            L("            Assert.AreEqual(HttpStatusCode.OK, chosen.StatusCode, await chosen.Content.ReadAsStringAsync());");
            L("            Assert.AreEqual(other, (await chosen.Content.ReadFromJsonAsync<JsonObject>())![\"planId\"]!.GetValue<int>());");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {apis}.SubscriptionTestSupport.RemovePlanAsync(ApiFixture.Services, other, tenant);");
            L("            await RemoveAsync(tenant, plan);");
            L("        }");
            L("    }");
        }
        L();
        L("    [TestMethod]");
        L($"    public async Task {Name("AdminSees")}()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        int tenant = await MakeTenantAsync();");
        L("        int plan = await MakePlanAsync();");
        L("        try");
        L("        {");
        L("            await SetAsync(tenant, new { planId = plan, status = \"active\" });");
        L($"            using var inside = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
        L("            using var shown = await inside.GetAsync(\"/api/subscription\");");
        L("            Assert.AreEqual(HttpStatusCode.OK, shown.StatusCode, await shown.Content.ReadAsStringAsync());");
        L("            var info = (await shown.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("            Assert.AreEqual(\"active\", info[\"status\"]!.GetValue<string>());");
        L("            Assert.AreEqual(plan, info[\"planId\"]!.GetValue<int>());");
        L("            Assert.IsTrue(info[\"writable\"]!.GetValue<bool>());");
        L("            using var member = ApiFixture.ClientInTenant(tenant, \"Member\");");
        L("            using var hidden = await member.GetAsync(\"/api/subscription\");");
        L("            Assert.AreEqual(HttpStatusCode.Forbidden, hidden.StatusCode);");
        L("        }");
        L("        finally");
        L("        {");
        L("            await RemoveAsync(tenant, plan);");
        L("        }");
        L("    }");
        L();
        L("    [TestMethod]");
        L($"    public async Task {Name("ViewPlans")}()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        int tenant = await MakeTenantAsync();");
        L("        int plan = await MakePlanAsync();");
        L("        try");
        L("        {");
        L("            await SetAsync(tenant, new { planId = plan, status = \"active\" });");
        L($"            using var inside = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
        L("            using var shown = await inside.GetAsync(\"/api/subscription\");");
        L("            Assert.AreEqual(HttpStatusCode.OK, shown.StatusCode, await shown.Content.ReadAsStringAsync());");
        L("            var info = (await shown.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("            var plans = info[\"plans\"]!.AsArray();");
        L("            var mine = plans.Single(p => p![\"planId\"]!.GetValue<int>() == plan)!;");
        L("            Assert.IsTrue(mine[\"current\"]!.GetValue<bool>());");
        L("            Assert.AreEqual(1, plans.Count(p => p![\"current\"]!.GetValue<bool>()), \"only the tenant's own plan is marked\");");
        L("            Assert.AreEqual(info[\"planName\"]!.GetValue<string>(), mine[\"name\"]!.GetValue<string>());");
        if (subscription.PlanPrice is not null)
            L("            Assert.AreEqual(mine[\"price\"]?.ToJsonString(), info[\"planPrice\"]?.ToJsonString(), \"the view shows the price of the tenant's plan\");");
        L($"            Assert.AreEqual({SubscriptionPlan.LimitedTables(database, project).Count}, mine[\"limits\"]!.AsArray().Count, \"each plan lists every limit\");");
        L("        }");
        L("        finally");
        L("        {");
        L("            await RemoveAsync(tenant, plan);");
        L("        }");
        L("    }");
        if (SubscriptionPlan.HasWebhook(database, project))
        {
            string typeSignature = SubscriptionWords.WebhookSignatureType.Split('/').Last(), typeExpired = SubscriptionWords.WebhookExpiredType.Split('/').Last();
            L();
            L("    private static HttpRequestMessage Signed(string body, string secret, long? at = null)");
            L("    {");
            L("        long time = at ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();");
            L("        string hex = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes($\"{time}.{body}\"))).ToLowerInvariant();");
            L($"        var request = new HttpRequestMessage(HttpMethod.Post, \"{SubscriptionWords.WebhookPath}\") {{ Content = new StringContent(body, System.Text.Encoding.UTF8, \"application/json\") }};");
            L($"        request.Headers.Add(\"{SubscriptionWords.WebhookHeader}\", $\"t={{time}},v1={{hex}}\");");
            L("        return request;");
            L("    }");
            L();
            L("    private static string Message(string gateway, string? status, string? id = null) =>");
            L("        System.Text.Json.JsonSerializer.Serialize(new { id, gatewayId = gateway, status }, new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });");
            L();
            L("    [TestMethod]");
            L($"    public async Task {Name("Webhook")}()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L($"        string? secret = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(ApiFixture.Services)[\"{project.SubscriptionWebhookSecret}\"];");
            L("        if (string.IsNullOrEmpty(secret))");
            L($"            Assert.Inconclusive(\"Set the environment variable {project.SubscriptionWebhookSecret} (the shared secret of the webhook) to play it.\");");
            L("        int tenant = await MakeTenantAsync();");
            L("        int plan = await MakePlanAsync();");
            L("        string gateway = \"gw-\" + Guid.NewGuid().ToString(\"N\");");
            L("        try");
            L("        {");
            L("            await SetAsync(tenant, new { planId = plan, status = \"active\", gatewayId = gateway });");
            L("            using var anonymous = ApiFixture.Anonymous();");
            L($"            using var unsigned = await anonymous.PostAsJsonAsync(\"{SubscriptionWords.WebhookPath}\", new {{ gatewayId = gateway, status = \"cancelled\" }});");
            L("            Assert.AreEqual(HttpStatusCode.Unauthorized, unsigned.StatusCode, \"a message with no signature is refused\");");
            L("            using (var wrong = await anonymous.SendAsync(Signed(Message(gateway, \"cancelled\"), \"not-the-secret\")))");
            L("                Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode, \"a message signed with another secret is refused\");");
            L("            using (var old = await anonymous.SendAsync(Signed(Message(gateway, \"cancelled\"), secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600)))");
            L("            {");
            L("                Assert.AreEqual(HttpStatusCode.BadRequest, old.StatusCode, \"a signed message an hour old is a replay\");");
            L($"                StringAssert.Contains((await old.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>(), \"{typeExpired}\");");
            L("            }");
            L($"            using var inside = ApiFixture.ClientInTenant(tenant, \"{admin}\");");
            L("            async Task<string> StatusAsync() => (await (await inside.GetAsync(\"/api/subscription\")).Content.ReadFromJsonAsync<JsonObject>())![\"status\"]!.GetValue<string>();");
            L("            Assert.AreEqual(\"active\", await StatusAsync(), \"the refused messages changed nothing\");");
            L();
            L("            string pastDue = Message(gateway, \"past_due\", \"evt-\" + Guid.NewGuid().ToString(\"N\"));");
            L("            using (var applied = await anonymous.SendAsync(Signed(pastDue, secret)))");
            L("            {");
            L("                Assert.AreEqual(HttpStatusCode.OK, applied.StatusCode, await applied.Content.ReadAsStringAsync());");
            L("                Assert.IsTrue((await applied.Content.ReadFromJsonAsync<JsonObject>())![\"applied\"]!.GetValue<bool>());");
            L("            }");
            L("            Assert.AreEqual(\"past_due\", await StatusAsync());");
            if (write is not null)
            {
                L($"            using var paused = await inside.PostAsJsonAsync(WriteRoute, await {write.Table.TableName}ApiTests.NewBodyAsync());");
                L($"            await AssertProblemAsync(paused, \"{SubscriptionWords.InactiveType.Split('/').Last()}\");");
            }
            L("            using (var again = await anonymous.SendAsync(Signed(pastDue.Replace(\"past_due\", \"active\"), secret)))");
            L("                Assert.IsFalse((await again.Content.ReadFromJsonAsync<JsonObject>())![\"applied\"]!.GetValue<bool>(), \"an event id is applied once\");");
            L("            Assert.AreEqual(\"past_due\", await StatusAsync());");
            L("            using (var resumed = await anonymous.SendAsync(Signed(Message(gateway, \"active\", \"evt-\" + Guid.NewGuid().ToString(\"N\")), secret)))");
            L("                Assert.AreEqual(HttpStatusCode.OK, resumed.StatusCode, await resumed.Content.ReadAsStringAsync());");
            L("            Assert.AreEqual(\"active\", await StatusAsync());");
            L("            using (var unknown = await anonymous.SendAsync(Signed(Message(\"gw-nobody\", \"active\"), secret)))");
            L("                Assert.AreEqual(HttpStatusCode.NotFound, unknown.StatusCode);");
            L("            using (var incomplete = await anonymous.SendAsync(Signed(Message(gateway, null), secret)))");
            L("                Assert.AreEqual(HttpStatusCode.BadRequest, incomplete.StatusCode);");
            L("        }");
            L("        finally");
            L("        {");
            L("            await RemoveAsync(tenant, plan);");
            L("        }");
            L("    }");
        }
        L("}");
        return o.ToString();
    }
}
