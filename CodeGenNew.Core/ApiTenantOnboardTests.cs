using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated ASP.NET tests of onboarding a tenant with its first administrator as a new person (<c>TenantOnboardApiTests.cs</c>): the body of <c>POST &lt;tenants&gt;/onboard</c> carries
/// <c>admin</c> (the user-table row) and <c>adminPassword</c>; the person is saved with the tenant, can sign in, and a short password, a taken user name or both ways of naming the person are a 400. </summary>
public static class ApiTenantOnboardTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.ApiTests && project.ApiAuth && TenantPlan.IsEnabled(project) && TenantPlan.TenantTableOf(database, project) is not null && TenantPlan.OnboardsNewAdmin(database, project);

    public static string ClassText(DatabaseModel database, ProjectSettings project)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var (userPlan, _) = UserTable.Resolve(database, project);
        var membership = TenantPlan.MembershipOf(database, project);
        string route = ApiRequests.Route(tenantTable), apis = project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis";
        string tenantKey = JsonNames.Camel(tenantTable.PrimaryKeyColumns[0].Name), login = JsonNames.Camel(userPlan!.UserName.Name);
        string activeLine = tenantTable.IsActiveColumn is { } flag ? $"        body[\"{JsonNames.Camel(flag.Name)}\"] = true;\n" : "";
        bool roleKnown = membership?.Role is not null || userPlan.Admin is not null;

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L("/// <summary> Onboarding a tenant with its first administrator as a new person. Each test makes its tenant and person with a platform token and removes them again. </summary>");
        L("[TestClass]");
        L("public class TenantOnboardApiTests");
        L("{");
        L($"    private const string Route = \"{route}/onboard\";");
        L("    private const string Password = \"Onboard-1234-password\";");
        L();
        L("    private static JsonObject NewPerson()");
        L("    {");
        L("        return new JsonObject");
        L("        {");
        foreach (var c in ApiSampleValues.BodyColumns(userPlan.Table))
            L($"            [{ApiSampleValues.Json(JsonNames.Camel(c.Name))}] = {TenantTests.Value(userPlan.Table, c, project)},");
        L("        };");
        L("    }");
        L();
        L("    private static async Task<JsonObject> NewTenantAsync()");
        L("    {");
        L($"        var body = await {tenantTable.TableName}ApiTests.NewBodyAsync();");
        o.Append(activeLine);
        L("        return body;");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_new_first_administrator_needs_a_long_enough_password_and_a_user_name_no_one_has()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        using var weak = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = await NewTenantAsync(), [\"admin\"] = NewPerson(), [\"adminPassword\"] = \"short\" });");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, weak.StatusCode, await weak.Content.ReadAsStringAsync());");
        L("        var person = NewPerson();");
        L("        using var first = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = await NewTenantAsync(), [\"admin\"] = person.DeepClone(), [\"adminPassword\"] = Password });");
        L("        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());");
        L($"        int key = (await first.Content.ReadFromJsonAsync<JsonObject>())![\"{tenantKey}\"]!.GetValue<int>();");
        L($"        string login = person[\"{login}\"]!.GetValue<string>();");
        L("        try");
        L("        {");
        L("            using var taken = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = await NewTenantAsync(), [\"admin\"] = person.DeepClone(), [\"adminPassword\"] = Password });");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, taken.StatusCode, \"a user name that someone has is refused\");");
        if (membership is not null)
        {
            L("            using var both = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = await NewTenantAsync(), [\"admin\"] = NewPerson(), [\"adminPassword\"] = Password, [\"adminId\"] = 1 });");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, both.StatusCode, \"adminId and admin together are refused\");");
        }
        L("        }");
        L("        finally");
        L("        {");
        L("            await " + apis + ".TenantTestSupport.RemoveWithAdminAsync(ApiFixture.Services, key, login);");
        L("        }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_platform_administrator_onboards_a_tenant_with_a_new_first_administrator_who_signs_in()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        var person = NewPerson();");
        L("        using var onboarded = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = await NewTenantAsync(), [\"admin\"] = person.DeepClone(), [\"adminPassword\"] = Password });");
        L("        Assert.AreEqual(HttpStatusCode.Created, onboarded.StatusCode, await onboarded.Content.ReadAsStringAsync());");
        L($"        int key = (await onboarded.Content.ReadFromJsonAsync<JsonObject>())![\"{tenantKey}\"]!.GetValue<int>();");
        L($"        string login = person[\"{login}\"]!.GetValue<string>();");
        L("        try");
        L("        {");
        L("            using var anonymous = ApiFixture.Anonymous();");
        L("            using var wrong = await anonymous.PostAsJsonAsync(\"/api/auth/login\", new { userName = login, password = \"not-the-password\" });");
        L("            Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode);");
        L("            using var signedIn = await anonymous.PostAsJsonAsync(\"/api/auth/login\", new { userName = login, password = Password });");
        L("            Assert.AreEqual(HttpStatusCode.OK, signedIn.StatusCode, await signedIn.Content.ReadAsStringAsync());");
        if (roleKnown)
        {
            L("            var reply = (await signedIn.Content.ReadFromJsonAsync<JsonObject>())!;");
            L($"            Assert.AreEqual(\"{project.AuthAdminRole}\", reply[\"user\"]![\"role\"]!.GetValue<string>(), \"the first administrator is an administrator of the new tenant\");");
        }
        L("        }");
        L("        finally");
        L("        {");
        L("            await " + apis + ".TenantTestSupport.RemoveWithAdminAsync(ApiFixture.Services, key, login);");
        L("        }");
        L("    }");
        if (TenantPlan.PeopleOf(database, project) is { } people && people.User.Table.HasCrudApiOfAnyKey(project))
        {
            string userRoute = ApiRequests.Route(people.User.Table), userKey = JsonNames.Camel(people.Key.Name);
            L();
            L("    [TestMethod]");
            L("    public async Task A_person_added_inside_a_tenant_belongs_to_it_and_another_tenant_does_not_see_them()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        using var platform = ApiFixture.PlatformClient();");
            L("        using var madeA = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = await NewTenantAsync(), [\"admin\"] = NewPerson(), [\"adminPassword\"] = Password });");
            L("        Assert.AreEqual(HttpStatusCode.Created, madeA.StatusCode, await madeA.Content.ReadAsStringAsync());");
            L($"        int tenantA = (await madeA.Content.ReadFromJsonAsync<JsonObject>())![\"{tenantKey}\"]!.GetValue<int>();");
            L("        using var madeB = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = await NewTenantAsync() });");
            L("        Assert.AreEqual(HttpStatusCode.Created, madeB.StatusCode, await madeB.Content.ReadAsStringAsync());");
            L($"        int tenantB = (await madeB.Content.ReadFromJsonAsync<JsonObject>())![\"{tenantKey}\"]!.GetValue<int>();");
            L("        var person = NewPerson();");
            L($"        person.Remove(\"{userKey}\");");
            L($"        string login = person[\"{login}\"]!.GetValue<string>();");
            L("        try");
            L("        {");
            L($"            using var inA = ApiFixture.ClientInTenant(tenantA, \"{project.AuthAdminRole}\");");
            L($"            using var inB = ApiFixture.ClientInTenant(tenantB, \"{project.AuthAdminRole}\");");
            L($"            using var created = await inA.PostAsJsonAsync(\"{userRoute}\", person);");
            L("            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());");
            L($"            int id = (await created.Content.ReadFromJsonAsync<JsonObject>())![\"{userKey}\"]!.GetValue<int>();");
            L($"            using var seenByA = await inA.GetAsync($\"{userRoute}/{{id}}\");");
            L("            Assert.AreEqual(HttpStatusCode.OK, seenByA.StatusCode, \"the person belongs to the tenant they were added in\");");
            L($"            using var seenByB = await inB.GetAsync($\"{userRoute}/{{id}}\");");
            L("            Assert.AreEqual(HttpStatusCode.NotFound, seenByB.StatusCode, \"another tenant does not see the person\");");
            L($"            using var seenByPlatform = await platform.GetAsync($\"{userRoute}/{{id}}\");");
            L("            Assert.AreEqual(HttpStatusCode.OK, seenByPlatform.StatusCode, \"a platform administrator sees every person\");");
            L("        }");
            L("        finally");
            L("        {");
            L("            await " + apis + ".TenantTestSupport.RemoveWithAdminAsync(ApiFixture.Services, tenantA, login);");
            L("            await " + apis + ".TenantTestSupport.RemoveAsync(ApiFixture.Services, tenantB);");
            L("        }");
            L("    }");
        }
        if (TenantBypassPlan.HostColumn(database, project) is { } host)
        {
            string hostKey = JsonNames.Camel(host.Name);
            L();
            L("    private static async Task<HttpResponseMessage> LoginOnAsync(string host, string login, int? tenant = null)");
            L("    {");
            L("        using var anonymous = ApiFixture.Anonymous();");
            L("        var request = new HttpRequestMessage(HttpMethod.Post, \"/api/auth/login\") { Content = JsonContent.Create(tenant is null ? (object)new { userName = login, password = Password } : (object)new { userName = login, password = Password, tenantId = tenant }) };");
            L("        request.Headers.Host = host;");
            L("        return await anonymous.SendAsync(request);");
            L("    }");
            L();
            L("    [TestMethod]");
            L("    public async Task A_sign_in_on_the_host_of_a_tenant_works_in_that_tenant_and_not_in_another()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        using var platform = ApiFixture.PlatformClient();");
            L("        string own = \"o\" + Guid.NewGuid().ToString(\"N\")[..10], other = \"p\" + Guid.NewGuid().ToString(\"N\")[..10];");
            L("        var person = NewPerson();");
            L("        var ownBody = await NewTenantAsync();");
            L($"        ownBody[\"{hostKey}\"] = own;");
            L("        using var first = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = ownBody, [\"admin\"] = person.DeepClone(), [\"adminPassword\"] = Password });");
            L("        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());");
            L($"        int ownKey = (await first.Content.ReadFromJsonAsync<JsonObject>())![\"{tenantKey}\"]!.GetValue<int>();");
            L("        var otherBody = await NewTenantAsync();");
            L($"        otherBody[\"{hostKey}\"] = other;");
            L("        using var second = await platform.PostAsJsonAsync(Route, new JsonObject { [\"tenant\"] = otherBody });");
            L("        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode, await second.Content.ReadAsStringAsync());");
            L($"        int otherKey = (await second.Content.ReadFromJsonAsync<JsonObject>())![\"{tenantKey}\"]!.GetValue<int>();");
            L($"        string login = person[\"{login}\"]!.GetValue<string>();");
            L("        try");
            L("        {");
            L("            using var onOwn = await LoginOnAsync($\"{own}.example.test\", login);");
            L("            Assert.AreEqual(HttpStatusCode.OK, onOwn.StatusCode, await onOwn.Content.ReadAsStringAsync());");
            L("            using var onOther = await LoginOnAsync($\"{other}.example.test\", login);");
            L("            Assert.AreEqual(HttpStatusCode.Forbidden, onOther.StatusCode, \"a person who is not a member is refused on another tenant's host\");");
            L("            using var namingOther = await LoginOnAsync($\"{own}.example.test\", login, otherKey);");
            L("            Assert.AreEqual(HttpStatusCode.Forbidden, namingOther.StatusCode, \"a sign-in naming another tenant than the host is refused\");");
            L("            using var plain = await LoginOnAsync(\"localhost\", login);");
            L("            Assert.AreEqual(HttpStatusCode.OK, plain.StatusCode, \"localhost names no tenant\");");
            L("        }");
            L("        finally");
            L("        {");
            L("            await " + apis + ".TenantTestSupport.RemoveWithAdminAsync(ApiFixture.Services, ownKey, login);");
            L("            await " + apis + ".TenantTestSupport.RemoveAsync(ApiFixture.Services, otherKey);");
            L("        }");
            L("    }");
        }
        L("}");
        return o.ToString();
    }
}
