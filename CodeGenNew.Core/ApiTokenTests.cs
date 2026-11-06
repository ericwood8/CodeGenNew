using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated ASP.NET tests of the token routes (<c>&lt;Table&gt;TokenApiTests.cs</c>): a call without a token is a 401, a role that is not the administrator's is a 403, a token is spent once, an expired or
/// revoked token is not spent, the list never shows the hash, and revoking a person ends their tokens. Each test adds a row of its own through the test support and removes it again. A table whose other columns need
/// more than a value from the generator (<see cref="TokenPlan.RequiredFill"/>) gets no tests. </summary>
public static class ApiTokenTests
{
    public static IReadOnlyList<TokenShape> Tables(DatabaseModel database, ProjectSettings project) =>
        !project.ApiTests || !project.ApiAuth ? [] :
        database.Tables.Select(t => TableShapes.For(t, project).Token).OfType<TokenShape>().OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    public static string ClassText(TokenShape t, ProjectSettings project)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string apis = project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis", admin = project.AuthAdminRole;
        string table = t.Table.TableName, support = $"{apis}.{table}TokenTestSupport", route = ApiRequests.Route(t.Table);
        string key = JsonNames.Camel(t.Key.Name), hash = JsonNames.Camel(t.Hash.Name), person = t.Person is null ? "" : JsonNames.Camel(t.Person.Name);
        string personType = t.Person?.CSharpBase() ?? "int";
        string add = t.Person is null ? "" : ", person";

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L($"/// <summary> The token routes of {table}. Each test adds a row of its own and removes it again. </summary>");
        L("[TestClass]");
        L($"public class {table}TokenApiTests");
        L("{");
        L($"    private const string Route = \"{route}\";");
        L();
        L("    private static string NewToken() => \"test-\" + Guid.NewGuid().ToString(\"N\");");
        L();
        L($"    private static HttpClient Admin() => ApiFixture.ClientInRole(\"{admin}\");");
        L();
        L("    private static Task<HttpResponseMessage> SpendAsync(HttpClient client, string token) => client.PostAsJsonAsync($\"{Route}/spend\", new { token });");
        L();
        if (t.Person is not null)
        {
            L($"    private static async Task<{personType}?> PersonAsync()");
            L("    {");
            L($"        var person = await {support}.FirstPersonAsync(ApiFixture.Services);");
            if (!t.Person.IsNullable)
                L("        if (person is null)\n            Assert.Inconclusive(\"The user table has no row to hold a token for.\");");
            L("        return person;");
            L("    }");
            L();
        }
        L("    [TestMethod]");
        L("    public async Task Without_a_token_the_routes_answer_401()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var nobody = ApiFixture.Anonymous();");
        L("        using var list = await nobody.GetAsync(Route);");
        L("        Assert.AreEqual(HttpStatusCode.Unauthorized, list.StatusCode);");
        L("        using var revoke = await nobody.PostAsync($\"{Route}/1/revoke\", null);");
        L("        Assert.AreEqual(HttpStatusCode.Unauthorized, revoke.StatusCode);");
        if (!t.Anonymous)
        {
            L("        using var spend = await SpendAsync(nobody, NewToken());");
            L("        Assert.AreEqual(HttpStatusCode.Unauthorized, spend.StatusCode);");
        }
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_role_that_is_not_the_administrators_can_neither_list_nor_revoke()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var other = ApiFixture.ClientInRole(\"NoSuchRole\");");
        L("        using var list = await other.GetAsync(Route);");
        L("        Assert.AreEqual(HttpStatusCode.Forbidden, list.StatusCode);");
        L("        using var revoke = await other.PostAsync($\"{Route}/1/revoke\", null);");
        L("        Assert.AreEqual(HttpStatusCode.Forbidden, revoke.StatusCode);");
        L($"        Assert.AreEqual(\"{ProblemTypes.RoleNotAllowed}\", (await revoke.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_token_is_spent_once()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        if (t.Person is not null)
            L("        var person = await PersonAsync();");
        L("        string token = NewToken();");
        L($"        var id = await {support}.AddAsync(ApiFixture.Services, token, DateTime.Now.AddHours(1){add});");
        L("        try");
        L("        {");
        L("            using var client = Admin();");
        L("            using var first = await SpendAsync(client, token);");
        L("            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(id.ToString(), (await first.Content.ReadFromJsonAsync<JsonObject>())![\"id\"]!.ToString());");
        L("            using var second = await SpendAsync(client, token);");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, second.StatusCode);");
        L($"            Assert.AreEqual(\"{TokenWords.SpendType}\", (await second.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>());");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, id);");
        L("        }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task An_expired_token_and_one_that_is_not_known_cannot_be_spent()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        if (t.Person is not null)
            L("        var person = await PersonAsync();");
        L("        string token = NewToken();");
        L($"        var id = await {support}.AddAsync(ApiFixture.Services, token, DateTime.Now.AddHours(-1){add});");
        L("        try");
        L("        {");
        L("            using var client = Admin();");
        L("            using var expired = await SpendAsync(client, token);");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, expired.StatusCode);");
        L("            using var unknown = await SpendAsync(client, NewToken());");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);");
        L("            using var empty = await SpendAsync(client, \"\");");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, id);");
        L("        }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_revoked_token_cannot_be_spent_and_the_list_never_shows_the_hash()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        if (t.Person is not null)
            L("        var person = await PersonAsync();");
        L("        string token = NewToken();");
        L($"        var id = await {support}.AddAsync(ApiFixture.Services, token, DateTime.Now.AddHours(1){add});");
        L("        try");
        L("        {");
        L("            using var client = Admin();");
        L("            using var revoked = await client.PostAsync($\"{Route}/{id}/revoke\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode, await revoked.Content.ReadAsStringAsync());");
        L("            Assert.IsFalse((await revoked.Content.ReadFromJsonAsync<JsonObject>())![\"active\"]!.GetValue<bool>(), \"the revoked row is no longer active\");");
        L("            using var again = await client.PostAsync($\"{Route}/{id}/revoke\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, again.StatusCode, \"revoking twice changes nothing more\");");
        L("            using var spent = await SpendAsync(client, token);");
        L("            Assert.AreEqual(HttpStatusCode.BadRequest, spent.StatusCode);");
        L("            using var list = await client.GetAsync(Route);");
        L("            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, await list.Content.ReadAsStringAsync());");
        L("            var rows = (await list.Content.ReadFromJsonAsync<JsonArray>())!;");
        L($"            Assert.IsTrue(rows.Any(r => r![\"{key}\"]!.ToString() == id.ToString()), \"the row is listed\");");
        L($"            Assert.IsFalse(rows.Any(r => r!.AsObject().ContainsKey(\"{hash}\")), \"the hash is never answered\");");
        L("            using var missing = await client.PostAsync($\"{Route}/2000000000/revoke\", null);");
        L("            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, id);");
        L("        }");
        L("    }");
        if (t.Ends.Count >= 2)
        {
            string used = JsonNames.Camel(t.Spent!.Name), revokedName = JsonNames.Camel(t.Revoked!.Name);
            L();
            L("    private static bool IsSet(JsonNode? value) => value is not null && !(value is JsonValue flag && flag.TryGetValue<bool>(out var on) && !on);");
            L();
            L("    [TestMethod]");
            L("    public async Task A_revoke_sets_the_revoked_column_and_a_spend_the_used_one()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            if (t.Person is not null)
                L("        var person = await PersonAsync();");
            L("        string revokeToken = NewToken(), spendToken = NewToken();");
            L($"        var revokeId = await {support}.AddAsync(ApiFixture.Services, revokeToken, DateTime.Now.AddHours(1){add});");
            L($"        var spendId = await {support}.AddAsync(ApiFixture.Services, spendToken, DateTime.Now.AddHours(1){add});");
            L("        try");
            L("        {");
            L("            using var client = Admin();");
            L("            using var revoked = await client.PostAsync($\"{Route}/{revokeId}/revoke\", null);");
            L("            Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode, await revoked.Content.ReadAsStringAsync());");
            L("            using var spent = await SpendAsync(client, spendToken);");
            L("            Assert.AreEqual(HttpStatusCode.OK, spent.StatusCode, await spent.Content.ReadAsStringAsync());");
            L("            using var list = await client.GetAsync(Route);");
            L("            var rows = (await list.Content.ReadFromJsonAsync<JsonArray>())!;");
            L($"            var revokedRow = rows.First(r => r![\"{key}\"]!.ToString() == revokeId.ToString())!;");
            L($"            var spentRow = rows.First(r => r![\"{key}\"]!.ToString() == spendId.ToString())!;");
            L($"            Assert.IsTrue(IsSet(revokedRow[\"{revokedName}\"]) && !IsSet(revokedRow[\"{used}\"]), \"a revoke sets the revoked column only\");");
            L($"            Assert.IsTrue(IsSet(spentRow[\"{used}\"]) && !IsSet(spentRow[\"{revokedName}\"]), \"a spend sets the used column only\");");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, revokeId);");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, spendId);");
            L("        }");
            L("    }");
        }
        if (t.Anonymous)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task A_token_is_spent_without_signing_in_and_a_wrong_one_is_refused_the_same_way()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            if (t.Person is not null)
                L("        var person = await PersonAsync();");
            L("        string token = NewToken();");
            L($"        var id = await {support}.AddAsync(ApiFixture.Services, token, DateTime.Now.AddHours(1){add});");
            L("        try");
            L("        {");
            L("            using var nobody = ApiFixture.Anonymous();");
            L("            using var wrong = await SpendAsync(nobody, NewToken());");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, wrong.StatusCode);");
            L("            using var first = await SpendAsync(nobody, token);");
            L("            Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());");
            L("            using var second = await SpendAsync(nobody, token);");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, second.StatusCode);");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, id);");
            L("        }");
            L("    }");
        }
        if (t.Family is not null)
        {
            string familyType = t.Family.CSharpBase();
            string value = t.Family.IsStringColumn ? "Guid.NewGuid().ToString(\"N\")" : t.Family.IsGuidColumn ? "Guid.NewGuid()" : $"({familyType})Random.Shared.Next(1_000_000, 2_000_000_000)";
            L();
            L("    [TestMethod]");
            L("    public async Task Using_a_token_twice_ends_every_token_of_its_family()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            if (t.Person is not null)
                L("        var person = await PersonAsync();");
            L($"        var family = {value};");
            L("        string first = NewToken(), second = NewToken();");
            L($"        var one = await {support}.AddAsync(ApiFixture.Services, first, DateTime.Now.AddHours(1){add}, family: family);");
            L($"        var two = await {support}.AddAsync(ApiFixture.Services, second, DateTime.Now.AddHours(1){add}, family: family);");
            L("        try");
            L("        {");
            L("            using var client = Admin();");
            L("            using var spent = await SpendAsync(client, first);");
            L("            Assert.AreEqual(HttpStatusCode.OK, spent.StatusCode, await spent.Content.ReadAsStringAsync());");
            L("            Assert.AreEqual(family.ToString(), (await spent.Content.ReadFromJsonAsync<JsonObject>())![\"family\"]!.ToString());");
            L("            using var reused = await SpendAsync(client, first);");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, reused.StatusCode);");
            L("            using var sibling = await SpendAsync(client, second);");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, sibling.StatusCode, \"the reuse ended the family\");");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, one);");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, two);");
            L("        }");
            L("    }");
        }
        if (t.Person is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task Revoking_a_person_ends_every_active_token_of_that_person()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        var person = await PersonAsync();");
            L("        string first = NewToken(), second = NewToken();");
            L($"        var one = await {support}.AddAsync(ApiFixture.Services, first, DateTime.Now.AddHours(1), person);");
            L($"        var two = await {support}.AddAsync(ApiFixture.Services, second, DateTime.Now.AddHours(1), person);");
            L("        try");
            L("        {");
            L("            using var client = Admin();");
            L("            using var revoked = await client.PostAsync($\"{Route}/revoke-person/{person}\", null);");
            L("            Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode, await revoked.Content.ReadAsStringAsync());");
            L("            Assert.IsTrue((await revoked.Content.ReadFromJsonAsync<JsonObject>())![\"revoked\"]!.GetValue<int>() >= 2, \"both tokens ended\");");
            L("            using var spentFirst = await SpendAsync(client, first);");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, spentFirst.StatusCode);");
            L("            using var spentSecond = await SpendAsync(client, second);");
            L("            Assert.AreEqual(HttpStatusCode.BadRequest, spentSecond.StatusCode);");
            L($"            using var list = await client.GetAsync($\"{{Route}}?personId={{person}}\");");
            L("            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, await list.Content.ReadAsStringAsync());");
            L($"            var mine = (await list.Content.ReadFromJsonAsync<JsonArray>())!;");
            L($"            Assert.IsTrue(mine.All(r => r![\"{person}\"]!.ToString() == person.ToString()), \"the list holds the person's rows only\");");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, one);");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, two);");
            L("        }");
            L("    }");
        }
        L("}");
        return o.ToString();
    }
}
