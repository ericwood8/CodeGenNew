using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Rust;
using CodeGenNew.Core.Totals;

namespace CodeGenNew.Tests.Tax;

/// <summary> DocumentTotals: a document header whose totals the server writes from its lines, its tax and its allowance rows. </summary>
[TestClass]
public class DocumentTotalsTests
{
    private const string Setting = "Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Allowances=AllowanceTotal:Charges=ChargeTotal:TaxExclusive=TaxExclusive:Tax=TaxTotal:TaxInclusive=TaxInclusive:Prepaid=Prepaid:Rounding=Rounding:Payable=Payable:Currency=Currency:Round=CHF/0.05";

    private static TableModel Header() => Sample.Table("Invoice",
    [
        Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Customer", SqlDbType.VarChar, characters: 60, ordinal: 2),
        Sample.Column("Currency", SqlDbType.VarChar, characters: 3, ordinal: 3),
        Sample.Column("ItemsTotal", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 4),
        Sample.Column("AllowanceTotal", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 5),
        Sample.Column("ChargeTotal", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 6),
        Sample.Column("TaxExclusive", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 7),
        Sample.Column("TaxTotal", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 8),
        Sample.Column("TaxInclusive", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 9),
        Sample.Column("Prepaid", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 10),
        Sample.Column("Rounding", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 11),
        Sample.Column("Payable", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 12),
    ], []);

