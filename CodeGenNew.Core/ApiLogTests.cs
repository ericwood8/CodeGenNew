using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated API tests of a log table (LogTables): it is only read, so the tests do not add, change or remove rows. They check that no route changes rows, that the search answers a page and
/// takes the date range, that an empty range is an empty page, that a bad date is refused, and that the export takes the range. </summary>
public static class ApiLogTests
{
    public static string Text(ApiTestPlan plan, ProjectSettings project, QueueTable? queue = null)
    {
        string route = plan.Route;
        string absent = plan.AbsentKey;
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        string from = LogTable.FromParameter, to = LogTable.ToParameter;

        L($"namespace {project.ProjectName ?? "MyApp"}.Api.Tests;");
        L();
        L("[TestClass]");
        L($"public class {plan.Singular}ApiTests");
        L("{");
        L($"    private const string Route = \"{route}\";");
        L();
        L("    [TestMethod]");
        L("    public async Task A_row_that_is_not_there_answers_404()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L($"        using var response = await ApiFixture.Client.GetAsync($\"{{Route}}/{{{absent}}}\");");
        L("        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);");
        L("    }");
        if (project.ApiAuth)
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
        }
        L();
        L("    [TestMethod]");
        L("    public async Task No_route_changes_a_row_of_a_log_table()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var client = ApiFixture.Client;");
        if (queue is null)
        {
            L("        using var created = await client.PostAsJsonAsync(Route, new JsonObject());");
            L("        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, created.StatusCode, \"there is no POST\");");
        }
        L($"        using var updated = await client.PutAsJsonAsync($\"{{Route}}/{{{absent}}}\", new JsonObject());");
        L("        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, updated.StatusCode, \"there is no PUT\");");
        L($"        using var removed = await client.DeleteAsync($\"{{Route}}/{{{absent}}}\");");
        L("        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, removed.StatusCode, \"there is no DELETE\");");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task The_search_answers_a_page()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var response = await ApiFixture.Client.GetAsync($\"{Route}/search?pageNumber=1&pageSize=5\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        Assert.IsFalse(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync()));");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_date_range_in_the_far_future_lists_no_rows()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L($"        using var response = await ApiFixture.Client.GetAsync($\"{{Route}}/search?{from}=2999-01-01&{to}=2999-12-31&pageNumber=1&pageSize=5\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        var page = (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("        Assert.AreEqual(0, page[\"totalCount\"]!.GetValue<int>());");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_date_that_is_not_a_date_is_refused()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L($"        using var response = await ApiFixture.Client.GetAsync($\"{{Route}}/search?{from}=not-a-date&pageNumber=1&pageSize=5\");");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);");
        L("    }");
        if (plan.SortNames.Count > 0)
        {
            L();
            L("    [TestMethod]");
            foreach (string sortName in plan.SortNames)
                L($"    [DataRow({ApiSampleValues.Json(sortName)})]");
            L("    public async Task The_search_sorts_by_each_column_it_offers(string column)");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        foreach (string direction in new[] { \"asc\", \"desc\" })");
            L("        {");
            L("            using var response = await ApiFixture.Client.GetAsync($\"{Route}/search?pageNumber=1&pageSize=5&sortBy={Uri.EscapeDataString(column)}&sortDir={direction}\");");
            L("            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, column + \" \" + direction + \": \" + await response.Content.ReadAsStringAsync());");
            L("        }");
            L("    }");
        }
        if (project.ApiCsv)
        {
            L();
            L("    [TestMethod]");
            L("    public async Task The_export_takes_the_date_range_and_has_no_import()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L($"        using var export = await ApiFixture.Client.GetAsync($\"{{Route}}/export.csv?{from}=2999-01-01\");");
            L("        Assert.AreEqual(HttpStatusCode.OK, export.StatusCode, await export.Content.ReadAsStringAsync());");
            L("        string[] lines = (await export.Content.ReadAsStringAsync()).Split('\\n', StringSplitOptions.RemoveEmptyEntries);");
            L("        Assert.HasCount(1, lines, \"only the header line\");");
            L("        using var import = await ApiFixture.Client.PostAsync($\"{Route}/import\", new StringContent(\"\"));");
            L("        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, import.StatusCode);");
            L("    }");
        }
        if (queue is not null)
            QueueMethods(L, plan, queue, project, absent);
        L("}");
        return o.ToString();
    }

    private static void QueueMethods(Action<string> L, ApiTestPlan plan, QueueTable queue, ProjectSettings project, string absent)
    {
        string id = JsonNames.Camel(queue.Key.Name), status = JsonNames.Camel(queue.Status.Name), retry = JsonNames.Camel(queue.Retry.Name), claimed = JsonNames.Camel(queue.Claimed.Name);
        string pending = QueueStatus.Pending, claimedStatus = QueueStatus.Claimed, done = QueueStatus.Done, failedStatus = QueueStatus.Failed;
        L("");
        L("    // every test puts its own row in the queue first, so a claim always finds one");
        L("    private static async Task<JsonObject> EnqueueAsync()");
        L("    {");
        L("        var body = new JsonObject");
        L("        {");
        foreach (var c in queue.Payload)
            L($"            [{ApiSampleValues.Json(JsonNames.Camel(c.Name))}] = {plan.Expression(c)},");
        L("        };");
        L("        using var response = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("    }");
        L("");
        L("    private static async Task<JsonObject?> ClaimAsync()");
        L("    {");
        L("        using var response = await ApiFixture.Client.PostAsync($\"{Route}/claim?worker=tests\", null);");
        L("        if (response.StatusCode == HttpStatusCode.NoContent)");
        L("            return null;");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("    }");
        L("");
        L("    // takes the oldest pending row and finishes it, so a test that leaves a row pending does not grow the queue");
        L("    private static async Task SettleAsync()");
        L("    {");
        L("        var row = await ClaimAsync();");
        L("        if (row is null)");
        L("            return;");
        L($"        using var completed = await ApiFixture.Client.PostAsync($\"{{Route}}/{{row[\"{id}\"]!.GetValue<long>()}}/complete\", null);");
        L("        Assert.AreEqual(HttpStatusCode.OK, completed.StatusCode);");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    public async Task A_row_put_in_the_queue_is_pending_with_no_retries_whatever_the_producer_sent()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        var row = await EnqueueAsync();");
        L($"        Assert.AreEqual(\"{pending}\", row[\"{status}\"]!.GetValue<string>());");
        L($"        Assert.AreEqual(0, row[\"{retry}\"]!.GetValue<int>());");
        L($"        Assert.IsNull(row[\"{claimed}\"]);");
        L("        var body = new JsonObject");
        L("        {");
        foreach (var c in queue.Payload)
            L($"            [{ApiSampleValues.Json(JsonNames.Camel(c.Name))}] = {plan.Expression(c)},");
        L($"            [\"{status}\"] = \"{done}\",");
        L($"            [\"{retry}\"] = 7,");
        L("        };");
        L("        using var forced = await ApiFixture.Client.PostAsJsonAsync(Route, body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, forced.StatusCode, await forced.Content.ReadAsStringAsync());");
        L("        var forcedRow = (await forced.Content.ReadFromJsonAsync<JsonObject>())!;");
        L($"        Assert.AreEqual(\"{pending}\", forcedRow[\"{status}\"]!.GetValue<string>(), \"a producer cannot set the status\");");
        L($"        Assert.AreEqual(0, forcedRow[\"{retry}\"]!.GetValue<int>(), \"a producer cannot set the retries\");");
        L("        await SettleAsync();");
        L("        await SettleAsync();");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    public async Task The_stats_count_every_status()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        using var response = await ApiFixture.Client.GetAsync($\"{Route}/stats\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        var counts = (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("        foreach (string name in new[] { \"pending\", \"claimed\", \"done\", \"failed\" })");
        L("            Assert.IsGreaterThanOrEqualTo(0, counts[name]!.GetValue<int>(), name);");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    public async Task A_claimed_row_is_completed_once()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        await EnqueueAsync();");
        L("        var row = await ClaimAsync();");
        L("        Assert.IsNotNull(row, \"the row just put in the queue is pending\");");
        L($"        Assert.AreEqual(\"{claimedStatus}\", row![\"{status}\"]!.GetValue<string>());");
        L($"        var key = row[\"{id}\"]!.GetValue<long>();");
        L("        using var completed = await ApiFixture.Client.PostAsync($\"{Route}/{key}/complete\", null);");
        L("        Assert.AreEqual(HttpStatusCode.OK, completed.StatusCode, await completed.Content.ReadAsStringAsync());");
        L($"        Assert.AreEqual(\"{done}\", (await completed.Content.ReadFromJsonAsync<JsonObject>())![\"{status}\"]!.GetValue<string>());");
        L("        using var again = await ApiFixture.Client.PostAsync($\"{Route}/{key}/complete\", null);");
        L("        Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode, \"only a claimed row can be completed\");");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    public async Task Two_claims_never_return_the_same_row()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        await EnqueueAsync();");
        L("        await EnqueueAsync();");
        L("        var first = await ClaimAsync();");
        L("        var second = await ClaimAsync();");
        L("        Assert.IsNotNull(first);");
        L("        Assert.IsNotNull(second, \"two rows were put in the queue\");");
        L($"        Assert.AreNotEqual(first![\"{id}\"]!.GetValue<long>(), second![\"{id}\"]!.GetValue<long>());");
        L("        foreach (var row in new[] { first, second })");
        L("        {");
        L($"            using var released = await ApiFixture.Client.PostAsync($\"{{Route}}/{{row![\"{id}\"]!.GetValue<long>()}}/complete\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, released.StatusCode);");
        L("        }");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    public async Task A_renewed_claim_starts_again_and_only_a_claimed_row_can_be_renewed()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        await EnqueueAsync();");
        L("        var row = await ClaimAsync();");
        L("        Assert.IsNotNull(row);");
        L($"        var key = row![\"{id}\"]!.GetValue<long>();");
        L($"        var first = row[\"{claimed}\"]!.GetValue<DateTime>();");
        L("        await Task.Delay(1100);");
        L("        using var renewed = await ApiFixture.Client.PostAsync($\"{Route}/{key}/renew\", null);");
        L("        Assert.AreEqual(HttpStatusCode.OK, renewed.StatusCode, await renewed.Content.ReadAsStringAsync());");
        L("        var after = (await renewed.Content.ReadFromJsonAsync<JsonObject>())!;");
        L($"        Assert.AreEqual(\"{claimedStatus}\", after[\"{status}\"]!.GetValue<string>());");
        L($"        Assert.IsGreaterThan(first, after[\"{claimed}\"]!.GetValue<DateTime>(), \"the claim time moved on\");");
        L("        using var completed = await ApiFixture.Client.PostAsync($\"{Route}/{key}/complete\", null);");
        L("        Assert.AreEqual(HttpStatusCode.OK, completed.StatusCode);");
        L("        using var late = await ApiFixture.Client.PostAsync($\"{Route}/{key}/renew\", null);");
        L("        Assert.AreEqual(HttpStatusCode.Conflict, late.StatusCode, \"a finished row has no claim to renew\");");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    public async Task A_failed_row_counts_a_retry_and_goes_back_to_pending_until_the_retries_are_used()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        await EnqueueAsync();");
        L("        var row = await ClaimAsync();");
        L("        Assert.IsNotNull(row);");
        L($"        var key = row![\"{id}\"]!.GetValue<long>();");
        L($"        int before = row[\"{retry}\"]!.GetValue<int>();");
        L("        using var failed = await ApiFixture.Client.PostAsJsonAsync($\"{Route}/{key}/fail\", new JsonObject { [\"error\"] = \"a test failure\" });");
        L("        Assert.AreEqual(HttpStatusCode.OK, failed.StatusCode, await failed.Content.ReadAsStringAsync());");
        L("        var after = (await failed.Content.ReadFromJsonAsync<JsonObject>())!;");
        L($"        Assert.AreEqual(before + 1, after[\"{retry}\"]!.GetValue<int>());");
        L($"        Assert.AreEqual(before + 1 >= {project.QueueMaxRetries} ? \"{failedStatus}\" : \"{pending}\", after[\"{status}\"]!.GetValue<string>());");
        L($"        if (after[\"{status}\"]!.GetValue<string>() == \"{failedStatus}\")");
        L("        {");
        L("            using var retried = await ApiFixture.Client.PostAsync($\"{Route}/{key}/retry\", null);");
        L("            Assert.AreEqual(HttpStatusCode.OK, retried.StatusCode, await retried.Content.ReadAsStringAsync());");
        L($"            Assert.AreEqual(\"{pending}\", (await retried.Content.ReadFromJsonAsync<JsonObject>())![\"{status}\"]!.GetValue<string>());");
        L("        }");
        L("        await SettleAsync();");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    public async Task A_row_that_is_not_failed_cannot_be_retried()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        await EnqueueAsync();");
        L("        var row = await ClaimAsync();");
        L("        Assert.IsNotNull(row);");
        L($"        var key = row![\"{id}\"]!.GetValue<long>();");
        L("        using var retried = await ApiFixture.Client.PostAsync($\"{Route}/{key}/retry\", null);");
        L("        Assert.AreEqual(HttpStatusCode.Conflict, retried.StatusCode);");
        L("        using var completed = await ApiFixture.Client.PostAsync($\"{Route}/{key}/complete\", null);");
        L("        Assert.AreEqual(HttpStatusCode.OK, completed.StatusCode);");
        L("    }");
        L("");
        L("    [TestMethod]");
        L("    [DataRow(\"complete\")]");
        L("    [DataRow(\"fail\")]");
        L("    [DataRow(\"retry\")]");
        L("    [DataRow(\"renew\")]");
        L("    public async Task A_row_that_is_not_there_answers_404_to_every_queue_route(string action)");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L($"        using var response = await ApiFixture.Client.PostAsync($\"{{Route}}/{{{absent}}}/{{action}}\", null);");
        L("        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);");
        L("    }");
        if (project.ApiAuth)
        {
            L("");
            L("    [TestMethod]");
            L("    public async Task Without_a_token_the_claim_and_the_enqueue_answer_401()");
            L("    {");
            L("        ApiFixture.RequireDatabase();");
            L("        using var anonymous = ApiFixture.Anonymous();");
            L("        using var response = await anonymous.PostAsync($\"{Route}/claim\", null);");
            L("        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);");
            L("        using var enqueue = await anonymous.PostAsJsonAsync(Route, new JsonObject());");
            L("        Assert.AreEqual(HttpStatusCode.Unauthorized, enqueue.StatusCode);");
            L("    }");
        }
    }
}
