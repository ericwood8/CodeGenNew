using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> QueueTables (work queues with claim, complete, fail and retry routes) and ArchiveTables (the script that moves old rows to <Table>Archive or deletes them). </summary>
[TestClass]
public class QueueArchiveTests
{
    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_queue_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        string[] statements =
        [
            "CREATE TABLE ErrorLog (ErrorLogId INTEGER PRIMARY KEY, LoggedOn DATETIME NOT NULL, Level VARCHAR(10) NOT NULL, UserName VARCHAR(50) NULL, Source VARCHAR(100) NULL, Message VARCHAR(500) NOT NULL)",
            "CREATE TABLE ErrorLogArchive (ErrorLogId INTEGER NOT NULL PRIMARY KEY, LoggedOn DATETIME NOT NULL, Level VARCHAR(10) NOT NULL, UserName VARCHAR(50) NULL, Source VARCHAR(100) NULL, Message VARCHAR(500) NOT NULL, ArchivedOn DATETIME NOT NULL)",
            "CREATE TABLE JobQueue (JobQueueId INTEGER PRIMARY KEY, JobType VARCHAR(40) NOT NULL, Payload VARCHAR(500) NULL, Status VARCHAR(10) NOT NULL DEFAULT 'pending', RetryCount INTEGER NOT NULL DEFAULT 0, CreatedOn DATETIME NOT NULL, ClaimedOn DATETIME NULL, ClaimedBy VARCHAR(50) NULL, ProcessedOn DATETIME NULL, LastError VARCHAR(500) NULL)",
            "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY, Name VARCHAR(60) NOT NULL, Email VARCHAR(80) NULL, City VARCHAR(40) NULL, Region VARCHAR(40) NULL, Notes VARCHAR(200) NULL)"
        ];
        foreach (string statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static async Task<(bool Success, List<string> Errors, Func<string, string> Read, List<string> Files)> GenerateAsync(string stacks, Dictionary<string, string>? extra = null)
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_queue_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stacks, ["Dashboard"] = "false", ["AccessMode"] = "Ef",
                ["QueueTables"] = "JobQueue", ["ArchiveTables"] = "ErrorLog:90,JobQueue:30:delete", ["ApiHttp"] = "true", ["ApiTests"] = "true", ["BlazorTests"] = "true"
            };
            foreach (var (key, value) in extra ?? new Dictionary<string, string>())
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = stacks.Split(","), OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            var contents = files.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(output, f)).Replace("\r\n", "\n"));
            return (report.Success, report.Errors.ToList(), name => contents[files.Single(f => f.EndsWith(name))], files);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    private static ColumnModel C(string name, SqlDbType type, bool nullable = false, bool primaryKey = false, int ordinal = 0, int? characters = null, bool identity = false) =>
        Sample.Column(name, type, nullable: nullable, primaryKey: primaryKey, ordinal: ordinal, characters: characters, identity: identity);

    private static TableModel Queue(SqlDialect dialect = SqlDialect.SqlServer, bool withProcessed = true, bool claimedNullable = true, int statusLength = 10) => Sample.Table("JobQueue",
        new List<ColumnModel>
        {
            C("JobQueueId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("JobType", SqlDbType.VarChar, ordinal: 2, characters: 40), C("Status", SqlDbType.VarChar, ordinal: 3, characters: statusLength),
            C("RetryCount", SqlDbType.Int, ordinal: 4), C("CreatedOn", SqlDbType.DateTime, ordinal: 5), C("ClaimedOn", SqlDbType.DateTime, nullable: claimedNullable, ordinal: 6), C("ClaimedBy", SqlDbType.VarChar, nullable: true, ordinal: 7, characters: 50),
        }.Concat(withProcessed ? [C("ProcessedOn", SqlDbType.DateTime, nullable: true, ordinal: 8), C("LastError", SqlDbType.VarChar, nullable: true, ordinal: 9, characters: 500)] : []).ToList(), dialect: dialect);

    private static TableModel Log(string name, SqlDialect dialect = SqlDialect.SqlServer, bool archivedOn = false, bool identity = true) => Sample.Table(name,
        new List<ColumnModel>
        {
            C("ErrorLogId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: identity), C("LoggedOn", SqlDbType.DateTime, ordinal: 2), C("Level", SqlDbType.VarChar, ordinal: 3, characters: 10),
            C("Source", SqlDbType.VarChar, nullable: true, ordinal: 4, characters: 100), C("Message", SqlDbType.VarChar, ordinal: 5, characters: 500)
        }.Concat(archivedOn ? [C("ArchivedOn", SqlDbType.DateTime, ordinal: 6)] : []).ToList(), dialect: dialect);

    private static DatabaseModel Database(SqlDialect dialect, params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = dialect == SqlDialect.Sqlite ? "main" : "dbo", Dialect = dialect, Tables = tables.ToList() };

    private static ProjectSettings Project(string queues = "JobQueue", string archives = "", Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string> { ["AccessMode"] = "Ef", ["DatabaseProvider"] = "SqlServer", ["QueueTables"] = queues, ["ArchiveTables"] = archives };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public void The_queue_setting_names_a_table_and_optionally_its_status_and_retry_columns()
    {
        var project = Project("JobQueue, Mail:State:Attempts");

        CollectionAssert.AreEqual(new[] { ("JobQueue", "Status", "RetryCount"), ("Mail", "State", "Attempts") }, QueuePlan.Entries(project).ToArray());
        Assert.AreEqual(ProjectSettings.DefaultQueueMaxRetries, project.QueueMaxRetries);
        Assert.AreEqual(ProjectSettings.DefaultQueueClaimMinutes, project.QueueClaimMinutes);
        Assert.AreEqual(7, Project(extra: new() { ["QueueMaxRetries"] = "7" }).QueueMaxRetries);
    }

    [TestMethod]
    public void The_plan_finds_the_stamps_by_their_names_and_a_queue_is_also_a_read_only_list()
    {
        var queue = QueuePlan.For(Queue(), Project())!;

        Assert.AreEqual("CreatedOn", queue.Created.Name);
        Assert.AreEqual("ClaimedOn", queue.Claimed.Name);
        Assert.AreEqual("ProcessedOn", queue.Processed.Name);
        Assert.AreEqual("ClaimedBy", queue.ClaimedBy!.Name);
        Assert.AreEqual("LastError", queue.LastError!.Name);
        Assert.IsNotNull(LogTablePlan.For(Queue(), Project()), "the rows are not edited by hand, so the list is read only");
    }

    [TestMethod]
    public void A_table_that_cannot_be_a_queue_says_what_it_lacks()
    {
        Assert.Contains("ProcessedOn", QueuePlan.Problem(Queue(withProcessed: false), Project())!);
        Assert.Contains("ClaimedOn to allow NULL", QueuePlan.Problem(Queue(claimedNullable: false), Project())!);
        Assert.Contains("at least 7 characters", QueuePlan.Problem(Queue(statusLength: 4), Project())!);
        Assert.Contains("State", QueuePlan.Problem(Queue(), Project("JobQueue:State"))!);
        Assert.IsNull(QueuePlan.Problem(Queue(), Project()));
        Assert.IsNull(QueuePlan.Problem(Log("ErrorLog"), Project()), "a table the project does not list is not asked");
    }

    [TestMethod]
    public void A_queue_the_application_cannot_serve_through_the_list_protocol_or_routines_is_refused_with_a_reason()
    {
        Assert.Contains("AccessMode=Ef", QueuePlan.Problem(Queue(), Project(extra: new() { ["AccessMode"] = "Routines" }))!);
        Assert.Contains("QueueTables", QueuePlan.Problem(Queue(), Project(extra: new() { ["ListProtocol"] = "Search" }))!);
    }

    [TestMethod]
    public void The_archive_setting_names_a_table_the_days_to_keep_and_optionally_delete()
    {
        var project = Project("", "ErrorLog:90, JobQueue:30:delete");

        CollectionAssert.AreEqual(new[] { ("ErrorLog", "90", false), ("JobQueue", "30", true) }, ArchivePlan.Entries(project).ToArray());
        CollectionAssert.AreEqual(new[] { "ErrorLogArchive" }, ArchivePlan.ArchiveTableNames(project).ToArray(), "a table that is only deleted from has no archive to show");
    }

    [TestMethod]
    public void An_archive_table_the_database_has_is_a_read_only_list_ordered_by_its_event_time()
    {
        var project = Project("", "ErrorLog:90");
        var archive = Log("ErrorLogArchive", archivedOn: true, identity: false);

        Assert.AreEqual("LoggedOn", LogTablePlan.For(archive, project)!.Timestamp.Name);
        Assert.IsNull(LogTablePlan.For(Log("ErrorLog"), project), "the table that is archived is still an ordinary table");
    }

    [TestMethod]
    public void The_archive_plan_measures_a_queue_by_when_it_was_processed_and_only_moves_finished_rows()
    {
        var database = Database(SqlDialect.SqlServer, Queue(), Log("ErrorLog"));
        var resolution = ArchivePlan.Resolve(database, Project("JobQueue", "JobQueue:30:delete,ErrorLog:90"));

        Assert.IsEmpty(resolution.Problems);
        var queueJob = resolution.Jobs.Single(j => j.Table.TableName == "JobQueue");
        Assert.AreEqual("ProcessedOn", queueJob.Age.Name);
        Assert.IsNotNull(queueJob.Queue);
        Assert.AreEqual("LoggedOn", resolution.Jobs.Single(j => j.Table.TableName == "ErrorLog").Age.Name);
        string script = ArchiveScript.Text(database, Project("JobQueue", "JobQueue:30:delete,ErrorLog:90"));
        Assert.Contains("[Status] = 'done'", script);
        Assert.DoesNotContain("[Status] = 'done'", script[script.IndexOf("ArchiveErrorLog", StringComparison.Ordinal)..script.IndexOf("ArchiveJobQueue", StringComparison.Ordinal)]);
    }

    [TestMethod]
    public void The_archive_plan_names_the_problem_of_each_table_it_cannot_script()
    {
        var database = Database(SqlDialect.SqlServer, Queue(), Log("ErrorLog"), Log("ErrorLogArchive"));

        Assert.Contains("is not a table", ArchivePlan.Resolve(database, Project("", "Missing:30")).Problems.Single());
        Assert.Contains("days of rows to keep", ArchivePlan.Resolve(database, Project("", "ErrorLog")).Problems.Single());
        Assert.Contains("days of rows to keep", ArchivePlan.Resolve(database, Project("", "ErrorLog:soon")).Problems.Single());
        Assert.Contains("identity column", ArchivePlan.Resolve(database, Project("", "ErrorLog:90")).Problems.Single(), "SQL Server cannot move rows into an identity column");
        Assert.Contains("lacks the column", ArchivePlan.Resolve(Database(SqlDialect.SqlServer, Log("ErrorLog"), Sample.Table("ErrorLogArchive", [C("ErrorLogId", SqlDbType.Int, primaryKey: true, ordinal: 1)])), Project("", "ErrorLog:90")).Problems.Single());
        Assert.IsEmpty(ArchivePlan.Resolve(Database(SqlDialect.PostgreSql, Log("ErrorLog", SqlDialect.PostgreSql), Log("ErrorLogArchive", SqlDialect.PostgreSql, archivedOn: true)), Project("", "ErrorLog:90")).Problems, "an identity key that takes a given value is fine elsewhere");
    }

    [TestMethod]
    public void The_script_of_each_database_moves_in_batches_and_the_scheduling_text_is_commented_out()
    {
        var project = Project("", "ErrorLog:90");
        string sqlServer = ArchiveScript.Text(Database(SqlDialect.SqlServer, Log("ErrorLog")), project);
        string postgres = ArchiveScript.Text(Database(SqlDialect.PostgreSql, Log("ErrorLog", SqlDialect.PostgreSql)), project);
        string mysql = ArchiveScript.Text(Database(SqlDialect.MySql, Log("ErrorLog", SqlDialect.MySql)), project);
        string sqlite = ArchiveScript.Text(Database(SqlDialect.Sqlite, Log("ErrorLog", SqlDialect.Sqlite)), project);

        Assert.Contains("CREATE TABLE [dbo].[ErrorLogArchive]", sqlServer, "the archive table is created when the database has none");
        Assert.Contains("DELETE TOP (@BatchSize) FROM [dbo].[ErrorLog]", sqlServer);
        Assert.Contains("OUTPUT deleted.[ErrorLogId]", sqlServer);
        Assert.Contains("/* Schedule it with an Agent job", sqlServer);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"dbo\".\"ErrorLogArchive\"", postgres);
        Assert.Contains("CREATE OR REPLACE PROCEDURE \"dbo\".\"ArchiveErrorLog\"", postgres);
        Assert.Contains("COMMIT;", postgres, "each batch commits so the log never holds a lock for long");
        Assert.Contains("-- Schedule it with pg_cron", postgres);
        Assert.Contains("DELIMITER $$", mysql);
        Assert.Contains("LIMIT p_batch", mysql);
        Assert.Contains("-- CREATE EVENT", mysql);
        Assert.Contains("datetime('now', 'localtime', '-90 days')", sqlite);
        Assert.Contains("run this file from the operating system's", sqlite);
    }

    [TestMethod]
    public void The_batch_size_and_the_time_of_day_are_settings_and_the_defaults_are_1000_and_two_in_the_morning()
    {
        var plain = Project("", "ErrorLog:90");
        var set = Project("", "ErrorLog:90", new() { ["ArchiveBatchSize"] = "250", ["ArchiveRunAt"] = "23:30" });
        string Script(SqlDialect dialect, ProjectSettings project) => ArchiveScript.Text(Database(dialect, Log("ErrorLog", dialect)), project);

        Assert.AreEqual(ProjectSettings.DefaultArchiveBatchSize, plain.ArchiveBatchSize);
        Assert.AreEqual(new TimeOnly(2, 0), plain.ArchiveRunTime);
        Assert.Contains("@BatchSize int = 1000", Script(SqlDialect.SqlServer, plain));
        Assert.Contains("@active_start_time = 020000", Script(SqlDialect.SqlServer, plain));
        Assert.Contains("@BatchSize int = 250", Script(SqlDialect.SqlServer, set));
        Assert.Contains("@active_start_time = 233000", Script(SqlDialect.SqlServer, set));
        Assert.Contains("p_batch integer DEFAULT 250", Script(SqlDialect.PostgreSql, set));
        Assert.Contains("'30 23 * * *'", Script(SqlDialect.PostgreSql, set));
        Assert.Contains("INTERVAL 23 HOUR + INTERVAL 30 MINUTE) DO CALL `ArchiveErrorLog`(90, 250)", Script(SqlDialect.MySql, set));
        Assert.Contains("250 rows at a time", Script(SqlDialect.MySql, set));
        Assert.IsNull(Project("", "ErrorLog:90", new() { ["ArchiveRunAt"] = "late" }).ArchiveRunTime);
        Assert.Contains("ArchiveRunAt", ArchivePlan.Resolve(Database(SqlDialect.SqlServer, Log("ErrorLog"), Log("ErrorLogArchive", archivedOn: true, identity: false)), Project("", "ErrorLog:90", new() { ["ArchiveRunAt"] = "25:00" })).Problems.Single());
    }

    [TestMethod]
    public void A_row_another_table_refers_to_stays_where_it_is_instead_of_failing_the_batch()
    {
        var child = new ChildForeignKeyModel
        {
            ConstraintName = "FK_Detail_ErrorLog", ReferencingSchema = "dbo", ReferencingTable = "ErrorDetail", ReferencingColumns = ["ErrorLogId"], ReferencedColumns = ["ErrorLogId"]
        };
        var withChild = Sample.Table("ErrorLog", Log("ErrorLog").Columns, childForeignKeys: [child]);
        var project = Project("", "ErrorLog:90");
        string sqlServer = ArchiveScript.Text(Database(SqlDialect.SqlServer, withChild), project);
        string postgres = ArchiveScript.Text(Database(SqlDialect.PostgreSql, Sample.Table("ErrorLog", Log("ErrorLog", SqlDialect.PostgreSql).Columns, childForeignKeys: [child], dialect: SqlDialect.PostgreSql)), project);
        string mysql = ArchiveScript.Text(Database(SqlDialect.MySql, Sample.Table("ErrorLog", Log("ErrorLog", SqlDialect.MySql).Columns, childForeignKeys: [child], dialect: SqlDialect.MySql)), project);
        string sqlite = ArchiveScript.Text(Database(SqlDialect.Sqlite, Sample.Table("ErrorLog", Log("ErrorLog", SqlDialect.Sqlite).Columns, childForeignKeys: [child], dialect: SqlDialect.Sqlite)), project);

        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM [dbo].[ErrorDetail] AS [refs] WHERE [refs].[ErrorLogId] = [ErrorLog].[ErrorLogId])", sqlServer);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM \"dbo\".\"ErrorDetail\" AS \"refs\" WHERE \"refs\".\"ErrorLogId\" = \"ErrorLog\".\"ErrorLogId\")", postgres);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM `ErrorDetail` AS `refs` WHERE `refs`.`ErrorLogId` = `ErrorLog`.`ErrorLogId`)", mysql);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM \"ErrorDetail\" AS \"refs\"", sqlite);
        Assert.DoesNotContain("NOT EXISTS", ArchiveScript.Text(Database(SqlDialect.SqlServer, Log("ErrorLog")), project), "a table nothing refers to has no check");
    }

    [TestMethod]
    public void Deleting_rows_writes_no_archive_table_and_no_insert()
    {
        string script = ArchiveScript.Text(Database(SqlDialect.PostgreSql, Log("ErrorLog", SqlDialect.PostgreSql)), Project("", "ErrorLog:30:delete"));

        Assert.DoesNotContain("Archive\"", script.Replace("ArchiveErrorLog", ""));
        Assert.DoesNotContain("INSERT INTO", script);
        Assert.Contains("DELETE FROM \"dbo\".\"ErrorLog\"", script);
    }

    [TestMethod]
    public async Task The_API_gets_the_queue_routes_and_the_table_itself_has_no_write_route()
    {
        var (success, errors, read, files) = await GenerateAsync("api");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string queue = read("Apis/Queue.cs");
        foreach (string route in new[] { "/stats", "/failed", "/claim", "/{id}/renew", "/{id}/complete", "/{id}/fail", "/{id}/retry" })
            Assert.Contains($"\"/api/jobqueues{route}\"", queue);
        Assert.Contains("public const int MaxRetries = 3;", queue);
        Assert.Contains("TimeSpan.FromMinutes(15)", queue);
        Assert.Contains("x.Status == \"pending\")\n", queue.Replace("\r", ""), "the claim is one UPDATE of the row that is still pending");
        Assert.Contains("ExecuteUpdateAsync", queue);
        Assert.Contains("QueueApi.Register(app);", read("Registration.cs"));
        Assert.Contains("ReadOnly => true", read("Apis/JobQueueApi.cs"));
        Assert.Contains("Sql/ArchiveJobs.sql", string.Join("|", files.Select(f => f.Replace("sql/", "Sql/"))));
        Assert.Contains("POST {{host}}/api/jobqueues/claim", read("JobQueue.http"));
        Assert.Contains("POST {{host}}/api/jobqueues/{{id}}/renew", read("JobQueue.http"));
    }

    [TestMethod]
    public async Task A_producer_puts_a_row_in_the_queue_with_a_POST_that_sets_the_queues_own_columns()
    {
        var (success, errors, read, _) = await GenerateAsync("api");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string queue = read("Apis/Queue.cs");
        Assert.Contains("app.MapPost(\"/api/jobqueues\", EnqueueAsync)", queue);
        foreach (string line in new[] { "row.JobQueueId = default;", "row.Status = \"pending\";", "row.RetryCount = 0;", "row.CreatedOn = DateTime.Now;", "row.ClaimedOn = null;", "row.ProcessedOn = null;", "row.ClaimedBy = null;", "row.LastError = null;" })
            Assert.Contains(line, queue);
        Assert.Contains("catch (DbUpdateException)", queue);
        string http = read("JobQueue.http");
        Assert.Contains("### A producer puts a row in the queue", http);
        Assert.Contains("\"jobType\"", http.Substring(http.IndexOf("A producer puts a row", StringComparison.Ordinal), 300));
        Assert.DoesNotContain("\"status\"", http.Substring(http.IndexOf("A producer puts a row", StringComparison.Ordinal), 300), "the queue sets the status itself");
    }

    [TestMethod]
    public async Task The_settings_set_the_retries_and_the_claim_time_of_the_routes()
    {
        var (success, errors, read, _) = await GenerateAsync("api", new() { ["QueueMaxRetries"] = "5", ["QueueClaimMinutes"] = "2", ["ApiAuth"] = "false" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string queue = read("Apis/Queue.cs");
        Assert.Contains("public const int MaxRetries = 5;", queue);
        Assert.Contains("TimeSpan.FromMinutes(2)", queue);
        Assert.DoesNotContain("RequireAuthorization", queue);
    }

    [TestMethod]
    public async Task The_API_tests_of_a_queue_claim_complete_fail_and_retry()
    {
        var (success, errors, read, _) = await GenerateAsync("api");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tests = read("JobQueueApiTests.cs");
        Assert.DoesNotContain("Inconclusive", tests, "every test puts its own row in the queue");
        foreach (string name in new[] { "A_row_put_in_the_queue_is_pending", "A_renewed_claim_starts_again", "The_stats_count_every_status", "A_claimed_row_is_completed_once", "Two_claims_never_return_the_same_row", "A_failed_row_counts_a_retry", "A_row_that_is_not_failed_cannot_be_retried", "to_every_queue_route" })
            Assert.Contains(name, tests);
        Assert.Contains("No_route_changes_a_row_of_a_log_table", tests, "the table itself stays read only");
        Assert.DoesNotContain("there is no POST", tests, "the queue table takes the enqueue POST");
    }

    [TestMethod]
    public async Task React_shows_the_counts_and_a_Retry_button_on_a_failed_row()
    {
        var (success, errors, read, _) = await GenerateAsync("api,react");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string page = read("pages/JobQueuePage.tsx");
        Assert.Contains("getQueueStats()", page);
        Assert.Contains("Pending: {stats.pending}", page);
        Assert.Contains("=== 'failed' && <button", page);
        Assert.Contains("retry: (id: number)", read("api/jobqueueApi.ts"));
        Assert.Contains("puts a failed row back to pending with Retry", read("pages/__tests__/JobQueuePage.test.tsx"));
    }

    [TestMethod]
    public async Task Angular_shows_the_counts_and_a_Retry_button_on_a_failed_row()
    {
        var (success, errors, read, _) = await GenerateAsync("api,angular", new() { ["AngularVersion"] = "22" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.Contains("class=\"queue-summary\"", read("jobqueue/jobqueue.component.html"));
        Assert.Contains("Retry</button>", read("jobqueue/jobqueue.component.html"));
        Assert.Contains("getQueueStats(): Observable<QueueStats>", read("jobqueue.service.ts"));
        Assert.Contains("retry(id: number)", read("jobqueue/jobqueue.component.ts"));
        Assert.Contains("puts a failed row back to pending with Retry", read("jobqueue/jobqueue.component.spec.ts"));
    }

    [TestMethod]
    public async Task Blazor_shows_the_counts_and_a_Retry_button_on_a_failed_row()
    {
        var (success, errors, read, _) = await GenerateAsync("api,blazor");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.Contains("class=\"queue-summary\"", read("Pages/JobQueuePage.razor"));
        Assert.Contains("RetryAsync(item)", read("Pages/JobQueuePage.razor"));
        Assert.Contains("GetQueueStatsAsync()", read("Services/JobQueueClient.cs"));
        Assert.Contains("Retry_puts_a_failed_row_back_to_pending", read("JobQueuePageTests.cs"));
    }

    [TestMethod]
    public async Task Python_gets_the_queue_routes_before_the_tables_routes_and_pytest_tests_for_them()
    {
        var (success, errors, read, files) = await GenerateAsync("python", new() { ["PythonTests"] = "true" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string queue = read("app/routes/queue.py");
        foreach (string route in new[] { "/stats", "/failed", "/claim", "/{id}/renew", "/{id}/complete", "/{id}/fail", "/{id}/retry" })
            Assert.Contains($"spec.route + \"{route}\"", queue);
        Assert.Contains("MAX_RETRIES = 3", queue);
        Assert.Contains("ordered_values", queue, "the retry count is written last");
        Assert.Contains(QueueWords.NotClaimed, queue);
        Assert.Contains("@router.post(spec.route, response_model=spec.schema, status_code=201", queue, "a producer puts a row in the queue");
        Assert.Contains("names={\"status\": \"status\", \"retry\": \"retryCount\", \"created\": \"createdOn\"", queue);
        Assert.DoesNotContain("@router.post", read("app/routes/job_queue.py"), "the table itself writes nothing");
        Assert.Contains("def test_an_enqueued_row_is_pending", read("tests/test_queue.py"));
        Assert.IsTrue(read("app/routes/__init__.py").IndexOf("queue", StringComparison.Ordinal) < read("app/routes/__init__.py").IndexOf("job_queue", StringComparison.Ordinal), "a table's /{id} would read stats as a key");
        Assert.Contains("def test_a_claim_takes_the_oldest_pending_rows_in_turn", read("tests/test_queue.py"));
        Assert.Contains("def test_a_renewed_claim_is_not_released", read("tests/test_queue.py"));
        Assert.IsTrue(files.Any(f => f.EndsWith("tests/conftest.py")));
    }

    [TestMethod]
    public async Task Rust_gets_the_queue_routes_a_409_of_its_own_and_integration_tests_for_them()
    {
        var (success, errors, read, _) = await GenerateAsync("rust", new() { ["RustTests"] = "true" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string queue = read("src/routes/queue.rs");
        foreach (string route in new[] { "/stats", "/failed", "/claim", "/{id}/renew", "/{id}/complete", "/{id}/fail", "/{id}/retry" })
            Assert.Contains($"\"/api/jobqueues{route}\"", queue);
        Assert.Contains("enum QueueError", queue, "ApiError has no problem form without sign-in");
        Assert.Contains(".route(\"/api/jobqueues\", post(enqueue_job_queue))", queue, "a producer puts a row in the queue");
        Assert.Contains("object.insert(\"status\".to_string(), serde_json::json!(\"\"));", queue);
        Assert.Contains("row.status = \"pending\".to_string();", queue);
        Assert.DoesNotContain("async fn create", read("src/routes/job_queue.rs"), "the table itself writes nothing");
        Assert.Contains("async fn an_enqueued_row_is_pending", read("tests/api/queue_routes.rs"));
        Assert.DoesNotContain("METHOD_NOT_ALLOWED, \"no route adds a row\"", read("tests/api/job_queue.rs"), "a POST to a queue is the enqueue");
        Assert.Contains("\"RetryCount\" = \"RetryCount\" + 1 WHERE", queue, "the retry count is the last assignment of the UPDATE");
        Assert.Contains(".merge(queue::router())", read("src/routes/mod.rs"));
        Assert.Contains("mod queue_routes;", read("tests/api/main.rs"));
        Assert.Contains("async fn a_claim_takes_the_oldest_pending_rows_in_turn", read("tests/api/queue_routes.rs"));
        Assert.Contains("async fn a_renewed_claim_is_not_released", read("tests/api/queue_routes.rs"));
    }

    [TestMethod]
    public async Task Uno_and_WinUI3_show_the_counts_and_a_Retry_button_on_a_failed_row()
    {
        var uno = await GenerateAsync("uno");
        var winui3 = await GenerateAsync("winui3");

        Assert.IsTrue(uno.Success && winui3.Success, string.Join(" | ", uno.Errors.Concat(winui3.Errors)));
        foreach (var app in new[] { uno, winui3 })
        {
            string xaml = app.Read("Views/JobQueueListPage.xaml");
            Assert.Contains("Text=\"{x:Bind ViewModel.QueueSummary, Mode=OneWay}\"", xaml);
            Assert.Contains("Click=\"OnRetryClick\" Visibility=\"{x:Bind RetryVisibility}\"", xaml);
            Assert.DoesNotContain("OnEditClick", xaml);
            Assert.Contains("private async void OnRetryClick", app.Read("Views/JobQueueListPage.xaml.cs"));
            string model = app.Read("ViewModels/JobQueueListViewModel.cs");
            Assert.Contains("RetryVisibility = e.Status == \"failed\" ? Visibility.Visible : Visibility.Collapsed", model);
            Assert.Contains("public async Task RetryAsync(int id)", model);
            Assert.Contains("private async Task LoadStatsAsync()", model);
            Assert.DoesNotContain("QueueSummary", app.Read("Views/ErrorLogListPage.xaml"), "only a queue has counts");
        }
        Assert.Contains("await Client.GetQueueStatsAsync()", uno.Read("ViewModels/JobQueueListViewModel.cs"));
        Assert.Contains("ExecuteUpdateAsync", winui3.Read("ViewModels/JobQueueListViewModel.cs"));
    }

    [TestMethod]
    public void The_queue_setting_adds_the_templates_of_every_stack_that_has_queue_routes()
    {
        var names = Project(extra: new() { ["PythonTests"] = "true", ["RustTests"] = "true" }).ImpliedPlanTemplates.ToList();

        foreach (string template in new[] { "API_Queue", "PY_Queue", "RS_Queue", "PY_TestQueue", "RS_TestQueue" })
            CollectionAssert.Contains(names, template);
        CollectionAssert.DoesNotContain(Project("").ImpliedPlanTemplates.ToList(), "PY_Queue");
    }

    [TestMethod]
    public async Task A_queue_the_setting_cannot_serve_fails_the_run_with_the_reason()
    {
        var (_, errors, _, _) = await GenerateAsync("api", new() { ["QueueTables"] = "JobQueue:Missing" });

        Assert.IsTrue(errors.Any(e => e.Contains("Missing")), string.Join(" | ", errors));
    }

    [TestMethod]
    public void The_new_settings_are_on_a_tab_and_have_a_hint()
    {
        foreach (string key in new[] { "QueueTables", "QueueMaxRetries", "QueueClaimMinutes", "ArchiveTables", "ArchiveBatchSize", "ArchiveRunAt" })
        {
            Assert.AreEqual("Table shapes", ProjectSettingGroups.TabOf(key));
            Assert.IsTrue(ProjectSettingsHints.All.ContainsKey(key), key);
        }
    }
}
