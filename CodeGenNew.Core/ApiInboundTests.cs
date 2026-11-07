using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated ASP.NET tests of the inbound routes (<c>&lt;Table&gt;InboundApiTests.cs</c>): a message with no signature, a wrong one or an old one is refused and stores nothing, a signed message is kept and listed
/// as unprocessed, marking it processed takes it out of the list (and again is harmless), an event id is applied once, a body over the limit is refused, and the worker routes need a sign-in. The tests need the shared secret
/// (the environment variable <c>InboundSecret</c> names) and are inconclusive without it. Each test removes the events it wrote. </summary>
public static class ApiInboundTests
{
    public static IReadOnlyList<InboundShape> Tables(DatabaseModel database, ProjectSettings project) =>
        !project.ApiTests ? [] :
        database.Tables.Select(t => TableShapes.For(t, project).Inbound).OfType<InboundShape>().OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The largest body, in characters, the route keeps: the limit of the route or the length of the body column, whichever is smaller. </summary>
    public static int Limit(InboundShape t, ProjectSettings project) => t.BodyLength > 0 ? Math.Min(t.BodyLength, project.InboundMaxBytes) : project.InboundMaxBytes;

    /// <summary> The tests of the collect route: a message is kept with no signature and the answer holds nothing, an empty or large body is refused, and the caller over the limit gets 429 with Retry-After. </summary>
    private static void ClientTests(Action<string> L, InboundShape t, ProjectSettings project, string support, string anonymous)
    {
        int limit = Math.Min(t.BodyLength > 0 ? t.BodyLength : int.MaxValue, project.InboundClientMaxBytes);
        L("");
        L("    [TestMethod]");
        L("    public async Task A_client_message_is_kept_with_no_signature_and_the_answer_holds_nothing()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        string marker = \"inbound-\" + Guid.NewGuid().ToString(\"N\");");
        L("        try");
        L("        {");
        L($"            {support}.ResetClientLimit();");
        L($"            {anonymous};");
        L("            using (var sent = await client.PostAsync(Route + \"/collect\", new StringContent(Message(marker), System.Text.Encoding.UTF8, \"application/json\")))");
        L("            {");
        L("                Assert.AreEqual(HttpStatusCode.Accepted, sent.StatusCode, await sent.Content.ReadAsStringAsync());");
        L("                Assert.AreEqual(\"\", await sent.Content.ReadAsStringAsync(), \"a write-only route tells the browser nothing\");");
        L("            }");
        L("            var row = Find(await UnprocessedAsync(), marker);");
        L("            Assert.IsNotNull(row, \"the message is listed as unprocessed\");");
        if (t.Source is not null)
            L($"            Assert.AreEqual(\"{InboundWords.ClientSource}\", row![\"{JsonNames.Camel(t.Source.Name)}\"]!.GetValue<string>());");
        L("            using (var empty = await client.PostAsync(Route + \"/collect\", new StringContent(\"\", System.Text.Encoding.UTF8, \"application/json\")))");
        L("                Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);");
        L($"            using (var large = await client.PostAsync(Route + \"/collect\", new StringContent(new string('x', {limit + 1}), System.Text.Encoding.UTF8, \"application/json\")))");
        L("                Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, marker);");
        L($"            {support}.ResetClientLimit();");
        L("        }");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    public async Task A_caller_over_the_limit_of_requests_a_minute_is_refused_with_Retry_After()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        string marker = \"inbound-\" + Guid.NewGuid().ToString(\"N\");");
        L("        if (DateTimeOffset.UtcNow.Second > 52)");
        L("            await Task.Delay(8000);");
        L("        try");
        L("        {");
        L($"            {support}.ResetClientLimit();");
        L($"            {anonymous};");
        L($"            for (int i = 0; i < {project.InboundClientPerMinute}; i++)");
        L("            {");
        L("                using var allowed = await client.PostAsync(Route + \"/collect\", new StringContent(Message(marker), System.Text.Encoding.UTF8, \"application/json\"));");
        L("                Assert.AreEqual(HttpStatusCode.Accepted, allowed.StatusCode, $\"request {i + 1} is inside the limit\");");
        L("            }");
        L("            using var refused = await client.PostAsync(Route + \"/collect\", new StringContent(Message(marker), System.Text.Encoding.UTF8, \"application/json\"));");
        L("            Assert.AreEqual((HttpStatusCode)429, refused.StatusCode);");
        L("            Assert.IsTrue(refused.Headers.Contains(\"Retry-After\"), \"the answer says when to try again\");");
        L($"            StringAssert.Contains((await refused.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>(), \"{InboundWords.RateLimitedType.Split('/').Last()}\");");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, marker);");
        L($"            {support}.ResetClientLimit();");
        L("        }");
        L("    }");
    }

    /// <summary> The status route counts the events that wait: one more after a message, back after it is marked processed, and it says whether the collect route is on. </summary>
    private static void StatusTest(Action<string> L, InboundShape t, string support, string anonymous)
    {
        string key = JsonNames.Camel(t.Key.Name);
        L("");
        L("    [TestMethod]");
        L("    public async Task The_status_counts_the_events_that_wait()");
        L("    {");
        L("        string secret = Secret();");
        L("        string marker = \"inbound-\" + Guid.NewGuid().ToString(\"N\");");
        L("        async Task<JsonObject> StatusAsync()");
        L("        {");
        L("            using var response = await ApiFixture.Client.GetAsync(Route + \"/status\");");
        L("            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("            return (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("        }");
        L("        try");
        L("        {");
        L("            var before = await StatusAsync();");
        L($"            {anonymous};");
        L("            using (var received = await client.SendAsync(Signed(Route + \"/receive\", Message(marker), secret)))");
        L("                Assert.AreEqual(HttpStatusCode.OK, received.StatusCode);");
        L("            var after = await StatusAsync();");
        L("            Assert.AreEqual(before[\"unprocessed\"]!.GetValue<int>() + 1, after[\"unprocessed\"]!.GetValue<int>(), \"the new event waits\");");
        L("            Assert.AreEqual(before[\"total\"]!.GetValue<int>() + 1, after[\"total\"]!.GetValue<int>());");
        L("            Assert.IsNotNull(after[\"oldestUnprocessedId\"]);");
        L($"            StringAssert.StartsWith(after[\"summary\"]!.GetValue<string>(), \"{InboundScreens.UnprocessedLabel}\");");
        L($"            Assert.AreEqual({(t.Client ? "true" : "false")}, after[\"collect\"]![\"enabled\"]!.GetValue<bool>());");
        L($"            var id = Find(await UnprocessedAsync(), marker)![\"{key}\"]!.GetValue<{(t.Key.IsInt32Column ? "int" : t.Key.CSharpBase())}>();");
        L("            using (var done = await ApiFixture.Client.PostAsync($\"{Route}/{id}/processed\", null))");
        L("                Assert.AreEqual(HttpStatusCode.OK, done.StatusCode);");
        L("            Assert.AreEqual(before[\"unprocessed\"]!.GetValue<int>(), (await StatusAsync())[\"unprocessed\"]!.GetValue<int>(), \"a processed event no longer waits\");");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, marker);");
        L("        }");
        L("    }");
    }

    /// <summary> The test of a sender with a secret of its own: the shared secret is refused for it and its own is accepted, with the sender kept as it is configured. </summary>
    private static void OwnSecretTest(Action<string> L, InboundShape t, (string Source, string Secret) own, string support, string anonymous, ProjectSettings project)
    {
        L("");
        L("    [TestMethod]");
        L("    public async Task A_sender_with_its_own_secret_is_checked_against_that_secret_only()");
        L("    {");
        L("        string shared = Secret();");
        L($"        string own = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(ApiFixture.Services)[\"{own.Secret}\"] ?? \"\";");
        L("        if (string.IsNullOrEmpty(own))");
        L($"            Assert.Inconclusive(\"Set the environment variable {own.Secret} (the secret of the sender {own.Source}) to play it.\");");
        L("        if (own == shared)");
        L($"            Assert.Inconclusive(\"{own.Secret} and {project.InboundSecret} must hold different secrets to play it.\");");
        L("        string marker = \"inbound-\" + Guid.NewGuid().ToString(\"N\");");
        L("        try");
        L("        {");
        L($"            {anonymous};");
        L($"            string path = Route + \"/receive/{own.Source.ToUpperInvariant()}\";");
        L("            using (var wrong = await client.SendAsync(Signed(path, Message(marker), shared)))");
        L("                Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode, \"the shared secret does not work for this sender\");");
        L("            using (var sent = await client.SendAsync(Signed(path, Message(marker), own)))");
        L("                Assert.AreEqual(HttpStatusCode.OK, sent.StatusCode, await sent.Content.ReadAsStringAsync());");
        L("            var row = Find(await UnprocessedAsync(), marker);");
        L("            Assert.IsNotNull(row, \"the event is listed as unprocessed\");");
        if (t.Source is not null)
            L($"            Assert.AreEqual(\"{own.Source}\", row![\"{JsonNames.Camel(t.Source.Name)}\"]!.GetValue<string>(), \"the sender is kept as it is configured\");");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, marker);");
        L("        }");
        L("    }");
    }

    public static string ClassText(InboundShape t, ProjectSettings project)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string apis = project.ApiNamespace ?? (project.ProjectName ?? "MyApp") + ".Api.Apis";
        string table = t.Table.TableName, support = $"{apis}.{table}InboundTestSupport", route = ApiRequests.Route(t.Table);
        string body = JsonNames.Camel(t.Body.Name), key = JsonNames.Camel(t.Key.Name), processed = JsonNames.Camel(t.Processed.Name);
        int limit = Limit(t, project);
        bool auth = project.ApiAuth;
        string testSource = InboundPlan.TestSource(project);
        string anonymous = auth ? "using var client = ApiFixture.Anonymous()" : "var client = ApiFixture.Client";
        string badIdValue = t.Key.IsBigIntColumn ? "long.MaxValue" : t.Key.IsSmallIntColumn ? "short.MaxValue" : t.Key.IsTinyIntColumn ? "byte.MaxValue" : "int.MaxValue";
        string sampleKey = t.Key.IsInt32Column ? "int" : t.Key.CSharpBase();

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L($"/// <summary> The inbound event table {table}. Each test removes the events it wrote. </summary>");
        L("[TestClass]");
        L($"public class {table}InboundApiTests");
        L("{");
        L($"    private const string Route = \"{route}\";");
        L();
        L("    private static string Secret()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L($"        string? secret = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(ApiFixture.Services)[\"{project.InboundSecret}\"];");
        L("        if (string.IsNullOrEmpty(secret))");
        L($"            Assert.Inconclusive(\"Set the environment variable {project.InboundSecret} (the shared secret of the senders) to play it.\");");
        L("        return secret!;");
        L("    }");
        L();
        L("    private static HttpRequestMessage Signed(string path, string text, string secret, long? at = null, string? eventId = null)");
        L("    {");
        L("        long time = at ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();");
        L("        string hex = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes($\"{time}.{text}\"))).ToLowerInvariant();");
        L("        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(text, System.Text.Encoding.UTF8, \"application/json\") };");
        L($"        request.Headers.Add(\"{InboundWords.SignatureHeader}\", $\"t={{time}},v1={{hex}}\");");
        L("        if (eventId is not null)");
        L($"            request.Headers.Add(\"{InboundWords.EventHeader}\", eventId);");
        L("        return request;");
        L("    }");
        L();
        L("    private static string Message(string marker, string? id = null) => System.Text.Json.JsonSerializer.Serialize(new { id, marker });");
        L();
        L("    private static async Task<JsonArray> UnprocessedAsync()");
        L("    {");
        L("        using var response = await ApiFixture.Client.GetAsync(Route + \"/unprocessed\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        return (await response.Content.ReadFromJsonAsync<JsonArray>())!;");
        L("    }");
        L();
        L($"    private static JsonNode? Find(JsonArray rows, string marker) => rows.FirstOrDefault(r => (r![\"{body}\"]?.GetValue<string>() ?? \"\").Contains(marker));");
        L();
        L("    [TestMethod]");
        L("    public async Task A_message_with_no_signature_a_wrong_one_or_an_old_one_is_refused_and_stores_nothing()");
        L("    {");
        L("        string secret = Secret();");
        L("        string marker = \"inbound-\" + Guid.NewGuid().ToString(\"N\");");
        L("        try");
        L("        {");
        L($"            {anonymous};");
        L("            using (var unsigned = await client.PostAsync(Route + \"/receive\", new StringContent(Message(marker), System.Text.Encoding.UTF8, \"application/json\")))");
        L("                Assert.AreEqual(HttpStatusCode.Unauthorized, unsigned.StatusCode, \"a message with no signature is refused\");");
        L("            using (var wrong = await client.SendAsync(Signed(Route + \"/receive\", Message(marker), \"not-the-secret\")))");
        L("                Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode, \"a message signed with another secret is refused\");");
        L("            using (var old = await client.SendAsync(Signed(Route + \"/receive\", Message(marker), secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600)))");
        L("            {");
        L("                Assert.AreEqual(HttpStatusCode.BadRequest, old.StatusCode, \"a signed message an hour old is a replay\");");
        L($"                StringAssert.Contains((await old.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>(), \"{InboundWords.ExpiredType.Split('/').Last()}\");");
        L("            }");
        L("            Assert.IsNull(Find(await UnprocessedAsync(), marker), \"the refused messages stored nothing\");");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, marker);");
        L("        }");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_signed_message_is_kept_listed_as_unprocessed_and_marked_processed()");
        L("    {");
        L("        string secret = Secret();");
        L("        string marker = \"inbound-\" + Guid.NewGuid().ToString(\"N\");");
        L("        try");
        L("        {");
        L($"            {anonymous};");
        L($"            string path = Route + \"/receive{(t.Source is null ? "" : "/" + testSource)}\";");
        L("            string text = Message(marker);");
        L("            using (var received = await client.SendAsync(Signed(path, text, secret)))");
        L("            {");
        L("                Assert.AreEqual(HttpStatusCode.OK, received.StatusCode, await received.Content.ReadAsStringAsync());");
        L("                Assert.IsTrue((await received.Content.ReadFromJsonAsync<JsonObject>())![\"received\"]!.GetValue<bool>());");
        L("            }");
        L("            var row = Find(await UnprocessedAsync(), marker);");
        L("            Assert.IsNotNull(row, \"the event is listed as unprocessed\");");
        L($"            Assert.AreEqual(text, row![\"{body}\"]!.GetValue<string>(), \"the raw body is kept as it came\");");
        if (t.Source is not null)
            L($"            Assert.AreEqual(\"{testSource}\", row[\"{JsonNames.Camel(t.Source.Name)}\"]!.GetValue<string>());");
        L($"            var id = row[\"{key}\"]!.GetValue<{sampleKey}>();");
        L("            using (var done = await ApiFixture.Client.PostAsync($\"{Route}/{id}/processed\", null))");
        L("            {");
        L("                Assert.AreEqual(HttpStatusCode.OK, done.StatusCode, await done.Content.ReadAsStringAsync());");
        L("                Assert.IsTrue((await done.Content.ReadFromJsonAsync<JsonObject>())![\"processed\"]!.GetValue<bool>());");
        L("            }");
        L("            Assert.IsNull(Find(await UnprocessedAsync(), marker), \"a processed event is no longer listed\");");
        L("            using (var again = await ApiFixture.Client.PostAsync($\"{Route}/{id}/processed\", null))");
        L("            {");
        L("                Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);");
        L("                Assert.IsTrue((await again.Content.ReadFromJsonAsync<JsonObject>())![\"alreadyProcessed\"]!.GetValue<bool>(), \"marking it again changes nothing\");");
        L("            }");
        L($"            using (var missing = await ApiFixture.Client.PostAsync($\"{{Route}}/{{{badIdValue}}}/processed\", null))");
        L("                Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);");
        L("        }");
        L("        finally");
        L("        {");
        L($"            await {support}.RemoveAsync(ApiFixture.Services, marker);");
        L("        }");
        L("    }");
        if (t.EventId is not null)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task An_event_id_is_kept_once()");
            L("    {");
            L("        string secret = Secret();");
            L("        string marker = \"inbound-\" + Guid.NewGuid().ToString(\"N\");");
            L("        string eventId = \"evt-\" + Guid.NewGuid().ToString(\"N\");");
            L("        try");
            L("        {");
            L($"            {anonymous};");
            L("            using (var first = await client.SendAsync(Signed(Route + \"/receive\", Message(marker, eventId), secret)))");
            L("                Assert.IsFalse((await first.Content.ReadFromJsonAsync<JsonObject>())![\"duplicate\"]!.GetValue<bool>());");
            L("            using (var second = await client.SendAsync(Signed(Route + \"/receive\", Message(marker, eventId), secret)))");
            L("                Assert.IsTrue((await second.Content.ReadFromJsonAsync<JsonObject>())![\"duplicate\"]!.GetValue<bool>(), \"the same event id is kept once\");");
            L("            using (var header = await client.SendAsync(Signed(Route + \"/receive\", Message(marker), secret, eventId: eventId)))");
            L("                Assert.IsTrue((await header.Content.ReadFromJsonAsync<JsonObject>())![\"duplicate\"]!.GetValue<bool>(), \"the id in the header counts the same\");");
            L("            Assert.AreEqual(1, (await UnprocessedAsync()).Count(r => (r![\"" + body + "\"]?.GetValue<string>() ?? \"\").Contains(marker)));");
            L("        }");
            L("        finally");
            L("        {");
            L($"            await {support}.RemoveAsync(ApiFixture.Services, marker);");
            L("        }");
            L("    }");
        }
        StatusTest(L, t, support, anonymous);
        if (t.Client)
            ClientTests(L, t, project, support, anonymous);
        if (InboundPlan.SourceSecrets(project).FirstOrDefault() is { Source: not null } own)
            OwnSecretTest(L, t, own, support, anonymous, project);
        L();
        L("    [TestMethod]");
        L("    public async Task A_body_over_the_limit_is_refused()");
        L("    {");
        L("        string secret = Secret();");
        L($"        string text = new('x', {limit + 1});");
        L($"        {anonymous};");
        L("        using var response = await client.SendAsync(Signed(Route + \"/receive\", text, secret));");
        L("        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);");
        L($"        StringAssert.Contains((await response.Content.ReadFromJsonAsync<JsonObject>())![\"type\"]!.GetValue<string>(), \"{InboundWords.TooLargeType.Split('/').Last()}\");");
        L("    }");
        if (auth)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task Without_a_sign_in_the_worker_routes_answer_401()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        using var client = ApiFixture.Anonymous();");
            L("        using (var list = await client.GetAsync(Route + \"/unprocessed\"))");
            L("            Assert.AreEqual(HttpStatusCode.Unauthorized, list.StatusCode);");
            L("        using (var done = await client.PostAsync($\"{Route}/1/processed\", null))");
            L("            Assert.AreEqual(HttpStatusCode.Unauthorized, done.StatusCode);");
            L("    }");
        }
        L("}");
        return o.ToString();
    }
}
