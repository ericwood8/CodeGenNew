using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated API tests of a multi-tenant API (<c>TenantTable</c>): for a tenant-owned table that a tenant cannot read, change, delete or list another's rows and that the tenant column is the caller's whatever the body says;
/// for the tenant table itself that only the caller's tenant is visible, that it is not created or deleted by the ordinary routes, and that only a platform administrator onboards one. </summary>
public static class TenantTests
{
    public static bool HasMethods(TableModel table, ProjectSettings project) => TenantPlan.OwnedBy(table, project) is not null;

    /// <summary> The isolation tests, appended to the table's own test class. </summary>
    public static string Methods(string keyJson, TableModel table, ProjectSettings project)
    {
        var owned = TenantPlan.OwnedBy(table, project);
        if (owned is null)
            return "";
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string id = keyJson, tenant = JsonNames.Camel(owned.Column.Name), admin = project.AuthAdminRole;

        L();
        L("    private static async Task<JsonObject> MakeInTenantAsync(Action<JsonObject>? change = null)");
        L("    {");
        L("        var body = await NewBodyAsync();");
        L("        change?.Invoke(body);");
        L("        using var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task Another_tenant_cannot_read_change_delete_or_find_a_row()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        if (ApiFixture.TenantId is null || ApiFixture.OtherTenantId is not int other)");
        L("        {");
        L("            Assert.Inconclusive(\"the database needs two active tenants\");");
        L("            return;");
        L("        }");
        L("        var row = await MakeInTenantAsync();");
        L($"        long key = row[\"{id}\"]!.GetValue<long>();");
        L("        using var rival = ApiFixture.ClientInTenant(other, \"" + admin + "\");");
        L("        using var read = await rival.GetAsync($\"{Route}/{key}\");");
        L("        Assert.AreEqual(HttpStatusCode.NotFound, read.StatusCode, \"another tenant does not read the row\");");
        L("        var body = await NewBodyAsync();");
        L($"        body[\"{id}\"] = key;");
        L("        using var changed = await rival.PutAsJsonAsync($\"{Route}/{key}\", body);");
        L("        Assert.AreEqual(HttpStatusCode.NotFound, changed.StatusCode, \"another tenant does not change the row\");");
        L("        using var removed = await rival.DeleteAsync($\"{Route}/{key}\");");
        L("        Assert.AreNotEqual(HttpStatusCode.OK, removed.StatusCode, \"another tenant does not delete the row\");");
        L("        using var still = await ApiFixture.Client.GetAsync($\"{Route}/{key}\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, still.StatusCode, \"the row is still its own tenant's\");");
        L("        using var listed = await rival.GetAsync(Route);");
        L("        string listedText = await listed.Content.ReadAsStringAsync();");
        L($"        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(listedText, \"\\\"{id}\\\":\" + key + @\"(?!\\d)\"), \"another tenant's list does not hold the row\");");
        L("        using (await ApiFixture.Client.DeleteAsync($\"{Route}/{key}\")) { }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_new_row_belongs_to_the_callers_tenant_whatever_the_body_says()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        if (ApiFixture.TenantId is not int mine)");
        L("        {");
        L("            Assert.Inconclusive(\"the database needs an active tenant\");");
        L("            return;");
        L("        }");
        L($"        var row = await MakeInTenantAsync(b => b[\"{tenant}\"] = (ApiFixture.OtherTenantId ?? 2147483000));");
        L($"        Assert.AreEqual(mine, row[\"{tenant}\"]!.GetValue<int>());");
        L($"        long key = row[\"{id}\"]!.GetValue<long>();");
        L("        var body = await NewBodyAsync();");
        L($"        body[\"{id}\"] = key;");
        L($"        body[\"{tenant}\"] = (ApiFixture.OtherTenantId ?? 2147483000);");
        L("        using var changed = await ApiFixture.Client.PutAsJsonAsync($\"{Route}/{key}\", body);");
        L("        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, await changed.Content.ReadAsStringAsync());");
        L($"        Assert.AreEqual(mine, (await ApiFixture.Client.GetFromJsonAsync<JsonObject>($\"{{Route}}/{{key}}\"))![\"{tenant}\"]!.GetValue<int>(), \"an update keeps the tenant\");");
        L("        using (await ApiFixture.Client.DeleteAsync($\"{Route}/{key}\")) { }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_caller_in_no_tenant_sees_no_row_and_cannot_create_one()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        using var listed = await platform.GetAsync(Route);");
        L("        Assert.AreEqual(HttpStatusCode.OK, listed.StatusCode, await listed.Content.ReadAsStringAsync());");
        L($"        Assert.DoesNotContain(\"\\\"{tenant}\\\"\", await listed.Content.ReadAsStringAsync(), \"a platform caller is shown no tenant-owned row\");");
        L("        using var created = await platform.PostAsJsonAsync(Route, await NewBodyAsync());");
        L("        Assert.AreEqual(HttpStatusCode.Forbidden, created.StatusCode, await created.Content.ReadAsStringAsync());");
        L($"        Assert.AreEqual(\"{TenantWords.NoTenantType}\", (await created.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
        L("    }");
        if (project.ApiCsv)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task A_caller_in_no_tenant_exports_no_row()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        using var platform = ApiFixture.PlatformClient();");
            L("        using var export = await platform.GetAsync($\"{Route}/export.csv\");");
            L("        Assert.AreEqual(HttpStatusCode.OK, export.StatusCode);");
            L("        var lines = (await export.Content.ReadAsStringAsync()).Split('\\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);");
            L("        Assert.HasCount(1, lines, \"only the header line\");");
            L("    }");
        }
        if (ODataPlan.Serves(table, project))
        {
            L();
            L("    [TestMethod]");
            L("    public async Task A_caller_in_no_tenant_counts_no_row_in_the_OData_set()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        using var platform = ApiFixture.PlatformClient();");
            L($"        using var response = await platform.GetAsync(\"/odata/{ODataNames.SetName(table.TableName)}?$count=true&$top=1\");");
            L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
            L("        Assert.AreEqual(0, (await response.Content.ReadFromJsonAsync<JsonObject>())![\"@odata.count\"]!.GetValue<int>());");
            L("    }");
        }
        return o.ToString();
    }

