using System.Text;

namespace CodeGenNew.Core;

/// <summary> MSTest tests of the approvals of the two desktop apps. WinUI3 runs the rules in process, so its tests drive <c>ApprovalService</c> over an in-memory SQLite database with a manager, an
/// employee and a row; Uno reads the API, so its tests check the calls <c>ApprovalsClient</c> makes and the sentence of a refusal. </summary>
public static class DesktopApprovalTests
{
    /// <summary> The JSON of a sample row of the table: every column with a value that fits it, the identity key left out, and <paramref name="overrides"/> (column name to JSON value) in place of the sample. </summary>
    public static string SampleJson(TableModel table, ProjectSettings project, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var members = ApiSampleValues.BodyColumns(table).Where(c => !c.IsRowVersionColumn && !(c.IsPrimaryKey && c.IsIntegerColumn))
            .Select(c => ApiSampleValues.Json(JsonNames.Camel(c.Name)) + ": " + (overrides is not null && overrides.TryGetValue(c.Name, out var v) ? v : ApiSampleValues.Value(c, project, table.TableName)));
        return "{ " + string.Join(", ", members) + " }";
    }

    public static string WinUI3(ApprovalPlan plan, ProjectSettings project, string testNamespace, IEnumerable<string> usings)
    {
        var user = plan.User!;
        var table = plan.Tables[0];
        string Person(string login, bool manager)
        {
            var overrides = new Dictionary<string, string> { [user.UserName.Name] = ApiSampleValues.Json(login) };
            if (plan.Approvers is null)
                overrides[user.Role!.Name] = manager ? "true" : "false";
            if (user.Admin is { } admin)
                overrides[admin.Name] = "false";
            return SampleJson(user.Table, project, overrides);
        }
        var (approverConstants, approverRows, approverTests) = ApproverParts(plan, project, table.Table.TableName);
        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append($$""""

namespace {{testNamespace}};

/// <summary> The approval rules of {{table.Table.TableName}} run in process by <c>ApprovalService</c>: who may request and decide, the four eyes, withdrawal, the history and the hooks. </summary>
[TestClass]
public class ApprovalServiceTests
{
    private const string Ann = """{{Person("ann", false)}}""";
    private const string Bob = """{{Person("bob", true)}}""";
    private const string Cy = """{{Person("cy", true)}}""";
    private const string RowJson = """{{SampleJson(table.Table, project)}}""";
{{approverConstants}}
    private static T Make<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json, TestJson.Options)!;

    private static async Task<(TestDb Db, ApprovalService Service)> OpenAsync(string? caller)
    {
        var db = await TestDb.CreateAsync();
        db.Context.Add(Make<{{user.Table.TableName}}>(Ann));
        db.Context.Add(Make<{{user.Table.TableName}}>(Bob));
        db.Context.Add(Make<{{user.Table.TableName}}>(Cy));
        db.Context.Add(Make<{{table.Table.TableName}}>(RowJson));
        await db.Context.SaveChangesAsync();
{{approverRows}}        db.Context.ChangeTracker.Clear();
        DesktopCaller.Name = caller;
        return (db, new ApprovalService(db.Context));
    }

    private static async Task<ApprovalRefused> RefusedAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (ApprovalRefused ex)
        {
            return ex;
        }
        throw new AssertFailedException("The call was not refused.");
    }

    [TestMethod]
    public async Task Refuses_a_person_who_is_not_in_the_user_table()
    {
        var (db, service) = await OpenAsync("stranger");
        await using var scope = db;
        var refused = await RefusedAsync(() => service.RequestAsync("{{table.Table.TableName}}", 1, null));
        Assert.AreEqual(403, refused.Status);
        Assert.AreEqual("{{ApprovalWords.RequesterUnknown}}", refused.Message);
    }

    [TestMethod]
    public async Task A_request_waits_for_a_decision_and_a_second_one_is_refused()
    {
        var (db, service) = await OpenAsync("ann");
        await using var scope = db;
        await service.RequestAsync("{{table.Table.TableName}}", 1, "please");
        var refused = await RefusedAsync(() => service.RequestAsync("{{table.Table.TableName}}", 1, null));
        Assert.AreEqual(409, refused.Status);
        Assert.AreEqual("{{ApprovalWords.AlreadyWaiting}}", refused.Message);
        var history = await service.HistoryAsync("{{table.Table.TableName}}", 1);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("Pending", history[0].Status);
        Assert.AreEqual("please", history[0].Reason);
    }

    [TestMethod]
    public async Task Only_an_approver_decides_and_never_on_their_own_request()
    {
        var (db, service) = await OpenAsync("ann");
        await using var scope = db;
        await service.RequestAsync("{{table.Table.TableName}}", 1, null);
        Assert.AreEqual("{{ApprovalWords.NotApprover}}", (await RefusedAsync(() => service.DecideAsync("{{table.Table.TableName}}", 1, true, null))).Message);
        Assert.AreEqual(403, (await RefusedAsync(() => service.ToApproveAsync("{{table.Table.TableName}}"))).Status);

        DesktopCaller.Name = "bob";
        Assert.AreEqual(1, (await service.ToApproveAsync("{{table.Table.TableName}}")).Count);
        DesktopCaller.Name = "ann";
        await service.WithdrawAsync("{{table.Table.TableName}}", 1);

        DesktopCaller.Name = "bob";
        await service.RequestAsync("{{table.Table.TableName}}", 1, null);
        Assert.AreEqual("{{ApprovalWords.FourEyes}}", (await RefusedAsync(() => service.DecideAsync("{{table.Table.TableName}}", 1, true, null))).Message);
        Assert.AreEqual(0, (await service.ToApproveAsync("{{table.Table.TableName}}")).Count);
    }

    [TestMethod]
    public async Task A_decision_is_written_once_and_the_history_shows_every_cycle()
    {
        var (db, service) = await OpenAsync("ann");
        await using var scope = db;
        await service.RequestAsync("{{table.Table.TableName}}", 1, null);
        await service.WithdrawAsync("{{table.Table.TableName}}", 1);
        await service.RequestAsync("{{table.Table.TableName}}", 1, null);

        DesktopCaller.Name = "cy";
        await service.DecideAsync("{{table.Table.TableName}}", 1, true, "fine");
        var again = await RefusedAsync(() => service.DecideAsync("{{table.Table.TableName}}", 1, false, null));
        Assert.AreEqual(409, again.Status);

        var history = await service.HistoryAsync("{{table.Table.TableName}}", 1);
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("Approved", history[0].Status);
        Assert.AreEqual("cy", history[0].DecidedBy);
        Assert.AreEqual("fine", history[0].DecisionNote);
        Assert.AreEqual("Withdrawn", history[1].Status);
    }

    [TestMethod]
    public async Task Only_the_requester_withdraws()
    {
        var (db, service) = await OpenAsync("ann");
        await using var scope = db;
        await service.RequestAsync("{{table.Table.TableName}}", 1, null);
        DesktopCaller.Name = "bob";
        Assert.AreEqual("{{ApprovalWords.NotRequester}}", (await RefusedAsync(() => service.WithdrawAsync("{{table.Table.TableName}}", 1))).Message);
    }

    [TestMethod]
    public async Task A_hook_can_refuse_a_request_and_sees_a_decision()
    {
        var (db, service) = await OpenAsync("ann");
        await using var scope = db;
        ApprovalHooks.Validate = e => Task.FromResult<string?>(e.Action == "request" ? "Not today." : null);
        try
        {
            Assert.AreEqual("Not today.", (await RefusedAsync(() => service.RequestAsync("{{table.Table.TableName}}", 1, null))).Message);
        }
        finally
        {
            ApprovalHooks.Validate = null;
        }
        await service.RequestAsync("{{table.Table.TableName}}", 1, null);
        string? seen = null;
        ApprovalHooks.OnDecided = e =>
        {
            seen = e.Action;
            return Task.CompletedTask;
        };
        DesktopCaller.Name = "bob";
        try
        {
            await service.DecideAsync("{{table.Table.TableName}}", 1, false, null);
        }
        finally
        {
            ApprovalHooks.OnDecided = null;
        }
        Assert.AreEqual("reject", seen);
    }
{{approverTests}}}
"""");
        return o.ToString();
    }

    /// <summary> With <c>ApproverTable</c>: the rows that make Bob and Cy approvers (their keys are 2 and 3), a person with a row for another table, and the test that such a row does not let them decide. </summary>
    private static (string Constants, string Rows, string Tests) ApproverParts(ApprovalPlan plan, ProjectSettings project, string tableName)
    {
        if (plan.Approvers is not { } approvers)
            return ("", "", "");
        var user = plan.User!;
        string Row(int person, string? forTable)
        {
            var overrides = new Dictionary<string, string> { [approvers.Person.Name] = person.ToString() };
            if (approvers.ForTable is { } required)
                overrides[required.Name] = forTable is null ? "null" : ApiSampleValues.Json(forTable);
            string json = SampleJson(approvers.Table, project, overrides);
            // an optional column is left out of the sample, so the table's name is added here
            if (approvers.ForTable is { } named && forTable is not null && !json.Contains($"\"{JsonNames.Camel(named.Name)}\":"))
                json = json[..^2] + ", " + ApiSampleValues.Json(JsonNames.Camel(named.Name)) + ": " + ApiSampleValues.Json(forTable) + " }";
            return json;
        }
        string approver = approvers.Table.TableName;
        string constants = $"    private const string BobApprover = \"\"\"{Row(2, tableName)}\"\"\";\n    private const string CyApprover = \"\"\"{Row(3, null)}\"\"\";\n";
        var rows = new StringBuilder($"        db.Context.Add(Make<{approver}>(BobApprover));\n        db.Context.Add(Make<{approver}>(CyApprover));\n");
        var tests = new StringBuilder();
        if (approvers.ForTable is not null)
        {
            var dee = new Dictionary<string, string> { [user.UserName.Name] = ApiSampleValues.Json("dee") };
            if (user.Admin is { } admin)
                dee[admin.Name] = "false";
            constants += $"    private const string Dee = \"\"\"{SampleJson(user.Table, project, dee)}\"\"\";\n    private const string DeeApprover = \"\"\"{Row(4, "Elsewhere")}\"\"\";\n";
            rows.Append($"        db.Context.Add(Make<{user.Table.TableName}>(Dee));\n        await db.Context.SaveChangesAsync();\n        db.Context.Add(Make<{approver}>(DeeApprover));\n");
            tests.Append($$"""

    [TestMethod]
    public async Task A_row_for_another_table_does_not_let_a_person_decide_this_one()
    {
        var (db, service) = await OpenAsync("ann");
        await using var scope = db;
        await service.RequestAsync("{{tableName}}", 1, null);
        DesktopCaller.Name = "dee";
        Assert.AreEqual("{{ApprovalWords.NotApprover}}", (await RefusedAsync(() => service.DecideAsync("{{tableName}}", 1, true, null))).Message);
        DesktopCaller.Name = "cy";
        await service.DecideAsync("{{tableName}}", 1, true, null);
    }

""");
        }
        rows.Append("        await db.Context.SaveChangesAsync();\n");
        return (constants, rows.ToString(), tests.ToString());
    }

    public static string Uno(ApprovalScreenTable table, string testNamespace, IEnumerable<string> usings)
    {
        var o = new StringBuilder();
        foreach (string u in usings)
            o.Append($"using {u};\n");
        o.Append($$"""

namespace {{testNamespace}};

/// <summary> The calls <c>ApprovalsClient</c> makes for {{table.Table}} and the sentence of a refusal, over a stand-in for the API. </summary>
[TestClass]
public class ApprovalsClientTests
{
    [TestMethod]
    public async Task Asks_for_the_requests_waiting_and_the_history_of_a_row()
    {
        var api = new FakeApi();

        Assert.AreEqual(0, (await api.Clients.Approvals.ToApproveAsync("{{table.Table}}")).Count);
        Assert.AreEqual(0, (await api.Clients.Approvals.HistoryAsync("{{table.Table}}", 7)).Count);
    }

    [TestMethod]
    public async Task A_request_carries_its_reason()
    {
        var api = new FakeApi();

        await api.Clients.Approvals.RequestAsync("{{table.Table}}", 7, "please");

        var sent = api.Writes.Single();
        Assert.AreEqual("{{table.Route}}/7/request", sent.Path);
        StringAssert.Contains(sent.Body, "\"reason\":\"please\"");
    }

    [TestMethod]
    public async Task A_decision_goes_to_approve_or_reject_with_its_note()
    {
        var api = new FakeApi();

        await api.Clients.Approvals.DecideAsync("{{table.Table}}", 7, true, "fine");
        await api.Clients.Approvals.DecideAsync("{{table.Table}}", 7, false, null);

        Assert.AreEqual("{{table.Route}}/7/approve", api.Writes[0].Path);
        StringAssert.Contains(api.Writes[0].Body, "\"note\":\"fine\"");
        Assert.AreEqual("{{table.Route}}/7/reject", api.Writes[1].Path);
    }

    [TestMethod]
    public async Task A_withdrawal_posts_to_withdraw()
    {
        var api = new FakeApi();

        await api.Clients.Approvals.WithdrawAsync("{{table.Table}}", 7);

        Assert.AreEqual("{{table.Route}}/7/withdraw", api.Writes.Single().Path);
    }

    [TestMethod]
    public async Task A_refused_call_throws_with_the_status()
    {
        var api = new FakeApi();
        api.FailWrites(System.Net.HttpStatusCode.Forbidden);

        var refused = await Assert.ThrowsAsync<ApprovalRefused>(() => api.Clients.Approvals.DecideAsync("{{table.Table}}", 7, true, null));

        Assert.AreEqual(403, refused.Status);
    }
}
""");
        return o.ToString();
    }
}
