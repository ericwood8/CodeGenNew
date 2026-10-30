using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> LogTables: insert-only tables the application only reads, a newest-first list with a date range, no write routes and no Add, Edit, Clone or Delete. </summary>
[TestClass]
public class LogTableTests
{
    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_log_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        string[] statements =
        [
            "CREATE TABLE ErrorLog (ErrorLogId INTEGER PRIMARY KEY, LoggedOn DATETIME NOT NULL, Level VARCHAR(10) NOT NULL, UserName VARCHAR(50) NULL, Source VARCHAR(100) NULL, Message VARCHAR(500) NOT NULL)",
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
        string output = Path.Combine(Path.GetTempPath(), "codegen_log_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stacks, ["Dashboard"] = "false", ["AccessMode"] = "Ef",
                ["LogTables"] = "ErrorLog", ["ApiCsv"] = "true", ["ApiHttp"] = "true", ["ApiTests"] = "true", ["BlazorTests"] = "true", ["DesktopTests"] = "true"
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

    private static TableModel Table(params (string Name, System.Data.SqlDbType Type, bool Nullable)[] columns) =>
        Sample.Table("Entry", columns.Select((c, i) => Sample.Column(c.Name, c.Type, nullable: c.Nullable, primaryKey: i == 0, ordinal: i + 1)).ToList());

    [TestMethod]
    public void The_setting_names_a_table_and_optionally_its_timestamp_column()
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["LogTables"] = "ErrorLog, UserSession:StartedOn" });

