using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> Two date columns where the second must not be earlier than the first: where the pair comes from (the names, a CHECK constraint, the project), what takes it away, and the check of stored rows. </summary>
[TestClass]
public class DatePairTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    [TestMethod]
    [DataRow("([CheckOut]>=[CheckIn])", "CheckIn", "CheckOut")]
    [DataRow("CHECK (((check_out >= check_in)))", "check_in", "check_out")]
    [DataRow("(`check_in` <= `check_out`)", "check_in", "check_out")]
    [DataRow("CheckIn < CheckOut", "CheckIn", "CheckOut")]
    [DataRow("(check_out > check_in)", "check_in", "check_out")]
    [DataRow("CHECK (((check_in)::date <= (check_out)::date))", "check_in", "check_out")]
    [DataRow("([EndDate] IS NULL OR [StartDate]<=[EndDate])", "StartDate", "EndDate")]
    [DataRow("(end_date IS NULL OR start_date <= end_date)", "start_date", "end_date")]
    public void A_check_that_puts_two_dates_in_order_names_the_start_and_the_end(string definition, string start, string end) =>
        Assert.AreEqual((start, end), CheckConstraintParser.DateOrder(definition));

    [TestMethod]
    [DataRow("([Rating]>=(1) AND [Rating]<=(5))")]
    [DataRow("([A]>=[B] AND [C]>=[D])")]
    [DataRow("([A]>=[B] OR [C]>=[D])")]
    [DataRow("([A]>=[A])")]
    [DataRow("([A]>='2020-01-01')")]
    [DataRow("([A]>=(5))")]
    [DataRow("([Other] IS NULL OR [A]<=[B])")]
    [DataRow("(year([A])<=[B])")]
    public void Anything_else_is_not_a_pair(string definition) => Assert.IsNull(CheckConstraintParser.DateOrder(definition));

    private static List<ColumnModel> Columns(string? closedAfter = null) =>
    [
        Sample.Column("ProjectId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2),
        Sample.Column("StartDate", SqlDbType.Date, ordinal: 3),
        Sample.Column("EndDate", SqlDbType.Date, nullable: true, ordinal: 4),
        Sample.Column("Opened", SqlDbType.Date, ordinal: 5),
        Sample.Column("Closed", SqlDbType.Date, nullable: true, ordinal: 6, notBefore: closedAfter),
        Sample.Column("Note", SqlDbType.NVarChar, characters: 40, nullable: true, ordinal: 7)
    ];

    private static TableModel Detected(List<object?[]>? rows = null) => Sample.Table("Project", Columns(), rows: rows, startDate: "StartDate", endDate: "EndDate");

    [TestMethod]
    public void The_pair_the_schema_names_the_project_adds_and_the_project_takes_away()
    {
        Assert.AreEqual(0, DatePairs.For(Sample.Table("Project", Columns()), Project()).Count, "a table with no start and end and no CHECK has no pair");

        var detected = Detected();
        var pairs = DatePairs.For(detected, Project());
        Assert.AreEqual(("StartDate", "EndDate"), (pairs.Single().Start.Name, pairs.Single().End.Name));
        Assert.AreEqual("End Date must not be earlier than Start Date.", pairs.Single().Message);

        var added = DatePairs.For(detected, Project(("DatePairs", "Opened>Closed")));
        CollectionAssert.AreEqual(new[] { "EndDate", "Closed" }, added.Select(p => p.End.Name).ToArray());
        Assert.AreEqual(1, DatePairs.For(detected, Project(("DatePairs", "Other.Opened>Closed"))).Count, "a pair named for another table is not this table's");
        Assert.AreEqual(2, DatePairs.For(detected, Project(("DatePairs", "Project.Opened>Closed,StartDate>EndDate"))).Count, "a pair the schema already names is not counted twice");

        foreach (string exempt in new[] { "Project", "EndDate", "Project.EndDate" })
            Assert.AreEqual(0, DatePairs.For(detected, Project(("NoDatePairs", exempt))).Count, exempt);
        Assert.AreEqual(1, DatePairs.For(detected, Project(("DatePairs", "Opened>Closed"), ("NoDatePairs", "EndDate"))).Count);
        Assert.AreEqual(0, DatePairs.For(Sample.Table("Project", Columns()), Project(("DatePairs", "Name>Note"))).Count, "a pair of text columns is not dates");
        Assert.AreEqual(0, DatePairs.For(Sample.Table("Project", Columns()), Project(("DatePairs", "Opened>Opened"))).Count);
    }

    [TestMethod]
    public void A_column_a_check_puts_after_another_date_column_ends_a_pair()
    {
        var table = Sample.Table("Project", Columns(closedAfter: "Opened"));

        var pair = DatePairs.For(table, Project()).Single();
        Assert.AreEqual(("Opened", "Closed"), (pair.Start.Name, pair.End.Name));
        Assert.AreEqual(pair, DatePairs.EndingAt(table, Project(), table.Columns.Single(c => c.Name == "Closed")));
        Assert.AreEqual(pair, DatePairs.StartingAt(table, Project(), table.Columns.Single(c => c.Name == "Opened")));
        Assert.IsNull(DatePairs.EndingAt(table, Project(), table.Columns.Single(c => c.Name == "Opened")));
    }

    [TestMethod]
    public void Stored_rows_that_end_before_they_start_are_found()
    {
        var table = Detected(
        [
            [1, "ok", new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), new DateTime(2026, 1, 1), null, null],
            [2, "late start", new DateTime(2026, 3, 1), new DateTime(2026, 2, 1), new DateTime(2026, 1, 1), null, null],
            [3, "open ended", new DateTime(2026, 3, 1), null, new DateTime(2026, 1, 1), null, null]
        ]);

        var finding = DataCheck.Of(table, Project()).Single();
        Assert.AreEqual(("Project", "EndDate", 1, 3), (finding.Table, finding.Column, finding.Count, finding.Total));
        Assert.AreEqual("a date not earlier than StartDate", finding.Rule);
        Assert.AreEqual("2026-03-01 to 2026-02-01", finding.Examples.Single());
    }

    [TestMethod]
    public async Task A_check_between_two_date_columns_of_a_sqlite_table_is_read_as_the_pair()
    {
        string database = Path.Combine(Path.GetTempPath(), "codegen_pairs_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ConnectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Booking (BookingId INTEGER PRIMARY KEY, Guest VARCHAR(40) NOT NULL, CheckIn DATE NOT NULL, CheckOut DATE NOT NULL, Note VARCHAR(50) NULL, CHECK (CheckOut >= CheckIn))";
                command.ExecuteNonQuery();
            }

            var provider = SchemaProviderFactory.Create(new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database }, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var model = await provider.BuildTableModelAsync("main", "Booking");

            Assert.AreEqual("CheckIn", model.Columns.Single(c => c.Name == "CheckOut").NotBeforeColumn);
            Assert.IsNull(model.Columns.Single(c => c.Name == "CheckIn").NotBeforeColumn);
            var pair = DatePairs.For(model, Project()).Single();
            Assert.AreEqual(("CheckIn", "CheckOut"), (pair.Start.Name, pair.End.Name));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(database); } catch (IOException) { }
        }
    }

    // ---- what each stack writes for the pair ----

    private static TableModel Shop() => Sample.Table("Project",
    [
        Sample.Column("ProjectId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2),
        Sample.Column("Code", SqlDbType.NVarChar, characters: 10, ordinal: 3),
        Sample.Column("StartDate", SqlDbType.Date, ordinal: 4),
        Sample.Column("EndDate", SqlDbType.Date, nullable: true, ordinal: 5),
        Sample.Column("Budget", SqlDbType.Int, ordinal: 6)
    ], childForeignKeys: [Sample.ChildForeignKey("ProjectTask", "ProjectId", "ProjectId", ["ProjectTaskId"])], startDate: "StartDate", endDate: "EndDate", dialect: SqlDialect.Sqlite);

    private static async Task<string> Render(string template, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Shop(), Project(values.Append(("DatabaseProvider", "Sqlite")).ToArray()));
        Assert.IsTrue(result.Success, template + ": " + string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private const string Sentence = "End Date must not be earlier than Start Date.";

    [TestMethod]
    public async Task The_server_validators_of_every_api_stack_refuse_an_end_before_its_start()
    {
        string cs = await Render("CS_Validator_v1.tt", ("ApiValidation", "true"));
        Expect.Contains(cs, "RuleFor(x => x.EndDate).Must((x, end) => ((DateTime?)x.StartDate) is not { } start || ((DateTime?)end) is not { } stop || stop.Date >= start.Date).WithMessage(\"" + Sentence + "\");");

        string py = await Render("PY_Validate_v1.tt", ("ApiValidation", "true"));
        Expect.Contains(py, "if stored is None or row.start_date != stored.start_date or row.end_date != stored.end_date:");
        Expect.Contains(py, "if row.start_date is not None and row.end_date is not None and row.end_date < row.start_date:");
        Expect.Contains(py, "problems.append((\"EndDate\", \"" + Sentence + "\"))");

        string rs = await Render("RS_Validate_v1.tt", ("ApiValidation", "true"));
        Expect.Contains(rs, "if stored.is_none_or(|s| s.start_date != row.start_date || s.end_date != row.end_date) {");
        Expect.Contains(rs, "if let (Some(start), Some(end)) = (Some(row.start_date), row.end_date.as_ref().map(|v| *v)) {");
        Expect.Contains(rs, "problems.push((\"EndDate\", \"" + Sentence + "\"));");

        foreach (string template in new[] { "CS_Validator_v1.tt", "PY_Validate_v1.tt", "RS_Validate_v1.tt" })
        {
            string exempt = await Render(template, ("ApiValidation", "true"), ("NoDatePairs", "Project"));
            Expect.DoesNotContain(exempt, Sentence);
        }
    }

    [TestMethod]
    public async Task The_zod_schema_and_the_angular_validators_check_the_pair_across_the_row()
    {
        string zod = await Render("TSX_Schema_v1.tt");
        Expect.Contains(zod, "}).refine((value) => !value.startDate || !value.endDate || value.endDate.substring(0, 10) >= value.startDate.substring(0, 10), { message: \"" + Sentence + "\", path: [\"endDate\"] });");
        Expect.DoesNotContain(await Render("TSX_Schema_v1.tt", ("NoDatePairs", "EndDate")), "refine(");

        string angular = await Render("TS_Validators_v1.tt");
        Expect.Contains(angular, "function notBefore(start: string, end: string): ValidatorFn {");
        Expect.Contains(angular, "export const projectGroupValidators: ValidatorFn[] = [notBefore(\"startDate\", \"endDate\")];");
        Expect.DoesNotContain(await Render("TS_Validators_v1.tt", ("NoDatePairs", "Project")), "notBefore");
    }

    [TestMethod]
    public async Task The_end_date_box_of_every_form_starts_at_the_start()
    {
        Expect.Contains(await Render("TSX_Page_v1.tt"), "min={selectedRow.startDate?.substring(0, 10)}");
        Expect.Contains(await Render("TSX_DetailMasterPage_v1.tt"), "min={selectedRow.startDate?.substring(0, 10)}");
        Expect.Contains(await Render("TS_Component_v1.tt"), "[attr.min]=\"selectedRow.startDate ? selectedRow.startDate.substring(0, 10) : null\"");
        Expect.Contains(await Render("TS_DetailMasterComponent_v1.tt"), "[attr.min]=\"selectedRow.startDate ? selectedRow.startDate.substring(0, 10) : null\"");
        Expect.Contains(await Render("BLZ_Page_v1.tt"), "<InputDate id=\"projectEndDate\" @bind-Value=\"row.EndDate\" min=\"@row.StartDate.ToString(\"yyyy-MM-dd\")\" />");

        foreach (string template in new[] { "TSX_Page_v1.tt", "TS_Component_v1.tt", "BLZ_Page_v1.tt" })
        {
            string exempt = await Render(template, ("NoDatePairs", "Project"));
            Expect.DoesNotContain(exempt, "min={selectedRow");
            Expect.DoesNotContain(exempt, "attr.min");
            Expect.DoesNotContain(exempt, " min=\"@row.");
        }
    }

    [TestMethod]
    public async Task The_desktop_dialogs_offer_the_end_calendar_from_the_start_and_refuse_a_changed_end_before_it()
    {
        foreach (string template in new[] { "WinUI3_DetailScreen_v1.tt", "UNO_DetailScreen_v1.tt" })
        {
            string text = await Render(template);
            Expect.Contains(text, "public DateTimeOffset EndDateMinDate => StartDate?.Date is { } start ? new DateTimeOffset(start, TimeSpan.Zero) : new DateTimeOffset(1920, 1, 1, 0, 0, 0, TimeSpan.Zero);");
            Expect.Contains(text, "partial void OnStartDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(EndDateMinDate));");
            Expect.Contains(text, "else if (StartDate is not null && EndDate.Value.Date < StartDate.Value.Date && (((DateTime?)entity.EndDate)?.Date != EndDate.Value.Date || ((DateTime?)entity.StartDate)?.Date != StartDate.Value.Date)) { ErrorMessage = \"" + Sentence + "\"; return false; }");
            Expect.DoesNotContain(await Render(template, ("NoDatePairs", "Project")), "EndDateMinDate");
        }

        var shop = Shop();
        var form = FormModel.For(shop, Project());
        string xaml = DesktopFormXaml.Field(form, Project(), shop, shop.Columns.Single(c => c.Name == "EndDate"), textKey: false);
        Expect.Contains(xaml, "<CalendarDatePicker Header=\"End Date\" MinDate=\"{x:Bind ViewModel.EndDateMinDate, Mode=OneWay}\" Date=\"{x:Bind ViewModel.EndDate, Mode=TwoWay}\"");
        Expect.DoesNotContain(DesktopFormXaml.Field(form, Project(), shop, shop.Columns.Single(c => c.Name == "StartDate"), textKey: false), "MinDate");
    }
}
