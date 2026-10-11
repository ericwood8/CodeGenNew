using System.Data;
using System.Text.RegularExpressions;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A required field (NOT NULL, filled in by the person) carries a red * after its caption on the React, Angular, Blazor and WinUI3 forms; the stacks read one rule in Core (FormModel.IsRequired). </summary>
[TestClass]
public class RequiredMarksTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    /// <summary> Required: Name, Email, Region, Balance. Not required: Nickname (nullable), Subscribed (a yes/no box always holds a value), CreateDate (the system writes it), the key. </summary>
    private static TableModel Signup() => Sample.Table("Signup",
    [
        Sample.Column("SignupId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2),
        Sample.Column("Email", SqlDbType.NVarChar, characters: 80, ordinal: 3),
        Sample.Column("Nickname", SqlDbType.NVarChar, characters: 40, nullable: true, ordinal: 4),
        Sample.Column("RegionId", SqlDbType.Int, ordinal: 5),
        Sample.Column("Balance", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 6, currency: true),
        Sample.Column("Subscribed", SqlDbType.Bit, ordinal: 7),
        Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 8)
    ],
    [Sample.ForeignKey("RegionId", "Region", "RegionId", "Name")]);

    private static readonly string[] Marked = ["Balance", "Email", "Name", "Region"];

    private static async Task<string> Rendered(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void The_rule_marks_a_NOT_NULL_field_the_person_fills_in_and_the_setting_turns_the_marks_off()
    {
        var table = Signup();
        var form = FormModel.For(table, Project());
        var required = form.Columns.Where(form.IsRequired).Select(c => c.Name).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "Balance", "Email", "Name", "RegionId" }, required);
        CollectionAssert.AreEqual(required, form.Columns.Where(form.IsMarked).Select(c => c.Name).Order().ToArray());

        var off = FormModel.For(table, Project(("RequiredMarks", "false")));
        Assert.IsEmpty(off.Columns.Where(off.IsMarked).ToList());
        CollectionAssert.AreEqual(required, off.Columns.Where(off.IsRequired).Select(c => c.Name).Order().ToArray(), "off only hides the mark; the field is still required");
        Assert.IsTrue(Project().RequiredMarks);
        Assert.IsFalse(Project(("RequiredMarks", "false")).RequiredMarks);
    }

    [TestMethod]
    public void The_text_key_a_person_types_is_required()
    {
        var table = Sample.Table("Country",
        [
            Sample.Column("Code", SqlDbType.NVarChar, characters: 2, primaryKey: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2)
        ]);
        var form = FormModel.For(table, Project());
        CollectionAssert.AreEqual(new[] { "Code", "Name" }, form.Columns.Where(form.IsMarked).Select(c => c.Name).Order().ToArray());
    }

    private static string[] MarkedCaptions(string text, string pattern) =>
        [.. Regex.Matches(text, pattern).Where(m => m.Groups[2].Success).Select(m => m.Groups[1].Value).Order()];

    private static int Fields(string text, string pattern) => Regex.Matches(text, pattern).Count;

    [TestMethod]
    public async Task Every_stack_marks_the_same_captions_and_only_those()
    {
        var table = Signup();
        var project = Project(("AngularVersion", "22"));

        // React: <label htmlFor="signupName">Name <span aria-hidden="true" style={{ color: '#b00020' }}>*</span></label>
        const string react = "<label htmlFor=\"signup\\w+\">([^<]+?)( <span aria-hidden=\"true\" style=\\{\\{ color: '#b00020' \\}\\}>\\*</span>)?</label>";
        string reactPage = await Rendered("TSX_Page_v1.tt", table, project);
        CollectionAssert.AreEqual(Marked, MarkedCaptions(reactPage, react), "React");
        Assert.AreEqual(7, Fields(reactPage, react), "React shows six fields and the system stamp");

        // Angular and Blazor: <label for="signupName">Name <span aria-hidden="true" style="color: #b00020">*</span></label>
        const string html = "<label for=\"signup\\w+\">([^<]+?)( <span aria-hidden=\"true\" style=\"color: #b00020\">\\*</span>)?</label>";
        string angular = await Rendered("TS_Component_v1.tt", table, project);
        CollectionAssert.AreEqual(Marked, MarkedCaptions(angular, html), "Angular");
        Assert.AreEqual(7, Fields(angular, html), "Angular shows six fields and the system stamp");
        string blazor = await Rendered("BLZ_Page_v1.tt", table, project);
        CollectionAssert.AreEqual(Marked, MarkedCaptions(blazor, html), "Blazor");
        Assert.AreEqual(7, Fields(blazor, html), "Blazor shows six fields and the system stamp");

        // WinUI3: the header template draws the red *, and UI Automation is told the field is required
        const string xaml = "<(?:TextBox|ComboBox|NumberBox|CalendarDatePicker) [^>]*?Header=\"([^\"]+)\"( AutomationProperties\\.IsRequiredForForm=\"True\" HeaderTemplate=\"\\{StaticResource RequiredHeader\\}\")?";
        string winui = await Rendered("WinUI3_DetailScreen_v1.tt", table, project);
        CollectionAssert.AreEqual(Marked, MarkedCaptions(winui, xaml), "WinUI3");
        Assert.AreEqual(5, Fields(winui, xaml), "WinUI3 shows five boxes; the yes/no box has no header");
        Expect.Contains(winui, "<DataTemplate x:Key=\"RequiredHeader\">");
        Expect.Contains(winui, "Foreground=\"Red\"");
    }

    [TestMethod]
    public async Task Without_RequiredMarks_no_stack_draws_a_mark_but_the_field_stays_required()
    {
        var table = Signup();
        var project = Project(("AngularVersion", "22"), ("RequiredMarks", "false"));

        foreach (var (template, name) in new[] { ("TSX_Page_v1.tt", "React"), ("TS_Component_v1.tt", "Angular"), ("BLZ_Page_v1.tt", "Blazor"), ("WinUI3_DetailScreen_v1.tt", "WinUI3") })
        {
            string text = await Rendered(template, table, project);
            Expect.DoesNotContain(text, "#b00020");
            Expect.DoesNotContain(text, "RequiredHeader");
            Expect.DoesNotContain(text, "aria-hidden=\"true\"");
        }

        Expect.Contains(await Rendered("TSX_Page_v1.tt", table, project), "              required");
        Expect.Contains(await Rendered("WinUI3_DetailScreen_v1.tt", table, project), "AutomationProperties.IsRequiredForForm=\"True\"");
    }
}
