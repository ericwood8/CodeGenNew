using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The dashboard: which widgets the schema's own facts give, the statements that fill them (run for real on a SQLite file and checked against the rows added up by hand), and the files written. </summary>
[TestClass]
public class DashboardTests
{
    private static readonly string[] Schema =
    [
        "CREATE TABLE Status (StatusId INTEGER PRIMARY KEY, Description VARCHAR(40) NOT NULL)",
        """
        CREATE TABLE Customer (
            CustomerId INTEGER PRIMARY KEY, CustomerName VARCHAR(50) NOT NULL, StatusId INTEGER NOT NULL REFERENCES Status (StatusId),
            PaymentTerms VARCHAR(20) NOT NULL CHECK (PaymentTerms IN ('Net30', 'Net60', 'Cash')),
            IsActive BOOLEAN NOT NULL DEFAULT 1, InactiveDate DATETIME NULL, DateAdded DATETIME NOT NULL)
        """,
        """
        CREATE TABLE Invoice (
            InvoiceId INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL REFERENCES Customer (CustomerId), InvoiceNumber VARCHAR(20) NOT NULL,
            InvoiceDate DATETIME NOT NULL, TotalAmount DECIMAL(19,4) NOT NULL)
        """,
        "CREATE TABLE InvoiceLine (LineId INTEGER PRIMARY KEY, InvoiceId INTEGER NOT NULL REFERENCES Invoice (InvoiceId), Description VARCHAR(50) NOT NULL, ExtendedAmount DECIMAL(18,2) NOT NULL)",
        "CREATE TABLE Promotion (PromotionId INTEGER PRIMARY KEY, PromotionName VARCHAR(50) NOT NULL, StartDate DATETIME NOT NULL, EndDate DATETIME NULL)",
        "INSERT INTO Status VALUES (1, 'Good'), (2, 'Bad'), (3, 'Hold')",
        """
        INSERT INTO Customer VALUES
            (1, 'Acme', 1, 'Net30', 1, NULL, '2026-01-01'), (2, 'Bolt', 1, 'Net30', 1, NULL, '2026-02-01'), (3, 'Cog', 2, 'Net30', 1, NULL, '2026-03-01'),
            (4, 'Dyno', 2, 'Net60', 0, '2026-04-01', '2026-04-01'), (5, 'Elan', 3, 'Cash', 0, '2026-05-01', '2026-05-01')
        """,
        """
        INSERT INTO Invoice VALUES
            (1, 1, 'A-1', '2026-01-15', 100.50), (2, 1, 'A-2', '2026-02-10', 200), (3, 2, 'B-1', '2026-02-20', 50.25), (4, 3, 'C-1', '2026-03-05', 400)
        """,
        "INSERT INTO InvoiceLine VALUES (1, 1, 'x', 10), (2, 1, 'y', 20), (3, 2, 'z', 30), (4, 3, 'w', 40), (5, 4, 'v', 50), (6, 4, 'u', 60)",
        "INSERT INTO Promotion VALUES (1, 'Now', '2000-01-01', '2999-01-01'), (2, 'Past', '2000-01-01', '2001-01-01'), (3, 'Later', '2999-01-01', NULL)"
    ];

    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_dashboard_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = Open(path);
        foreach (string statement in Schema)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static async Task<(ISchemaProvider Provider, DatabaseModel Database)> ReadAsync(string path)
    {
        var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = path };
        var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
        return (provider, await provider.BuildAsync(path, "main"));
    }

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Shop")));

    private static Dictionary<string, double?> Points(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, double?>();
        while (reader.Read())
            result[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetDouble(1);
        return result;
    }

    private static List<(string Label, string Detail)> Rows(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var result = new List<(string, string)>();
        while (reader.Read())
            result.Add((reader.GetString(0), reader.GetString(1)));
        return result;
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static DashboardWidget Find(DashboardPlan plan, DashboardWidgetKind kind, string table, string? titlePart = null)
    {
        var found = plan.Candidates.Where(w => w.Kind == kind && w.Table == table && (titlePart is null || w.Title.Contains(titlePart))).ToList();
        if (found.Count != 1)
            Assert.Fail($"{kind} on {table} {titlePart}: found {found.Count} of " + string.Join(" | ", plan.Candidates.Select(w => $"{w.Kind}:{w.Table}:{w.Title}")));
        return found[0];
    }

    // ------------------------------------------------------------------ the statements, run for real

    [TestMethod]
    public async Task Every_statement_runs_on_sqlite_and_gives_the_numbers_the_rows_add_up_to()
    {
        string path = CreateDatabase();
        try
        {
            var (_, database) = await ReadAsync(path);
            var plan = DashboardPlan.Build(database, Project(("Screens", "Customer,Invoice,InvoiceLine,Promotion")));

            var money = Points(path, Find(plan, DashboardWidgetKind.Money, "Invoice").Sql);
            Assert.AreEqual(750.75, money["Total"]!.Value, 1e-9);
            Assert.AreEqual(187.6875, money["Average"]!.Value, 1e-9);
            Assert.AreEqual(400, money["Largest"]!.Value, 1e-9);

            var top = Points(path, Find(plan, DashboardWidgetKind.TopBy, "Invoice").Sql);
            CollectionAssert.AreEqual(new[] { "Cog", "Acme", "Bolt" }, top.Keys.ToArray(), "the largest sum first");
            Assert.AreEqual(300.5, top["Acme"]!.Value, 1e-9);

            var terms = Points(path, Find(plan, DashboardWidgetKind.Breakdown, "Customer", "Payment Terms").Sql);
            Assert.AreEqual("Net30", terms.Keys.First());
            Assert.AreEqual(3, terms["Net30"]);
            Assert.AreEqual(1, terms["Net60"]);
            Assert.AreEqual(1, terms["Cash"]);

            var status = Points(path, Find(plan, DashboardWidgetKind.Breakdown, "Customer", "Status").Sql);
            Assert.AreEqual(2, status["Good"]);
            Assert.AreEqual(2, status["Bad"]);
            Assert.AreEqual(1, status["Hold"]);

            var trend = Points(path, Find(plan, DashboardWidgetKind.Trend, "Invoice").Sql);
            CollectionAssert.AreEqual(new[] { "2026-01-01", "2026-02-01", "2026-03-01" }, trend.Keys.ToArray());
            Assert.AreEqual(100.5, trend["2026-01-01"]!.Value, 1e-9);
            Assert.AreEqual(250.25, trend["2026-02-01"]!.Value, 1e-9);

            var ratio = Points(path, Find(plan, DashboardWidgetKind.Ratio, "Customer").Sql);
            Assert.AreEqual(3, ratio["Active"]);
            Assert.AreEqual(2, ratio["Inactive"]);

            var promotions = Points(path, Find(plan, DashboardWidgetKind.Status, "Promotion").Sql);
            Assert.AreEqual(1, promotions["Current"]);
            Assert.AreEqual(1, promotions["Expired"]);
            Assert.AreEqual(1, promotions["Not started"]);

            var children = Points(path, Find(plan, DashboardWidgetKind.ChildCount, "Invoice").Sql);
            Assert.AreEqual(4, children["Invoices"]);
            Assert.AreEqual(6, children["Invoice Lines"]);

            var recent = Rows(path, Find(plan, DashboardWidgetKind.Recent, "Customer").Sql);
            CollectionAssert.AreEqual(new[] { "Elan", "Dyno", "Cog", "Bolt", "Acme" }, recent.Select(r => r.Label).ToArray(), "the newest first");
            Assert.AreEqual("2026-05-01", recent[0].Detail);

            foreach (var widget in plan.Candidates)
                if (widget.IsRows)
                    Rows(path, widget.Sql);
                else
                    Points(path, widget.Sql);
        }
        finally
        {
            Delete(path);
        }
    }

    // ------------------------------------------------------------------ the choice

    [TestMethod]
    public async Task The_page_keeps_to_the_caps_and_never_shows_more_than_two_money_cards()
    {
        string path = CreateDatabase();
        try
        {
            var (_, database) = await ReadAsync(path);
            var plan = DashboardPlan.Build(database, Project(("Screens", "Customer,Invoice,InvoiceLine,Promotion")));

            Assert.IsLessThanOrEqualTo(DashboardPlan.MaxCards, plan.Widgets.Count(w => w.IsCard));
            Assert.IsLessThanOrEqualTo(DashboardPlan.MaxMoneyCards, plan.Widgets.Count(w => w.Kind == DashboardWidgetKind.Money));
            Assert.IsLessThanOrEqualTo(DashboardPlan.MaxBreakdowns, plan.Widgets.Count(w => w.Kind == DashboardWidgetKind.Breakdown));
            Assert.IsLessThanOrEqualTo(DashboardPlan.MaxTrends, plan.Widgets.Count(w => w.Kind == DashboardWidgetKind.Trend));
            Assert.IsLessThanOrEqualTo(DashboardPlan.MaxRecent, plan.Widgets.Count(w => w.Kind == DashboardWidgetKind.Recent));
            Assert.IsGreaterThan(plan.Widgets.Count, plan.Candidates.Count, "the caps left something out");
            Assert.AreEqual(plan.Widgets.Count, plan.Widgets.Select(w => w.Id).Distinct().Count(), "ids are unique");

            var kinds = plan.Widgets.Select(w => w.IsCard ? 0 : w.Kind is DashboardWidgetKind.TopBy or DashboardWidgetKind.Breakdown ? 1 : w.Kind == DashboardWidgetKind.Trend ? 2 : 3).ToList();
            CollectionAssert.AreEqual(kinds.OrderBy(k => k).ToList(), kinds, "cards, then rankings, then trends, then the recent list");
        }
        finally
        {
            Delete(path);
        }
    }

    [TestMethod]
    public async Task A_lookup_table_gets_no_screen_widgets_and_a_money_column_that_is_a_limit_or_a_rate_is_not_a_measure()
    {
        string path = CreateDatabase();
        try
        {
            var (_, database) = await ReadAsync(path);
            var plan = DashboardPlan.Build(database, Project());

            Assert.IsFalse(plan.Candidates.Any(w => w.Table == "Status"), "a lookup of three rows has no screen of its own");
            Assert.IsTrue(plan.Candidates.Any(w => w.Table == "Invoice" && w.Kind == DashboardWidgetKind.Money));
        }
        finally
        {
            Delete(path);
        }
    }

    [TestMethod]
    public async Task A_table_can_be_left_out_and_a_measure_added_or_refused()
    {
        string path = CreateDatabase();
        try
        {
            var (_, database) = await ReadAsync(path);

            var without = DashboardPlan.Build(database, Project(("Screens", "Customer,Invoice,Promotion"), ("NoDashboardTables", "Promotion")));
            Assert.IsFalse(without.Candidates.Any(w => w.Table == "Promotion"));

            var measured = DashboardPlan.Build(database, Project(("Screens", "Customer,Invoice"),
                ("DashboardMeasures", "Invoice.TotalAmount:max,Invoice.TotalAmount:sum:InvoiceDate:month")));
            Assert.IsEmpty(measured.Problems, string.Join(" | ", measured.Problems));
            var kinds = measured.Widgets.Where(w => w.Id.Contains("measure")).ToList();
            Assert.HasCount(2, kinds);
            Assert.AreEqual(400, Points(path, kinds.Single(w => w.Kind == DashboardWidgetKind.Money).Sql)["Max"]!.Value, 1e-9);
            Assert.HasCount(3, Points(path, kinds.Single(w => w.Kind == DashboardWidgetKind.Trend).Sql));
            Assert.AreEqual(1, measured.Widgets.Count(w => w.Table == "Invoice" && w.Kind == DashboardWidgetKind.Money), "the measure replaces the automatic widget of its kind");

            var refused = DashboardPlan.Build(database, Project(("DashboardMeasures", "Invoice.Nope:sum,Invoice.TotalAmount:median")));
            Assert.HasCount(2, refused.Problems);
        }
        finally
        {
            Delete(path);
        }
    }

    [TestMethod]
    public void A_measure_is_read_from_its_text()
    {
        Assert.AreEqual(new DashboardMeasure("Invoice", "TotalAmount", "sum", null, null), DashboardPlan.ParseMeasure("Invoice.TotalAmount:sum"));
        Assert.AreEqual(new DashboardMeasure("Invoice", "TotalAmount", "avg", "InvoiceDate", DateBucket.Year), DashboardPlan.ParseMeasure("Invoice.TotalAmount:AVG:InvoiceDate:year"));
        foreach (string bad in new[] { "", "Invoice:sum", "Invoice.TotalAmount", "Invoice.TotalAmount:sum:InvoiceDate", "Invoice.TotalAmount:sum:InvoiceDate:week", "a.b.c:sum" })
            Assert.IsNull(DashboardPlan.ParseMeasure(bad), bad);
    }

    // ------------------------------------------------------------------ one statement per database

    private static DatabaseModel Typed(SqlDialect dialect)
    {
        var invoice = Sample.Table("Invoice",
        [
            Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2),
            Sample.Column("InvoiceDate", SqlDbType.DateTime, ordinal: 3),
            Sample.Column("TotalAmount", SqlDbType.Money, currency: true, ordinal: 4)
        ], [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "CustomerName")], dialect: dialect);
        var customer = Sample.Table("Customer",
        [
            Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("CustomerName", SqlDbType.VarChar, characters: 50, ordinal: 2)
        ], dialect: dialect);
        return new DatabaseModel { DatabaseName = "Shop", SchemaName = invoice.SchemaName, Dialect = dialect, Tables = [invoice, customer] };
    }

    [TestMethod]
    [DataRow(SqlDialect.SqlServer, "AS [Label]", "OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY", "DATEADD(month, -11,", "CAST(SUM([TotalAmount]) AS float)")]
    [DataRow(SqlDialect.PostgreSql, "AS \"Label\"", "LIMIT 10 OFFSET 0", "interval '11 months'", "CAST(SUM(CAST(\"TotalAmount\" AS numeric)) AS double precision)")]
    [DataRow(SqlDialect.MySql, "AS `Label`", "LIMIT 10 OFFSET 0", "INTERVAL 11 MONTH", "CAST(SUM(`TotalAmount`) AS DOUBLE)")]
    [DataRow(SqlDialect.Sqlite, "AS \"Label\"", "LIMIT 10 OFFSET 0", "'-11 months'", "CAST(SUM(\"TotalAmount\") AS REAL)")]
    public void Each_database_gets_its_own_quotes_paging_date_arithmetic_and_number_cast(SqlDialect dialect, string alias, string paging, string months, string sum)
    {
        var plan = DashboardPlan.Build(Typed(dialect), Project(("Screens", "Invoice,Customer")));
        string all = string.Join("\n", plan.Candidates.Select(w => w.Sql));

        Expect.Contains(all, alias);
        Expect.Contains(all, paging);
        Expect.Contains(all, months);
        Expect.Contains(all, sum);
        Assert.IsFalse(plan.Candidates.Any(w => w.Sql.Contains("--") || w.Sql.Contains(';')), "one fixed statement, no comment, no second statement");
    }

    // ------------------------------------------------------------------ the files

    [TestMethod]
    public async Task Dashboard_true_writes_the_queries_the_endpoint_the_page_and_the_strip_of_every_stack_and_false_writes_none()
    {
        string path = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_dashboard_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var (provider, _) = await ReadAsync(path);
            var settings = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api,WinUI3,React,Angular",
                ["OutputApi"] = "Api", ["OutputWinUI3"] = "App", ["OutputReact"] = "web", ["OutputAngular"] = "ng", ["Screens"] = "Customer,Invoice,InvoiceLine,Promotion",
                ["Dashboard"] = "true", ["DashboardStrip"] = "true", ["ProjectDocs"] = "false"
            };
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(settings), Stacks = ["Api", "WinUI3", "React", "Angular"], OutputDirectory = output, DatabaseName = path, Schema = "main"
            });

            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            foreach (string expected in new[]
            {
                "Api/Repositories/DashboardData.cs", "Api/Apis/DashboardApi.cs", "Api/docs/Dashboard.md", "sql/DashboardQueries.sql",
                "App/Repositories/DashboardData.cs", "App/Views/DashboardPage.cs", "App/Views/DashboardWidgetViews.cs", "App/Views/DashboardStrip.cs",
                "web/src/api/dashboardApi.ts", "web/src/pages/DashboardPage.tsx", "web/src/components/DashboardWidgetView.tsx", "web/src/components/DashboardStrip.tsx",
                "ng/src/app/services/dashboard.service.ts", "ng/src/app/components/dashboard/dashboard.component.ts", "ng/src/app/components/dashboard-widget/dashboard-widget.component.html",
                "ng/src/app/components/dashboard-strip/dashboard-strip.component.ts", "ng/src/app/components/screen-with-strip/screen-with-strip.component.ts"
            })
                CollectionAssert.Contains(files, expected);

            string Read(string relative) => File.ReadAllText(Path.Combine(output, relative)).Replace("\r\n", "\n");
            Expect.Contains(Read("Api/Apis/ApiRegistration.cs"), "DashboardApi.Register(app);");
            Expect.Contains(Read("Api/Apis/DashboardApi.cs"), "app.MapGet(\"/api/dashboard\", Get)");
            Expect.Contains(Read("Api/Repositories/DashboardData.cs"), "public static readonly string[] StripTables = [");
            Expect.Contains(Read("web/src/screens.tsx"), "{ path: 'dashboard', label: 'Dashboard', element: <DashboardPage /> },");
            Expect.Contains(Read("web/src/screens.tsx"), "<DashboardStrip table=\"Invoice\" />");
            Expect.Contains(Read("ng/src/app/app.routes.ts"), "{ path: 'dashboard', label: 'Dashboard', component: DashboardComponent },");
            Expect.Contains(Read("ng/src/app/app.routes.ts"), "strip: 'Invoice'");
            string window = Read("App/MainWindow.Screens.cs");
            Expect.Contains(window, "private const string FirstScreen = \"Dashboard\";");
            Expect.Contains(window, "\"Dashboard\" => new DashboardPage(GoTo),");
            Expect.Contains(window, "\"Invoice\" => WithStrip(\"Invoice\", new InvoiceListPage(_context)),");

            string plain = Path.Combine(Path.GetTempPath(), "codegen_dashboard_plain_" + Guid.NewGuid().ToString("N"));
            try
            {
                settings["Dashboard"] = "false";
                settings["DashboardStrip"] = "false";
                var off = await Repo.GenerateAsync(provider, new GenerateOptions
                {
                    Project = ProjectSettings.FromValues(settings), Stacks = ["Api", "WinUI3", "React", "Angular"], OutputDirectory = plain, DatabaseName = path, Schema = "main"
                });
                Assert.IsTrue(off.Success, string.Join(" | ", off.Errors));
                var offFiles = Directory.GetFiles(plain, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(plain, f).ToLowerInvariant()).ToList();
                Assert.IsFalse(offFiles.Any(f => f.Contains("dashboard")), "no dashboard file without the setting: " + string.Join(", ", offFiles.Where(f => f.Contains("dashboard"))));
                Assert.DoesNotContain("Dashboard", File.ReadAllText(Path.Combine(plain, "Api", "Apis", "ApiRegistration.cs")));
            }
            finally
            {
                if (Directory.Exists(plain))
                    Directory.Delete(plain, recursive: true);
            }
        }
        finally
        {
            Delete(path);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public void The_dashboard_settings_are_known_flags_and_imply_the_templates()
    {
        var on = Project(("Dashboard", "true"));
        CollectionAssert.IsSubsetOf(new[] { "CS_Dashboard", "API_Dashboard", "TSX_Dashboard", "TS_Dashboard", "WinUI3_DashboardPage", "SP_Dashboard", "MD_Dashboard" }, on.ImpliedPlanTemplates.ToArray());
        Assert.IsFalse(Project().ImpliedPlanTemplates.Any(n => n.Contains("Dashboard")));
        Assert.IsFalse(Project(("DashboardStrip", "true")).DashboardStrip, "the strip needs the dashboard");
        Assert.IsTrue(Project(("Dashboard", "true"), ("DashboardStrip", "true")).DashboardStrip);
        CollectionAssert.AreEqual(new[] { "A", "B" }, Project(("NoDashboardTables", "A, B")).NoDashboardTables);
    }

    [TestMethod]
    public void The_date_text_and_cast_helpers_write_every_database_the_same_shape()
    {
        foreach (var dialect in new[] { SqlDialect.SqlServer, SqlDialect.PostgreSql, SqlDialect.MySql, SqlDialect.Sqlite })
        {
            var info = DialectInfo.For(dialect);
            foreach (string text in new[] { info.ToText("x"), info.DateText("x"), info.ToDouble("x"), info.DateBefore("x", DateBucket.Day, 29), info.DateBefore("x", DateBucket.Year, 4) })
                Assert.IsTrue(text.Contains('x') && !text.Contains("--"), dialect + ": " + text);
        }

        Assert.AreEqual("DATEADD(month, -11, x)", DialectInfo.For(SqlDialect.SqlServer).DateBefore("x", DateBucket.Month, 11));
        Assert.AreEqual("(x - interval '4 years')", DialectInfo.For(SqlDialect.PostgreSql).DateBefore("x", DateBucket.Year, 4));
        Assert.AreEqual("DATE_SUB(x, INTERVAL 29 DAY)", DialectInfo.For(SqlDialect.MySql).DateBefore("x", DateBucket.Day, 29));
        Assert.AreEqual("datetime(x, '-11 months')", DialectInfo.For(SqlDialect.Sqlite).DateBefore("x", DateBucket.Month, 11));
    }
}
