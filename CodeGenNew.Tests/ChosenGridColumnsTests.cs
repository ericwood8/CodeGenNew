using System.Data;
using System.Text.RegularExpressions;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> Item 118: the Columns list of every grid (Angular, React and Blazor; REST and OData). One Core rule says which columns a grid offers, starts with, sorts and filters by and how a saved choice is read;
/// the three stacks write the same lists from it, so they cannot drift apart. </summary>
[TestClass]
public class ChosenGridColumnsTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<string> Run(string template, TableModel table, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static async Task<string> RunSupport(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [] }, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    // Title, City, a parent, a money column, long notes, a credential and a flag; the long notes and the credential are the interesting ones
    private static TableModel Account() => Sample.Table("Account",
    [
        Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("City", SqlDbType.NVarChar, characters: 40, nullable: true, ordinal: 3),
        Sample.Column("StatusId", SqlDbType.Int, ordinal: 4),
        Sample.Column("Balance", SqlDbType.Money, ordinal: 5, currency: true),
        Sample.Column("Notes", SqlDbType.NVarChar, characters: 1000, nullable: true, ordinal: 6),
        Sample.Column("PasswordHash", SqlDbType.VarChar, characters: 200, ordinal: 7),
        Sample.Column("IsOpen", SqlDbType.Bit, ordinal: 8)
    ], [Sample.ForeignKey("StatusId", "Status", "StatusId", "Name")]);

    private static TableModel Wide() => Sample.Table("Wide",
    [
        Sample.Column("WideId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        .. Enumerable.Range(2, 25).Select(i => Sample.Column("Field" + i, SqlDbType.NVarChar, characters: 20, ordinal: i))
    ]);

    private static TableModel Colour() => Sample.Table("Colour",
    [
        Sample.Column("ColourId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("IsActive", SqlDbType.Bit, ordinal: 3)
    ]);

    private static string[] Names(IEnumerable<ChosenColumn> columns) => columns.Select(c => c.Name).ToArray();

    [TestMethod]
    public void A_grid_offers_every_column_of_the_form_and_starts_with_the_ones_the_generated_grid_shows()
    {
        var offered = ChosenGridColumns.For(FormModel.For(Account(), Project()));

        CollectionAssert.AreEqual(new[] { "Title", "City", "StatusId", "Balance", "Notes", "IsOpen" }, Names(offered.Offered), "the key and the credential are never offered");
        CollectionAssert.AreEqual(new[] { "Title", "City", "StatusId", "Balance", "IsOpen" }, Names(offered.Initial), "long text is offered, but the grid does not start with it");
        Assert.IsTrue(offered.Offered.Single(c => c.Name == "Notes").Cut, "a long text cell shows the first characters only");
        Assert.IsFalse(offered.Offered.Single(c => c.Name == "Title").Cut);
    }

    [TestMethod]
    public void A_column_sorts_when_the_server_sorts_by_it_and_has_the_filter_control_of_its_kind()
    {
        var offered = ChosenGridColumns.For(FormModel.For(Account(), Project())).Offered.ToDictionary(c => c.Name);

        Assert.IsTrue(offered["Title"].Sortable);
        Assert.IsTrue(offered["StatusId"].Sortable, "a foreign key sorts by the parent's name");
        Assert.IsFalse(offered["Notes"].Sortable, "long text is never sorted");
        Assert.AreEqual(ODataFilterKind.Text, offered["Title"].Filter!.Kind);
        Assert.AreEqual(ODataFilterKind.Number, offered["Balance"].Filter!.Kind);
        Assert.AreEqual(ODataFilterKind.Boolean, offered["IsOpen"].Filter!.Kind);
        Assert.IsNull(offered["Notes"].Filter, "long text has no filter control");
        Assert.AreEqual("statusId", offered["StatusId"].Property);
        Assert.AreEqual("Title", offered["Title"].Caption);
    }

    [TestMethod]
    public void A_grid_of_more_than_eighteen_columns_offers_the_rest()
    {
        var offered = ChosenGridColumns.For(FormModel.For(Wide(), Project()));

        Assert.HasCount(25, offered.Offered);
        Assert.HasCount(GridColumns.MaxColumns, offered.Initial);
        Assert.IsFalse(offered.Initial.Any(c => c.Name == "Field26"));
        Assert.IsTrue(offered.Offered.Any(c => c.Name == "Field26"));
    }

    [TestMethod]
    public void A_text_key_is_a_column_of_the_grid_and_can_be_hidden()
    {
        var offered = ChosenGridColumns.For(FormModel.For(Sample.NaturalKey(), Project()));

        CollectionAssert.AreEqual(new[] { "Code", "Label" }, Names(offered.Offered), "a typed key is the first column of the form and so of the list");
        CollectionAssert.AreEqual(new[] { "Code", "Label" }, Names(offered.Initial));
    }

    [TestMethod]
    public void A_saved_choice_is_read_back_by_one_rule()
    {
        string[] offered = ["Title", "City", "Balance", "Notes"];
        string[] initial = ["Title", "City"];

        CollectionAssert.AreEqual(initial, ChosenGridColumns.Resolve(null, offered, initial).ToArray(), "nothing saved");
        CollectionAssert.AreEqual(new[] { "Title", "Balance" }, ChosenGridColumns.Resolve(["Balance", "Title"], offered, initial).ToArray(), "the table's order wins");
        CollectionAssert.AreEqual(new[] { "Title" }, ChosenGridColumns.Resolve(["Gone", "Title"], offered, initial).ToArray(), "a column the table no longer has is dropped");
        CollectionAssert.AreEqual(initial, ChosenGridColumns.Resolve(["Gone"], offered, initial).ToArray(), "none of the saved names is a column any more");
        CollectionAssert.AreEqual(initial, ChosenGridColumns.Resolve([], offered, initial).ToArray());
    }

    [TestMethod]
    public void Ticking_and_clearing_keep_the_tables_order_and_the_last_column()
    {
        string[] offered = ["Title", "City", "Balance", "Notes"];

        CollectionAssert.AreEqual(new[] { "Title", "City", "Balance" }, ChosenGridColumns.Toggle(["Title", "City"], offered, "Balance").ToArray());
        CollectionAssert.AreEqual(new[] { "Title", "City" }, ChosenGridColumns.Toggle(["City"], offered, "Title").ToArray(), "a ticked column goes where the table has it");
        CollectionAssert.AreEqual(new[] { "Title" }, ChosenGridColumns.Toggle(["Title", "City"], offered, "City").ToArray());
        CollectionAssert.AreEqual(new[] { "Title" }, ChosenGridColumns.Toggle(["Title"], offered, "Title").ToArray(), "the last shown column cannot be cleared");
        CollectionAssert.AreEqual(new[] { "Title" }, ChosenGridColumns.Toggle(["Title"], offered, "Gone").ToArray());
    }

    // The names a stack wrote for the Columns list, in order, and the ones it starts with
    private static string[] ReactOffered(string page) => Regex.Matches(page, @"\{ name: '(\w+)', label: '").Select(m => m.Groups[1].Value).ToArray();
    private static string[] ReactInitial(string page) => Regex.Matches(page.Split('\n').Single(l => l.StartsWith("const initialColumns = [")), "'(\\w+)'").Select(m => m.Groups[1].Value).ToArray();
    private static string[] AngularOffered(string files) => Regex.Matches(files, @"\{ name: '(\w+)', label: '[^']*', property: '\w+', sortable: (?:true|false) \}").Select(m => m.Groups[1].Value).ToArray();
    private static string[] AngularInitial(string files) => Regex.Matches(files.Split('\n').Single(l => l.StartsWith("  ], ['")), "'(\\w+)'").Select(m => m.Groups[1].Value).ToArray();
    private static string[] BlazorOffered(string page) => Regex.Matches(page, "new\\(\"(\\w+)\", \"[^\"]*\", \"\\w+\", (?:true|false)\\),").Select(m => m.Groups[1].Value).ToArray();
    private static string[] BlazorInitial(string page) => Regex.Matches(page.Split('\n').Single(l => l.Contains("private static readonly string[] InitialColumns = [")), "\"(\\w+)\"").Select(m => m.Groups[1].Value).ToArray();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Every_web_grid_writes_the_same_columns_in_the_same_order(bool odata)
    {
        var form = FormModel.For(Account(), Project());
        var expected = ChosenGridColumns.For(form);
        (string Key, string Value)[] on = odata ? [("OData", "true")] : [];

        string react = await Run(odata ? "TSX_ODataPage_v1.tt" : "TSX_Page_v1.tt", Account(), on);
        string angular = await Run(odata ? "TS_ODataComponent_v1.tt" : "TS_Component_v1.tt", Account(), on);
        string blazor = await Run(odata ? "BLZ_ODataPage_v1.tt" : "BLZ_Page_v1.tt", Account(), on);

        foreach (var (stack, offered, initial) in new[]
        {
            ("React", ReactOffered(react), ReactInitial(react)),
            ("Angular", AngularOffered(angular), AngularInitial(angular)),
            ("Blazor", BlazorOffered(blazor), BlazorInitial(blazor))
        })
        {
            CollectionAssert.AreEqual(Names(expected.Offered), offered, stack + " offers");
            CollectionAssert.AreEqual(Names(expected.Initial), initial, stack + " starts with");
        }

        // the headers a stack draws are wrapped in the test of their column, so a hidden column draws nothing
        foreach (string name in Names(expected.Offered))
        {
            Expect.Contains(react, "{show('" + name + "') && ");
            Expect.Contains(angular, "*ngIf=\"columns.has('" + name + "')\"");
            Expect.Contains(blazor, "@if (Shown(\"" + name + "\")) {");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_long_text_cell_is_cut_by_each_stacks_helper_and_a_credential_is_never_offered(bool odata)
    {
        (string Key, string Value)[] on = odata ? [("OData", "true")] : [];
        string react = await Run(odata ? "TSX_ODataPage_v1.tt" : "TSX_Page_v1.tt", Account(), on);
        string angular = await Run(odata ? "TS_ODataComponent_v1.tt" : "TS_Component_v1.tt", Account(), on);
        string blazor = await Run(odata ? "BLZ_ODataPage_v1.tt" : "BLZ_Page_v1.tt", Account(), on);

        Expect.Contains(react, "{show('Notes') && <td>{shortText(account.notes)}</td>}");
        Expect.Contains(angular, "<td *ngIf=\"columns.has('Notes')\">{{ cut(account.notes) }}</td>");
        Expect.Contains(angular, "readonly cut = cutText;");
        Expect.Contains(blazor, "@if (Shown(\"Notes\")) { <td>@GridColumns.Cut(item.Notes)</td> }");
        foreach (string text in new[] { react, angular, blazor })
        {
            Expect.DoesNotContain(text, "PasswordHash'");
            Expect.DoesNotContain(text, "PasswordHash\"");
        }
        Expect.Contains(react, "{show('Notes') && <th>Notes</th>}");   // a long text column has a plain header: it cannot be sorted
    }

    [TestMethod]
    public async Task A_hidden_column_loses_its_sort_and_its_filter_and_an_OData_page_reads_only_the_shown_columns()
    {
        (string, string)[] on = [("OData", "true")];
        string react = await Run("TSX_ODataPage_v1.tt", Account(), on);
        Expect.Contains(react, "const keptSorts = sorts.filter((s) => next.includes(s.column));");
        Expect.Contains(react, "const keptFilters = onlyShown(columnFilters, next);");
        Expect.Contains(react, "select: (over.columns ?? chosen).map((name) => columnProperty[name]),");
        Expect.Contains(react, "load(page, { sorts: keptSorts, columnFilters: keptFilters, columns: next });");

        string angular = await Run("TS_ODataComponent_v1.tt", Account(), on);
        Expect.Contains(angular, "const kept = this.sorts.filter((s) => this.columns.has(s.column));");
        Expect.Contains(angular, "if (!this.columns.has(field.name)) this.columnFilters[field.name] = {};");
        Expect.Contains(angular, "protected override readonly selectColumns = this.columns.properties;");

        string blazor = await Run("BLZ_ODataPage_v1.tt", Account(), on);
        Expect.Contains(blazor, "if (sorts.RemoveAll(s => !Shown(s.Column)) > 0)");
        Expect.Contains(blazor, "typed.Remove(name);");
        Expect.Contains(blazor, "applied = ODataFilters.ConditionsOf(FilterFields.Where(f => Shown(f.Name)), typed);");
        Expect.Contains(blazor, "AllColumns.Where(c => Shown(c.Name)).Select(c => c.Property).ToList()");
    }

    [TestMethod]
    public async Task A_REST_grid_drops_the_sort_of_a_hidden_column_and_reads_whole_rows()
    {
        string react = await Run("TSX_Page_v1.tt", Account());
        Expect.Contains(react, "if (sort && !next.includes(sort.column)) clearSort();");
        Expect.DoesNotContain(react, "select:");
        Expect.DoesNotContain(react, "onlyShown");

        string angular = await Run("TS_Component_v1.tt", Account());
        Expect.Contains(angular, "if (this.sort && !this.columns.has(this.sort.column)) this.clearSort();");
        Expect.DoesNotContain(angular, "selectColumns");

        string blazor = await Run("BLZ_Page_v1.tt", Account());
        Expect.Contains(blazor, "if (sortBy is not null && !Shown(sortBy))");
        Expect.DoesNotContain(blazor, "ODataPageQuery");
    }

    [TestMethod]
    public async Task A_name_and_active_list_has_no_columns_list_in_the_web_stacks_that_write_it()
    {
        foreach (string template in new[] { "TSX_Page_v1.tt", "TS_Component_v1.tt" })   // Blazor writes no screen for a name / active table
        {
            string text = await Run(template, Colour());
            Expect.DoesNotContain(text, "column-picker");
            Expect.DoesNotContain(text, "gridColumns");
            Expect.DoesNotContain(text, "GridColumns");
        }
    }

    [TestMethod]
    public async Task The_helpers_of_the_three_stacks_cut_a_long_text_at_the_same_length_and_read_the_choice_back_the_same_way()
    {
        string react = await RunSupport("TSX_GridColumns_v1.tt");
        string angular = await RunSupport("TS_GridColumns_v1.tt");
        string blazor = await RunSupport("BLZ_GridColumns_v1.tt");

        Expect.Contains(react, "@@@FILE components/gridColumns.tsx@@@");
        Expect.Contains(angular, "@@@FILE grid-columns.ts@@@");
        Expect.Contains(blazor, "@@@FILE Services/GridColumns.cs@@@");
        Expect.Contains(react, $"text.length > {ChosenGridColumns.CutLength} ? text.substring(0, {ChosenGridColumns.CutLength - 3}) + '...'");
        Expect.Contains(angular, $"text.length > {ChosenGridColumns.CutLength} ? text.substring(0, {ChosenGridColumns.CutLength - 3}) + '...'");
        Expect.Contains(blazor, $"public const int CutLength = {ChosenGridColumns.CutLength};");
        Expect.Contains(blazor, "text.Length > CutLength ? text[..(CutLength - 3)] + \"...\" : text");

        // the key a choice is kept under and what a bad value does are the same in the three
        Expect.Contains(react, "`gridColumns:${gridKey}`");
        Expect.Contains(angular, "`gridColumns:${gridKey}`");
        Expect.Contains(blazor, "catch (JsonException)");
        Expect.Contains(react, "return kept.length > 0 ? kept : [...initial];");
        Expect.Contains(angular, "return kept.length > 0 ? kept : [...initial];");
        Expect.Contains(blazor, "return kept.Count > 0 ? kept : [.. initial];");
    }
}
