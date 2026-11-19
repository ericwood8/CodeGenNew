using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Desktop;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rust;

namespace CodeGenNew.Tests;

/// <summary> RateDocuments and RateInverse: a document that keeps the rate it was written with, and a lookup that answers from the opposite pair. </summary>
[TestClass]
public class RateDocumentTests
{
    private static TableModel Rates() => Sample.Table("ExchangeRate",
    [
        Sample.Column("ExchangeRateId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("FromCurrency", SqlDbType.Char, characters: 3, ordinal: 2),
        Sample.Column("ToCurrency", SqlDbType.Char, characters: 3, ordinal: 3),
        Sample.Column("Rate", SqlDbType.Decimal, ordinal: 4),
        Sample.Column("RateDate", SqlDbType.Date, ordinal: 5),
    ], []);

    private static TableModel Invoices(SqlDbType day = SqlDbType.Date, bool nullableRate = false) => Sample.Table("SalesInvoice",
    [
        Sample.Column("SalesInvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CurrencyCode", SqlDbType.Char, characters: 3, ordinal: 2),
        Sample.Column("FxRate", SqlDbType.Decimal, nullable: nullableRate, ordinal: 3),
        Sample.Column("Total", SqlDbType.Decimal, ordinal: 4),
        Sample.Column("BaseTotal", SqlDbType.Decimal, ordinal: 5),
        Sample.Column("InvoiceDate", day, ordinal: 6),
    ], []);

    private static DatabaseModel Database(TableModel? invoices = null) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Rates(), invoices ?? Invoices()] };

    private static ProjectSettings Project(string documents = "SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate", string baseCurrency = "usd", bool inverse = false, string extra = "") =>
        ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["RateTables"] = "ExchangeRate", ["RateDocuments"] = documents, ["BaseCurrency"] = baseCurrency, ["RateInverse"] = inverse ? "true" : "false", ["AccessMode"] = "Ef", ["ApiTests"] = "true"
        });

    [TestMethod]
    public void The_setting_names_the_columns_and_the_base_currency_is_upper_case()
    {
        var database = Database();
        var project = Project();
        Assert.AreEqual("USD", project.BaseCurrency);
        var shape = RateDocumentPlan.For(database.Tables[1], project, database)!;
        Assert.AreEqual("CurrencyCode", shape.Currency.Name);
        Assert.AreEqual("FxRate", shape.Rate.Name);
        Assert.AreEqual("InvoiceDate", shape.Day!.Name);
        Assert.AreEqual("ExchangeRate", shape.Rates.Table.TableName);
        Assert.HasCount(0, RowShapePlan.Problems(database, project));
        Assert.IsTrue(RowShapeWriter.HasSaveCheck(database.Tables[1], project));
        Assert.IsNull(RateDocumentPlan.For(database.Tables[1], Project("Other"), database));
        Assert.IsNull(RateDocumentPlan.For(database.Tables[1], Project("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal"), database)!.Day, "the date is optional");
    }

    [TestMethod]
    [DataRow("SalesInvoice:CurrencyCode:FxRate:Total", "needs its currency, rate, amount and converted amount")]
    [DataRow("Missing:A:B:C:D", "is not a table of the database")]
    [DataRow("SalesInvoice:Total:FxRate:Total:BaseTotal", "text column that cannot be NULL")]
    [DataRow("SalesInvoice:CurrencyCode:CurrencyCode:Total:BaseTotal", "number column that cannot be NULL")]
    [DataRow("SalesInvoice:CurrencyCode:FxRate:Total:Total", "four different columns")]
    [DataRow("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:Total", "date column that cannot be NULL")]
    [DataRow("ExchangeRate:FromCurrency:Rate:Rate:Rate", "is a rate table itself")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_missing_base_currency_and_a_rate_that_allows_null_are_reported()
    {
        Assert.IsTrue(RowShapePlan.Problems(Database(), Project(baseCurrency: "")).Any(p => p.Contains("needs BaseCurrency")));
        Assert.IsTrue(RowShapePlan.Problems(Database(Invoices(nullableRate: true)), Project()).Any(p => p.Contains("number column that cannot be NULL")));
    }

    [TestMethod]
    public void An_inverse_is_offered_for_a_rate_column_the_plan_accepts()
    {
        Assert.HasCount(0, RowShapePlan.Problems(Database(), Project(inverse: true)));
        Assert.IsTrue(RatePlan.InvertibleRate(Rates().Columns[3]));
    }

    [TestMethod]
    public void The_api_converts_a_document_and_answers_the_opposite_pair()
    {
        var database = Database();
        var project = Project(inverse: true);
        RatePlan.Remember(database, project);
        string text = RowShapeWriter.File(database, project, "ShopContext", []);
        Expect.Contains(text, "ConvertCurrencyAsync(ShopContext context, SalesInvoice row, bool isNew)");
        Expect.Contains(text, "await ExchangeRateShape.FindRateAsync(context, row.CurrencyCode, \"USD\", day);");
        Expect.Contains(text, "row.BaseTotal = (decimal)Math.Round((decimal)row.Total * (decimal)row.FxRate, 2, MidpointRounding.AwayFromZero);");
        Expect.Contains(text, "if (await ConvertCurrencyAsync(context, row, isNew) is { } conversionProblem)");
        Expect.Contains(text, "FindRateAsync(ShopContext context, string from, string to, DateTime day)");
        Expect.Contains(text, "other.Rate = 1m / other.Rate;");
        Expect.Contains(text, "other.ExchangeRateId = default;");
        Expect.Contains(text, "response.Headers[\"" + RateWords.InvertedHeader + "\"] = \"true\";");
        Assert.DoesNotContain("§", text);
        string plain = RowShapeWriter.File(database, Project(inverse: false), "ShopContext", []);
        Assert.DoesNotContain("other.Rate", plain);
        Assert.DoesNotContain(RateWords.InvertedHeader, plain);
    }


    [TestMethod]
    public void The_generated_tests_play_the_rate_rules_the_lookup_the_inverse_and_the_document()
    {
        var database = Database();
        var project = Project(inverse: true);
        RatePlan.Remember(database, project);
        var rates = ApiTestPlan.For(database.Tables[0], project)!;
        string rateTests = RateApiTests.Methods(rates, database.Tables[0], project);
        Expect.Contains(rateTests, "public async Task A_rate_is_kept_trimmed_in_upper_case_and_once_per_pair_and_day()");
        Expect.Contains(rateTests, "public async Task A_rate_that_is_not_valid_is_refused()");
        Expect.Contains(rateTests, "public async Task The_rate_in_force_is_the_latest_one_on_or_before_the_day()");
        Expect.Contains(rateTests, "public async Task A_newer_rate_of_the_opposite_pair_answers_as_one_divided_by_it()");
        Expect.Contains(rateTests, "\"" + RateWords.TakenType + "\"");
        Assert.DoesNotContain("A_newer_rate_of_the_opposite_pair", RateApiTests.Methods(rates, database.Tables[0], Project(inverse: false)));
        string documentTests = RateApiTests.Methods(ApiTestPlan.For(database.Tables[1], project)!, database.Tables[1], project);
        Expect.Contains(documentTests, "public async Task A_document_is_written_with_the_rate_in_force_and_keeps_it()");
        Expect.Contains(documentTests, "public async Task A_document_in_a_currency_with_no_rate_is_refused()");
        Expect.Contains(documentTests, "\"" + RateWords.NeededType + "\"");
        Assert.DoesNotContain("§", rateTests + documentTests);
    }

    [TestMethod]
    public void The_screens_show_a_documents_rate_and_converted_amount_as_text_and_say_when_a_rate_is_an_inverse()
    {
        var database = Database();
        var project = Project(inverse: true);
        var invoices = database.Tables[1];
        Assert.IsTrue(RateDocumentPlan.IsOwnedColumn(invoices, project, invoices.Columns[2]));
        Assert.IsTrue(RateDocumentPlan.IsOwnedColumn(invoices, project, invoices.Columns[4]));
        Assert.IsFalse(RateDocumentPlan.IsOwnedColumn(invoices, project, invoices.Columns[1]));
        Assert.IsFalse(RateDocumentPlan.IsOwnedColumn(invoices, project, invoices.Columns[3]));
        Assert.IsFalse(RateDocumentPlan.IsOwnedColumn(invoices, Project("Other"), invoices.Columns[2]));
        var form = FormModel.For(invoices, project);
        Assert.IsTrue(form.Field(invoices.Columns[2]).IsSystemStampField);
        Assert.IsTrue(form.Field(invoices.Columns[4]).IsSystemStampField);
        Assert.IsFalse(form.Field(invoices.Columns[1]).IsSystemStampField);
        Assert.AreEqual("", DesktopFormCode.SaveLines(form, project, invoices.Columns[2]));

        var names = RateScreens.Names(database.Tables[0], project)!.Value;
        Assert.IsTrue(names.Inverse);
        string react = string.Join("\n", RateScreens.ReactHandlers("api", names));
        Expect.Contains(react, "${row.exchangeRateId ? '' : ', " + RateScreens.InvertedText + "'}");
        Expect.Contains(string.Join("\n", RateScreens.ReactBoxTests("api", "Page", names)), RateScreens.InvertedText);
        Expect.Contains(RateScreens.AngularBoxTest(names), RateScreens.InvertedText);
        var cs = RateScreens.CsNames(database.Tables[0], project)!.Value;
        Expect.Contains(string.Join("\n", RateScreens.BlazorHandlers("Client", cs)), "{(row.ExchangeRateId == 0 ? \", " + RateScreens.InvertedText + "\" : \"\")}");
        Expect.Contains(RateScreens.BlazorBoxTest(names), "Says_when_the_rate_is_worked_out_from_the_opposite_pair");
        string desktop = RateScreens.WinUI3Read(database.Tables[0], project, "_context");
        Expect.Contains(desktop, "other.Rate = 1m / other.Rate;");
        Expect.Contains(desktop, "inverted ? \", " + RateScreens.InvertedText + "\"");

        var plain = Project(inverse: false);
        Assert.IsFalse(RateScreens.Names(database.Tables[0], plain)!.Value.Inverse);
        Assert.DoesNotContain(RateScreens.InvertedText, RateScreens.WinUI3Read(database.Tables[0], plain, "_context"));
        Assert.DoesNotContain(RateScreens.InvertedText, string.Join("\n", RateScreens.ReactHandlers("api", RateScreens.Names(database.Tables[0], plain)!.Value)));
    }

    [TestMethod]
    public void A_document_can_name_its_rate_table_and_its_base_currency()
    {
        var other = Sample.Table("CardRate", Rates().Columns.ToList(), []);
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Rates(), other, Invoices()] };
        ProjectSettings With(string documents, string baseCurrency = "usd") => ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["RateTables"] = "ExchangeRate,CardRate", ["RateDocuments"] = documents, ["BaseCurrency"] = baseCurrency, ["AccessMode"] = "Ef"
        });
        var plain = RateDocumentPlan.For(database.Tables[2], With("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate"), database)!;
        Assert.AreEqual("ExchangeRate", plain.Rates.Table.TableName);
        Assert.AreEqual("USD", plain.BaseCurrency);
        var named = RateDocumentPlan.For(database.Tables[2], With("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate:CardRate:eur", ""), database)!;
        Assert.AreEqual("CardRate", named.Rates.Table.TableName);
        Assert.AreEqual("EUR", named.BaseCurrency, "the table's own base currency needs no BaseCurrency setting");
        var noDate = RateDocumentPlan.For(database.Tables[2], With("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal::CardRate"), database)!;
        Assert.IsNull(noDate.Day);
        Assert.AreEqual("CardRate", noDate.Rates.Table.TableName);
        Assert.IsTrue(RowShapePlan.Problems(database, With("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate:Missing")).Any(p => p.Contains("is not in RateTables")));
        Assert.IsTrue(RowShapePlan.Problems(database, With("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate", "")).Any(p => p.Contains("needs BaseCurrency")));
        RatePlan.Remember(database, With("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate:CardRate:eur", ""));
        string text = RowShapeWriter.File(database, With("SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate:CardRate:eur", ""), "ShopContext", []);
        Expect.Contains(text, "await CardRateShape.FindRateAsync(context, row.CurrencyCode, \"EUR\", day);");
    }

    [TestMethod]
    public void The_python_helpers_find_the_rate_with_the_inverse_and_convert_a_document()
    {
        var names = new PythonRateWriter.Names("ExchangeRate", "exchange_rate_id", "from_currency", "to_currency", "rate", "rate_date", "date", "ExchangeRateSchema", true);
        string helper = PythonRateWriter.Helper(names);
        Expect.Contains(helper, "def find_rate(session: Session, source: str, target: str, day):");
        Expect.Contains(helper, "other.rate = 1 / other.rate");
        Expect.Contains(helper, "other.exchange_rate_id = 0");
        Expect.Contains(PythonRateWriter.Route(names, "/api/exchangerates", "", ""), "response.headers[\"" + RateWords.InvertedHeader + "\"] = \"true\"");
        Assert.DoesNotContain("other.rate", PythonRateWriter.Helper(names with { Inverse = false }));
        Assert.DoesNotContain("§", helper);

        var document = new PythonRateDocumentWriter.Names("SalesInvoice", "currency_code", "fx_rate", "Decimal", "total", "Decimal", "base_total", "Decimal", "invoice_date", "datetime", "date", "rate", "USD", 2);
        string convert = PythonRateDocumentWriter.Helper(document);
        Expect.Contains(convert, "def convert_currency(session: Session, row, stored: tuple | None) -> None:");
        Expect.Contains(convert, "found, _ = find_rate(session, row.currency_code, \"USD\", day_of(row.invoice_date))");
        Expect.Contains(convert, "row.fx_rate = Decimal(1)");
        Expect.Contains(convert, "row.base_total = (row.total * row.fx_rate).quantize(Decimal(1).scaleb(-2), rounding=ROUND_HALF_UP)");
        Expect.Contains(convert, "return (currency, day_of(row.invoice_date))");
        Assert.DoesNotContain("§", convert);
        string floats = PythonRateDocumentWriter.Helper(document with { RateAnnotation = "float", AmountAnnotation = "float", BaseAnnotation = "float", RateDayAnnotation = "datetime", Day = null, DayAnnotation = null });
        Expect.Contains(floats, "row.fx_rate = 1.0");
        Expect.Contains(floats, "row.base_total = float((Decimal(str(row.total)) * Decimal(str(row.fx_rate))).quantize(");
        Expect.Contains(floats, "datetime.combine(day_of(date.today()), time.min)");
        Expect.Contains(floats, "return (currency,)");
        CollectionAssert.AreEqual(new[] { "fx_rate", "base_total" }, PythonRateDocumentWriter.Owned(document).ToArray());
    }

    [TestMethod]
    public void The_rust_helpers_find_the_rate_with_the_inverse_and_convert_a_document()
    {
        var names = new RustRateWriter.Names("ExchangeRate", "\"ExchangeRate\"", "\"ExchangeRateId\"", "i32", "from_currency", "\"FromCurrency\"", "to_currency", "\"ToCurrency\"", "rate", RustRateWriter.ZeroOf("f64"),
            "rate_date", "\"RateDate\"", "NaiveDate", "*", true, RustRateWriter.OneOf("f64"), "exchange_rate_id");
        string rust = RustRateWriter.Code(names);
        Expect.Contains(rust, "pub async fn find_rate(pool: &Pool, source: &str, target: &str, day: chrono::NaiveDate) -> ApiResult<(Option<ExchangeRate>, bool)>");
        Expect.Contains(rust, "other.rate = 1.0 / other.rate;");
        Expect.Contains(rust, "other.exchange_rate_id = Default::default();");
        Expect.Contains(rust, "\"" + RateWords.InvertedHeader + "\"");
        Assert.DoesNotContain("other.rate", RustRateWriter.Code(names with { Inverse = false }));
        Assert.DoesNotContain("§", rust);

        var document = new RustRateDocumentWriter.Names("SalesInvoice", "currency_code", "fx_rate", "Decimal", "total", "Decimal", "base_total", "Decimal", "invoice_date", "NaiveDateTime", "NaiveDate", "rate", "Decimal", "USD", 2);
        string convert = RustRateDocumentWriter.Code(document);
        Expect.Contains(convert, "async fn convert_currency(pool: &Pool, row: &mut SalesInvoice, stored: Option<&SalesInvoice>) -> ApiResult<()>");
        Expect.Contains(convert, "let day = row.invoice_date.date();");
        Expect.Contains(convert, "find_rate(pool, &row.currency_code, \"USD\", day)");
        Expect.Contains(convert, "row.base_total = (row.total * row.fx_rate).round_dp_with_strategy(2, rust_decimal::RoundingStrategy::MidpointAwayFromZero);");
        Assert.DoesNotContain("§", convert);
        string floats = RustRateDocumentWriter.Code(document with { RateType = "f64", AmountType = "f64", BaseType = "f64", FoundRateType = "f64", RateDayType = "NaiveDateTime", Day = null, DayType = null });
        Expect.Contains(floats, "let day = chrono::Local::now().date_naive();");
        Expect.Contains(floats, "day.and_hms_opt(0, 0, 0).unwrap_or_default()");
        Expect.Contains(floats, "row.fx_rate = 1.0");
        Expect.Contains(floats, "row.base_total = ((row.total as f64 * row.fx_rate as f64 * 10f64.powi(2)).round() / 10f64.powi(2)) as f64;");
        Assert.DoesNotContain("rust_decimal", floats, "SQLite has no rust_decimal crate");
    }

    [TestMethod]
    public void The_python_and_rust_tests_play_the_document_rules()
    {
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.Sqlite, Tables = [Rates(), Invoices()] };
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["RateTables"] = "ExchangeRate", ["RateDocuments"] = "SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate", ["BaseCurrency"] = "usd", ["AccessMode"] = "Ef", ["PythonTests"] = "true", ["RustTests"] = "true"
        });
        RatePlan.Remember(database, project);
        var plan = RateDocumentTestPlan.For(database, project)!;
        Assert.AreEqual("{ \"salesInvoiceId\": 0, \"currencyCode\": \"\", \"fxRate\": 1, \"total\": 1, \"baseTotal\": 1, \"invoiceDate\": \"\" }", plan.Body);
        Assert.AreEqual("2024-01-01", plan.DocumentDay("2024-01-01"));
        Assert.AreEqual("USD", plan.Shape.BaseCurrency);

        string python = PythonRateDocumentTests.File(database, project);
        Expect.Contains(python, "def test_a_document_is_written_with_the_rate_in_force_and_keeps_it(rows):");
        Expect.Contains(python, "def test_a_document_in_a_currency_with_no_rate_is_refused(rows):");
        Expect.Contains(python, "NEEDED = \"" + RateWords.NeededType + "\"");
        Expect.Contains(python, "body[\"invoiceDate\"] = day");
        Expect.Contains(python, "assert close(number(again[\"fxRate\"]), 1.2)");
        string rust = RustRateDocumentTests.File(database, project);
        Expect.Contains(rust, "async fn a_document_is_written_with_the_rate_in_force_and_keeps_it()");
        Expect.Contains(rust, "body[\"invoiceDate\"] = json!(day);");
        Expect.Contains(rust, "moving[\"invoiceDate\"] = json!(\"2024-03-01\");");
        Assert.DoesNotContain("§", python + rust);
        Assert.IsTrue(PythonRateDocumentTests.Applies(database, project));
        Assert.IsTrue(RustRateDocumentTests.Applies(database, project));

        var dated = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.Sqlite, Tables = [Rates(), Invoices(SqlDbType.DateTime2)] };
        Assert.AreEqual("2024-01-01T00:00:00", RateDocumentTestPlan.For(dated, project)!.DocumentDay("2024-01-01"));
        Assert.IsFalse(PythonRateDocumentTests.Applies(database, Project()), "no PythonTests");
        Assert.IsNull(RateDocumentTestPlan.For(database, Project(documents: "")), "no document table");
    }

    [TestMethod]
    public async Task The_rust_bodies_write_the_base_currency_into_a_document_and_the_templates_list_the_document_tests()
    {
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "main", Dialect = SqlDialect.Sqlite, Tables = [Rates(), Invoices()] };
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["RateTables"] = "ExchangeRate", ["RateDocuments"] = "SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate", ["BaseCurrency"] = "usd", ["AccessMode"] = "Ef",
            ["RustTests"] = "true", ["PythonTests"] = "true", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["Dashboard"] = "false"
        });
        RatePlan.Remember(database, project);
        string bodies = (await Repo.Cache.RunAsync(Repo.Template("RS_TestBodies_v1.tt"), database, project)).GeneratedText!;
        Expect.Contains(bodies, "\"currencyCode\": \"USD\",");
        Expect.Contains(bodies, "mod rate_document_routes;");
        string rust = (await Repo.Cache.RunAsync(Repo.Template("RS_TestRates_v1.tt"), database, project)).GeneratedText!;
        Expect.Contains(rust, "@@@FILE tests/api/rate_document_routes.rs@@@");
        string python = (await Repo.Cache.RunAsync(Repo.Template("PY_TestRates_v1.tt"), database, project)).GeneratedText!;
        Expect.Contains(python, "@@@FILE tests/test_rate_documents.py@@@");
        Expect.Contains(python, "@@@FILE tests/test_rates.py@@@");
    }

    [TestMethod]
    public void The_winui3_dialog_converts_a_document_before_it_saves()
    {
        var database = Database();
        var project = Project(inverse: true);
        RatePlan.Remember(database, project);
        string lines = RateScreens.WinUI3DocumentCheck(database.Tables[1], project, "_context");
        Expect.Contains(lines, "entity.CurrencyCode = (entity.CurrencyCode ?? \"\").Trim().ToUpperInvariant();");
        Expect.Contains(lines, "ErrorMessage = \"" + RateWords.DocumentNeedsCurrency + "\";");
        Expect.Contains(lines, "var rdDay = entity.InvoiceDate;");
        Expect.Contains(lines, "if (entity.CurrencyCode == \"USD\")");
        Expect.Contains(lines, "var rdOther = await _context.Set<ExchangeRate>()");
        Expect.Contains(lines, "ErrorMessage = $\"There is no exchange rate from {entity.CurrencyCode} to USD on or before {rdDay:yyyy-MM-dd}.\";");
        Expect.Contains(lines, "entity.BaseTotal = (decimal)Math.Round((decimal)entity.Total * (decimal)entity.FxRate, 2, MidpointRounding.AwayFromZero);");
        Assert.DoesNotContain("rdOther", RateScreens.WinUI3DocumentCheck(database.Tables[1], Project(), "_context"), "no inverse without RateInverse");
        Assert.AreEqual("", RateScreens.WinUI3DocumentCheck(database.Tables[0], project, "_context"), "not a document");
    }

    [TestMethod]
    [DataRow("UNO_EssentialSupport_v1.tt")]
    [DataRow("BLZ_EssentialSupport_v1.tt")]
    public async Task The_uno_and_blazor_clients_show_the_apis_sentence_of_a_rate_refusal_only_when_there_are_rate_tables(string template)
    {
        string with = (await Repo.Cache.RunAsync(Repo.Template(template), Project())).GeneratedText!.Replace("\r\n", "\n");
        Expect.Contains(with, "public class ApiException(int status, string message, bool sentence = false) : Exception(message)");
        Expect.Contains(with, "        _ when Sentence => Message,\n");
        Expect.Contains(with, "type.GetString()?.StartsWith(\"" + ProblemTypes.Prefix + "rate-\")");
        Expect.Contains(with, "throw new ApiException((int)response.StatusCode, sentence, true);");
        string without = (await Repo.Cache.RunAsync(Repo.Template(template), ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop" }))).GeneratedText!.Replace("\r\n", "\n");
        Assert.DoesNotContain("Sentence", without);
        Assert.DoesNotContain("RateRefusalAsync", without);
        Expect.Contains(without, "public class ApiException(int status, string message) : Exception(message)\n{\n    public int Status => status;\n\n    // The sentence a screen shows");
        Expect.Contains(without, "    {\n        400 => action ==");
    }

    [TestMethod]
    public void The_generic_tests_write_the_base_currency_into_a_document_and_never_change_it()
    {
        var database = Database();
        var project = Project();
        RatePlan.Remember(database, project);
        var plan = ApiTestPlan.For(database.Tables[1], project)!;
        Assert.IsTrue(RateDocumentPlan.IsCurrency(database.Tables[1], database.Tables[1].Columns[1], project));
        Assert.IsFalse(RateDocumentPlan.IsCurrency(database.Tables[1], database.Tables[1].Columns[3], project));
        Assert.AreNotEqual("CurrencyCode", plan.Changed?.Name);
    }
}
