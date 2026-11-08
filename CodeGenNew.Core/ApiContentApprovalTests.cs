using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated ASP.NET tests of a content table that is also in <c>RowApprovals</c> (<c>&lt;Table&gt;ApprovalPublishApiTests.cs</c>): publishing asks for a second person, the row waits as pending and is
/// not public, the requester cannot approve their own request, another person's approval publishes it, a rejection or leaving the pending state ends the request, and a person who is not a row of the user table
/// is refused. The people and the row are put in the scratch database with the statements of <see cref="ApprovalTestSeed"/> and taken out again after each test. </summary>
public static class ApiContentApprovalTests
{
    private const string Sample = "zz_nobody";

    /// <summary> The tables to test and their seed, or none when the project has no such table or the seed cannot be made from the schema. </summary>
    public static IReadOnlyList<(ContentShape Content, ApprovalTestSeed Seed)> Tables(DatabaseModel database, ProjectSettings project)
    {
        var result = new List<(ContentShape, ApprovalTestSeed)>();
        if (!project.ApiTests || !project.ApiAuth || ContentPlan.ApprovalTables(project).Count == 0)
            return result;
        var plan = ApprovalPlan.Resolve(database, project);
        if (plan.Problems.Count > 0)
            return result;
        foreach (var table in plan.Tables.OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase))
        {
            if (TableShapes.For(table.Table, project).Content is not { NeedsApproval: true } content)
                continue;
            var first = plan with { Tables = [table, .. plan.Tables.Where(t => t != table)] };   // the seed puts its row in the first table
            if (ApprovalTestSeed.For(first, database, project, out _) is { } seed)
                result.Add((content, seed));
        }
        return result;
    }

    private static string Literal(string text) => "\"" + CSharpText.Escape(text) + "\"";

    public static string ClassText(ContentShape content, ApprovalTestSeed seed, ProjectSettings project)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string admin = project.AuthAdminRole, apis = project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis";
        string table = content.Table.TableName, route = ApiRequests.Route(content.Table), key = seed.RowKey.ToString();
        string status = JsonNames.Camel(content.Status.Name), slug = JsonNames.Camel(content.Slug.Name);
        string? publishedOn = content.PublishedOn is null ? null : JsonNames.Camel(content.PublishedOn.Name);
        bool tenants = TenantPlan.IsEnabled(project);
        bool publicRoutes = content.TenantColumn is null;   // the public routes of a tenant's content are read on the tenant's host name
        string ann = ApprovalTestSeed.Requester, bob = ApprovalTestSeed.Approver;

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L($"/// <summary> Publishing {table} asks for a second person's approval (the table is in ContentTables and RowApprovals). Each test puts its people and a draft row in the scratch database and removes them again. </summary>");
        L("[TestClass]");
        L($"public class {table}ApprovalPublishApiTests");
        L("{");
        L($"    private const string Route = \"{route}/{key}\";");
        L();
        L("    private static readonly string[] Setup =");
        L("    [");
        foreach (string sql in seed.Setup)
            L("        " + Literal(sql) + ",");
        L("    ];");
        L();
        L("    private static readonly string[] Cleanup =");
        L("    [");
        foreach (string sql in seed.Cleanup)
            L("        " + Literal(sql) + ",");
        L("    ];");
        L();
        L("    private static async Task RunAsync(string[] statements)");
        L("    {");
        L("        foreach (string sql in statements)");
        L($"            await {apis}.ApprovalTestSupport.RunAsync(ApiFixture.Services, sql);");
        L("    }");
        L();
        L("    private static async Task WithRowAsync(Func<Task> test)");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        if (tenants)
        {
            L("        if (ApiFixture.TenantId is null)");
            L("            Assert.Inconclusive(\"the database needs an active tenant\");");
        }
        L("        await RunAsync(Cleanup);");
        L("        await RunAsync(Setup);");
        L("        try");
        L("        {");
        L("            await test();");
        L("        }");
        L("        finally");
        L("        {");
        L("            await RunAsync(Cleanup);");
        L("        }");
        L("    }");
        L();
        L(tenants
            ? $"    private static HttpClient Person(string name) => ApiFixture.ClientInTenant(ApiFixture.TenantId!.Value, \"{admin}\", name);"
            : $"    private static HttpClient Person(string name) => ApiFixture.ClientInRole(\"{admin}\", name);");
        L();
        L("    private static async Task<JsonObject> StateAsync(HttpClient client)");
        L("    {");
        L("        using var response = await client.GetAsync(Route);");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("    }");
        L();
        L("    private static async Task<string> ProblemTypeAsync(HttpResponseMessage response) =>");
        L("        (await response.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>();");
        if (publicRoutes)
        {
            L();
            L("    private static async Task<HttpStatusCode> PublicAsync(string address)");
            L("    {");
            L("        using var reader = ApiFixture.Anonymous();");
            L($"        using var response = await reader.GetAsync($\"{route}/public/{{address}}\");");
            L("        return response.StatusCode;");
            L("    }");
        }
        L();
        L("    [TestMethod]");
        L("    public async Task Publishing_waits_for_a_second_person_who_publishes_the_row()");
        L("    {");
        L("        await WithRowAsync(async () =>");
        L("        {");
        L($"            using var ann = Person(\"{ann}\");");
        L($"            using var bob = Person(\"{bob}\");");
        L("            var draft = await StateAsync(ann);");
        L($"            Assert.AreEqual(\"{ContentWords.Draft}\", draft[\"{status}\"]!.GetValue<string>());");
        if (publicRoutes)
            L($"            string address = draft[\"{slug}\"]!.GetValue<string>();");
        L();
        L("            using var asked = await ann.PostAsync(Route + \"/publish\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, asked.StatusCode, await asked.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(\"{ContentWords.Pending}\", (await asked.Content.ReadFromJsonAsync<JsonObject>())![\"{status}\"]!.GetValue<string>(), \"publishing asks, it does not publish\");");
        L($"            Assert.AreEqual(\"{ContentWords.Pending}\", (await StateAsync(ann))[\"{status}\"]!.GetValue<string>());");
        if (publishedOn is not null)
            L($"            Assert.IsNull((await StateAsync(ann))[\"{publishedOn}\"], \"nothing is published yet\");");
        if (publicRoutes)
            L("            Assert.AreEqual(HttpStatusCode.NotFound, await PublicAsync(address), \"a pending row is not public\");");
        L();
        L("            using var twice = await ann.PostAsync(Route + \"/publish\", null);");
        L("            Assert.AreEqual(HttpStatusCode.Conflict, twice.StatusCode, \"a request is already waiting\");");
        L($"            Assert.AreEqual(\"{ApprovalWriter.WrongStateType}\", await ProblemTypeAsync(twice));");
        L("            using var own = await ann.PostAsJsonAsync(Route + \"/approve\", new { note = \"mine\" });");
        L("            Assert.AreEqual(HttpStatusCode.Forbidden, own.StatusCode, \"the requester cannot approve their own request\");");
        L($"            Assert.AreEqual(\"{ApprovalWriter.FourEyesType}\", await ProblemTypeAsync(own));");
        L($"            Assert.AreEqual(\"{ContentWords.Pending}\", (await StateAsync(ann))[\"{status}\"]!.GetValue<string>());");
        L();
        L("            using var approved = await bob.PostAsJsonAsync(Route + \"/approve\", new { note = \"fine\" });");
        L("            Assert.AreEqual(HttpStatusCode.OK, approved.StatusCode, await approved.Content.ReadAsStringAsync());");
        L("            var published = await StateAsync(ann);");
        L($"            Assert.AreEqual(\"{ContentWords.Published}\", published[\"{status}\"]!.GetValue<string>(), \"the approval publishes the row\");");
        if (publishedOn is not null)
            L($"            Assert.IsNotNull(published[\"{publishedOn}\"], \"and stamps the time\");");
        if (publicRoutes)
            L("            Assert.AreEqual(HttpStatusCode.OK, await PublicAsync(address), \"a published row is public\");");
        L();
        L("            using var again = await ann.PostAsync(Route + \"/publish\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, again.StatusCode, \"publishing a published row changes nothing\");");
        L($"            Assert.AreEqual(\"{ContentWords.Published}\", (await StateAsync(ann))[\"{status}\"]!.GetValue<string>());");
        L("            using var unpublished = await ann.PostAsync(Route + \"/unpublish\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, unpublished.StatusCode, await unpublished.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(\"{ContentWords.Draft}\", (await StateAsync(ann))[\"{status}\"]!.GetValue<string>(), \"unpublishing needs no approval\");");
        L("        });");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_rejection_or_leaving_the_pending_state_ends_the_request()");
        L("    {");
        L("        await WithRowAsync(async () =>");
        L("        {");
        L($"            using var ann = Person(\"{ann}\");");
        L($"            using var bob = Person(\"{bob}\");");
        L("            (await ann.PostAsync(Route + \"/publish\", null)).Dispose();");
        L("            using var rejected = await bob.PostAsJsonAsync(Route + \"/reject\", new { note = \"not yet\" });");
        L("            Assert.AreEqual(HttpStatusCode.OK, rejected.StatusCode, await rejected.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(\"{ContentWords.Draft}\", (await StateAsync(ann))[\"{status}\"]!.GetValue<string>(), \"a rejected row is a draft again\");");
        L();
        L("            (await ann.PostAsync(Route + \"/publish\", null)).Dispose();");
        L("            using var archived = await ann.PostAsync(Route + \"/archive\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, archived.StatusCode, await archived.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(\"{ContentWords.Archived}\", (await StateAsync(ann))[\"{status}\"]!.GetValue<string>());");
        L("            using var late = await bob.PostAsJsonAsync(Route + \"/approve\", new { note = \"late\" });");
        L("            Assert.AreEqual(HttpStatusCode.Conflict, late.StatusCode, \"archiving withdrew the request, so there is nothing to approve\");");
        L();
        L("            using var asked = await ann.PostAsync(Route + \"/publish\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, asked.StatusCode, await asked.Content.ReadAsStringAsync());");
        L("            using var approved = await bob.PostAsJsonAsync(Route + \"/approve\", new { note = \"fine\" });");
        L("            Assert.AreEqual(HttpStatusCode.OK, approved.StatusCode, await approved.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(\"{ContentWords.Published}\", (await StateAsync(ann))[\"{status}\"]!.GetValue<string>());");
        L("        });");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_person_who_is_not_in_the_user_table_cannot_ask_to_publish()");
        L("    {");
        L("        await WithRowAsync(async () =>");
        L("        {");
        L($"            using var nobody = Person(\"{Sample}\");");
        L("            using var refused = await nobody.PostAsync(Route + \"/publish\", null);");
        L("            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, await refused.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(\"{ApprovalWriter.RequesterUnknownType}\", await ProblemTypeAsync(refused));");
        L($"            Assert.AreEqual(\"{ContentWords.Draft}\", (await StateAsync(nobody))[\"{status}\"]!.GetValue<string>(), \"the row stays a draft\");");
        L($"            using var absent = await nobody.PostAsync($\"{route}/{{int.MaxValue}}/publish\", null);");
        L("            Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);");
        L("        });");
        L("    }");
        L("}");
        return o.ToString();
    }
}