    /// <summary> Whether the table gets a test class of its own here: the tenant table, and a tenant-owned Name and IsActive table (API_Test writes no tests for a name/active table). </summary>
    public static bool HasClass(TableModel table, ProjectSettings project) =>
        project.ApiAuth && TenantPlan.IsEnabled(project) && (TenantPlan.IsTenantTable(table, project) || (table.IsNameActiveTable && TenantPlan.OwnedBy(table, project) is not null));

    /// <summary> The expression of the value a new row's body holds for a column. </summary>
    private static string Value(TableModel table, ColumnModel c, ProjectSettings project) =>
        c.IsStringColumn && !c.IsPrimaryKey ? $"ApiFixture.Unique({ApiSampleValues.Json(ApiSampleValues.Text(c, project, table.TableName))}, {c.CharacterLength})" : ApiSampleValues.Value(c, project, table.TableName);

    /// <summary> The whole test class of the tenant table (it takes no create and no delete, shows only the caller's tenant and is onboarded by a platform administrator) or of a tenant-owned Name and IsActive table. </summary>
    public static string ClassText(TableModel table, ProjectSettings project)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string route = ApiRequests.Route(table), id = JsonNames.Camel(table.PrimaryKeyColumns[0].Name);
        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L("[TestClass]");
        L($"public class {table.TableName}ApiTests");
        L("{");
        L($"    private const string Route = \"{route}\";");
        L();
        L("    internal static async Task<JsonObject> NewBodyAsync() => new()");
        L("    {");
        foreach (var c in ApiSampleValues.BodyColumns(table))
            L($"        [{ApiSampleValues.Json(JsonNames.Camel(c.Name))}] = {Value(table, c, project)},");
        L("    };");
        if (!TenantPlan.IsTenantTable(table, project))
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
            o.Append(Methods(id, table, project));
            L("}");
            return o.ToString();
        }
        L();
        L("    [TestMethod]");
        L("    public async Task A_caller_reads_their_own_tenant_and_no_other()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        if (ApiFixture.TenantId is not int mine)");
        L("        {");
        L("            Assert.Inconclusive(\"the database needs an active tenant\");");
        L("            return;");
        L("        }");
        L("        using var own = await ApiFixture.Client.GetAsync($\"{Route}/{mine}\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, own.StatusCode, await own.Content.ReadAsStringAsync());");
        L("        if (ApiFixture.OtherTenantId is int other)");
        L("        {");
        L("            using var rival = await ApiFixture.Client.GetAsync($\"{Route}/{other}\");");
        L("            Assert.AreEqual(HttpStatusCode.NotFound, rival.StatusCode, \"another tenant is not shown\");");
        L("        }");
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        using var none = await platform.GetAsync($\"{Route}/{mine}\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, none.StatusCode, \"a platform administrator is shown every tenant\");");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_tenant_is_created_by_a_platform_administrator_only_and_never_deleted()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var created = await ApiFixture.Client.PostAsJsonAsync(Route, await NewBodyAsync());");
        L("        Assert.AreEqual(HttpStatusCode.Forbidden, created.StatusCode, await created.Content.ReadAsStringAsync());");
        L($"        Assert.AreEqual(\"{TenantWords.OnboardingType}\", (await created.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
        L("        if (ApiFixture.TenantId is int mine)");
        L("        {");
        L("            using var removed = await ApiFixture.Client.DeleteAsync($\"{Route}/{mine}\");");
        L("            Assert.AreEqual(HttpStatusCode.Forbidden, removed.StatusCode);");
        L("        }");
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        using var made = await platform.PostAsJsonAsync(Route, await NewBodyAsync());");
        L("        Assert.AreEqual(HttpStatusCode.Created, made.StatusCode, \"a platform administrator creates a tenant with the ordinary Add\" + await made.Content.ReadAsStringAsync());");
        L($"        int key = (await made.Content.ReadFromJsonAsync<JsonObject>())![\"{id}\"]!.GetValue<int>();");
        L("        using var seen = await platform.GetAsync($\"{Route}/{key}\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, seen.StatusCode);");
        L("        using var cannotDelete = await platform.DeleteAsync($\"{Route}/{key}\");");
        L("        Assert.AreEqual(HttpStatusCode.Forbidden, cannotDelete.StatusCode);");
        L("        await " + (project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis") + ".TenantTestSupport.RemoveAsync(ApiFixture.Services, key);");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task Only_a_platform_administrator_onboards_a_tenant()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        if (ApiFixture.TenantId is null)");
        L("        {");
        L("            Assert.Inconclusive(\"the database needs an active tenant\");");
        L("            return;");
        L("        }");
        L("        using var inTenant = await ApiFixture.Client.PostAsJsonAsync($\"{Route}/onboard\", new JsonObject { [\"tenant\"] = await NewBodyAsync() });");
        L("        Assert.AreEqual(HttpStatusCode.Forbidden, inTenant.StatusCode, \"an administrator inside a tenant is not a platform administrator\");");
        L($"        Assert.AreEqual(\"{TenantWords.OnboardingType}\", (await inTenant.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
        L("        using var platform = ApiFixture.PlatformClient();");
        L("        using var onboarded = await platform.PostAsJsonAsync($\"{Route}/onboard\", new JsonObject { [\"tenant\"] = await NewBodyAsync() });");
        L("        Assert.AreEqual(HttpStatusCode.Created, onboarded.StatusCode, await onboarded.Content.ReadAsStringAsync());");
        L($"        int key = (await onboarded.Content.ReadFromJsonAsync<JsonObject>())![\"{id}\"]!.GetValue<int>();");
        L("        await " + (project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis") + ".TenantTestSupport.RemoveAsync(ApiFixture.Services, key);");
        L("    }");
        L();
        L("    [TestMethod]");
        if (table.IsActiveColumn is not null)
        {
            L("    public async Task A_token_of_a_tenant_that_was_switched_off_is_refused()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        using var platform = ApiFixture.PlatformClient();");
            L("        var body = await NewBodyAsync();");
            L($"        body[\"{JsonNames.Camel(table.IsActiveColumn.Name)}\"] = true;");
            L("        using var made = await platform.PostAsJsonAsync(Route, body);");
            L("        Assert.AreEqual(HttpStatusCode.Created, made.StatusCode, await made.Content.ReadAsStringAsync());");
            L($"        int key = (await made.Content.ReadFromJsonAsync<JsonObject>())![\"{id}\"]!.GetValue<int>();");
            string apis = project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis";
            L("        try");
            L("        {");
            L("            using var worker = ApiFixture.ClientInTenant(key, \"Administrator\");");
            L("            using var before = await worker.GetAsync($\"{Route}/{key}\");");
            L("            Assert.AreEqual(HttpStatusCode.OK, before.StatusCode, await before.Content.ReadAsStringAsync());");
            L($"            await {apis}.TenantTestSupport.DeactivateAsync(ApiFixture.Services, key);");
            L("            using var after = await worker.GetAsync($\"{Route}/{key}\");");
            L("            Assert.AreEqual(HttpStatusCode.Unauthorized, after.StatusCode, \"a token keeps no tenant that was switched off\");");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {apis}.TenantTestSupport.RemoveAsync(ApiFixture.Services, key);");
            L("        }");
            L("    }");
            L();
            L("    [TestMethod]");
        }
        L("    public async Task Without_a_token_the_tenant_is_refused()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var anonymous = ApiFixture.Anonymous();");
        L("        using var response = await anonymous.GetAsync(Route);");
        L("        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);");
        L("    }");
        L("}");
        return o.ToString();
    }
}