    private static TableModel Lines() => Sample.Table("InvoiceLine",
    [
        Sample.Column("InvoiceLineId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("InvoiceId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Description", SqlDbType.VarChar, characters: 100, ordinal: 3),
        Sample.Column("TaxCategoryId", SqlDbType.Int, nullable: true, ordinal: 4),
        Sample.Column("TaxRate", SqlDbType.Decimal, precision: 9, scale: 4, ordinal: 5),
        Sample.Column("LineTotal", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 6),
        Sample.Column("TaxAmount", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 7),
    ], [Sample.ForeignKey("InvoiceId", "Invoice", "InvoiceId", "Customer"), Sample.ForeignKey("TaxCategoryId", "TaxCategory", "TaxCategoryId", "Code")]);

    private static TableModel Categories() => Sample.Table("TaxCategory",
    [
        Sample.Column("TaxCategoryId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Code", SqlDbType.VarChar, characters: 10, ordinal: 2),
        Sample.Column("Rate", SqlDbType.Decimal, precision: 9, scale: 4, ordinal: 3),
    ], []);

    private static TableModel Extras() => Sample.Table("InvoiceAllowanceCharge",
    [
        Sample.Column("InvoiceAllowanceChargeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("InvoiceId", SqlDbType.Int, ordinal: 2),
        Sample.Column("IsCharge", SqlDbType.Bit, ordinal: 3),
        Sample.Column("BaseAmount", SqlDbType.Decimal, precision: 18, scale: 2, nullable: true, ordinal: 4),
        Sample.Column("Percentage", SqlDbType.Decimal, precision: 9, scale: 4, nullable: true, ordinal: 5),
        Sample.Column("Amount", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 6),
    ], [Sample.ForeignKey("InvoiceId", "Invoice", "InvoiceId", "Customer")]);

    private static DatabaseModel Database(TableModel? header = null) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [header ?? Header(), Lines(), Extras(), Categories()] };

    private static ProjectSettings Project(string setting = Setting) => ProjectSettings.FromValues(new Dictionary<string, string>
    {
        ["ProjectName"] = "Shop", ["DocumentTotals"] = setting, ["AllowanceCharges"] = "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId", ["TaxLines"] = "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate", ["AccessMode"] = "Ef",
    });

    [TestMethod]
    public void The_plan_finds_the_lines_the_allowance_table_and_every_total()
    {
        var shape = DocumentTotalsPlan.For(Header(), Project(), Database())!;
        Assert.AreEqual("InvoiceLine", shape.Lines.TableName);
        Assert.AreEqual("InvoiceId", shape.LineKey.Name);
        Assert.AreEqual("LineTotal", shape.LineAmount.Name);
        Assert.AreEqual("InvoiceAllowanceCharge", shape.Allowance!.Table.TableName);
        Assert.AreEqual("Currency", shape.Currency!.Name);
        Assert.AreEqual(1, shape.Rounds.Count);
        Assert.AreEqual(0.05m, shape.Rounds[0].Increment);
        Assert.AreEqual(8, shape.Columns.Owned().Count());
        Assert.AreEqual("Prepaid", shape.Columns.Prepaid!.Name);
        Assert.AreEqual(0, DocumentTotalsPlan.Problems(Database(), Project()).Count);
    }

    [TestMethod]
    [DataRow("Python", true)]
    [DataRow("Rust", true)]
    [DataRow("Api", false)]
    public void Tax_over_a_limited_line_table_is_refused_for_the_python_and_rust_apis_only(string stack, bool refused)
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["DocumentTotals"] = "Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Tax=TaxTotal:Payable=Payable", ["TaxLines"] = "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate",
            ["AccessMode"] = "Ef", ["ApiAuth"] = "true", ["OwnedTables"] = "Invoice", ["Stacks"] = stack,
        });
        var problems = DocumentTotalsPlan.Problems(Database(), project);
        Assert.AreEqual(refused, problems.Any(p => p.Contains("write no tax amount")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_decimal_places_rule_reaches_the_plan_and_all_three_writers()
    {
        string setting = Setting + ":Decimals=JPY/0:Decimals=*/3";
        var shape = DocumentTotalsPlan.For(Header(), Project(setting), Database())!;
        CollectionAssert.AreEqual(new[] { 0, 3 }, shape.Places.Select(p => p.Places).ToArray());
        Assert.AreEqual(0, DocumentTotalsPlan.Problems(Database(), Project(setting)).Count);
        string csharp = DocumentTotalsWriter.Context([shape], "ShopContext");
        Assert.IsTrue(csharp.Contains("private static int Places(string? currency, int scale)") && csharp.Contains("return Math.Min(0, scale);") && csharp.Contains("Round(lines, Places(currency, 2))"));
        var names = new PythonTotalsWriter.Names(new("Invoice", "invoice", "invoice_id"), "invoice", new("InvoiceLine", "invoice_line", "invoice_line_id"), "invoice_id", "line_total", null, null, null, null, null, "prepaid", "currency",
            [("Lines", "items_total", "Decimal", 2)], [], "/api/invoices", shape.Places);
        string module = PythonTotalsWriter.Module(names);
        Assert.IsTrue(module.Contains("TOTALS_PLACES = {\"JPY\": 0}") && module.Contains("TOTALS_PLACES_ANY = 3") && module.Contains("money(lines, totals_places(currency, 2))"));
        RustTotalsWriter.Tbl Table(string t) => new(t, t.ToLowerInvariant(), "\"" + t + "\"", "\"Id\"");
        RustTotalsWriter.Field Field(string name, string type = "f64") => new(name, type, false);
        var rust = new RustTotalsWriter.Names(Table("Invoice"), Field("invoice_id", "i32"), "\"InvoiceId\"", Field("prepaid"), Field("currency", "String"), Table("InvoiceLine"), "\"InvoiceId\"", Field("invoice_id", "i32"), Field("line_total"), null, null, null, null,
            null, null, [("Lines", Field("items_total"), "\"ItemsTotal\"", 2)], [], "/api/invoices", shape.Places);
        string header = RustTotalsWriter.Header(rust);
        Assert.IsTrue(header.Contains("fn totals_places(currency: Option<&str>, scale: u32) -> u32") && header.Contains("scale.min(0)") && header.Contains("scale.min(3)") && header.Contains("totals_places(currency, 2)"));
    }

    [TestMethod]
    public void The_columns_the_server_writes_are_owned_and_the_prepaid_amount_is_not()
    {
        var header = Header();
        Assert.IsTrue(DocumentTotalsPlan.IsOwnedColumn(header, Project(), header.Columns.First(c => c.Name == "Payable")));
        Assert.IsTrue(DocumentTotalsPlan.IsOwnedColumn(header, Project(), header.Columns.First(c => c.Name == "ItemsTotal")));
        Assert.IsFalse(DocumentTotalsPlan.IsOwnedColumn(header, Project(), header.Columns.First(c => c.Name == "Prepaid")));
        Assert.IsFalse(DocumentTotalsPlan.IsOwnedColumn(header, Project(), header.Columns.First(c => c.Name == "Customer")));
        Assert.IsTrue(DocumentTotalsPlan.MoneyColumns(Project()).Contains("Invoice.Prepaid"));
        Assert.IsTrue(Project().MoneyColumns.Contains("Invoice.Payable"));
    }

    [TestMethod]
    [DataRow("Missing:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal", "is not a table of the database")]
    [DataRow("Invoice", "needs the line table")]
    [DataRow("Invoice:Missing:InvoiceId:LineTotal:Lines=ItemsTotal", "must be another table")]
    [DataRow("Invoice:InvoiceLine:Missing:LineTotal:Lines=ItemsTotal", "must be a column of InvoiceLine")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:Description:Lines=ItemsTotal", "must be a decimal, double or float column of InvoiceLine")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=Customer", "must be a decimal, double or float column of Invoice")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Payable=ItemsTotal", "a column of its own")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal:Speed=ItemsTotal", "is not a part of the totals")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal", "names no total to write")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Round=CHF/0.05", "needs Currency=Column")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Currency=Customer:Round=CHF/zero", "must read Currency/Increment")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Decimals=JPY/0", "needs Currency=Column")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Currency=Customer:Decimals=JPY/many", "must read Currency/Places")]
    [DataRow("Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Currency=Customer:Decimals=JPY/12", "must read Currency/Places")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_tax_part_needs_the_line_table_to_be_listed_in_tax_lines()
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["DocumentTotals"] = "Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Tax=TaxTotal", ["AccessMode"] = "Ef" });
        var problems = RowShapePlan.Problems(Database(), project);
        Assert.IsTrue(problems.Any(p => p.Contains("needs the line table InvoiceLine to be listed in TaxLines")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_shape_detector_suggests_a_header_with_the_columns_of_a_totals_block_and_stops_once_it_is_listed()
    {
        var bare = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["TaxLines"] = "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate",
            ["AllowanceCharges"] = "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId", ["AccessMode"] = "Ef",
        });
        var found = DatabaseShapeDetector.Of(Database(), bare).Where(d => d.Suggestion.Setting == "DocumentTotals").ToList();
        Assert.AreEqual(1, found.Count);
        Assert.AreEqual("Invoice", found[0].Table);
        Assert.IsTrue(found[0].Suggestion.Fits, found[0].Suggestion.Note);
        StringAssert.StartsWith(found[0].Suggestion.Item, "Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal");
        Assert.AreEqual(0, DatabaseShapeDetector.Of(Database(), Project()).Count(d => d.Suggestion.Setting == "DocumentTotals"));
    }

    [TestMethod]
    [DataRow("Python", true)]
    [DataRow("Rust", true)]
    [DataRow("Api", false)]
    public void A_header_that_is_the_tenant_table_is_refused_for_the_python_and_rust_apis_only(string stack, bool refused)
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["DocumentTotals"] = Setting, ["TaxLines"] = "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate",
            ["AllowanceCharges"] = "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId", ["AccessMode"] = "Ef", ["TenantTable"] = "Invoice", ["Stacks"] = stack,
        });
        var problems = DocumentTotalsPlan.Problems(Database(), project);
        Assert.AreEqual(refused, problems.Any(p => p.Contains("is the tenant table")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_allowance_part_needs_the_allowance_table_to_point_at_the_header()
    {
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Header(), Lines(), Categories()] };
        var problems = RowShapePlan.Problems(database, Project("Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Allowances=AllowanceTotal"));
        Assert.IsTrue(problems.Any(p => p.Contains("need a table of AllowanceCharges")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_header_the_lines_and_the_allowance_table_are_tables_of_the_setting()
    {
        var project = Project();
        RatePlan.Remember(Database(), project);
        Assert.IsTrue(DocumentTotalsPlan.IsListed("Invoice", project));
        Assert.IsTrue(DocumentTotalsPlan.Feeds("InvoiceLine", project));
        Assert.IsTrue(DocumentTotalsPlan.Feeds("InvoiceAllowanceCharge", project));
        Assert.IsFalse(DocumentTotalsPlan.Feeds("Invoice", project));
        Assert.IsTrue(RowShapePlan.Has(Header(), project));
    }

    [TestMethod]
    public void The_context_notes_the_documents_a_save_touches_and_writes_the_totals_in_the_same_transaction()
    {
        string text = DocumentTotalsWriter.Context(DocumentTotalsPlan.All(Project(), Database()), "ShopContext");
        foreach (string expected in new[]
        {
            "public static class InvoiceTotals", "Round(lines - allowances + charges, Places(currency, 2))", "Round(exclusive + tax, Places(currency, 2))", "string.Equals(currency?.Trim(), \"CHF\", StringComparison.OrdinalIgnoreCase)", "return 0.05m;",
            "Note(changed.Keys, line.InvoiceId)", "Select(x => x.InvoiceId).FirstOrDefaultAsync", "LockTotalsAsync(context, id, cancellationToken)", "ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemsTotal, x => x.ItemsTotal)",
            ".SetProperty(x => x.Payable, (decimal)worked.Payable)", "Keys.Order()", "await entry.ReloadAsync(cancellationToken)",
        })
            Assert.IsTrue(text.Contains(expected), expected);
    }

    [TestMethod]
    public void The_api_maps_the_check_route_of_the_header()
    {
        string text = RowShapeWriter.File(Database(), Project(), "ShopContext", []);
        Assert.IsTrue(text.Contains("document-totals-check"));
        Assert.IsTrue(text.Contains("DocumentTotalsCheckAsync"));
        Assert.IsTrue(text.Contains("Compare(\"Payable\""));
    }

    [TestMethod]
    public void The_python_module_locks_the_header_before_the_flush_and_writes_the_totals_after_it()
    {
        var names = new PythonTotalsWriter.Names(new("Invoice", "invoice", "invoice_id"), "invoice", new("InvoiceLine", "invoice_line", "invoice_line_id"), "invoice_id", "line_total", null, null, null, null, null, "prepaid", "currency",
            [("Lines", "items_total", "Decimal", 2), ("Payable", "payable", "Decimal", 2)], [new("CHF", 0.05m)], "/api/invoices");
        string module = PythonTotalsWriter.Module(names);
        foreach (string expected in new[] { "def lock_totals(session, key):", "@event.listens_for(Session, \"before_flush\")", "@event.listens_for(Session, \"after_flush\")", "TOTALS_ROUNDS = {\"CHF\": Decimal(\"0.05\")}", "rounding=ROUND_HALF_UP" })
            Assert.IsTrue(module.Contains(expected), expected);
        Assert.IsTrue(PythonTotalsWriter.Route(names).Contains("@router.get(\"/api/invoices/document-totals-check\")"));
    }

    [TestMethod]
    public void The_rust_helpers_save_inside_a_transaction_and_lock_the_header_first()
    {
        RustTotalsWriter.Tbl Table(string s) => new(s, s.ToLowerInvariant(), "\"" + s + "\"", "\"Id\"");
        RustTotalsWriter.Field Field(string name, string type = "f64", bool nullable = false) => new(name, type, nullable);
        var names = new RustTotalsWriter.Names(Table("Invoice"), Field("invoice_id", "i32"), "\"InvoiceId\"", Field("prepaid"), Field("currency", "String"), Table("InvoiceLine"), "\"InvoiceId\"", Field("invoice_id", "i32"), Field("line_total"), null, null, null, null,
            null, null, [("Lines", Field("items_total"), "\"ItemsTotal\"", 2), ("Payable", Field("payable"), "\"Payable\"", 2)], [new("CHF", 0.05m)], "/api/invoices");
        string header = RustTotalsWriter.Header(names);
        foreach (string expected in new[] { "pub(crate) async fn lock_totals", "pub(crate) async fn recalculate_totals", "async fn insert_with_totals", "async fn document_totals_check", "currency.is_some_and(|c| c.trim().eq_ignore_ascii_case(\"CHF\"))" })
            Assert.IsTrue(header.Contains(expected), expected);
        string feeder = RustTotalsWriter.Feeder(names, names.Lines, names.LineKey, "invoice_line_id", "i32", "id", "invoice");
        foreach (string expected in new[] { "repo::insert_in(&mut tx, row)", "crate::routes::invoice::lock_totals(&mut tx, *document)", "keys.sort();", "async fn delete_with_totals" })
            Assert.IsTrue(feeder.Contains(expected), expected);
    }

    [TestMethod]
    public void The_generated_api_tests_replace_the_totals_a_client_writes()
    {
        var project = Project();
        RatePlan.Remember(Database(), project);
        Assert.IsTrue(DocumentTotalsApiTests.Applies(Header(), project));
        Assert.IsFalse(DocumentTotalsApiTests.Applies(Lines(), project));
    }
}
