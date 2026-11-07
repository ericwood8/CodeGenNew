using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> The inbound event landing tables (InboundTables): which tables fit, what a table that does not fit is told, and the text of the routes and their tests. </summary>
[TestClass]
public class InboundPlanTests
{
    private static readonly string[] Tables =
    [
        "CREATE TABLE WebhookEvent (WebhookEventId INTEGER PRIMARY KEY AUTOINCREMENT, Source VARCHAR(40) NULL, EventId VARCHAR(80) NULL, Body VARCHAR(4000) NOT NULL, ReceivedOn DATETIME NOT NULL, ProcessedOn DATETIME NULL, Title VARCHAR(40) NULL)",
        "CREATE TABLE ErrorReport (ErrorReportId INTEGER PRIMARY KEY AUTOINCREMENT, Title VARCHAR(100) NULL, Payload TEXT NOT NULL, ReceivedAt DATETIME NOT NULL, IsProcessed BOOLEAN NOT NULL DEFAULT 0)",
        "CREATE TABLE NoStamp (NoStampId INTEGER PRIMARY KEY AUTOINCREMENT, Body VARCHAR(100) NOT NULL, Title VARCHAR(40) NULL, Processed BOOLEAN NOT NULL DEFAULT 0)",
        "CREATE TABLE Awkward (AwkwardId INTEGER PRIMARY KEY AUTOINCREMENT, Body VARCHAR(100) NOT NULL, ReceivedOn DATETIME NOT NULL, ProcessedOn DATETIME NULL, Region VARCHAR(10) NOT NULL)",
        "CREATE TABLE StrictStamp (StrictStampId INTEGER PRIMARY KEY AUTOINCREMENT, Body VARCHAR(100) NOT NULL, ReceivedOn DATETIME NOT NULL, ProcessedOn DATETIME NOT NULL)"
    ];

