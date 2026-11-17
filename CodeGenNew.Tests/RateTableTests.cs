using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> RateTables: an exchange rate table with a pair and a day. </summary>
[TestClass]
public class RateTableTests
{
    private static TableModel Rates(SqlDbType day = SqlDbType.Date, bool nullableRate = false, SqlDbType rate = SqlDbType.Decimal) => Sample.Table("ExchangeRate",
    [
        Sample.Column("ExchangeRateId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("FromCurrency", SqlDbType.Char, characters: 3, ordinal: 2),
        Sample.Column("ToCurrency", SqlDbType.Char, characters: 3, ordinal: 3),
        Sample.Column("Rate", rate, nullable: nullableRate, ordinal: 4),
        Sample.Column("RateDate", day, ordinal: 5),
    ], []);

    private static DatabaseModel Database(TableModel? rates = null) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [rates ?? Rates()] };

    private static ProjectSettings Project(string setting = "ExchangeRate") =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["RateTables"] = setting, ["AccessMode"] = "Ef" });

    [TestMethod]
    public void The_table_alone_finds_the_default_columns()
    {
        var database = Database();
        var shape = RatePlan.For(database.Tables[0], Project())!;
        Assert.AreEqual("FromCurrency", shape.From.Name);
        Assert.AreEqual("ToCurrency", shape.To.Name);
        Assert.AreEqual("Rate", shape.Rate.Name);
        Assert.AreEqual("RateDate", shape.Day.Name);
        Assert.HasCount(0, RowShapePlan.Problems(database, Project()));
        Assert.IsTrue(RowShapePlan.Has(database.Tables[0], Project()) && RowShapeWriter.HasSaveCheck(database.Tables[0], Project()));
    }

    [TestMethod]
    [DataRow("Missing", "is not a table of the database")]
    [DataRow("ExchangeRate:Rate:ToCurrency", "text columns that cannot be NULL")]
    [DataRow("ExchangeRate:FromCurrency:FromCurrency", "cannot be the same column")]
    [DataRow("ExchangeRate:FromCurrency:ToCurrency:RateDate", "number column that cannot be NULL")]
    [DataRow("ExchangeRate:FromCurrency:ToCurrency:Rate:Rate", "date column that cannot be NULL")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_rate_that_allows_null_is_reported()
    {
        Assert.IsTrue(RowShapePlan.Problems(Database(Rates(nullableRate: true)), Project()).Any(p => p.Contains("number column that cannot be NULL")));
    }

    [TestMethod]
    public void The_api_checks_the_currencies_the_rate_and_the_pair_and_serves_the_rate_in_force()
    {
        string text = RowShapeWriter.File(Database(), Project(), "ShopContext", []);
        StringAssert.Contains(text, "CheckRateAsync(ShopContext context, ExchangeRate row)");
        StringAssert.Contains(text, "row.FromCurrency = (row.FromCurrency ?? \"\").Trim().ToUpperInvariant();");
        StringAssert.Contains(text, "if (row.Rate <= 0)");
        StringAssert.Contains(text, "x.RateDate.Date == day.Date");
        StringAssert.Contains(text, "x.RateDate < end");
        StringAssert.Contains(text, "OrderByDescending(x => x.RateDate)");
        StringAssert.Contains(text, "app.MapGet(\"/api/exchangerates/rate\"");
        StringAssert.Contains(text, "RateTaken");
    }

    [TestMethod]
    public void A_table_without_a_unique_index_on_the_pair_and_day_is_warned_about()
    {
        Assert.HasCount(1, RatePlan.MissingIndexes(Database(), Project()));
        Assert.HasCount(0, RatePlan.MissingIndexes(Database(), Project("Other")));
    }

    [TestMethod]
    public void The_python_and_rust_apis_carry_the_same_rule_and_route()
    {
        string python = PythonRateWriter.Helper(new("ExchangeRate", "exchange_rate_id", "from_currency", "to_currency", "rate", "rate_date", "date", "ExchangeRateSchema"))
            + PythonRateWriter.Route(new("ExchangeRate", "exchange_rate_id", "from_currency", "to_currency", "rate", "rate_date", "date", "ExchangeRateSchema"), "/api/exchangerates", "", "");
        Expect.Contains(python, "def check_rate(session: Session, row) -> None:");
        Expect.Contains(python, "row.from_currency = (row.from_currency or \"\").strip().upper()");
        Expect.Contains(python, RateWords.TakenType);
        Expect.Contains(python, "@router.get(\"/api/exchangerates/rate\", response_model=ExchangeRateSchema)");
        Expect.Contains(python, "ExchangeRate.rate_date <= day");
        string rust = RustRateWriter.Code(new("ExchangeRate", "\"ExchangeRate\"", "\"ExchangeRateId\"", "i32", "from_currency", "\"FromCurrency\"", "to_currency", "\"ToCurrency\"", "rate", "0.0", "rate_date", "\"RateDate\"", "NaiveDate", "*"))
            + RustRateWriter.RouteLine("/api/exchangerates", "");
        Expect.Contains(rust, "async fn check_rate(pool: &Pool, row_id: Option<i32>, row: &mut ExchangeRate)");
        Expect.Contains(rust, "if row.rate <= 0.0 {");
        Expect.Contains(rust, RateWords.TakenType);
        Expect.Contains(rust, ".route(\"/api/exchangerates/rate\", get(rate_on))");
        Expect.Contains(rust, "chrono::NaiveDate");
    }

    [TestMethod]
    public void The_rust_check_of_a_decimal_rate_and_a_date_and_time_day_compares_whole_days()
    {
        string rust = RustRateWriter.Code(new("ExchangeRate", "\"ExchangeRate\"", "\"ExchangeRateId\"", "i32", "from_currency", "\"FromCurrency\"", "to_currency", "\"ToCurrency\"", "rate", RustRateWriter.ZeroOf("Decimal"), "rate_date", "\"RateDate\"", "NaiveDateTime", "*"));
        Expect.Contains(rust, "row.rate <= rust_decimal::Decimal::ZERO");
        Expect.Contains(rust, "start + chrono::Duration::days(1)");
        Expect.Contains(rust, "chrono::NaiveDateTime");
        Expect.Contains(rust, "on: Option<String>,");
        Expect.Contains(rust, "parse_rate_day(text)");
    }

    [TestMethod]
    public void Two_currencies_a_rate_and_a_day_are_suggested_as_a_rate_table()
    {
        var plain = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" });
        var found = DatabaseShapeDetector.Of(Database(), plain).Single(d => d.Suggestion.Shape == "Exchange rates");
        Assert.AreEqual("ExchangeRate", found.Table);
        Assert.AreEqual("ExchangeRate", found.Suggestion.Item);
        Assert.IsTrue(found.Suggestion.Fits, found.Suggestion.Note);
        Assert.IsFalse(DatabaseShapeDetector.Of(Database(), Project()).Any(d => d.Suggestion.Shape == "Exchange rates"), "a listed table is not suggested again");
    }

    [TestMethod]
    public void A_table_without_a_rate_and_a_day_is_not_suggested()
    {
        var plain = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" });
        var table = Sample.Table("Pair",
        [
            Sample.Column("PairId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("FromCurrency", SqlDbType.Char, characters: 3, ordinal: 2),
            Sample.Column("ToCurrency", SqlDbType.Char, characters: 3, ordinal: 3),
        ], []);
        Assert.IsFalse(DatabaseShapeDetector.Of(Database(table), plain).Any(d => d.Suggestion.Shape == "Exchange rates"));
    }

    [TestMethod]
    public void The_lookup_is_on_the_screens_of_a_rate_table_only()
    {
        var database = Database();
        Assert.IsTrue(RateScreens.HasBox(database.Tables[0], Project()));
        Assert.IsTrue(RatePlan.IsCurrency(database.Tables[0], database.Tables[0].Columns[1], Project()) && !RatePlan.IsCurrency(database.Tables[0], database.Tables[0].Columns[3], Project()));
        Assert.IsFalse(RateScreens.HasBox(database.Tables[0], ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop" })));
        var names = RateScreens.Names(database.Tables[0], Project())!.Value;
        Assert.AreEqual(new RateScreens.RateNames("fromCurrency", "toCurrency", "rate", "rateDate", "exchangeRateId", false), names);
        Expect.Contains(RateScreens.ReactApiLine("ExchangeRate"), "rateOn: (from: string, to: string, on?: string) => request<ExchangeRate>");
        Expect.Contains(string.Join("\n", RateScreens.ReactHandlers("exchangeRateApi", names)), "exchangeRateApi.rateOn(from, to, rateDay || undefined)");
        Expect.Contains(string.Join("\n", RateScreens.AngularHandlers(names)), "this.service.rateOn(from, to, this.rateDay || undefined)");
        Expect.Contains(string.Join("\n", RateScreens.BlazorHandlers("Client", new RateScreens.RateNames("FromCurrency", "ToCurrency", "Rate", "RateDate", "ExchangeRateId", false))), "await Client.RateOnAsync(from, to, rateDay)");
        Expect.Contains(RateScreens.CsClientLines("ExchangeRate", "{Url}")[2], "GetAsync($\"{Url}/rate?from=");
    }

    [TestMethod]
    public async Task A_save_the_unique_index_refuses_answers_as_the_rule_does()
    {
        TableModel Indexed(params IndexModel[] indexes) => Sample.Table("ExchangeRate", Rates().Columns.ToList(), [], indexes: indexes.ToList());
        var pair = new IndexModel("UX_Rate", true, ["FromCurrency", "ToCurrency", "RateDate"]);
        var project = Project();
        Assert.HasCount(0, RatePlan.PairIndexes(project));
        RatePlan.Remember(Database(), project);
        Assert.HasCount(0, RatePlan.PairIndexes(project));
        RatePlan.Remember(Database(Indexed(new IndexModel("UX_Other", true, ["Rate"]), pair)), project);
        var index = RatePlan.PairIndexes(project).Single();
        Assert.AreEqual("UX_Rate", index.Name);
        Assert.AreEqual("ExchangeRate.FromCurrency, ExchangeRate.ToCurrency, ExchangeRate.RateDate'", PartyPlan.RefusalMark(index, SqlDialect.Sqlite));

        string program = (await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), project)).GeneratedText!;
        Expect.Contains(program, "Constraint.Unique when RepeatsRate(error) => Problem(StatusCodes.Status409Conflict, \"" + RateWords.TakenType + "\", \"Rate taken\", \"" + RateWords.Taken + "\"),");
        Expect.Contains(program, "private static readonly string[] RateIndexes = [\"UX_Rate'\"];");
        string plain = (await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project("Other"))).GeneratedText!;
        Assert.DoesNotContain("RepeatsRate", plain);
    }

    [TestMethod]
    public void The_python_and_rust_tests_are_written_for_a_rate_table_they_can_fill()
    {
        var database = Database();
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["RateTables"] = "ExchangeRate", ["PythonTests"] = "true", ["RustTests"] = "true", ["AccessMode"] = "Ef" });
        var names = project.ImpliedPlanTemplates.ToList();
        CollectionAssert.Contains(names, "PY_TestRates");
        CollectionAssert.Contains(names, "RS_TestRates");
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToList(), "RS_TestRates");

        var plan = RateTestPlan.For(database, project)!;
        Assert.AreEqual("{ \"exchangeRateId\": 0, \"fromCurrency\": \"\", \"toCurrency\": \"\", \"rate\": 1, \"rateDate\": \"\" }", plan.Body);
        Assert.AreEqual("2024-01-01", plan.Day("2024-01-01"));
        Assert.AreEqual("2024-01-01T00:00:00", RateTestPlan.For(Database(Rates(SqlDbType.DateTime2)), project)!.Day("2024-01-01"));

        string python = PythonRateTests.File(database, project);
        Expect.Contains(python, "def test_a_rate_is_kept_trimmed_in_upper_case_and_once_per_pair_and_day(rows):");
        Expect.Contains(python, "def test_the_rate_in_force_is_the_latest_one_on_or_before_the_day(rows):");
        Expect.Contains(python, "TAKEN = \"" + RateWords.TakenType + "\"");
        string rust = RustRateTests.File(database, project);
        Expect.Contains(rust, "async fn the_rate_in_force_is_the_latest_one_on_or_before_the_day()");
        Expect.Contains(rust, "{ROUTE}/rate?from={source}&to={target}{when}");
        Assert.DoesNotContain("§", python + rust);

        var shortCode = Sample.Table("ExchangeRate",
        [
            Sample.Column("ExchangeRateId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("FromCurrency", SqlDbType.Char, characters: 2, ordinal: 2),
            Sample.Column("ToCurrency", SqlDbType.Char, characters: 2, ordinal: 3),
            Sample.Column("Rate", SqlDbType.Decimal, ordinal: 4),
            Sample.Column("RateDate", SqlDbType.Date, ordinal: 5),
        ], []);
        Assert.IsNull(RateTestPlan.For(Database(shortCode), project), "currencies too short for the test codes");
    }
}
