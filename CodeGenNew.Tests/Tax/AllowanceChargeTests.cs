using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Allowance;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rust;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Tests.Tax;

/// <summary> AllowanceCharges: a table of discounts and surcharges whose amount the server works out. </summary>
[TestClass]
public class AllowanceChargeTests
{
    private static TableModel Rows(bool nullableBase = true, SqlDbType amount = SqlDbType.Decimal, bool withDocument = true) => Sample.Table("InvoiceAllowanceCharge",
    [
        Sample.Column("InvoiceAllowanceChargeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("InvoiceId", SqlDbType.Int, ordinal: 2),
        Sample.Column("IsCharge", SqlDbType.Bit, ordinal: 3),
        Sample.Column("ReasonCode", SqlDbType.VarChar, characters: 10, nullable: true, ordinal: 4),
        Sample.Column("BaseAmount", SqlDbType.Decimal, precision: 9, scale: 2, nullable: nullableBase, ordinal: 5),
        Sample.Column("Percentage", SqlDbType.Decimal, precision: 9, scale: 4, nullable: true, ordinal: 6),
        Sample.Column("Amount", amount, precision: 9, scale: 2, ordinal: 7),
    ], []);

    private static DatabaseModel Database(TableModel? table = null) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [table ?? Rows()] };

    private static ProjectSettings Project(string setting = "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId") =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AllowanceCharges"] = setting, ["AccessMode"] = "Ef" });

    private static string Api(string setting = "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId", TableModel? table = null) =>
        RowShapeWriter.File(Database(table), Project(setting), "ShopContext", []);

    [TestMethod]
    public void The_table_alone_finds_the_default_columns()
    {
        var database = Database();
        var shape = AllowanceChargePlan.For(database.Tables[0], Project("InvoiceAllowanceCharge"))!;
        Assert.AreEqual("IsCharge", shape.Flag.Name);
        Assert.AreEqual("BaseAmount", shape.Base.Name);
        Assert.AreEqual("Percentage", shape.Percent.Name);
        Assert.AreEqual("Amount", shape.Amount.Name);
        Assert.IsNull(shape.Document);
        Assert.HasCount(0, RowShapePlan.Problems(database, Project("InvoiceAllowanceCharge")));
        Assert.IsTrue(RowShapePlan.Has(database.Tables[0], Project()) && RowShapeWriter.HasSaveCheck(database.Tables[0], Project()));
    }