    private static async Task<DatabaseModel> ReadAsync()
    {
        string database = TenantPlanTests.CreateDatabase(Tables);
        try
        {
            var request = new CodeGenNew.Connections.ConnectionRequest { Provider = CodeGenNew.Connections.DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            return await provider.BuildAsync(database, "main");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    private static ProjectSettings Project(string tables, Dictionary<string, string>? extra = null)
    {
        var values = TenantPlanTests.Values(new Dictionary<string, string> { ["InboundTables"] = tables });
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public async Task A_table_with_a_body_a_received_stamp_and_a_processed_column_is_found_by_name()
    {
        var database = await ReadAsync();
        var project = Project("WebhookEvent,ErrorReport:Payload");

        var webhook = InboundPlan.For(database.Tables.Single(t => t.TableName == "WebhookEvent"), project)!;
        Assert.AreEqual("Body", webhook.Body.Name);
        Assert.AreEqual("EventId", webhook.EventId!.Name);
        Assert.AreEqual("Source", webhook.Source!.Name);
        Assert.AreEqual("ReceivedOn", webhook.Received.Name);
        Assert.AreEqual("ProcessedOn", webhook.Processed.Name);
        Assert.IsFalse(webhook.ProcessedIsFlag);
        Assert.AreEqual(4000, webhook.BodyLength);

        var report = InboundPlan.For(database.Tables.Single(t => t.TableName == "ErrorReport"), project)!;
        Assert.AreEqual("Payload", report.Body.Name);
        Assert.IsNull(report.EventId, "no event id column, so no idempotence");
        Assert.AreEqual("ReceivedAt", report.Received.Name);
        Assert.IsTrue(report.ProcessedIsFlag);
        Assert.IsTrue(LogTablePlan.IsListed("WebhookEvent", project), "an inbound table is also a read-only list");
    }

    [TestMethod]
    public async Task A_table_that_does_not_fit_is_told_what_is_missing()
    {
        var database = await ReadAsync();
        var project = Project("NoStamp,Awkward,StrictStamp");

        Assert.IsNull(InboundPlan.For(database.Tables.Single(t => t.TableName == "NoStamp"), project));
        StringAssert.Contains(InboundPlan.Problem(database.Tables.Single(t => t.TableName == "NoStamp"), project)!, "no date and time column");
        StringAssert.Contains(InboundPlan.Problem(database.Tables.Single(t => t.TableName == "Awkward"), project)!, "Region");
        StringAssert.Contains(InboundPlan.Problem(database.Tables.Single(t => t.TableName == "StrictStamp"), project)!, "to allow NULL");
        Assert.IsNull(InboundPlan.Problem(database.Tables.Single(t => t.TableName == "WebhookEvent"), project), "a table that is not listed has no problem");
    }

    [TestMethod]
    public async Task The_routes_and_their_tests_are_written_from_the_shape()
    {
        var database = await ReadAsync();
        var project = Project("WebhookEvent,ErrorReport:Payload");
        var tables = database.Tables.Select(t => TableShapes.For(t, project).Inbound).OfType<InboundShape>().OrderBy(t => t.Table.TableName).ToList();

        string routes = InboundWriter.File(tables, project, "ShopContext", []);
        Assert.IsFalse(routes.Contains('§'), "every token is filled");
        StringAssert.Contains(routes, "public static class WebhookEventInbound");
        StringAssert.Contains(routes, "public static class ErrorReportInbound");
        StringAssert.Contains(routes, "configuration[\"INBOUND_WEBHOOK_SECRET\"]");
        StringAssert.Contains(routes, "private const int BodyMax = 4000;");
        StringAssert.Contains(routes, "x.EventId == eventId && x.Source == source", "an event id is looked up with its source");
        StringAssert.Contains(routes, "x.ProcessedOn == null");
        StringAssert.Contains(routes, "x.IsProcessed != true");
        StringAssert.Contains(routes, "IsProcessed = false,");
        StringAssert.Contains(routes, ".AllowAnonymous()");
        StringAssert.Contains(routes, ".RequireAuthorization()");
        StringAssert.Contains(routes, "InboundApi", "the routes are registered");
        Assert.AreEqual(1, routes.Split("JsonEventId(string body)").Length - 1, "the event id reader is written only for the table that has an event id");

        var custom = Project("WebhookEvent", new() { ["InboundSecret"] = "SHOP_SECRET", ["InboundMaxBytes"] = "2048", ["InboundToleranceSeconds"] = "60" });
        string tuned = InboundWriter.File([InboundPlan.For(database.Tables.Single(t => t.TableName == "WebhookEvent"), custom)!], custom, "ShopContext", []);
        StringAssert.Contains(tuned, "configuration[\"SHOP_SECRET\"]");
        StringAssert.Contains(tuned, "MaxBytes = 2048;");
        StringAssert.Contains(tuned, "ToleranceSeconds = 60;");

        string tests = ApiInboundTests.ClassText(tables[1], project);
        StringAssert.Contains(tests, "public class WebhookEventInboundApiTests");
        StringAssert.Contains(tests, "public async Task An_event_id_is_kept_once()");
        StringAssert.Contains(tests, "new('x', 4001)", "the body over the limit is the body column's length plus one");
        StringAssert.Contains(tests, "Without_a_sign_in_the_worker_routes_answer_401");
        StringAssert.Contains(tests, "\"/receive/stripe\"");
        Assert.IsFalse(ApiInboundTests.ClassText(tables[0], project).Contains("An_event_id_is_kept_once"), "a table with no event id column has no such test");
        StringAssert.Contains(ApiInboundTests.ClassText(tables[0], project), "new('x', 1048577)");
    }

    [TestMethod]
    public async Task The_python_and_rust_routes_and_tests_are_written_from_the_shape()
    {
        var database = await ReadAsync();
        var project = Project("WebhookEvent,ErrorReport:Payload", new() { ["PythonTests"] = "true", ["RustTests"] = "true" });
        var tables = database.Tables.Select(t => TableShapes.For(t, project).Inbound).OfType<InboundShape>().OrderBy(t => t.Table.TableName).ToList();

        string python = PythonInboundWriter.File(tables, project);
        Assert.IsFalse(python.Contains('§'), "every token is filled");
        Assert.Contains("table=\"WebhookEvent\"", python);
        Assert.Contains("event_id=\"event_id\"", python);
        Assert.Contains("flag=True", python, "the processed column of ErrorReport is a flag");
        Assert.Contains("@public_router.post(spec.route + \"/receive\"", python);
        Assert.Contains("SECRET_NAME = \"INBOUND_WEBHOOK_SECRET\"", python);
        Assert.Contains("column == false()", python, "a flag that is not set is compared with false, which SQL Server writes as 0");
        Assert.IsFalse(python.Contains("is_not(True)"), "IS NOT 1 is not valid SQL Server");
        string sqlServerConftest = PythonInboundTests.Conftest(project, SqlDialect.SqlServer);
        Assert.Contains("os.environ[\"DATABASE_URL\"] = url", sqlServerConftest, "the tests open the URL of a SQL Server copy as they do for PostgreSQL and MySQL");
        Assert.IsFalse(sqlServerConftest.Contains("SKIP = \"the approval"), "SQL Server is not skipped");

        string rust =RustInboundWriter.File(tables, project, SqlDialect.Sqlite);
        Assert.IsFalse(rust.Contains('§'), "every token is filled");
        Assert.Contains("const SQL_INSERT_webhook_event", rust);
        Assert.Contains("RETURNING", rust);
        Assert.Contains("pub fn public_router() -> Router<Pool>", rust);
        Assert.Contains("\"/api/webhookevents/receive/{source}\"", rust);
        Assert.Contains("AND \"Source\" IS NULL LIMIT 1", rust, "an event id without a source is looked up with IS NULL");
        Assert.AreEqual(1, rust.Split("fn json_event_id(").Length - 1, "the event id reader is written only for the table that has an event id");

        string pythonTests = PythonInboundTests.File(tables[1], database, project);
        Assert.IsFalse(pythonTests.Contains('§'), "every token is filled");
        Assert.Contains("def test_an_event_id_is_kept_once(api):", pythonTests);
        Assert.Contains("\"x\" * 4001", pythonTests);
        Assert.Contains("/receive/stripe", pythonTests);
        Assert.Contains("def test_without_a_sign_in_the_worker_routes_answer_401(api):", pythonTests);
        Assert.IsFalse(PythonInboundTests.File(tables[0], database, project).Contains("test_an_event_id_is_kept_once"), "a table with no event id column has no such test");
        Assert.AreEqual("tests/test_inbound_webhook_event.py", PythonInboundTests.FileName(tables[1], project));

        string rustTests = RustInboundTests.File(tables[1], database, project);
        Assert.IsFalse(rustTests.Contains('§'), "every token is filled");
        Assert.Contains("async fn an_event_id_is_kept_once()", rustTests);
        Assert.Contains("\"x\".repeat(4001)", rustTests);
        Assert.Contains("async fn without_a_sign_in_the_worker_routes_answer_401()", rustTests);
        Assert.Contains("static ROWS: Mutex<()>", rustTests, "the tests of a table take turns over one shared database");
        Assert.IsFalse(RustInboundTests.File(tables[0], database, project).Contains("an_event_id_is_kept_once"));
        Assert.AreEqual("inbound_webhook_event_routes", RustInboundTests.Module(tables[1], project));
    }

    [TestMethod]
    public async Task A_client_table_gets_the_write_only_collect_route_in_every_stack()
    {
        var database = await ReadAsync();
        var plain = Project("WebhookEvent,ErrorReport:Payload", new() { ["PythonTests"] = "true", ["RustTests"] = "true" });
        var project = Project("WebhookEvent,ErrorReport:Payload", new() { ["InboundClientTables"] = "WebhookEvent", ["InboundClientPerMinute"] = "12", ["InboundClientMaxBytes"] = "512", ["PythonTests"] = "true", ["RustTests"] = "true" });
        InboundShape[] Shapes(ProjectSettings p) => database.Tables.Select(t => TableShapes.For(t, p).Inbound).OfType<InboundShape>().OrderBy(t => t.Table.TableName).ToArray();
        var tables = Shapes(project);
        var none = Shapes(plain);
        Assert.IsTrue(tables[1].Client);
        Assert.IsFalse(tables[0].Client);

        string routes = InboundWriter.File(tables, project, "ShopContext", []);
        Assert.IsFalse(routes.Contains('§'), "every token is filled");
        Assert.Contains("app.MapPost(\"/api/webhookevents/collect\", CollectAsync).AllowAnonymous();", routes);
        Assert.Contains("public const int ClientPerMinute = 12;", routes);
        Assert.Contains("ReadBodyAsync(http, ClientMaxBytes)", routes);
        Assert.Contains("Source = \"client\",", routes);
        Assert.Contains("Results.Accepted()", routes);
        Assert.AreEqual(1, routes.Split("CollectAsync(HttpContext").Length - 1, "only the client table has the route");
        Assert.IsFalse(InboundWriter.File(none, plain, "ShopContext", []).Contains("Collect"), "no client table, no collect route");

        string python = PythonInboundWriter.File(tables, project);
        Assert.IsFalse(python.Contains('§'), "every token is filled");
        Assert.Contains("CLIENT_PER_MINUTE = 12", python);
        Assert.Contains("client=True", python);
        Assert.Contains("spec.route + \"/collect\"", python);
        Assert.IsFalse(PythonInboundWriter.File(none, plain).Contains("client_allowed"), "no client table, no limiter");

        string rust = RustInboundWriter.File(tables, project, SqlDialect.Sqlite);
        Assert.IsFalse(rust.Contains('§'), "every token is filled");
        Assert.Contains("const CLIENT_PER_MINUTE: u32 = 12;", rust);
        Assert.Contains("\"/api/webhookevents/collect\", post(collect_webhook_event)", rust);
        Assert.Contains("InboundError::RateLimited(60 - now % 60)", rust);
        Assert.Contains("let source_text: Option<String> = fit(Some(\"client\".to_string()), 40);", rust);
        Assert.IsFalse(RustInboundWriter.File(none, plain, SqlDialect.Sqlite).Contains("ClientLimit"), "no client table, no limiter");

        string tests = ApiInboundTests.ClassText(tables[1], project);
        Assert.Contains("A_client_message_is_kept_with_no_signature_and_the_answer_holds_nothing", tests);
        Assert.Contains("new string('x', 513)", tests, "the body over the client limit is the limit plus one");
        Assert.Contains("for (int i = 0; i < 12; i++)", tests);
        Assert.IsFalse(ApiInboundTests.ClassText(tables[0], project).Contains("/collect"));
        Assert.Contains("def test_a_caller_over_the_limit_of_requests_a_minute_is_refused_with_retry_after(api):", PythonInboundTests.File(tables[1], database, project));
        Assert.Contains("async fn a_caller_over_the_limit_of_requests_a_minute_is_refused()", RustInboundTests.File(tables[1], database, project));
        Assert.IsFalse(PythonInboundTests.File(tables[0], database, project).Contains("/collect"));
    }

    [TestMethod]
    public async Task A_client_table_must_also_be_an_inbound_table()
    {
        var database = await ReadAsync();
        var project = Project("WebhookEvent", new() { ["InboundClientTables"] = "ErrorReport,Missing" });
        var problems = InboundPlan.ClientProblems(database, project);
        Assert.HasCount(2, problems);
        StringAssert.Contains(problems[0], "ErrorReport must also be a table of InboundTables");
        Assert.IsEmpty(InboundPlan.ClientProblems(database, Project("WebhookEvent", new() { ["InboundClientTables"] = "WebhookEvent" })));
    }

    [TestMethod]
    public async Task A_sender_with_its_own_secret_is_checked_against_that_secret_only_in_every_stack()
    {
        var database = await ReadAsync();
        var plain = Project("WebhookEvent,ErrorReport:Payload", new() { ["PythonTests"] = "true", ["RustTests"] = "true", ["ApiTests"] = "true" });
        var project = Project("WebhookEvent,ErrorReport:Payload", new() { ["InboundSourceSecrets"] = "stripe:STRIPE_SECRET, github:GITHUB_SECRET", ["PythonTests"] = "true", ["RustTests"] = "true", ["ApiTests"] = "true" });
        InboundShape[] Shapes(ProjectSettings p) => database.Tables.Select(t => TableShapes.For(t, p).Inbound).OfType<InboundShape>().OrderBy(t => t.Table.TableName).ToArray();
        var tables = Shapes(project);
        var none = Shapes(plain);
        CollectionAssert.AreEqual(new[] { ("stripe", "STRIPE_SECRET"), ("github", "GITHUB_SECRET") }, InboundPlan.SourceSecrets(project).ToArray());
        Assert.AreEqual("stripe-x", InboundPlan.TestSource(project), "the generated tests post as a sender that has no secret of its own");
        Assert.AreEqual("stripe", InboundPlan.TestSource(plain));

        string routes = InboundWriter.File(tables, project, "ShopContext", []);
        Assert.IsFalse(routes.Contains('§'), "every token is filled");
        Assert.Contains("SourceSecrets = [(\"stripe\", \"STRIPE_SECRET\"), (\"github\", \"GITHUB_SECRET\")];", routes);
        Assert.Contains("secretName = own.Secret;", routes);
        Assert.Contains("configuration[secretName]", routes);
        string shared = InboundWriter.File(none, plain, "ShopContext", []);
        Assert.Contains("configuration[\"INBOUND_WEBHOOK_SECRET\"]", shared);
        Assert.IsFalse(shared.Contains("SourceSecrets"), "no sender has its own secret, so the code is the shared one");

        string python = PythonInboundWriter.File(tables, project);
        Assert.IsFalse(python.Contains('§'), "every token is filled");
        Assert.Contains("SOURCE_SECRETS = {\"stripe\": \"STRIPE_SECRET\", \"github\": \"GITHUB_SECRET\"}", python);
        Assert.Contains("secret_name, source = source_secret(source)", python);
        Assert.Contains("os.environ.get(secret_name, \"\")", python);
        string pythonShared = PythonInboundWriter.File(none, plain);
        Assert.Contains("os.environ.get(SECRET_NAME, \"\")", pythonShared);
        Assert.IsFalse(pythonShared.Contains("source_secret"));

        string rust = RustInboundWriter.File(tables, project, SqlDialect.Sqlite);
        Assert.IsFalse(rust.Contains('§'), "every token is filled");
        Assert.Contains("const SOURCE_SECRETS: &[(&str, &str)] = &[(\"stripe\", \"STRIPE_SECRET\"), (\"github\", \"GITHUB_SECRET\")];", rust);
        Assert.Contains("let (secret_name, source) = source_secret(source);", rust);
        Assert.Contains("std::env::var(secret_name)", rust);
        string rustShared = RustInboundWriter.File(none, plain, SqlDialect.Sqlite);
        Assert.Contains("std::env::var(SECRET_NAME)", rustShared);
        Assert.IsFalse(rustShared.Contains("source_secret"));

        string tests = ApiInboundTests.ClassText(tables[1], project);
        Assert.Contains("A_sender_with_its_own_secret_is_checked_against_that_secret_only", tests);
        Assert.Contains("\"/receive/STRIPE\"", tests);
        Assert.Contains("\"/receive/stripe-x\"", tests);
        Assert.IsFalse(ApiInboundTests.ClassText(none[0], plain).Contains("its_own_secret"));
        Assert.Contains("def test_a_sender_with_its_own_secret_is_checked_against_that_secret_only(api, monkeypatch):", PythonInboundTests.File(tables[1], database, project));
        Assert.Contains("async fn a_sender_with_its_own_secret_is_checked_against_that_secret_only()", RustInboundTests.File(tables[1], database, project));
        Assert.IsFalse(PythonInboundTests.File(none[0], database, plain).Contains("OWN_SECRET"));
        Assert.IsFalse(RustInboundTests.File(none[0], database, plain).Contains("OWN_SECRET"));
    }

    [TestMethod]
    public void A_sender_secret_that_is_not_valid_is_told_what_is_wrong()
    {
        var problems = InboundPlan.SourceSecretProblems(Project("WebhookEvent", new() { ["InboundSourceSecrets"] = "stripe,my sender:X_SECRET,github:GH SECRET,stripe:OTHER" }));
        Assert.HasCount(4, problems);
        StringAssert.Contains(problems[0], "stripe needs the environment variable");
        StringAssert.Contains(problems[1], "may hold only letters, digits, - and _");
        StringAssert.Contains(problems[2], "GH SECRET may hold only letters, digits and _");
        StringAssert.Contains(problems[3], "stripe is named twice");
        Assert.IsEmpty(InboundPlan.SourceSecretProblems(Project("WebhookEvent", new() { ["InboundSourceSecrets"] = "stripe:STRIPE_SECRET" })));
        Assert.IsEmpty(InboundPlan.SourceSecrets(Project("WebhookEvent", new() { ["InboundSourceSecrets"] = "stripe" })), "an entry that is not valid writes nothing");
    }

    [TestMethod]
    public async Task The_processed_button_tests_the_flag_or_the_stamp_of_the_row()
    {
        var database = await ReadAsync();
        var project = Project("WebhookEvent,ErrorReport:Payload");
        var tables = database.Tables.Select(t => TableShapes.For(t, project).Inbound).OfType<InboundShape>().OrderBy(t => t.Table.TableName).ToArray();
        var flag = tables[0];
        var stamp = tables[1];
        Assert.AreEqual("!row.isProcessed", InboundScreens.UnprocessedTs(flag, "row"));
        Assert.AreEqual("!row.processedOn", InboundScreens.UnprocessedTs(stamp, "row"));
        Assert.AreEqual("item.IsProcessed != true", InboundScreens.UnprocessedCs(flag, "item", "IsProcessed"));
        Assert.AreEqual("item.ProcessedOn == null", InboundScreens.UnprocessedCs(stamp, "item", "ProcessedOn"));
        Assert.AreEqual("(bool)true", InboundScreens.ProcessedValue(flag));
        Assert.AreEqual("(DateTime?)DateTime.Now", InboundScreens.ProcessedValue(stamp));

        string Member(string name, string value) => $"{name}: {value}";
        string waiting = InboundScreens.Row(flag, project, 7, false, Member), done = InboundScreens.Row(flag, project, 8, true, Member);
        Assert.Contains("errorReportId: 7", waiting);
        Assert.Contains("isProcessed: false", waiting);
        Assert.Contains("errorReportId: 8", done);
        Assert.Contains("isProcessed: true", done);
        Assert.Contains("processedOn: null", InboundScreens.Row(stamp, project, 7, false, Member));
        Assert.Contains("processedOn: \"2026-10-26T08:12:00\"", InboundScreens.Row(stamp, project, 8, true, Member));
        Assert.Contains("\"webhookEventId\": 7", InboundScreens.Row(stamp, project, 7, false, (name, value) => $"\"{name}\": {value}"));
        Assert.AreEqual("Failed to mark the webhook event processed.", InboundScreens.FailedText("webhook event"));
    }

    [TestMethod]
    public async Task An_inbound_table_that_is_editable_is_not_a_read_only_list()
    {
        var database = await ReadAsync();
        var project = Project("WebhookEvent,ErrorReport:Payload", new() { ["InboundEditTables"] = "WebhookEvent" });
        var webhook = database.Tables.Single(t => t.TableName == "WebhookEvent");
        var report = database.Tables.Single(t => t.TableName == "ErrorReport");
        Assert.IsNull(TableShapes.For(webhook, project).Log, "an editable table has the ordinary screens");
        Assert.IsNotNull(TableShapes.For(webhook, project).Inbound, "and keeps the inbound routes");
        Assert.IsNotNull(TableShapes.For(report, project).Log, "the other table is still a read-only list");
        Assert.IsEmpty(InboundPlan.EditProblems(project));
        var wrong = InboundPlan.EditProblems(Project("WebhookEvent", new() { ["InboundEditTables"] = "ErrorReport" }));
        Assert.HasCount(1, wrong);
        StringAssert.Contains(wrong[0], "ErrorReport must also be a table of InboundTables");
    }
}
