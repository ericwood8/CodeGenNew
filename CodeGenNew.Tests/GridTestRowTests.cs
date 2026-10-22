using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The generated React and Angular tests check what a list page shows: a row the server returns, and the Edit button of that row. </summary>
[TestClass]
public class GridTestRowTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Account() => Sample.Table("Account",
    [
        Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 40, ordinal: 2),
        Sample.Column("Region", SqlDbType.NVarChar, characters: 20, nullable: true, ordinal: 3),
        Sample.Column("Balance", SqlDbType.Int, ordinal: 4),
        Sample.Column("IsOpen", SqlDbType.Bit, ordinal: 5)
    ]);

    private static async Task<string> Render(string template, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Account(), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void The_row_has_a_key_the_marked_text_column_and_a_value_for_every_required_column()
    {
        var form = FormModel.For(Account(), Project());

        var row = GridTestRow.For(form, ChosenGridColumns.For(form))!;

        Assert.AreEqual("Sample row", row.Marker);
        Expect.Contains(row.Literal, "accountId: 1");
        Expect.Contains(row.Literal, "title: 'Sample row'");
        Expect.Contains(row.Literal, "balance: 0");
        Expect.DoesNotContain(row.Literal, "region");   // optional
    }

    [TestMethod]
    public async Task The_react_page_shows_a_row_and_opens_the_dialog_from_its_Edit_button()
    {
        string test = await Render("TSX_Page_v1.tt");

        Expect.Contains(test, "it('shows a row the server returns', async () => {");
        Expect.Contains(test, "expect(await screen.findByText('Sample row')).toBeInTheDocument();");
        Expect.Contains(test, "it('opens the Account dialog from the Edit button of a row', async () => {");
        Expect.Contains(test, "HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };");
        Expect.Contains(test, "import { fireEvent, render, screen } from '@testing-library/react';");
        Expect.DoesNotContain(test, "'getById'");
    }

    [TestMethod]
    public async Task The_react_odata_page_answers_the_read_by_key_that_Edit_makes()
    {
        string test = await Render("TSX_ODataPage_v1.tt", ("OData", "true"));

        Expect.Contains(test, "'getById'");
    }

    [TestMethod]
    public async Task The_angular_component_flushes_a_row_and_looks_for_it_in_the_page()
    {
        string spec = await Render("TS_Component_v1.tt");

        Expect.Contains(spec, "it('shows a row the server returns', () => {");
        Expect.Contains(spec, "{ items: [{ accountId: 1, title: 'Sample row'");
        Expect.Contains(spec, "expect(fixture.nativeElement.textContent).toContain('Sample row');");
        Expect.Contains(spec, "it('opens the Account dialog from the Edit button of a row', () => {");
        Expect.Contains(spec, "expect(fixture.nativeElement.textContent).toContain('Edit Account');");
    }

    [TestMethod]
    public async Task A_screen_whose_Edit_button_needs_a_sign_in_gets_only_the_row_check()
    {
        string spec = await Render("TS_Component_v1.tt", ("ApiAuth", "true"), ("WriteRoles", "Administrator,Manager"), ("Auth", "true"));

        Expect.Contains(spec, "it('shows a row the server returns', () => {");
        Expect.DoesNotContain(spec, "from the Edit button of a row");
    }

    [TestMethod]
    public void A_grid_with_no_plain_required_text_column_has_no_row_to_look_for()
    {
        var numbers = Sample.Table("Count",
        [
            Sample.Column("CountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Total", SqlDbType.Int, ordinal: 2),
            Sample.Column("Note", SqlDbType.NVarChar, characters: 20, nullable: true, ordinal: 3)
        ]);
        var form = FormModel.For(numbers, Project());

        Assert.IsNull(GridTestRow.For(form, ChosenGridColumns.For(form)));
    }
}
