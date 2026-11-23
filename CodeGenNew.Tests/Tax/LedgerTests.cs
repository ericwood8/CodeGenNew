using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Payment;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rust;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Tests.Tax;

/// <summary> LedgerTables: an append-only payment transaction ledger that is also a log table, recorded with POST record, with the paid amount of a document worked out from its rows. </summary>
[TestClass]
public class LedgerTests
{
    private static TableModel Documents(SqlDbType total = SqlDbType.Decimal) => Sample.Table("PayDocument",
    [
        Sample.Column("PayDocumentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("OrderedOn", SqlDbType.Date, ordinal: 2),
        Sample.Column("Total", total, precision: 12, scale: 2, ordinal: 3),
    ], []);

    private static TableModel Ledger(bool unique = false, params ColumnModel[] extra) => Sample.Table("PayTransaction",
    [
        Sample.Column("PayTransactionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("PayDocumentId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Kind", SqlDbType.VarChar, characters: 12, ordinal: 3),
        Sample.Column("Status", SqlDbType.VarChar, characters: 12, ordinal: 4),
        Sample.Column("Amount", SqlDbType.Decimal, precision: 12, scale: 2, ordinal: 5),
        Sample.Column("GatewayId", SqlDbType.VarChar, characters: 100, ordinal: 6),
        Sample.Column("IdempotencyKey", SqlDbType.VarChar, characters: 100, ordinal: 7),
        Sample.Column("Payload", SqlDbType.VarChar, nullable: true, characters: 4000, ordinal: 8),
        Sample.Column("CreatedOn", SqlDbType.DateTime, ordinal: 9),
        .. extra
    ], [Sample.ForeignKey("PayDocumentId", "PayDocument", "PayDocumentId")],
    indexes: unique ? [new IndexModel("UX_Gateway", true, ["GatewayId"]), new IndexModel("UX_Key", true, ["IdempotencyKey"])] : []);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Documents(), .. tables] };

