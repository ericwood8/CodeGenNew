using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The loose ends of the React master-detail and write checks: a grid that is not left with one column because the text is long, a credential column that no screen shows, and the Angular form that
/// refuses a blank required field instead of posting it. Run live in a scratch project (a blank form posts nothing, a required field on a hidden tab opens that tab, a filled form posts once). </summary>
[TestClass]
public class ScreenColumnsTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static ColumnModel Text(string name, int characters, int ordinal, bool nullable = false) =>
        Sample.Column(name, SqlDbType.NVarChar, characters: characters, nullable: nullable, ordinal: ordinal);

    private static bool Multiline(ColumnModel c) => c.CharacterLength >= 100;

    private static string[] Names(IEnumerable<ColumnModel> columns) => columns.Select(c => c.Name).ToArray();

    [TestMethod]
    public void A_grid_with_fewer_than_three_columns_is_filled_with_multi_line_text()
    {
        var shown = new List<ColumnModel> { Text("Title", 50, 1), Text("Summary", 200, 2), Text("Body", 300, 3), Text("Remarks", 300, 4) };
        CollectionAssert.AreEqual(new[] { "Title", "Summary", "Body" }, Names(GridColumns.Choose(shown, Multiline)), "filled up to three, in table order");

        var enough = new List<ColumnModel> { Text("A", 50, 1), Text("B", 50, 2), Text("C", 50, 3), Text("Summary", 200, 4) };
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Names(GridColumns.Choose(enough, Multiline)), "three ordinary columns are enough");

        var allLong = new List<ColumnModel> { Text("Summary", 200, 1), Text("Body", 300, 2) };
        CollectionAssert.AreEqual(new[] { "Summary", "Body" }, Names(GridColumns.Choose(allLong, Multiline)), "nothing ordinary: all of them");
    }

    [TestMethod]
    public void A_long_text_column_does_not_count_as_a_visible_column()
    {
        var details = Sample.Column("Details", SqlDbType.NText, ordinal: 3);
        var shown = new List<ColumnModel> { Text("Title", 50, 1), Text("Summary", 200, 2), details };
        var grid = GridColumns.Choose(shown, Multiline).ForGrid();
        CollectionAssert.AreEqual(new[] { "Title", "Summary" }, Names(grid), "Details is long text and stays out of the grid, so Summary fills in");
    }

    [TestMethod]
    public void A_credential_is_known_by_its_name_and_never_searched()
    {
        foreach (string name in new[] { "PasswordHash", "Password", "UserPassword", "PasswordSalt", "Salt", "ClientSecret", "StripeApiKey", "RefreshToken" })
            Assert.IsTrue(Text(name, 100, 1).IsCredentialColumn, name);
        foreach (string name in new[] { "UserName", "Email", "Title", "TokenCount", "Hashtag" })
            Assert.IsFalse(Text(name, 100, 1).IsCredentialColumn, name);
        Assert.IsFalse(Sample.Column("PasswordAge", SqlDbType.Int).IsCredentialColumn, "only text can hold a credential");

        var users = Sample.Table("AppUser",
        [
            Sample.Column("AppUserId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Text("UserName", 50, 2), Text("PasswordHash", 200, 3), Text("Email", 100, 4)
        ]);
        CollectionAssert.AreEqual(new[] { "UserName", "Email" }, Names(users.SearchableColumns));
    }

    private static TableModel Ticket() => Sample.Table("Ticket",
    [
        Sample.Column("TicketId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Text("Title", 50, 2), Text("Summary", 200, 3), Sample.Column("Details", SqlDbType.NText, ordinal: 4)
    ]);

    private static TableModel AppUser() => Sample.Table("AppUser",
    [
        Sample.Column("AppUserId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Text("UserName", 50, 2), Text("PasswordHash", 200, 3), Text("Email", 100, 4)
    ]);

    private static async Task<Dictionary<string, string>> Render(string template, TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project(("AngularVersion", "22")));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public async Task The_screens_show_the_summary_in_the_grid_and_no_credential_anywhere()
    {
        var ticket = await Render("TS_Component_v1.tt", Ticket());
        string grid = ticket["ticket.component.html"];
        Expect.Contains(grid, "ariaSort('Summary')");

        var user = await Render("TS_Component_v1.tt", AppUser());
        Expect.DoesNotContain(user["appuser.component.html"], "passwordHash");
        Expect.DoesNotContain(user["appuser.component.html"], "Password Hash");
        Expect.DoesNotContain(user["appuser.component.ts"], "filters = { userName: '', passwordHash");
        Expect.Contains(user["appuser.component.html"], "selectedRow.email");

        var page = await Repo.Cache.RunAsync(Repo.Template("TSX_Page_v1.tt"), AppUser(), Project());
        Assert.IsTrue(page.Success, string.Join(" | ", page.Errors));
        Expect.DoesNotContain(page.GeneratedText!, "Password Hash");
    }

    [TestMethod]
    public async Task The_Angular_form_validates_natively_and_opens_the_tab_of_an_invalid_field()
    {
        var tabbed = await Render("TS_Component_v1.tt", Ticket());
        string html = tabbed["ticket.component.html"];
        string ts = tabbed["ticket.component.ts"];
        Expect.Contains(html, "<form id=\"ticketForm\" (ngSubmit)=\"submitForm($event, selectedRow)\">");
        Expect.Contains(html, "<div role=\"tabpanel\" data-tab=\"1\" [hidden]=\"activeTab !== 1\">");
        Expect.Contains(html, "<button type=\"submit\" class=\"btn\">Submit</button>");
        Expect.Contains(ts, "protected override showBroken(field: HTMLElement & { reportValidity(): boolean }): void {");
        Expect.Contains(ts, "field.closest('[role=\"tabpanel\"]')");

        var plain = await Render("TS_Component_v1.tt", AppUser());
        Expect.Contains(plain["appuser.component.html"], "(ngSubmit)=\"submitForm($event, selectedRow)\"");
        Expect.DoesNotContain(plain["appuser.component.html"], "ngNativeValidate");
        Expect.DoesNotContain(plain["appuser.component.ts"], "showBroken");
    }
}
