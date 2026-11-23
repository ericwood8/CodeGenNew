using System.Text;

namespace CodeGenNew.Core.Payment;

/// <summary> The generated ASP.NET API tests of <c>LedgerTables</c>, appended to the ledger table's own log test class by <see cref="CodeGenNew.Core.Api.ApiLogTests"/>: a row is recorded and the paid amount is worked out from
/// the succeeded rows, a refund is negative and cannot exceed what has been paid, a capture cannot exceed the total, a retried call answers with the stored row and another transaction cannot take its key, and a gateway id is recorded
/// once. A ledger keeps its rows, so each test adds a document of its own and leaves its rows in the scratch database. </summary>
public static class LedgerApiTests
{
    private static string Q(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static string Methods(LedgerTestPlan plan)
    {
        var o = new StringBuilder();
        void L(string text = "") => o.Append(text).Append('\n');
        string rowRoute = plan.RowRoute, docRoute = plan.DocumentRoute;
        L();
        L("    // a ledger keeps its rows: every test adds a document of its own and records against it");
        L("    private static async Task<long> AddDocumentAsync(double total)");
        L("    {");
        L("        var body = (JsonObject)JsonNode.Parse(" + Q(plan.DocumentBody) + ")!;");
        L($"        body[\"{plan.TotalJson}\"] = total;");
        L($"        using var response = await ApiFixture.Client.PostAsJsonAsync(\"{docRoute}\", body);");
        L("        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L($"        return (await response.Content.ReadFromJsonAsync<JsonObject>())![\"{plan.DocumentKeyJson}\"]!.GetValue<long>();");
        L("    }");
        L();
        L("    private static async Task<HttpResponseMessage> RecordAsync(long document, string kind, double amount, string status = \"succeeded\", string? gateway = null, string? key = null)");
        L("    {");
        L("        string tag = Guid.NewGuid().ToString(\"N\");");
        L("        var body = (JsonObject)JsonNode.Parse(" + Q(plan.RowBody) + ")!;");
        L($"        body[\"{plan.DocumentJson}\"] = document;");
        L($"        body[\"{plan.KindJson}\"] = kind;");
        L($"        body[\"{plan.StatusJson}\"] = status;");
        L($"        body[\"{plan.AmountJson}\"] = amount;");
        L($"        body[\"{plan.GatewayJson}\"] = gateway ?? \"g\" + tag;");
        L($"        body[\"{plan.IdempotencyJson}\"] = key ?? \"k\" + tag;");
        L($"        return await ApiFixture.Client.PostAsJsonAsync(\"{rowRoute}/{LedgerPlan.RecordRoute}\", body);");
        L("    }");
        L();
        L("    private static async Task<JsonObject> PaidAsync(long document)");
        L("    {");
        L($"        using var response = await ApiFixture.Client.GetAsync($\"{rowRoute}/{LedgerPlan.PaidRoute}?document={{document}}\");");
        L("        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());");
        L("        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task What_a_document_has_been_paid_is_worked_out_from_the_succeeded_rows()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        long document = await AddDocumentAsync(300);");
        L("        using var authorize = await RecordAsync(document, \"Authorize\", 300);");
        L("        Assert.AreEqual(HttpStatusCode.Created, authorize.StatusCode, await authorize.Content.ReadAsStringAsync());");
        L($"        Assert.AreEqual(\"authorize\", (await authorize.Content.ReadFromJsonAsync<JsonObject>())![\"{plan.KindJson}\"]!.GetValue<string>(), \"the kind is written in lower case\");");
        L("        using var capture = await RecordAsync(document, \"capture\", 200);");
        L("        using var pending = await RecordAsync(document, \"capture\", 40, \"pending\");");
        L("        using var refund = await RecordAsync(document, \"refund\", -50);");
        L("        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode, await refund.Content.ReadAsStringAsync());");
        L("        var paid = await PaidAsync(document);");
        L("        Assert.AreEqual(200, paid[\"captured\"]!.GetValue<double>());");
        L("        Assert.AreEqual(50, paid[\"refunded\"]!.GetValue<double>());");
        L("        Assert.AreEqual(150, paid[\"paid\"]!.GetValue<double>());");
        L("        Assert.AreEqual(150, paid[\"remaining\"]!.GetValue<double>());");
        L("        Assert.IsFalse(paid[\"settled\"]!.GetValue<bool>());");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_refund_is_negative_and_never_more_than_has_been_paid_and_a_capture_never_more_than_the_total()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        long document = await AddDocumentAsync(100);");
        L("        using var positive = await RecordAsync(document, \"refund\", 10);");
        L($"        Assert.AreEqual(HttpStatusCode.BadRequest, positive.StatusCode, \"a refund is a negative row\");");
        L("        using var negativeCapture = await RecordAsync(document, \"capture\", -10);");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, negativeCapture.StatusCode);");
        L("        using var capture = await RecordAsync(document, \"capture\", 60);");
        L("        Assert.AreEqual(HttpStatusCode.Created, capture.StatusCode, await capture.Content.ReadAsStringAsync());");
        L("        using var tooMuch = await RecordAsync(document, \"refund\", -61);");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, tooMuch.StatusCode);");
        L($"        StringAssert.Contains(await tooMuch.Content.ReadAsStringAsync(), \"{PaymentWords.RefundOverType}\");");
        L("        using var over = await RecordAsync(document, \"capture\", 41);");
        L("        Assert.AreEqual(HttpStatusCode.BadRequest, over.StatusCode);");
        L($"        StringAssert.Contains(await over.Content.ReadAsStringAsync(), \"{PaymentWords.CaptureOverType}\");");
        L("        using var rest = await RecordAsync(document, \"capture\", 40);");
        L("        Assert.AreEqual(HttpStatusCode.Created, rest.StatusCode, await rest.Content.ReadAsStringAsync());");
        L("        Assert.IsTrue((await PaidAsync(document))[\"settled\"]!.GetValue<bool>());");
        L("    }");
        L();
        L("    [TestMethod]");
        L("    public async Task A_retried_call_answers_with_the_stored_row_and_a_key_or_gateway_id_is_recorded_once()");
        L("    {");
        L("        ApiFixture.RequireDatabase();");
        L("        long document = await AddDocumentAsync(100);");
        L("        using var first = await RecordAsync(document, \"capture\", 30, key: \"retry-\" + Guid.NewGuid().ToString(\"N\"));");
        L("        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, await first.Content.ReadAsStringAsync());");
        L("        var stored = (await first.Content.ReadFromJsonAsync<JsonObject>())!;");
        L($"        string key = stored[\"{plan.IdempotencyJson}\"]!.GetValue<string>(), gateway = stored[\"{plan.GatewayJson}\"]!.GetValue<string>();");
        L("        using var retry = await RecordAsync(document, \"capture\", 30, key: key);");
        L("        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, await retry.Content.ReadAsStringAsync());");
        L($"        Assert.AreEqual(stored[\"{plan.KeyJson}\"]!.GetValue<long>(), (await retry.Content.ReadFromJsonAsync<JsonObject>())![\"{plan.KeyJson}\"]!.GetValue<long>());");
        L("        Assert.AreEqual(30, (await PaidAsync(document))[\"captured\"]!.GetValue<double>(), \"the retry wrote nothing\");");
        L("        using var other = await RecordAsync(document, \"capture\", 5, key: key);");
        L("        Assert.AreEqual(HttpStatusCode.Conflict, other.StatusCode);");
        L($"        StringAssert.Contains(await other.Content.ReadAsStringAsync(), \"{PaymentWords.LedgerDuplicateType}\");");
        L("        using var sameGateway = await RecordAsync(document, \"capture\", 5, gateway: gateway);");
        L("        Assert.AreEqual(HttpStatusCode.Conflict, sameGateway.StatusCode);");
        L("    }");
        return o.ToString();
    }
}
