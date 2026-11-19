using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rust;
using CodeGenNew.Core.Tax;

namespace CodeGenNew.Tests;

/// <summary> TaxLines: a document line that carries the rate of its tax category and the tax worked out from it. </summary>
[TestClass]
public class TaxLineTests
{
    private static TableModel Categories() => Sample.Table("TaxCategory",
    [
        Sample.Column("TaxCategoryId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Code", SqlDbType.VarChar, characters: 10, ordinal: 2),
        Sample.Column("Rate", SqlDbType.Decimal, ordinal: 3),
        Sample.Column("ValidFrom", SqlDbType.Date, ordinal: 4),
        Sample.Column("ValidTo", SqlDbType.Date, nullable: true, ordinal: 5),
    ], []);

    private static TableModel Lines(bool nullableCategory = false) => Sample.Table("InvoiceLine",
    [
        Sample.Column("InvoiceLineId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("TaxCategoryId", SqlDbType.Int, nullable: nullableCategory, ordinal: 2),
        Sample.Column("TaxRate", SqlDbType.Decimal, ordinal: 3),
        Sample.Column("LineTotal", SqlDbType.Decimal, ordinal: 4),
        Sample.Column("TaxAmount", SqlDbType.Decimal, ordinal: 5),
        Sample.Column("LineDate", SqlDbType.Date, ordinal: 6),
        Sample.Column("InvoiceId", SqlDbType.Int, ordinal: 7),
    ], []);

    private static DatabaseModel Database(TableModel? lines = null) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Categories(), lines ?? Lines()] };

    private static ProjectSettings Project(string lines = "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory") =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["TaxLines"] = lines, ["AccessMode"] = "Ef" });

    [TestMethod]
    public void The_setting_names_the_columns_and_the_category_table()
    {
        var database = Database();
        var project = Project();
        var shape = TaxLinePlan.For(database.Tables[1], project, database)!;
        Assert.AreEqual("TaxCategoryId", shape.Category.Name);
        Assert.AreEqual("TaxRate", shape.Rate.Name);
        Assert.AreEqual("LineTotal", shape.Taxable.Name);
        Assert.AreEqual("TaxAmount", shape.Tax.Name);
        Assert.AreEqual("TaxCategory", shape.CategoryTable.TableName);
        Assert.AreEqual("Rate", shape.CategoryRate.Name);
        Assert.HasCount(0, RowShapePlan.Problems(database, project));
        Assert.IsTrue(RowShapeWriter.HasSaveCheck(database.Tables[1], project));
        Assert.IsTrue(RowShapePlan.Has(database.Tables[1], project));
        Assert.IsNull(TaxLinePlan.For(database.Tables[1], Project("Other"), database));
        Assert.IsTrue(TaxLinePlan.IsOwnedColumn(database.Tables[1], project, database.Tables[1].Columns[2]));
        Assert.IsTrue(TaxLinePlan.IsOwnedColumn(database.Tables[1], project, database.Tables[1].Columns[4]));
        Assert.IsFalse(TaxLinePlan.IsOwnedColumn(database.Tables[1], project, database.Tables[1].Columns[3]));
    }

    [TestMethod]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount", "needs its category, rate, taxable amount and tax amount columns")]
    [DataRow("Missing:A:B:C:D:TaxCategory", "is not a table of the database")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:Nothing", "Nothing is not a table of the database")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:InvoiceLine", "must be another table")]
    [DataRow("InvoiceLine:LineTotal:TaxRate:LineTotal:TaxAmount:TaxCategory", "whole-number column of the same type")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxCategoryId:LineTotal:TaxAmount:TaxCategory", "must be a decimal, double or float column")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:LineTotal:TaxCategory", "four different columns")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Code", "the rate as a fraction")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_api_copies_the_category_rate_once_and_works_out_the_tax()
    {
        var database = Database();
        var project = Project();
        RatePlan.Remember(database, project);
        string text = RowShapeWriter.File(database, project, "ShopContext", []);
        Expect.Contains(text, "CheckTaxAsync(ShopContext context, InvoiceLine row, bool isNew)");
        Expect.Contains(text, "if (await CheckTaxAsync(context, row, isNew) is { } taxProblem)");
        Expect.Contains(text, "context.Set<TaxCategory>().AsNoTracking().Where(x => x.TaxCategoryId == category)");
        Expect.Contains(text, "row.TaxRate = (decimal)found.Rate;");
        Expect.Contains(text, "row.TaxAmount = (decimal)Math.Round((decimal)row.LineTotal * (decimal)row.TaxRate, 2, MidpointRounding.AwayFromZero);");
        Expect.Contains(text, "\"" + TaxWords.UnknownType + "\"");
        Assert.DoesNotContain("category is null", text);
        Assert.DoesNotContain("§", text);
    }

    [TestMethod]
    public void A_line_without_a_category_carries_rate_zero()
    {
        var database = Database(Lines(nullableCategory: true));
        var project = Project();
        RatePlan.Remember(database, project);
        string text = RowShapeWriter.File(database, project, "ShopContext", []);
        Expect.Contains(text, "if (category is null)");
        Expect.Contains(text, "row.TaxRate = 0m;");
        Expect.Contains(text, "row.TaxAmount = 0m;");
        Assert.DoesNotContain("§", text);
    }

    private const string Dated = "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code";

    [TestMethod]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:Code", "must be a date column that cannot be NULL")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate::ValidTo", "needs a valid-from column")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:Description", "must be a date column that cannot be NULL")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:LineTotal", "must be a whole-number column")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Rate", "must be a text column")]
    public void A_dating_or_subtotals_part_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_dating_the_day_the_document_and_the_label_are_read()
    {
        var database = Database();
        var shape = TaxLinePlan.For(database.Tables[1], Project(Dated), database)!;
        Assert.AreEqual("ValidFrom", shape.ValidFrom!.Name);
        Assert.AreEqual("ValidTo", shape.ValidTo!.Name);
        Assert.AreEqual("LineDate", shape.Day!.Name);
        Assert.AreEqual("InvoiceId", shape.Document!.Name);
        Assert.AreEqual("Code", shape.Label!.Name);
        Assert.HasCount(0, RowShapePlan.Problems(database, Project(Dated)));
        Assert.AreEqual("Code", TaxLinePlan.For(database.Tables[1], Project(), database)!.Label!.Name, "the label defaults to Code or Name");
        Assert.IsNull(TaxLinePlan.For(database.Tables[1], Project(), database)!.ValidFrom);
    }

    [TestMethod]
    public void The_group_columns_keep_the_periods_of_a_category_group_apart()
    {
        var database = Database();
        var project = Project(Dated + ":Code");
        CollectionAssert.Contains(project.EffectiveDatedTables, "TaxCategory:ValidFrom:ValidTo:Code");
        Assert.IsNotNull(EffectiveDatePlan.For(database.Tables[0], project));
        Assert.HasCount(0, RowShapePlan.Problems(database, project));
        Assert.DoesNotContain("TaxCategory", string.Join(",", Project(Dated).EffectiveDatedTables), "no group, no period rule");
        var own = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["TaxLines"] = Dated + ":Code", ["AccessMode"] = "Ef", ["EffectiveDatedTables"] = "TaxCategory:ValidFrom:ValidTo:Name" });
        Assert.HasCount(1, own.EffectiveDatedTables.Where(item => item.StartsWith("TaxCategory:")), "a table the project lists itself keeps its own entry");
    }

    [TestMethod]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom::LineDate:InvoiceId:Code:Code", "needs a valid-from and a valid-to column")]
    [DataRow("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code:Nothing", "is not a column")]
    public void Group_columns_that_cannot_work_are_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_api_refuses_a_category_that_is_not_in_force_and_answers_the_subtotals()
    {
        var database = Database();
        var project = Project(Dated);
        RatePlan.Remember(database, project);
        string text = RowShapeWriter.File(database, project, "ShopContext", []);
        Expect.Contains(text, "var asOf = row.LineDate;");
        Expect.Contains(text, "if (found.ValidFrom > asOf || (found.ValidTo is { } validTo && validTo < asOf))");
        Expect.Contains(text, "RowShapeProblems.TaxNotInForce(");
        Expect.Contains(text, "\"" + TaxWords.NotInForceType + "\"");
        Expect.Contains(text, "stored.LineDate.Date == row.LineDate.Date");
        Expect.Contains(text, "TaxSubtotalsAsync(ShopContext context, int? document)");
        Expect.Contains(text, "x.InvoiceId == document");
        Expect.Contains(text, "Label = c.Code");
        Expect.Contains(text, "app.MapGet(\"/api/invoicelines/tax-subtotals\"");
        Assert.DoesNotContain("§", text);
        string plain = RowShapeWriter.File(database, Project(), "ShopContext", []);
        Assert.DoesNotContain("tax-subtotals", plain);
    }

    [TestMethod]
    public void The_python_helper_and_route_carry_the_same_rules()
    {
        var names = new PythonTaxWriter.Names("InvoiceLine", "id", "tax_category_id", false, "tax_rate", "Decimal", "line_total", "Decimal", "tax_amount", "Decimal", 2, "TaxCategory", "tax_category_id", "rate",
            "valid_from", "valid_to", "date", "line_date", "date", "invoice_id", "code");
        string helper = PythonTaxWriter.Helper(names);
        Expect.Contains(helper, "def apply_tax(session: Session, row, stored: tuple | None) -> None:");
        Expect.Contains(helper, "return (row.tax_category_id, day_of(row.line_date))");
        Expect.Contains(helper, "found = session.get(TaxCategory, row.tax_category_id)");
        Expect.Contains(helper, "if found.valid_from > as_of or (found.valid_to is not None and found.valid_to < as_of):");
        Expect.Contains(helper, TaxWords.NotInForceType);
        Expect.Contains(helper, "row.tax_amount = (row.line_total * row.tax_rate).quantize(Decimal(1).scaleb(-2), rounding=ROUND_HALF_UP)");
        Assert.DoesNotContain("§", helper);
        string route = PythonTaxWriter.Route(names, "/invoicelines", "", "");
        Expect.Contains(route, "@router.get(\"/invoicelines/tax-subtotals\")");
        Expect.Contains(route, "InvoiceLine.invoice_id == document");
        Assert.DoesNotContain("§", route);
        Assert.AreEqual("", PythonTaxWriter.Route(names with { Document = null }, "/invoicelines", "", ""));
    }

    [TestMethod]
    public void The_rust_code_carries_the_same_rules()
    {
        var names = new RustTaxWriter.Names("InvoiceLine", "\"InvoiceLine\"", "*", "tax_category", "tax_category_id", "i32", true, "tax_rate", "f64", "line_total", "f64", "tax_amount", "f64", 2, "rate", "f64",
            "valid_from", "NaiveDate", "valid_to", true, "line_date", "NaiveDate", "invoice_id", "\"InvoiceId\"", "i32", "code");
        string code = RustTaxWriter.Code(names);
        Expect.Contains(code, "async fn apply_tax(pool: &Pool, row: &mut InvoiceLine, stored: Option<&InvoiceLine>) -> ApiResult<()> {");
        Expect.Contains(code, "let Some(category) = row.tax_category_id.clone() else {");
        Expect.Contains(code, "let as_of = row.line_date;");
        Expect.Contains(code, "found.valid_from > as_of || found.valid_to.map_or(false, |end| end < as_of)");
        Expect.Contains(code, TaxWords.NotInForceType);
        Assert.DoesNotContain("§", code);
        string subtotals = RustTaxWriter.Subtotals(names);
        Expect.Contains(subtotals, "async fn tax_subtotals(State(pool): State<Pool>, Query(query): Query<TaxSubtotalsQuery>)");
        Assert.DoesNotContain("§", subtotals);
        Assert.AreEqual("", RustTaxWriter.Subtotals(names with { Document = null }));
    }

    [TestMethod]
    public void The_winui3_dialog_runs_the_rule_before_it_saves()
    {
        var database = Database();
        var project = Project(Dated);
        RatePlan.Remember(database, project);
        string text = TaxScreens.WinUI3Check(database.Tables[1], project, "_context");
        Expect.Contains(text, "var taxAsOf = entity.LineDate;");
        Expect.Contains(text, "ErrorMessage = $\"" + TaxWords.NotInForce("{taxCategory}", "{taxAsOf:yyyy-MM-dd}") + "\";");
        Expect.Contains(text, "entity.TaxAmount = (decimal)Math.Round((decimal)entity.LineTotal * (decimal)entity.TaxRate, 2, MidpointRounding.AwayFromZero);");
        Assert.AreEqual("", TaxScreens.WinUI3Check(database.Tables[0], project, "_context"));
    }

    [TestMethod]
    public void The_forms_do_not_treat_the_rate_and_the_tax_as_dates_or_boxes()
    {
        var database = Database();
        var project = Project();
        var form = FormModel.For(database.Tables[1], project);
        Assert.IsFalse(form.DateColumns.Any(c => c.Name == "TaxRate" || c.Name == "TaxAmount"));
        Assert.IsTrue(form.Field(database.Tables[1].Columns[2]).IsSystemStampField);
        Assert.IsTrue(form.Field(database.Tables[1].Columns[4]).IsSystemStampField);
        Assert.IsFalse(form.Field(database.Tables[1].Columns[3]).IsSystemStampField);
    }

    [TestMethod]
    public void The_list_screens_get_a_tax_box_only_when_the_entry_names_the_document()
    {
        var database = Database();
        Assert.IsTrue(TaxScreens.HasBox(database.Tables[1], Project(Dated)));
        Assert.IsFalse(TaxScreens.HasBox(database.Tables[1], Project()));
        Assert.IsFalse(TaxScreens.HasBox(database.Tables[0], Project(Dated)));
        Expect.Contains(TaxScreens.ReactApiLine(), "taxSubtotals: (document: string) => request<Array<");
        Expect.Contains(string.Join(" ", TaxScreens.ReactBox()), "aria-label=\"" + TaxScreens.DocumentLabel + "\"");
        Expect.Contains(string.Join(" ", TaxScreens.AngularBox()), "*ngFor=\"let row of taxRows\"");
        Expect.Contains(string.Join(" ", TaxScreens.BlazorHandlers("Client")), "await Client.TaxSubtotalsAsync(document)");
        Expect.Contains(TaxScreens.AngularBoxTest(), "/tax-subtotals?document=7");
        Expect.Contains(TaxScreens.BlazorBoxTest(), "/tax-subtotals?document=7");
        Expect.Contains(string.Join(" ", TaxScreens.ReactChildLoad("InvoiceLine", "/api", "invoicelines")), "/api/invoicelines/tax-subtotals?document=");
        Expect.Contains(TaxScreens.ReactChildText("invoiceLine"), "{invoiceLineTax && <p");
        Expect.Contains(string.Join(" ", TaxScreens.AngularChildLoad("invoiceLine", "api", "invoicelines")), "this.invoiceLineTax =");
        Expect.Contains(TaxScreens.AngularChildText("invoiceLine"), "*ngIf=\"invoiceLineTax\"");
        Expect.Contains(TaxScreens.BlazorChildLoad("childInvoiceLine"), "childInvoiceLineClient.TaxSubtotalsAsync(");
        Expect.Contains(TaxScreens.BlazorChildText("childInvoiceLine"), "@childInvoiceLineTaxText");
        Assert.IsFalse(TaxScreens.IsChild("InvoiceLine", "InvoiceId", Project()), "no document part, no summary");
    }

    [TestMethod]
    public void The_generated_api_tests_play_the_rate_the_unknown_category_the_validity_and_the_subtotals()
    {
        var database = Database();
        var project = Project(Dated);
        RatePlan.Remember(database, project);
        string text = TaxApiTests.Methods(ApiTestPlan.For(database.Tables[1], project)!, database.Tables[1], project);
        Expect.Contains(text, "public async Task A_line_carries_the_rate_of_its_category_and_the_tax_worked_out_from_it()");
        Expect.Contains(text, "public async Task A_line_keeps_its_rate_when_the_rate_of_its_category_changes()");
        Expect.Contains(text, "public async Task A_line_with_a_category_that_does_not_exist_is_refused()");
        Expect.Contains(text, "public async Task A_category_outside_its_validity_is_refused()");
        Expect.Contains(text, "public async Task The_subtotals_of_a_document_add_up_by_category_and_rate()");
        Expect.Contains(text, TaxWords.NotInForceType);
        Assert.DoesNotContain("§", text);
        string plain = TaxApiTests.Methods(ApiTestPlan.For(database.Tables[1], Project())!, database.Tables[1], Project());
        Assert.DoesNotContain("A_category_outside_its_validity", plain);
        Assert.DoesNotContain("The_subtotals_of_a_document", plain);
        Assert.IsTrue(TaxApiTests.Applies(database.Tables[1], project));
        Assert.IsFalse(TaxApiTests.Applies(database.Tables[0], project));
    }

    [TestMethod]
    public void The_generated_python_and_rust_tests_play_the_same_cases()
    {
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.Sqlite, Tables = Database().Tables };
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["TaxLines"] = Dated, ["AccessMode"] = "Ef", ["PythonTests"] = "true", ["RustTests"] = "true" });
        RatePlan.Remember(database, project);
        Assert.IsNotNull(TaxTestPlan.For(database, project));
        string python = PythonTaxTests.File(database, project);
        string rust = RustTaxTests.File(database, project);
        foreach (string name in new[] { "a_line_carries_the_rate_of_its_category_and_the_tax_worked_out_from_it", "a_line_keeps_its_rate_when_the_rate_of_its_category_changes",
            "a_line_with_a_category_that_does_not_exist_is_refused", "a_category_outside_its_validity_is_refused", "the_subtotals_of_a_document_add_up_by_category_and_rate" })
        {
            Expect.Contains(python, "def test_" + name + "(rows)");
            Expect.Contains(rust, "async fn " + name + "()");
        }
        Expect.Contains(python, TaxWords.NotInForceType);
        Expect.Contains(rust, TaxWords.NotInForceType);
        Assert.DoesNotContain("§", python + rust);
        Assert.IsTrue(PythonTaxTests.Applies(database, project));
        Assert.IsTrue(RustTaxTests.Applies(database, project));
        var plain = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["TaxLines"] = "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory", ["AccessMode"] = "Ef", ["PythonTests"] = "true", ["RustTests"] = "true" });
        Assert.DoesNotContain("not_in_force", PythonTaxTests.File(database, plain));
        Assert.DoesNotContain("tax-subtotals", RustTaxTests.File(database, plain));
        Assert.IsFalse(PythonTaxTests.Applies(database, Project()), "PythonTests is off");
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "PY_TestTax");
    }

    private static DatabaseModel Linked()
    {
        var lines = Sample.Table("InvoiceLine", Lines().Columns.ToList(), [Sample.ForeignKey("TaxCategoryId", "TaxCategory", "TaxCategoryId", "Code")]);
        return new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Categories(), lines] };
    }

    [TestMethod]
    public void The_shape_detector_suggests_the_tax_lines_with_the_parts_it_finds()
    {
        var database = Linked();
        var plain = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" });
        var found = DatabaseShapeDetector.Of(database, plain).Single(d => d.Suggestion.Shape == "Tax lines");
        Assert.AreEqual("InvoiceLine", found.Table);
        Assert.AreEqual("TaxLines", found.Suggestion.Setting);
        Assert.AreEqual("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code", found.Suggestion.Item);
        Assert.IsTrue(found.Suggestion.Fits, found.Suggestion.Note);
        Assert.IsFalse(DatabaseShapeDetector.Of(database, Project()).Any(d => d.Suggestion.Shape == "Tax lines"), "a listed table is not suggested again");
    }

    [TestMethod]
    public void The_shape_detector_leaves_out_what_the_tables_do_not_have_and_skips_other_tables()
    {
        var categories = Sample.Table("TaxCategory", [Sample.Column("TaxCategoryId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Rate", SqlDbType.Decimal, ordinal: 2)], []);
        var lines = Sample.Table("InvoiceLine", Lines().Columns.Take(5).ToList(), [Sample.ForeignKey("TaxCategoryId", "TaxCategory", "TaxCategoryId", "Rate")]);
        var plain = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" });
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [categories, lines] };
        Assert.AreEqual("InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate", DatabaseShapeDetector.Of(database, plain).Single(d => d.Suggestion.Shape == "Tax lines").Suggestion.Item);
        var notTax = Sample.Table("Shelf", [Sample.Column("ShelfId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Rate", SqlDbType.Decimal, ordinal: 2)], []);
        var other = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [notTax, Sample.Table("InvoiceLine", Lines().Columns.Take(5).ToList(), [Sample.ForeignKey("TaxCategoryId", "Shelf", "ShelfId", "Rate")])] };
        Assert.IsFalse(DatabaseShapeDetector.Of(other, plain).Any(d => d.Suggestion.Shape == "Tax lines"), "the parent's name must say tax");
    }

    [TestMethod]
    public void The_desktop_lists_and_the_document_dialog_show_the_tax_of_a_document()
    {
        var database = Linked();
        var project = Project(Dated);
        RatePlan.Remember(database, project);
        Expect.Contains(TaxDesktopScreens.Box(), "AutomationProperties.Name=\"" + TaxScreens.DocumentLabel + "\"");
        Expect.Contains(TaxDesktopScreens.Handlers(), "ViewModel.ShowTaxAsync()");
        string winui3 = TaxDesktopScreens.WinUI3Read(database.Tables[1], project, "_context");
        Expect.Contains(winui3, "x.InvoiceId == documentId");
        Expect.Contains(winui3, "TaxText = groups.Count == 0");
        string uno = TaxDesktopScreens.UnoRead();
        Expect.Contains(uno, "await Client.TaxSubtotalsAsync(document)");
        Assert.IsNotNull(TaxDesktopScreens.ChildShape("InvoiceLine", "InvoiceId", project));
        Assert.IsNull(TaxDesktopScreens.ChildShape("InvoiceLine", "OtherId", project), "the child's foreign key must be the document column");
        Assert.IsNull(TaxDesktopScreens.ChildShape("InvoiceLine", "InvoiceId", Project()), "no document part, no summary");
        Expect.Contains(TaxDesktopScreens.DialogMembers("InvoiceLine", "InvoiceId", "InvoiceLine", project, "_context", "_editing!.InvoiceId"), "private async Task LoadInvoiceLineTaxAsync()");
        Assert.AreEqual("", TaxDesktopScreens.LoadCall("Other", "InvoiceId", "Other", project));
        Assert.DoesNotContain("§", winui3 + uno);
    }
}