        CollectionAssert.AreEqual(new[] { ("ErrorLog", (string?)null), ("UserSession", "StartedOn") }, LogTablePlan.Entries(project).ToArray());
        Assert.IsTrue(LogTablePlan.IsListed("usersession", project));
        Assert.IsFalse(LogTablePlan.IsListed("Customer", project));
    }

    [TestMethod]
    public void The_timestamp_is_the_date_and_time_column_whose_name_says_when_something_happened()
    {
        var table = Table(("EntryId", System.Data.SqlDbType.Int, false), ("Archived", System.Data.SqlDbType.DateTime, true), ("LoggedOn", System.Data.SqlDbType.DateTime, false), ("Message", System.Data.SqlDbType.NVarChar, false));

        Assert.AreEqual("LoggedOn", LogTablePlan.Guess(table)!.Name);
        Assert.IsNull(LogTablePlan.Guess(Table(("EntryId", System.Data.SqlDbType.Int, false), ("Day", System.Data.SqlDbType.Date, false))), "a date with no time is not a timestamp");
    }

    [TestMethod]
    public void A_table_that_looks_like_a_log_is_recognised_for_the_run_note_and_one_that_edits_itself_is_not()
    {
        var log = Table(("EntryId", System.Data.SqlDbType.Int, false), ("LoggedOn", System.Data.SqlDbType.DateTime, false), ("Level", System.Data.SqlDbType.NVarChar, false), ("Message", System.Data.SqlDbType.NVarChar, false));
        var edited = Table(("EntryId", System.Data.SqlDbType.Int, false), ("LoggedOn", System.Data.SqlDbType.DateTime, false), ("ModifiedOn", System.Data.SqlDbType.DateTime, true), ("Level", System.Data.SqlDbType.NVarChar, false), ("Message", System.Data.SqlDbType.NVarChar, false));

        Assert.IsTrue(LogTablePlan.Looks(log));
        Assert.IsFalse(LogTablePlan.Looks(Table(("EntryId", System.Data.SqlDbType.Int, false), ("Name", System.Data.SqlDbType.NVarChar, false))));
        Assert.IsTrue(edited.Columns.Any(c => c.Name == "ModifiedOn"));
    }

    [TestMethod]
    public void The_search_of_a_log_ends_with_the_date_range_and_lists_newest_first()
    {
        var table = Table(("EntryId", System.Data.SqlDbType.Int, false), ("LoggedOn", System.Data.SqlDbType.DateTime, false), ("Message", System.Data.SqlDbType.NVarChar, false));
        var plain = SearchPlan.For(table, []);
        var log = SearchPlan.For(table, [], new LogTable(table, table.Columns[1]));

        Assert.IsNull(plain.Range);
        Assert.IsFalse(plain.DefaultDescending);
        Assert.AreEqual("from", log.Range!.FromParameter);
        Assert.AreEqual("to", log.Range.ToParameter);
        Assert.IsTrue(log.DefaultDescending);
        CollectionAssert.AreEqual(new[] { "LoggedOn", "EntryId" }, log.DefaultOrder.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public async Task The_API_registers_only_reads_lists_newest_first_and_takes_a_date_range()
    {
        var (success, errors, read, files) = await GenerateAsync("Api");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Apis/ErrorLogApi.cs"), "protected override bool ReadOnly => true;");
        Expect.Contains(read("Apis/ErrorLogApi.cs"), "NewestFirst => c => c.LoggedOn;");
        Expect.DoesNotContain(read("Apis/CustomerApi.cs"), "ReadOnly");
        Expect.Contains(read("Apis/CrudApi.cs"), "protected virtual bool ReadOnly => false;");
        Expect.Contains(read("Apis/CrudApi.cs"), "if (!ReadOnly)");
        Expect.Contains(read("Apis/CrudApi.cs"), "private void RegisterWrites(");
        string query = read("Repositories/ErrorLogSearchQuery.cs");
        Expect.Contains(query, "DateTime? from = null, DateTime? to = null");
        Expect.Contains(query, "t.LoggedOn >= fromValue.Date");
        Expect.Contains(query, "t.LoggedOn < toEnd");
        Expect.Contains(query, "? query.OrderByDescending(t => t.LoggedOn).ThenByDescending(t => t.ErrorLogId)");
        Expect.Contains(read("Apis/ErrorLogSearchApi.cs"), "[FromQuery] DateTime? from,");
        Expect.Contains(read("Apis/ErrorLogSearchApi.cs"), ", from, to);");
        Expect.DoesNotContain(read("Apis/CustomerSearchApi.cs"), "DateTime? from");
        Assert.IsTrue(files.Any(f => f.EndsWith("Sql/LogIndexes.sql", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task The_export_takes_the_range_and_there_is_no_import_and_the_http_file_has_no_write_request()
    {
        var (success, errors, read, _) = await GenerateAsync("Api");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string csv = read("Apis/Csv.cs");
        Expect.Contains(csv, "MapLogTable<ErrorLog, ShopContext>(app, \"/api/errorlogs\"");
        Expect.Contains(csv, "ErrorLogSearchQuery.Filter(rows, from: from, to: to)");
        Expect.Contains(csv, "MapTable<Customer, ShopContext>");
        Expect.Contains(csv, "Take(CsvSupport.MaxRows)");
        string http = read("ErrorLog.http");
        Expect.Contains(http, "from=2026-01-01&to=2026-01-31");
        Expect.DoesNotContain(http, "POST ");
        Expect.DoesNotContain(http, "DELETE ");
        Expect.Contains(read("Customer.http"), "POST ");
    }

    [TestMethod]
    public async Task The_API_tests_of_a_log_table_do_not_change_rows_and_check_the_range()
    {
        var (success, errors, read, _) = await GenerateAsync("Api");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tests = read("ErrorLogApiTests.cs");
        Expect.Contains(tests, "No_route_changes_a_row_of_a_log_table");
        Expect.Contains(tests, "HttpStatusCode.MethodNotAllowed");
        Expect.Contains(tests, "A_date_range_in_the_far_future_lists_no_rows");
        Expect.Contains(tests, "A_date_that_is_not_a_date_is_refused");
        Expect.DoesNotContain(tests, "A_row_can_be_added_read_changed_and_removed");
        Expect.Contains(read("CustomerApiTests.cs"), "A_row_can_be_added_read_changed_and_removed");
    }

    [TestMethod]
    public async Task The_index_script_gives_the_log_an_index_on_its_timestamp_unless_it_has_one()
    {
        var (success, errors, read, _) = await GenerateAsync("Api");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Sql/LogIndexes.sql"), "CREATE INDEX IF NOT EXISTS \"IX_ErrorLog_LoggedOn\" ON \"ErrorLog\" (\"LoggedOn\" DESC);");
    }

    [TestMethod]
    public async Task React_writes_the_log_page_and_not_the_ordinary_page()
    {
        var (success, errors, read, files) = await GenerateAsync("Api,React");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string page = read("src/pages/ErrorLogPage.tsx");
        Expect.Contains(page, "export function ErrorLogPage()");
        Expect.Contains(page, "aria-label=\"From\"");
        Expect.Contains(page, "from: '', to: ''");
        Expect.DoesNotContain(page, "Add New");
        Expect.DoesNotContain(page, "Delete");
        Expect.DoesNotContain(page, ">Edit<");
        Expect.Contains(page, "onClick={() => setViewing(errorLog)}>View</button>");
        Expect.Contains(page, "{viewing && (");
        Expect.Contains(page, "<dd>{viewing.message}</dd>");
        Expect.DoesNotContain(page, "<form");
        Expect.Contains(page, "{show('LoggedOn') && <td>{errorLog.loggedOn.substring(0, 16).replace('T', ' ')}</td>}");
    }

    [TestMethod]
    public async Task Angular_writes_the_date_boxes_and_neither_a_form_nor_the_buttons_that_change_a_row()
    {
        var (success, errors, read, _) = await GenerateAsync("Api,Angular", new() { ["AngularVersion"] = "22" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string html = read("errorlog/errorlog.component.html");
        Expect.Contains(html, "aria-label=\"From\" [(ngModel)]=\"filters.from\"");
        Expect.Contains(html, "aria-label=\"To\" [(ngModel)]=\"filters.to\"");
        Expect.DoesNotContain(html, "Add New");
        Expect.DoesNotContain(html, "Delete</button>");
        Expect.DoesNotContain(html, "<form");
        Expect.Contains(html, "(click)=\"view(errorLog)\" class=\"btn-action\">View</button>");
        Expect.Contains(html, "<dialog #viewDialog class=\"form-container\"");
        Expect.Contains(html, "(viewing)");
        Expect.Contains(html, "<dd>{{ viewing.message }}</dd>");
        Expect.Contains(read("errorlog/errorlog.component.spec.ts"), "sends the first and the last day when the person searches");
        Expect.Contains(read("errorlog/errorlog.component.ts"), "override filters = { level: '', userName: '', source: '', from: '', to: '' };");
        Expect.Contains(read("customer/customer.component.html"), "Add New");
    }

    [TestMethod]
    public async Task Blazor_writes_the_date_boxes_and_neither_an_editor_nor_the_buttons_that_change_a_row()
    {
        var (success, errors, read, _) = await GenerateAsync("Api,Blazor");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string page = read("Pages/ErrorLogPage.razor");
        Expect.Contains(page, "aria-label=\"From\" value=\"@filterFrom\"");
        Expect.Contains(page, "[\"from\"] = filterFrom,");
        Expect.DoesNotContain(page, "Add New");
        Expect.DoesNotContain(page, "<EditForm");
        Expect.Contains(page, "@onclick=\"() => View(item)\">View</button>");
        Expect.Contains(page, "@if (viewing is not null)");
        Expect.Contains(page, "<dd>@viewing.Message</dd>");
        Expect.Contains(read("ErrorLogPageTests.cs"), "Has_no_button_that_adds_changes_or_deletes_a_row");
        Expect.Contains(read("Pages/CustomerPage.razor"), "Add New");
    }

    [TestMethod]
    public async Task Both_desktop_apps_write_a_list_with_two_day_boxes_and_no_buttons_that_change_a_row()
    {
        var uno = await GenerateAsync("Api,Uno");
        var winui3 = await GenerateAsync("Api,WinUI3");

        Assert.IsTrue(uno.Success && winui3.Success, string.Join(" | ", uno.Errors.Concat(winui3.Errors)));
        foreach (string xaml in new[] { uno.Read("Views/ErrorLogListPage.xaml"), winui3.Read("Views/ErrorLogListPage.xaml") })
        {
            Expect.Contains(xaml, "PlaceholderText=\"From (yyyy-MM-dd)\"");
            Expect.Contains(xaml, "PlaceholderText=\"To (yyyy-MM-dd)\"");
            Expect.DoesNotContain(xaml, "Content=\"Add New\"");
            Expect.Contains(xaml, "Click=\"OnViewClick\"");
            Expect.DoesNotContain(xaml, "Content=\"Edit\"");
            Expect.DoesNotContain(xaml, "Content=\"Delete\"");
            Expect.DoesNotContain(xaml, "OnEditClick");
        }
        foreach (var app in new[] { uno, winui3 })
        {
            string code = app.Read("Views/ErrorLogListPage.xaml.cs");
            Expect.Contains(code, "private async void OnViewClick(object sender, RoutedEventArgs e)");
            Expect.Contains(code, "\"Message: \" + Show(entity.Message)");
            Expect.DoesNotContain(code, "OnEditClick");
            Expect.DoesNotContain(code, "OnAddClick");
            Expect.DoesNotContain(code, "OnDeleteClick");
            Assert.IsFalse(app.Files.Any(f => f.Contains("ErrorLogDetail", StringComparison.Ordinal)), "a log table has no edit dialog or view model of one");
            Assert.IsTrue(app.Files.Any(f => f.Contains("CustomerDetail", StringComparison.Ordinal)));
        }
        Expect.Contains(uno.Read("Views/CustomerListPage.xaml.cs"), "OnEditClick");
        Expect.DoesNotContain(uno.Read("Views/CustomerListPage.xaml.cs"), "OnViewClick");
        Expect.Contains(uno.Read("Views/CustomerListPage.xaml"), "Content=\"Add New\"");
        string model = winui3.Read("ViewModels/ErrorLogListViewModel.cs");
        Expect.Contains(model, "from: ParseDay(FromFilter), to: ParseDay(ToFilter), ");
        Expect.Contains(model, "private static DateTime? ParseDay(string? text)");
        Expect.DoesNotContain(winui3.Read("ViewModels/CustomerListViewModel.cs"), "ParseDay");
        Expect.Contains(winui3.Read("App/Repositories/ErrorLogRepo.cs"), "DateTime? from = null, DateTime? to = null");
        Expect.Contains(winui3.Read("App/Repositories/ErrorLogRepo.cs"), "from: from, to: to");
        Expect.Contains(uno.Read("ViewModels/ErrorLogListViewModel.cs"), "[\"from\"] = FromFilter,");
    }

    [TestMethod]
    public async Task A_log_table_with_the_one_box_list_protocol_is_refused_with_a_reason()
    {
        var search = await GenerateAsync("Api", new() { ["ListProtocol"] = "Search" });

        Assert.IsFalse(search.Success);
        StringAssert.Contains(string.Join(" | ", search.Errors), "needs the Columns list protocol");
    }

    [TestMethod]
    public async Task A_log_table_served_as_OData_is_only_read()
    {
        var odata = await GenerateAsync("Api", new() { ["OData"] = "true", ["ODataTables"] = "ErrorLog,Customer" });

        Assert.IsTrue(odata.Success, string.Join(" | ", odata.Errors));
        string file = odata.Read("OData.cs");
        Expect.Contains(file, "MapSet<ErrorLog, int, ShopContext>(odata, lists, \"errorlogs\"");
        var lines = file.Split('\n');
        Assert.IsTrue(lines.Single(l => l.Contains("MapSet<ErrorLog,")).TrimEnd().EndsWith(", true);", StringComparison.Ordinal), "the log table is mapped read-only");
        Assert.IsFalse(lines.Single(l => l.Contains("MapSet<Customer,")).TrimEnd().EndsWith(", true);", StringComparison.Ordinal), "an ordinary table keeps its writes");
        Expect.Contains(file, "if (logOnly)");
    }

    [TestMethod]
    public async Task The_Python_API_registers_only_reads_takes_the_date_range_and_lists_newest_first()
    {
        var (success, errors, read, files) = await GenerateAsync("Python");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string log = read("app/routes/error_log.py");
        Expect.DoesNotContain(log, "@router.post");
        Expect.DoesNotContain(log, "@router.put");
        Expect.DoesNotContain(log, "@router.delete");
        Expect.Contains(log, "range_from: date | None = Query(None, alias=\"from\"),");
        Expect.Contains(log, "func.datetime(ErrorLog.logged_on) >= func.datetime(datetime.combine(range_from, time.min))");
        Expect.Contains(log, "func.datetime(ErrorLog.logged_on) < func.datetime(datetime.combine(range_to + timedelta(days=1), time.min))");
        Expect.Contains(log, "default_order = [ErrorLog.logged_on.desc(), ErrorLog.error_log_id.desc()]");
        Expect.Contains(read("app/routes/customer.py"), "@router.post");
        Expect.DoesNotContain(read("app/routes/customer.py"), "range_from");
        string csv = read("app/routes/csv_files.py");
        Expect.Contains(csv, "def register_log(spec: CsvTable)");
        Expect.Contains(csv, "\"logged_on\"),");
    }

    [TestMethod]
    public async Task The_Rust_API_registers_only_reads_takes_the_date_range_and_lists_newest_first()
    {
        var (success, errors, read, files) = await GenerateAsync("Rust", new() { ["RustTests"] = "true" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string routes = read("src/routes/error_log.rs");
        Expect.DoesNotContain(routes, "async fn create");
        Expect.DoesNotContain(routes, "async fn update");
        Expect.DoesNotContain(routes, "async fn delete");
        Expect.Contains(routes, ".route(\"/api/errorlogs\", get(get_all))");
        Expect.Contains(read("src/routes/customer.rs"), "async fn create");
        string repo = read("src/repos/error_log.rs");
        Expect.Contains(repo, "pub range_from: Option<NaiveDate>,");
        Expect.Contains(repo, "\"LoggedOn\" >= ");
        Expect.Contains(repo, "\"LoggedOn\" < ");
        Expect.Contains(repo, "ORDER BY");
        Expect.Contains(repo, "DESC");
        Expect.DoesNotContain(read("src/repos/customer.rs"), "range_from");
        string csv = read("src/routes/csv_files.rs");
        Expect.DoesNotContain(csv, "import_error_log");
        Expect.Contains(csv, "import_customer");
        string test = read("tests/api/error_log.rs");
        Expect.Contains(test, "StatusCode::METHOD_NOT_ALLOWED");
    }

    [TestMethod]
    public void A_menu_run_of_the_log_page_lists_a_table_that_looks_like_a_log_for_that_run_only()
    {
        var log = Sample.Table("AuditEvent",
        [
            Sample.Column("AuditEventId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("OccurredOn", System.Data.SqlDbType.DateTime, ordinal: 2),
            Sample.Column("Level", System.Data.SqlDbType.NVarChar, characters: 10, ordinal: 3),
            Sample.Column("Message", System.Data.SqlDbType.NVarChar, characters: 200, ordinal: 4)
        ]);
        var plain = Sample.Table("Customer",
        [
            Sample.Column("CustomerId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", System.Data.SqlDbType.NVarChar, characters: 50, ordinal: 2)
        ]);
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["LogTables"] = "ErrorLog" });

        CollectionAssert.AreEqual(new[] { "ErrorLog", "AuditEvent" }, LogTablePlan.ForMenuRun(project, "TSX_LogPage", log).LogTables);
        CollectionAssert.AreEqual(new[] { "ErrorLog", "AuditEvent" }, LogTablePlan.ForMenuRun(project, "SP_LogIndex", log).LogTables);
        Assert.IsNotNull(LogTablePlan.For(log, LogTablePlan.ForMenuRun(ProjectSettings.FromValues(new Dictionary<string, string> { ["AccessMode"] = "Ef" }), "TSX_LogPage", log)));
        CollectionAssert.AreEqual(new[] { "ErrorLog" }, LogTablePlan.ForMenuRun(project, "TSX_Page", log).LogTables, "any other template runs as the project says");
        CollectionAssert.AreEqual(new[] { "ErrorLog" }, LogTablePlan.ForMenuRun(project, "TSX_LogPage", plain).LogTables, "a table that does not look like a log is not listed on a guess");
        CollectionAssert.AreEqual(new[] { "ErrorLog" }, project.LogTables, "the project itself is not changed");
        Assert.AreEqual(1, LogTablePlan.ForMenuRun(ProjectSettings.FromValues(new Dictionary<string, string> { ["LogTables"] = "AuditEvent" }), "TSX_LogPage", log).LogTables.Length, "a listed table is not listed twice");
    }

    [TestMethod]
    public void The_run_notes_suggest_LogTables_for_a_table_that_looks_like_a_log()
    {
        var log = Sample.Table("AuditEvent",
        [
            Sample.Column("AuditEventId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("OccurredOn", System.Data.SqlDbType.DateTime, ordinal: 2),
            Sample.Column("Level", System.Data.SqlDbType.NVarChar, characters: 10, ordinal: 3),
            Sample.Column("Message", System.Data.SqlDbType.NVarChar, characters: 200, ordinal: 4)
        ]);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [log] };
        var plain = ProjectSettings.FromValues(new Dictionary<string, string> { ["Stacks"] = "React" });
        var listed = ProjectSettings.FromValues(new Dictionary<string, string> { ["Stacks"] = "React", ["LogTables"] = "AuditEvent" });

        Assert.IsTrue(TableShapeNotes.Of([], database, plain, ["React"]).Any(n => n.StartsWith("AuditEvent: looks like a log", StringComparison.Ordinal)));
        Assert.IsFalse(TableShapeNotes.Of([], database, listed, ["React"]).Any(n => n.Contains("looks like a log", StringComparison.Ordinal)));
    }
}