    private static ProjectSettings Project(string value = Entry) => ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["LedgerTables"] = value, ["AccessMode"] = "Ef" });

    private const string Entry = "PayTransaction:PayDocumentId:PayDocument:Total";

    [TestMethod]
    public void The_defaults_find_the_columns_and_the_table_is_a_log_table_too()
    {
        var database = Database(Ledger());
        var project = Project();
        var shape = LedgerPlan.For(database.Tables.Single(t => t.TableName == "PayTransaction"), project, database)!;
        Assert.AreEqual("Kind", shape.Kind.Name);
        Assert.AreEqual("IdempotencyKey", shape.IdempotencyKey.Name);
        Assert.AreEqual("Total", shape.Total.Name);
        Assert.HasCount(0, RowShapePlan.Problems(database, project));
        Assert.IsTrue(RowShapePlan.Has(shape.Table, project));
        Assert.IsTrue(LogTablePlan.IsListed(shape.Table, project), "a ledger is also a log table, so its screens are read only");
        Assert.IsFalse(RowShapeWriter.HasSaveCheck(shape.Table, project), "a log table has no save check");
    }

    [TestMethod]
    [DataRow("Missing:PayDocumentId:PayDocument:Total", "is not a table of the database")]
    [DataRow("PayTransaction", "needs the document column, the document table and its total column")]
    [DataRow("PayTransaction:PayDocumentId:Nothing:Total", "is not a table of the database")]
    [DataRow("PayTransaction:PayDocumentId:PayDocument:Nothing", "must be a decimal, double or float column that cannot be NULL")]
    [DataRow("PayTransaction:PayDocumentId:PayDocument:Total:Nothing", "must be a text column that cannot be NULL (authorize, capture, refund or void)")]
    [DataRow("PayTransaction:PayDocumentId:PayDocument:Total:Kind:Nothing", "must be a text column that cannot be NULL (pending, succeeded or failed)")]
    [DataRow("PayTransaction:PayDocumentId:PayDocument:Total:Kind:Status:Nothing", "must be a decimal, double or float column that cannot be NULL")]
    [DataRow("PayTransaction:PayDocumentId:PayDocument:Total:Kind:Kind", "must be six different columns")]
    public void A_setting_that_cannot_work_is_reported(string value, string expected)
    {
        var problems = RowShapePlan.Problems(Database(Ledger()), Project(value));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void An_amount_and_a_total_of_different_types_are_refused()
    {
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Documents(SqlDbType.Float), Ledger()] };
        var problems = RowShapePlan.Problems(database, Project());
        Assert.IsTrue(problems.Any(p => p.Contains("must have the same type")), string.Join(" | ", problems));
    }

    [TestMethod]
    [DataRow("CardNumber")]
    [DataRow("Cvv")]
    [DataRow("ExpiryDate")]
    public void A_column_that_looks_like_a_card_is_refused_so_no_card_is_ever_stored(string column)
    {
        var ledger = Ledger(false, Sample.Column(column, SqlDbType.VarChar, nullable: true, characters: 20, ordinal: 10));
        var problems = RowShapePlan.Problems(Database(ledger), Project());
        Assert.IsTrue(problems.Any(p => p.Contains("A card is never stored")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_ledger_without_unique_indexes_on_the_gateway_id_and_the_key_is_warned_about()
    {
        Assert.HasCount(2, LedgerPlan.MissingIndexes(Database(Ledger()), Project()));
        Assert.HasCount(0, LedgerPlan.MissingIndexes(Database(Ledger(true)), Project()));
    }

    [TestMethod]
    public void The_api_records_a_row_and_works_out_what_is_paid()
    {
        var database = Database(Ledger());
        string text = RowShapeWriter.File(database, Project(), "ShopContext", []);
        Expect.Contains(text, "public static async Task<IResult?> CheckLedgerAsync(ShopContext context, PayTransaction row)");
        Expect.Contains(text, "if (row.Kind is not (\"authorize\" or \"capture\" or \"refund\" or \"void\"))");
        Expect.Contains(text, "if (row.Kind == \"refund\" ? amount >= 0 : amount <= 0)");
        Expect.Contains(text, "RowShapeProblems.LedgerDuplicate(");
        Expect.Contains(text, "if (held.Sum(x => (decimal)x) + amount < 0)");
        Expect.Contains(text, "row.CreatedOn = DateTime.Now;");
        Expect.Contains(text, "app.MapPost(\"/api/paytransactions/record\"");
        Expect.Contains(text, "app.MapGet(\"/api/paytransactions/paid\"");
        Expect.Contains(text, "settled = paid >= total");
        Expect.Contains(text, PaymentWords.LedgerDuplicateType);
        Expect.Contains(text, PaymentWords.RefundOverType);
        Assert.IsFalse(text.Contains("CheckSaveAsync(ShopContext context, PayTransaction"), "a log table has no save check");
    }

    [TestMethod]
    public void The_python_api_carries_the_same_rules()
    {
        var database = Database(Ledger());
        var project = Project();
        var shape = LedgerPlan.For(database.Tables.Single(t => t.TableName == "PayTransaction"), project, database)!;
        var names = PythonPaymentWriter.NamesFor(null, null, null, PythonTable.Of(shape.Table, project), project, shape)!;
        Assert.IsTrue(names.HasRoute && names.UsesDecimal);
        string python = PythonPaymentWriter.Helper(names) + PythonPaymentWriter.Route(names, "/api/paytransactions", "", "", "PayTransactionSchema", "");
        Expect.Contains(python, "def check_ledger(session: Session, row) -> None:");
        Expect.Contains(python, "if (amount >= 0) if row.kind == \"refund\" else (amount <= 0):");
        Expect.Contains(python, "@router.post(\"/api/paytransactions/record\", response_model=PayTransactionSchema, status_code=201)");
        Expect.Contains(python, "@router.get(\"/api/paytransactions/paid\")");
        Expect.Contains(python, "row.created_on = datetime.now()");
        Assert.AreEqual("    check_ledger(session, row)", PythonPaymentWriter.CheckLine(names));
    }

    [TestMethod]
    public void The_rust_api_carries_the_same_rules()
    {
        var database = Database(Ledger());
        var project = Project();
        var shape = LedgerPlan.For(database.Tables.Single(t => t.TableName == "PayTransaction"), project, database)!;
        var names = RustPaymentWriter.NamesFor(null, null, null, RustTable.Of(shape.Table, project), project, DialectInfo.For(SqlDialect.PostgreSql), "i32", shape)!;
        Assert.IsTrue(names.HasRoute);
        string rust = RustPaymentWriter.Code(names);
        Expect.Contains(rust, "async fn check_ledger(pool: &Pool, row: &mut PayTransaction) -> ApiResult<()> {");
        Expect.Contains(rust, "async fn record_transaction(State(pool): State<Pool>, ApiJson(mut row): ApiJson<PayTransaction>) -> ApiResult<Response> {");
        Expect.Contains(rust, "async fn paid_amount(State(pool): State<Pool>, Query(query): Query<PaidQuery>)");
        Expect.Contains(rust, "row.created_on = chrono::Local::now().naive_local();");
        string routes = RustPaymentWriter.RouteLine("/api/paytransactions", "", names);
        Expect.Contains(routes, ".route(\"/api/paytransactions/paid\", get(paid_amount))");
        Expect.Contains(routes, ".route(\"/api/paytransactions/record\", post(record_transaction))");
    }

    [TestMethod]
    public void The_detector_suggests_a_ledger()
    {
        var database = Database(Ledger());
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" });
        var found = DatabaseShapeDetector.Of(database, project).Where(d => d.Suggestion.Setting == "LedgerTables").ToList();
        Assert.HasCount(1, found);
        Assert.AreEqual(Entry, found[0].Suggestion.Item);
        Assert.IsTrue(found[0].Suggestion.Fits, found[0].Suggestion.Note);
        Assert.IsEmpty(DatabaseShapeDetector.Of(database, Project()).Where(d => d.Suggestion.Setting == "LedgerTables"), "a listed table is not suggested again");
    }

    [TestMethod]
    public void The_paid_box_is_written_for_every_front_end_of_a_ledger_table()
    {
        var project = Project();
        Assert.IsTrue(PaymentScreens.HasPaidBox(Ledger(), project));
        Assert.IsFalse(PaymentScreens.HasPaidBox(Documents(), project));
        var box = PaymentScreens.Paid;
        Expect.Contains(PaymentScreens.ReactApiLine(box), "paid: (document: string) => request<{ total: number; captured: number; refunded: number; paid: number; remaining: number; settled: boolean }>(`${apiUrl}/paid?document=${encodeURIComponent(document)}`),");
        Expect.Contains(string.Join("\n", PaymentScreens.ReactHandlers("payTransactionsApi", box)), "payTransactionsApi.paid(document)");
        Expect.Contains(string.Join("\n", PaymentScreens.ReactBox(box)), "aria-label=\"Paid document\"");
        Expect.Contains(string.Join("\n", PaymentScreens.ReactBoxTests("payTransactionsApi", "PayTransactionLogPage", box, false)), "render(<PayTransactionLogPage />);");
        Expect.Contains(string.Join("\n", PaymentScreens.AngularServiceLines("apiUrl", box)), "paid(document: string): Observable<");
        Expect.Contains(string.Join("\n", PaymentScreens.AngularBox(box)), "(click)=\"showPaid()\"");
        Expect.Contains(PaymentScreens.AngularBoxTest(box), "component.showPaid();");
        Expect.Contains(string.Join("\n", PaymentScreens.CsClientLines("/api/x", box)), "public async Task<System.Text.Json.JsonElement> PaidAsync(string document)");
        Expect.Contains(string.Join("\n", PaymentScreens.BlazorHandlers("Client", box)), "await Client.PaidAsync(document)");
        Expect.Contains(PaymentScreens.BlazorBoxTest(box), "Asks_for_the_paid_of_a_document_and_shows_it");
        Expect.Contains(PaymentScreens.DesktopBox(box), "Click=\"OnShowPaidClick\"");
        Expect.Contains(PaymentScreens.DesktopMembers("", "int", box), "if (!int.TryParse(PaidDocument.Trim(), out int document))");
        Expect.Contains(PaymentScreens.UnoRead(box), "await Client.PaidAsync(document.ToString())");
    }

    [TestMethod]
    public void The_winui3_list_reads_what_is_paid_over_its_own_context()
    {
        var database = Database(Ledger());
        var project = Project();
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        string read = PaymentScreens.WinUI3PaidRead(database.Tables.Single(t => t.TableName == "PayTransaction"), project, "_context");
        Expect.Contains(read, "x.Status == \"succeeded\"");
        Expect.Contains(read, "refunded = -entries.Where(x => x.Kind == \"refund\")");
        Expect.Contains(read, "Paid in full: {paid} of {total}");
        Assert.AreEqual("", PaymentScreens.WinUI3PaidRead(database.Tables.Single(t => t.TableName == "PayDocument"), project, "_context"));
    }
}