    [TestMethod]
    [DataRow("Missing", "is not a table of the database")]
    [DataRow("InvoiceAllowanceCharge:ReasonCode", "yes/no column that cannot be NULL")]
    [DataRow("InvoiceAllowanceCharge:IsCharge:ReasonCode", "decimal, double or float columns")]
    [DataRow("InvoiceAllowanceCharge:IsCharge:BaseAmount:BaseAmount:Amount", "four different columns")]
    [DataRow("InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:NoSuchColumn", "is not a column of the table")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void An_amount_that_allows_null_is_reported()
    {
        var table = Sample.Table("InvoiceAllowanceCharge",
        [
            Sample.Column("InvoiceAllowanceChargeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("IsCharge", SqlDbType.Bit, ordinal: 2),
            Sample.Column("BaseAmount", SqlDbType.Decimal, precision: 9, scale: 2, nullable: true, ordinal: 3),
            Sample.Column("Percentage", SqlDbType.Decimal, precision: 9, scale: 4, nullable: true, ordinal: 4),
            Sample.Column("Amount", SqlDbType.Decimal, precision: 9, scale: 2, nullable: true, ordinal: 5),
        ], []);
        Assert.IsTrue(RowShapePlan.Problems(Database(table), Project("InvoiceAllowanceCharge")).Any(p => p.Contains("one that cannot be NULL")));
    }

    [TestMethod]
    public void The_api_works_the_amount_and_the_percentage_out_and_refuses_what_cannot_be()
    {
        string text = Api();
        StringAssert.Contains(text, "CheckAllowanceCharge(InvoiceAllowanceCharge row)");
        StringAssert.Contains(text, "decimal baseAmount = (decimal)(row.BaseAmount ?? 0), percent = (decimal)(row.Percentage ?? 0), amount = (decimal)row.Amount;");
        StringAssert.Contains(text, "if (baseAmount < 0 || percent < 0 || amount < 0)");
        StringAssert.Contains(text, "if (percent > 0 && baseAmount <= 0)");
        StringAssert.Contains(text, "row.Amount = (decimal)Math.Round(baseAmount * percent / 100m, 2, MidpointRounding.AwayFromZero);");
        StringAssert.Contains(text, "row.Percentage = (decimal)Math.Round(amount / baseAmount * 100m, 4, MidpointRounding.AwayFromZero);");
        StringAssert.Contains(text, "RowShapeProblems.AllowanceInvalid(\"" + AllowanceChargeWords.NeedsAmount + "\")");
        StringAssert.Contains(text, "public static IResult AllowanceInvalid(string detail) => Problem(400, \"" + AllowanceChargeWords.InvalidType + "\", \"" + AllowanceChargeWords.InvalidTitle + "\", detail);");
        StringAssert.Contains(text, "if (CheckAllowanceCharge(row) is { } allowanceProblem)");
    }

    [TestMethod]
    public void A_base_that_cannot_be_null_is_read_without_a_null_check()
    {
        StringAssert.Contains(Api(table: Rows(nullableBase: false)), "decimal baseAmount = (decimal)row.BaseAmount, percent");
    }

    [TestMethod]
    public void The_totals_route_is_offered_with_a_document_column_only()
    {
        string text = Api();
        StringAssert.Contains(text, "AllowanceChargeTotalsAsync(ShopContext context, int? document)");
        StringAssert.Contains(text, "Where(x => x.InvoiceId == document)");
        StringAssert.Contains(text, "decimal allowances = rows.Where(r => !r.Charge).Sum(r => (decimal)r.Amount);");
        StringAssert.Contains(text, "Net = charges - allowances");
        StringAssert.Contains(text, "/allowance-charge-totals\"");
        string without = Api("InvoiceAllowanceCharge");
        Assert.IsFalse(without.Contains("AllowanceChargeTotalsAsync"), "no document column, no totals route");
        StringAssert.Contains(without, "CheckAllowanceCharge(InvoiceAllowanceCharge row)");
    }
}

/// <summary> AllowanceCharges on the Python and Rust APIs and on the screens of every front end. </summary>
[TestClass]
public class AllowanceChargeStackTests
{
    private static TableModel Rows(SqlDbType money = SqlDbType.Decimal) => Sample.Table("InvoiceAllowanceCharge",
    [
        Sample.Column("InvoiceAllowanceChargeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("InvoiceId", SqlDbType.Int, ordinal: 2),
        Sample.Column("IsCharge", SqlDbType.Bit, ordinal: 3),
        Sample.Column("BaseAmount", money, precision: 9, scale: 2, nullable: true, ordinal: 4),
        Sample.Column("Percentage", money, precision: 9, scale: 4, nullable: true, ordinal: 5),
        Sample.Column("Amount", money, precision: 9, scale: 2, ordinal: 6),
    ], []);

    private static ProjectSettings Project(string setting = "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId") =>
        ProjectSettings.FromValues([new("ProjectName", "Shop"), new("AllowanceCharges", setting), new("AccessMode", "Ef"), new("WinUI3Grid", "TableView")]);

    private static async Task<string> Run(string template, string setting = "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId", SqlDbType money = SqlDbType.Decimal)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Rows(money), Project(setting));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!;
    }

    [TestMethod]
    public async Task The_python_router_checks_every_save_and_serves_the_totals()
    {
        string text = await Run("PY_Routes_v1.tt");
        Expect.Contains(text, "from decimal import ROUND_HALF_UP, Decimal");
        Expect.Contains(text, "def check_allowance(row) -> None:");
        Expect.Contains(text, "row.amount = (base * percent / 100).quantize(Decimal(1).scaleb(-2), rounding=ROUND_HALF_UP)");
        Expect.Contains(text, "row.percentage = (amount / base * 100).quantize(Decimal(1).scaleb(-4), rounding=ROUND_HALF_UP)");
        Expect.Contains(text, "problem_type=\"" + AllowanceChargeWords.InvalidType + "\"");
        Assert.AreEqual(2, text.Split("    check_allowance(row)\n").Length - 1, "once on create and once on update");
        Expect.Contains(text, "/" + AllowanceChargeWords.TotalsRoute + "\")");
        Expect.Contains(text, "def allowance_charge_totals(");
        Expect.DoesNotContain(await Run("PY_Routes_v1.tt", "InvoiceAllowanceCharge"), "def allowance_charge_totals(");
    }

    [TestMethod]
    public async Task The_rust_router_checks_every_save_and_serves_the_totals()
    {
        string text = await Run("RS_Routes_v1.tt");
        Expect.Contains(text, "fn check_allowance(row: &mut InvoiceAllowanceCharge) -> ApiResult<()> {");
        Expect.Contains(text, "round_dp_with_strategy(2, rust_decimal::RoundingStrategy::MidpointAwayFromZero)");
        Assert.AreEqual(2, text.Split("    check_allowance(&mut row)?;").Length - 1, "once on create and once on update");
        Expect.Contains(text, "\"/api/invoiceallowancecharges/" + AllowanceChargeWords.TotalsRoute + "\", get(allowance_charge_totals)");
        Expect.Contains(text, "(charges - allowances).to_f64().unwrap_or_default()");
        Expect.DoesNotContain(await Run("RS_Routes_v1.tt", "InvoiceAllowanceCharge"), "async fn allowance_charge_totals");
    }

    [TestMethod]
    public async Task The_rust_router_works_in_f64_when_no_column_is_a_decimal()
    {
        string text = await Run("RS_Routes_v1.tt", money: SqlDbType.Float);
        Expect.Contains(text, "let base = row.base_amount.unwrap_or_default() as f64;");
        Expect.Contains(text, "10f64.powi(2)).round() / 10f64.powi(2)) as f64;");
        Expect.DoesNotContain(text, "rust_decimal::Decimal::ZERO");
    }

    [TestMethod]
    public void The_list_screens_get_a_totals_box_only_when_the_entry_names_the_document()
    {
        var table = Rows();
        Assert.IsTrue(AllowanceScreens.HasBox(table, Project()));
        Assert.IsFalse(AllowanceScreens.HasBox(table, Project("InvoiceAllowanceCharge")));
        Expect.Contains(AllowanceScreens.ReactApiLine(), "allowanceTotals: (document: string) => request<{ allowances: number;");
        Expect.Contains(string.Join(" ", AllowanceScreens.ReactBox()), "aria-label=\"" + AllowanceScreens.DocumentLabel + "\"");
        Expect.Contains(string.Join(" ", AllowanceScreens.AngularBox()), "(click)=\"showAllowances()\"");
        Expect.Contains(string.Join(" ", AllowanceScreens.BlazorHandlers("Client")), "await Client.AllowanceChargeTotalsAsync(document)");
        Expect.Contains(AllowanceScreens.AngularBoxTest(), "/allowance-charge-totals?document=7");
        Expect.Contains(AllowanceScreens.BlazorBoxTest(), "/allowance-charge-totals?document=7");
    }

    [TestMethod]
    public async Task Every_front_end_list_shows_the_totals_box()
    {
        Expect.Contains(await Run("TSX_Page_v1.tt"), "aria-label=\"" + AllowanceScreens.DocumentLabel + "\"");
        Expect.Contains(await Run("TSX_Api_v1.tt"), "allowanceTotals: (document: string)");
        Expect.Contains(await Run("TS_Component_v1.tt"), "showAllowances(): void {");
        Expect.Contains(await Run("TS_Service_v1.tt"), "allowanceTotals(document: string)");
        Expect.Contains(await Run("BLZ_Page_v1.tt"), "ShowAllowancesAsync");
        foreach (string template in new[] { "WinUI3_MasterScreen_v1.tt", "UNO_MasterScreen_v1.tt" })
        {
            string text = await Run(template);
            Expect.Contains(text, "AutomationProperties.Name=\"" + AllowanceScreens.DocumentLabel + "\"");
            Expect.Contains(text, "public async Task ShowAllowancesAsync()");
        }
        Expect.DoesNotContain(await Run("TSX_Page_v1.tt", "InvoiceAllowanceCharge"), AllowanceScreens.DocumentLabel);
    }

    [TestMethod]
    public void The_winui3_dialog_runs_the_rule_before_it_saves()
    {
        string text = AllowanceScreens.WinUI3Check(Rows(), Project());
        Expect.Contains(text, "if (allowanceBase < 0 || allowancePercent < 0 || allowanceAmount < 0)");
        Expect.Contains(text, "entity.Amount = (decimal)Math.Round(allowanceBase * allowancePercent / 100m, 2, MidpointRounding.AwayFromZero);");
        Expect.Contains(text, "entity.Percentage = (decimal)Math.Round(allowanceAmount / allowanceBase * 100m, 4, MidpointRounding.AwayFromZero);");
        Expect.Contains(text, "ErrorMessage = \"" + AllowanceChargeWords.NeedsAmount + "\";");
        Assert.AreEqual("", AllowanceScreens.WinUI3Check(Sample.Table("Other", [Sample.Column("OtherId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)], []), Project()));
    }

    [TestMethod]
    public void The_master_detail_screens_show_the_totals_under_the_grid_of_the_table()
    {
        var project = Project();
        Assert.IsTrue(AllowanceScreens.IsChild("InvoiceAllowanceCharge", "InvoiceId", project));
        Assert.IsFalse(AllowanceScreens.IsChild("InvoiceAllowanceCharge", "OtherId", project));
        Assert.IsFalse(AllowanceScreens.IsChild("InvoiceAllowanceCharge", "InvoiceId", Project("InvoiceAllowanceCharge")), "no document column, no line");
        Expect.Contains(string.Join(" ", AllowanceScreens.ReactChildLoad("InvoiceAllowanceCharge", "/api", "invoiceallowancecharges")), "/api/invoiceallowancecharges/allowance-charge-totals?document=");
        Expect.Contains(AllowanceScreens.ReactChildText("charge"), "{chargeAllowance && <p");
        Expect.Contains(string.Join(" ", AllowanceScreens.AngularChildLoad("charge", "api", "invoiceallowancecharges")), "this.chargeAllowance =");
        Expect.Contains(AllowanceScreens.AngularChildText("charge"), "*ngIf=\"chargeAllowance\"");
        Expect.Contains(AllowanceScreens.BlazorChildLoad("childCharge"), "childChargeClient.AllowanceChargeTotalsAsync(");
        Expect.Contains(AllowanceScreens.BlazorChildText("childCharge"), "@childChargeAllowanceText");
        Expect.Contains(AllowanceScreens.DesktopDialogRow("InvoiceAllowanceCharge", "InvoiceId", "Charges", project), "ViewModel.ChargesAllowanceSummary");
        Assert.AreEqual("", AllowanceScreens.DesktopDialogRow("InvoiceAllowanceCharge", "InvoiceId", "Charges", Project("InvoiceAllowanceCharge")));
        Expect.Contains(AllowanceScreens.UnoDialogMembers("InvoiceAllowanceCharge", "InvoiceId", "Charges", project, "_api.Charges", "_editing!.Id"), "await _api.Charges.AllowanceChargeTotalsAsync(");
    }

    [TestMethod]
    public void The_generated_api_tests_play_the_amount_the_refusals_and_the_totals()
    {
        var table = Rows();
        var project = Project();
        string text = AllowanceApiTests.Methods(ApiTestPlan.For(table, project)!, table, project);
        Expect.Contains(text, "public async Task The_amount_is_worked_out_from_the_base_and_the_percentage()");
        Expect.Contains(text, "public async Task The_percentage_is_worked_out_from_the_base_and_the_amount()");
        Expect.Contains(text, "public async Task A_negative_value_is_refused()");
        Expect.Contains(text, "public async Task A_percentage_without_a_base_is_refused()");
        Expect.Contains(text, "public async Task A_row_with_nothing_to_work_an_amount_from_is_refused()");
        Expect.Contains(text, "public async Task The_totals_of_a_document_add_up_the_allowances_and_the_charges()");
        Expect.Contains(text, AllowanceChargeWords.InvalidType);
        string plain = AllowanceApiTests.Methods(ApiTestPlan.For(table, Project("InvoiceAllowanceCharge"))!, table, Project("InvoiceAllowanceCharge"));
        Assert.IsFalse(plain.Contains("The_totals_of_a_document"), "no document column, no totals test");
        Assert.AreEqual("", AllowanceApiTests.Methods(ApiTestPlan.For(table, project)!, table, ProjectSettings.FromValues([new("ProjectName", "Shop")])));
    }

    [TestMethod]
    public void The_shape_detector_suggests_a_table_of_allowances_and_charges_with_its_document()
    {
        var invoice = Sample.Table("Invoice", [Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Customer", SqlDbType.NVarChar, ordinal: 2)], []);
        var rows = Sample.Table("InvoiceAllowanceCharge", Rows().Columns.ToList(), [Sample.ForeignKey("InvoiceId", "Invoice", "InvoiceId", "Customer")]);
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [invoice, rows] };
        var plain = ProjectSettings.FromValues([new("ProjectName", "Shop"), new("AccessMode", "Ef")]);
        var found = DatabaseShapeDetector.Of(database, plain).Single(d => d.Suggestion.Shape == "Allowances and charges");
        Assert.AreEqual("InvoiceAllowanceCharge", found.Table);
        Assert.AreEqual("AllowanceCharges", found.Suggestion.Setting);
        Assert.AreEqual("InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId", found.Suggestion.Item);
        Assert.IsTrue(found.Suggestion.Fits, found.Suggestion.Note);
        Assert.IsFalse(DatabaseShapeDetector.Of(database, Project()).Any(d => d.Suggestion.Shape == "Allowances and charges"), "a listed table is not suggested again");
        var plainName = Sample.Table("Shelf", Rows().Columns.ToList(), []);
        Assert.IsFalse(DatabaseShapeDetector.Of(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [plainName] }, plain).Any(d => d.Suggestion.Shape == "Allowances and charges"), "the table's name must say allowance, charge or discount");
    }

    [TestMethod]
    public void The_generated_python_and_rust_tests_play_the_amount_the_refusals_and_the_totals()
    {
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.Sqlite, Tables = [Rows()] };
        var project = ProjectSettings.FromValues([new("ProjectName", "Shop"), new("AllowanceCharges", "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId"), new("AccessMode", "Ef"), new("PythonTests", "true"), new("RustTests", "true")]);
        Assert.IsTrue(PythonAllowanceTests.Applies(database, project));
        Assert.IsTrue(RustAllowanceTests.Applies(database, project));
        string python = PythonAllowanceTests.File(database, project);
        Expect.Contains(python, "def test_the_amount_is_worked_out_from_the_base_and_the_percentage(rows):");
        Expect.Contains(python, "def test_a_negative_value_is_refused(rows):");
        Expect.Contains(python, "def test_the_totals_of_a_document_add_up_the_allowances_and_the_charges(rows):");
        Expect.Contains(python, AllowanceChargeWords.InvalidType);
        string rust = RustAllowanceTests.File(database, project);
        Expect.Contains(rust, "async fn the_percentage_is_worked_out_from_the_base_and_the_amount()");
        Expect.Contains(rust, "async fn a_row_with_nothing_to_work_an_amount_from_is_refused()");
        Expect.Contains(rust, "/allowance-charge-totals?document={document}");
        Assert.DoesNotContain("§", python + rust);
        var plain = ProjectSettings.FromValues([new("ProjectName", "Shop"), new("AllowanceCharges", "InvoiceAllowanceCharge"), new("AccessMode", "Ef"), new("PythonTests", "true"), new("RustTests", "true")]);
        Assert.DoesNotContain("totals", PythonAllowanceTests.File(database, plain));
        Assert.DoesNotContain("allowance-charge-totals", RustAllowanceTests.File(database, plain));
        Assert.IsFalse(PythonAllowanceTests.Applies(database, Project()), "PythonTests is off");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "PY_TestAllowance");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "RS_TestAllowance");
    }

    [TestMethod]
    public void A_line_column_narrows_the_totals_to_the_rows_of_one_line_on_every_api()
    {
        var table = Sample.Table("InvoiceAllowanceCharge",
        [
            Sample.Column("InvoiceAllowanceChargeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("InvoiceId", SqlDbType.Int, ordinal: 2),
            Sample.Column("InvoiceLineId", SqlDbType.Int, nullable: true, ordinal: 3),
            Sample.Column("IsCharge", SqlDbType.Bit, ordinal: 4),
            Sample.Column("BaseAmount", SqlDbType.Decimal, precision: 9, scale: 2, nullable: true, ordinal: 5),
            Sample.Column("Percentage", SqlDbType.Decimal, precision: 9, scale: 4, nullable: true, ordinal: 6),
            Sample.Column("Amount", SqlDbType.Decimal, precision: 9, scale: 2, ordinal: 7),
        ], []);
        const string setting = "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId:InvoiceLineId";
        var project = ProjectSettings.FromValues([new("ProjectName", "Shop"), new("AllowanceCharges", setting), new("AccessMode", "Ef"), new("PythonTests", "true"), new("RustTests", "true")]);
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.Sqlite, Tables = [table] };
        Assert.IsNull(AllowanceChargePlan.Problem(table, project));
        Assert.AreEqual("InvoiceLineId", AllowanceChargePlan.For(table, project)!.Line!.Name);
        string api = RowShapeWriter.File(database, project, "ShopContext", []);
        Expect.Contains(api, "AllowanceChargeTotalsAsync(ShopContext context, int? document, int? line)");
        Expect.Contains(api, "query = query.Where(x => x.InvoiceLineId == line);");
        Expect.Contains(api, "[FromQuery(Name = \"line\")] int? line");
        Expect.Contains(AllowanceApiTests.Methods(ApiTestPlan.For(table, project)!, table, project), "public async Task The_totals_of_one_line_count_only_the_rows_of_that_line()");
        Expect.Contains(PythonAllowanceTests.File(database, project), "def test_the_totals_of_one_line_count_only_the_rows_of_that_line(rows):");
        Expect.Contains(RustAllowanceTests.File(database, project), "async fn the_totals_of_one_line_count_only_the_rows_of_that_line()");
        var same = ProjectSettings.FromValues([new("ProjectName", "Shop"), new("AllowanceCharges", "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId:InvoiceId")]);
        Assert.IsNotNull(AllowanceChargePlan.Problem(table, same), "the line column must differ from the document column");
        var noDocument = ProjectSettings.FromValues([new("ProjectName", "Shop"), new("AllowanceCharges", "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount::InvoiceLineId")]);
        Assert.IsNotNull(AllowanceChargePlan.Problem(table, noDocument), "a line column needs the document column");
    }
}
