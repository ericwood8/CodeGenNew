using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Payment;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rust;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Tests.Tax;

/// <summary> BankAccountTables, InstallmentTables and PaymentTermsTables: a checked IBAN, installments that add up to the total of a document, and a due date worked out from payment terms. </summary>
[TestClass]
public class PaymentTests
{
    private static TableModel Documents(SqlDbType total = SqlDbType.Decimal) => Sample.Table("PayDocument",
    [
        Sample.Column("PayDocumentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("OrderedOn", SqlDbType.Date, ordinal: 2),
        Sample.Column("Total", total, precision: 12, scale: 2, ordinal: 3),
    ], []);

    private static TableModel Terms() => Sample.Table("PaymentTerms",
    [
        Sample.Column("PaymentTermsId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("DaysToPay", SqlDbType.Int, ordinal: 2),
    ], []);

    private static TableModel Installments(SqlDbType amount = SqlDbType.Decimal, bool unique = false, params ColumnModel[] extra) => Sample.Table("PaymentInstallment",
    [
        Sample.Column("PaymentInstallmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("PayDocumentId", SqlDbType.Int, ordinal: 2),
        Sample.Column("SequenceNo", SqlDbType.Int, ordinal: 3),
        Sample.Column("DueDate", SqlDbType.Date, ordinal: 4),
        Sample.Column("Amount", amount, precision: 12, scale: 2, ordinal: 5),
        .. extra
    ], [Sample.ForeignKey("PayDocumentId", "PayDocument", "PayDocumentId")], indexes: unique ? [new IndexModel("UX_Seq", true, ["PayDocumentId", "SequenceNo"])] : []);

    private static TableModel Means() => Sample.Table("PaymentMeans",
    [
        Sample.Column("PaymentMeansId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("PayDocumentId", SqlDbType.Int, ordinal: 2),
        Sample.Column("PaymentTermsId", SqlDbType.Int, nullable: true, ordinal: 3),
        Sample.Column("DueDate", SqlDbType.Date, nullable: true, ordinal: 4),
    ], [Sample.ForeignKey("PayDocumentId", "PayDocument", "PayDocumentId"), Sample.ForeignKey("PaymentTermsId", "PaymentTerms", "PaymentTermsId")]);

    private static TableModel Accounts(bool iban = true, bool pair = true, params ColumnModel[] extra) => Sample.Table("PartyBankAccount",
    [
        Sample.Column("PartyBankAccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        .. iban ? new[] { Sample.Column("Iban", SqlDbType.VarChar, nullable: true, characters: 34, ordinal: 2) } : [],
        .. pair ? new[] { Sample.Column("AccountNumber", SqlDbType.VarChar, nullable: true, characters: 20, ordinal: 3), Sample.Column("RoutingNumber", SqlDbType.VarChar, nullable: true, characters: 12, ordinal: 4) } : [],
        .. extra
    ], []);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Documents(), Terms(), .. tables] };

    private static ProjectSettings Project(string key, string value) => ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", [key] = value, ["AccessMode"] = "Ef" });

    private const string InstallmentEntry = "PaymentInstallment:PayDocumentId:PayDocument:Total";
    private const string MeansEntry = "PaymentMeans:PayDocumentId:PayDocument:OrderedOn";

    [TestMethod]
    public void The_defaults_find_the_columns_of_each_table()
    {
        var database = Database(Installments(), Means(), Accounts());
        var installment = PaymentPlan.InstallmentFor(database.Tables.Single(t => t.TableName == "PaymentInstallment"), Project("InstallmentTables", InstallmentEntry), database)!;
        Assert.AreEqual("SequenceNo", installment.Sequence.Name);
        Assert.AreEqual("DueDate", installment.Due.Name);
        Assert.AreEqual("Total", installment.Total.Name);
        var due = PaymentPlan.DueFor(database.Tables.Single(t => t.TableName == "PaymentMeans"), Project("PaymentTermsTables", MeansEntry), database)!;
        Assert.AreEqual("PaymentTermsId", due.Terms.Name);
        Assert.AreEqual("DaysToPay", due.Days.Name);
        var bank = PaymentPlan.BankFor(database.Tables.Single(t => t.TableName == "PartyBankAccount"), Project("BankAccountTables", "PartyBankAccount"), database)!;
        Assert.AreEqual("Iban", bank.Iban!.Name);
        Assert.AreEqual("RoutingNumber", bank.Routing!.Name);
        foreach (var (key, value) in new[] { ("InstallmentTables", InstallmentEntry), ("PaymentTermsTables", MeansEntry), ("BankAccountTables", "PartyBankAccount") })
        {
            var project = Project(key, value);
            Assert.HasCount(0, RowShapePlan.Problems(database, project));
            var table = database.Tables.Single(t => PaymentPlan.IsListed(t.TableName, project));
            Assert.IsTrue(RowShapePlan.Has(table, project) && RowShapeWriter.HasSaveCheck(table, project));
        }
    }

    [TestMethod]
    [DataRow("InstallmentTables", "Missing:PayDocumentId:PayDocument:Total", "is not a table of the database")]
    [DataRow("InstallmentTables", "PaymentInstallment", "needs the document column, the document table and its total column")]
    [DataRow("InstallmentTables", "PaymentInstallment:PayDocumentId:Nothing:Total", "is not a table of the database")]
    [DataRow("InstallmentTables", "PaymentInstallment:PayDocumentId:PayDocument:Nothing", "must be a decimal, double or float column that cannot be NULL")]
    [DataRow("InstallmentTables", "PaymentInstallment:PayDocumentId:PayDocument:Total:Nothing", "must be a whole-number column that cannot be NULL (the number of the installment)")]
    [DataRow("PaymentTermsTables", "PaymentMeans:PayDocumentId:PayDocument", "needs the document column, the document table and the date column")]
    [DataRow("PaymentTermsTables", "PaymentMeans:PayDocumentId:PayDocument:Nothing", "must be a date column")]
    [DataRow("PaymentTermsTables", "PaymentMeans:PayDocumentId:PayDocument:OrderedOn:Nothing", "must be a whole-number column of the same type as the key")]
    [DataRow("BankAccountTables", "PartyBankAccount:Nothing:Nothing:Nothing", "needs a text column")]
    public void A_setting_that_cannot_work_is_reported(string setting, string value, string expected)
    {
        var problems = RowShapePlan.Problems(Database(Installments(), Means(), Accounts()), Project(setting, value));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_due_day_that_cannot_be_empty_is_refused()
    {
        var means = Sample.Table("PaymentMeans", Means().Columns.Select(c => c.Name == "DueDate" ? Sample.Column("DueDate", SqlDbType.Date, ordinal: 4) : c).ToList(), []);
        var problems = RowShapePlan.Problems(Database(means), Project("PaymentTermsTables", MeansEntry));
        Assert.IsTrue(problems.Any(p => p.Contains("allows NULL")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void An_amount_and_a_total_of_different_types_are_refused()
    {
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Documents(SqlDbType.Float), Terms(), Installments()] };
        var problems = RowShapePlan.Problems(database, Project("InstallmentTables", InstallmentEntry));
        Assert.IsTrue(problems.Any(p => p.Contains("must have the same type")), string.Join(" | ", problems));
    }

    [TestMethod]
    [DataRow("CardNumber")]
    [DataRow("Cvv")]
    [DataRow("ExpiryDate")]
    [DataRow("SecurityCode")]
    public void A_column_that_looks_like_a_card_is_refused_so_no_card_is_ever_stored(string column)
    {
        var accounts = Accounts(true, true, Sample.Column(column, SqlDbType.VarChar, nullable: true, characters: 20, ordinal: 9));
        var problems = RowShapePlan.Problems(Database(accounts), Project("BankAccountTables", "PartyBankAccount"));
        Assert.IsTrue(problems.Any(p => p.Contains("A card is never stored")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_table_without_a_unique_index_on_the_document_and_the_number_is_warned_about()
    {
        Assert.HasCount(1, PaymentPlan.MissingIndexes(Database(Installments()), Project("InstallmentTables", InstallmentEntry)));
        Assert.HasCount(0, PaymentPlan.MissingIndexes(Database(Installments(unique: true)), Project("InstallmentTables", InstallmentEntry)));
    }

    [TestMethod]
    public void The_api_checks_an_account_an_installment_schedule_and_a_due_date()
    {
        string text = RowShapeWriter.File(Database(Installments(), Means(), Accounts()), ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["AccessMode"] = "Ef", ["BankAccountTables"] = "PartyBankAccount", ["InstallmentTables"] = InstallmentEntry, ["PaymentTermsTables"] = MeansEntry
        }), "ShopContext", []);
        Expect.Contains(text, "public static bool IbanValid(string iban)");
        Expect.Contains(text, "public static bool RoutingValid(string routing)");
        Expect.Contains(text, "if (await CheckAccountAsync(context, row) is { } accountProblem)");
        Expect.Contains(text, "public static async Task<IResult?> CheckInstallmentAsync(ShopContext context, PaymentInstallment row)");
        Expect.Contains(text, "others.Sum(x => (decimal)x.Amount) + (decimal)row.Amount > (decimal)held.Total ? RowShapeProblems.InstallmentOverTotal(");
        Expect.Contains(text, "app.MapGet(\"/api/paymentinstallments/schedule\"");
        Expect.Contains(text, "public static async Task<IResult?> CheckDueAsync(ShopContext context, PaymentMeans row)");
        Expect.Contains(text, "DateTime start = held.OrderedOn.Date;");
        Expect.Contains(text, "row.DueDate = start.AddDays(found.DaysToPay);");
        Expect.Contains(text, "public static IResult PaymentInvalid(string detail) => Problem(400, \"" + PaymentWords.InvalidType + "\"");
        Expect.Contains(text, PaymentWords.OverTotalType);
    }

    [TestMethod]
    public void The_iban_lengths_cover_the_countries_with_an_iban()
    {
        var lengths = PaymentPlan.IbanLengths.Split(' ').ToDictionary(x => x[..2], x => int.Parse(x[2..]));
        Assert.AreEqual(22, lengths["DE"]);
        Assert.AreEqual(22, lengths["GB"]);
        Assert.AreEqual(15, lengths["NO"]);
        Assert.AreEqual(27, lengths["FR"]);
        Assert.IsFalse(lengths.ContainsKey("US"));
    }

    [TestMethod]
    public void The_python_api_carries_the_same_rules()
    {
        var installment = PaymentPlan.InstallmentFor(Installments(), Project("InstallmentTables", InstallmentEntry), Database(Installments()))!;
        var project = Project("InstallmentTables", InstallmentEntry);
        var names = PythonPaymentWriter.NamesFor(null, installment, null, PythonTable.Of(installment.Table, project), project)!;
        string python = PythonPaymentWriter.Helper(names) + PythonPaymentWriter.Route(names, "/api/paymentinstallments", "", "");
        Expect.Contains(python, "def check_installment(session: Session, row) -> None:");
        Expect.Contains(python, "if sum((Decimal(str(amount)) for _, _, amount in others), Decimal(0)) + Decimal(str(row.amount)) > Decimal(str(document.total)):");
        Expect.Contains(python, "@router.get(\"/api/paymentinstallments/schedule\")");
        Assert.AreEqual("    check_installment(session, row)", PythonPaymentWriter.CheckLine(names));
        var bank = PaymentPlan.BankFor(Accounts(), Project("BankAccountTables", "PartyBankAccount"), Database(Accounts()))!;
        var accountNames = PythonPaymentWriter.NamesFor(bank, null, null, PythonTable.Of(bank.Table, project), project)!;
        string account = PythonPaymentWriter.Helper(accountNames);
        Expect.Contains(account, "def iban_valid(iban: str) -> bool:");
        Expect.Contains(account, "row.iban = iban");
        Expect.Contains(account, "def check_account(row) -> None:");
    }

    [TestMethod]
    public void The_rust_api_carries_the_same_rules()
    {
        var project = Project("InstallmentTables", InstallmentEntry);
        var installment = PaymentPlan.InstallmentFor(Installments(), project, Database(Installments()))!;
        var names = RustPaymentWriter.NamesFor(null, installment, null, RustTable.Of(installment.Table, project), project, DialectInfo.For(SqlDialect.PostgreSql), "i32")!;
        string rust = RustPaymentWriter.Code(names);
        Expect.Contains(rust, "async fn check_installment(pool: &Pool, row_id: Option<i32>, row: &mut PaymentInstallment) -> ApiResult<()> {");
        Expect.Contains(rust, "async fn installment_schedule(State(pool): State<Pool>, Query(query): Query<ScheduleQuery>)");
        Expect.Contains(rust, "if scheduled > total {");
        Assert.AreEqual("        .route(\"/api/paymentinstallments/schedule\", get(installment_schedule))\n", RustPaymentWriter.RouteLine("/api/paymentinstallments", ""));
        Assert.AreEqual("    check_installment(&pool, Some(id), &mut row).await?;", RustPaymentWriter.CheckLine(names, true));
    }

    [TestMethod]
    public void The_detector_suggests_the_three_payment_shapes()
    {
        var database = Database(Installments(), Means(), Accounts());
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" });
        var found = DatabaseShapeDetector.Of(database, project).Where(d => d.Suggestion.Setting is "BankAccountTables" or "InstallmentTables" or "PaymentTermsTables").ToList();
        Assert.HasCount(3, found);
        Assert.AreEqual("PartyBankAccount", found.Single(f => f.Suggestion.Setting == "BankAccountTables").Suggestion.Item);
        Assert.AreEqual(InstallmentEntry, found.Single(f => f.Suggestion.Setting == "InstallmentTables").Suggestion.Item);
        Assert.AreEqual(MeansEntry, found.Single(f => f.Suggestion.Setting == "PaymentTermsTables").Suggestion.Item);
        Assert.IsTrue(found.All(f => f.Suggestion.Fits), string.Join(" | ", found.Select(f => f.Suggestion.Note)));
        Assert.IsEmpty(DatabaseShapeDetector.Of(database, Project("BankAccountTables", "PartyBankAccount")).Where(d => d.Suggestion.Setting == "BankAccountTables"), "a listed table is not suggested again");
    }

    [TestMethod]
    public void The_schedule_box_is_written_for_every_front_end_of_an_installment_table()
    {
        var project = Project("InstallmentTables", InstallmentEntry);
        Assert.IsTrue(PaymentScreens.HasBox(Installments(), project));
        Assert.IsFalse(PaymentScreens.HasBox(Means(), project));
        Expect.Contains(PaymentScreens.ReactApiLine(), "schedule: (document: string) => request<{ total: number; scheduled: number; remaining: number; complete: boolean }>(`${apiUrl}/schedule?document=${encodeURIComponent(document)}`),");
        Expect.Contains(string.Join("\n", PaymentScreens.ReactHandlers("paymentInstallmentsApi")), "paymentInstallmentsApi.schedule(document)");
        Expect.Contains(string.Join("\n", PaymentScreens.ReactBox()), "aria-label=\"Schedule document\"");
        Expect.Contains(string.Join("\n", PaymentScreens.AngularServiceLines("apiUrl")), "schedule(document: string): Observable<");
        Expect.Contains(string.Join("\n", PaymentScreens.AngularBox()), "(click)=\"showSchedule()\"");
        Expect.Contains(string.Join("\n", PaymentScreens.CsClientLines("/api/x")), "public async Task<System.Text.Json.JsonElement> ScheduleAsync(string document)");
        Expect.Contains(string.Join("\n", PaymentScreens.BlazorHandlers("Client")), "await Client.ScheduleAsync(document)");
        Expect.Contains(PaymentScreens.DesktopBox(), "PlaceholderText=\"Document\"");
        Expect.Contains(PaymentScreens.DesktopMembers("", "int"), "if (!int.TryParse(ScheduleDocument.Trim(), out int document))");
    }

    [TestMethod]
    public void The_winui3_dialog_runs_the_installment_rule_before_it_saves()
    {
        var database = Database(Installments(), Means(), Accounts());
        var project = Project("InstallmentTables", InstallmentEntry);
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        string check = PaymentScreens.WinUI3Check(database.Tables.Single(t => t.TableName == "PaymentInstallment"), project, "_context");
        Expect.Contains(check, PaymentWords.OutOfOrder);
        Expect.Contains(check, PaymentWords.OverTotal);
        Expect.Contains(check, "pmTotal is not null && pmOthers.Sum");
        Assert.AreEqual("", PaymentScreens.WinUI3Check(database.Tables.Single(t => t.TableName == "PaymentMeans"), project, "_context"));
    }

    [TestMethod]
    public void A_generated_test_sends_an_iban_and_a_pair_that_pass_the_checks()
    {
        var database = Database(Accounts());
        var project = Project("BankAccountTables", "PartyBankAccount");
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        var table = database.Tables.Single(t => t.TableName == "PartyBankAccount");
        Assert.AreEqual("DE89370400440532013000", PaymentPlan.BankSample(table, table.FindColumn("Iban")!, project));
        Assert.AreEqual("011000015", PaymentPlan.BankSample(table, table.FindColumn("RoutingNumber")!, project));
        Assert.IsNull(PaymentPlan.BankSample(table, table.Columns.First(c => c.IsPrimaryKey), project));
    }

    private static TableModel TermsWithDiscount() => Sample.Table("PaymentTerms",
    [
        Sample.Column("PaymentTermsId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("DaysToPay", SqlDbType.Int, ordinal: 2),
        Sample.Column("EarlyDiscountPercent", SqlDbType.Decimal, nullable: true, precision: 5, scale: 2, ordinal: 3),
        Sample.Column("EarlyDiscountDays", SqlDbType.Int, nullable: true, ordinal: 4),
        Sample.Column("LatePenaltyPercent", SqlDbType.Decimal, nullable: true, precision: 5, scale: 2, ordinal: 5),
    ], []);

    private static DatabaseModel DatabaseWithDiscount() => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Documents(), TermsWithDiscount(), Means()] };

    [TestMethod]
    public void The_payable_amount_is_worked_out_when_the_terms_have_a_discount_or_a_penalty()
    {
        var database = DatabaseWithDiscount();
        var project = Project("PaymentTermsTables", MeansEntry);
        var shape = PaymentPlan.DueFor(database.Tables.Single(t => t.TableName == "PaymentMeans"), project, database)!;
        Assert.IsTrue(shape.CanPay);
        Assert.AreEqual("EarlyDiscountPercent", shape.EarlyPercent!.Name);
        Assert.AreEqual("Total", shape.Total!.Name);
        Assert.IsFalse(PaymentPlan.DueFor(Means(), project, Database(Means()))!.CanPay, "terms with no discount or penalty have nothing to work out");
        string text = RowShapeWriter.File(database, ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef", ["PaymentTermsTables"] = MeansEntry }), "ShopContext", []);
        Expect.Contains(text, "public static async Task<IResult> PayableAsync(ShopContext context, int? id, DateTime? on)");
        Expect.Contains(text, "app.MapGet(\"/api/paymentmeans/payable\"");
        Expect.Contains(text, "discount = Math.Round(total * earlyPercent.Value / 100m, scale, MidpointRounding.AwayFromZero);");
        Expect.Contains(text, "penalty = Math.Round(total * latePercent.Value / 100m, scale, MidpointRounding.AwayFromZero);");
        Expect.Contains(text, "rule = \"" + PaymentWords.RuleEarly + "\";");
    }

    [TestMethod]
    public void The_python_and_rust_apis_work_out_the_payable_amount_too()
    {
        var database = DatabaseWithDiscount();
        var project = Project("PaymentTermsTables", MeansEntry);
        var shape = PaymentPlan.DueFor(database.Tables.Single(t => t.TableName == "PaymentMeans"), project, database)!;
        var names = PythonPaymentWriter.NamesFor(null, null, shape, PythonTable.Of(shape.Table, project), project)!;
        Assert.IsTrue(names.HasRoute);
        string python = PythonPaymentWriter.Route(names, "/api/paymentmeans", "", "");
        Expect.Contains(python, "@router.get(\"/api/paymentmeans/payable\")");
        Expect.Contains(python, "discount = (total * Decimal(str(terms.early_discount_percent)) / 100).quantize(unit, ROUND_HALF_UP)");
        Expect.Contains(python, "elif terms is not None and terms.late_penalty_percent is not None");
        var rustNames = RustPaymentWriter.NamesFor(null, null, shape, RustTable.Of(shape.Table, project), project, DialectInfo.For(SqlDialect.PostgreSql), "i32")!;
        Assert.IsTrue(rustNames.HasRoute);
        string rust = RustPaymentWriter.Code(rustNames);
        Expect.Contains(rust, "async fn payable_on(State(pool): State<Pool>, Query(query): Query<PayableQuery>)");
        Expect.Contains(rust, "rule = \"" + PaymentWords.RuleLate + "\";");
        Assert.AreEqual("        .route(\"/api/paymentmeans/payable\", get(payable_on))\n", RustPaymentWriter.RouteLine("/api/paymentmeans", "", rustNames));
    }

    [TestMethod]
    public void The_winui3_dialog_checks_the_iban_and_works_out_the_due_date()
    {
        var database = Database(Means(), Accounts());
        CodeGenNew.Core.Rate.RatePlan.Remember(database, ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef", ["BankAccountTables"] = "PartyBankAccount", ["PaymentTermsTables"] = MeansEntry }));
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef", ["BankAccountTables"] = "PartyBankAccount", ["PaymentTermsTables"] = MeansEntry });
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        string account = PaymentDesktopChecks.AccountCheck(database.Tables.Single(t => t.TableName == "PartyBankAccount"), project);
        Expect.Contains(account, "bool IbanValid(string value)");
        Expect.Contains(account, "ErrorMessage = \"" + PaymentWords.IbanBad + "\";");
        Expect.Contains(account, "entity.Iban = iban;");
        string due = PaymentDesktopChecks.DueCheck(database.Tables.Single(t => t.TableName == "PaymentMeans"), project, "_context");
        Expect.Contains(due, "entity.DueDate = pdStart.AddDays(pdFound.DaysToPay);");
        Expect.Contains(due, "ErrorMessage = \"" + PaymentWords.DueBeforeDocument + "\";");
        Assert.AreEqual("", PaymentDesktopChecks.AccountCheck(database.Tables.Single(t => t.TableName == "PaymentMeans"), project));
        Assert.AreEqual("", PaymentDesktopChecks.DueCheck(database.Tables.Single(t => t.TableName == "PartyBankAccount"), project, "_context"));
    }
}
