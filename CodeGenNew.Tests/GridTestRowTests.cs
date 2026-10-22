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

    private static async Task<string> RenderProject(string template, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

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
    public async Task A_screen_whose_Edit_button_needs_a_sign_in_signs_the_spec_in_first()
    {
        string spec = await Render("TS_Component_v1.tt", ("ApiAuth", "true"), ("WriteRoles", "Administrator,Manager"), ("Auth", "true"));

        Expect.Contains(spec, "sessionStorage.setItem(");
        Expect.Contains(spec, "role: 'Manager'");
        Expect.Contains(spec, "it('opens the Account dialog from the Edit button of a row', () => {");
        Expect.Contains(spec, "afterEach(() => sessionStorage.clear());");
    }

    [TestMethod]
    public async Task The_angular_spec_fills_the_dialog_and_submits_the_row()
    {
        string spec = await Render("TS_Component_v1.tt");

        Expect.Contains(spec, "it('fills the Account dialog with the row and sends the row to the service on Submit', async () => {");
        Expect.Contains(spec, "r.method === 'PUT'");
    }

    [TestMethod]
    public void The_angular_odata_spec_answers_the_read_by_key_that_Edit_makes()
    {
        var row = new GridTestRow("{ id: 1 }", "x");

        string spec = string.Join("\n", row.AngularSpecs("Account", "(r) => true", "{}", "(r) => r.url.startsWith('odata/accounts/')"));

        Expect.Contains(spec, "http.match((r) => r.url.startsWith('odata/accounts/')).forEach((request) => request.flush({ id: 1 }));");
        Expect.DoesNotContain(string.Join("\n", row.AngularSpecs("Account", "(r) => true", "{}")), "http.match(");
    }

    [TestMethod]
    public void A_grid_with_no_plain_required_text_column_marks_a_required_whole_number()
    {
        var numbers = Sample.Table("Count",
        [
            Sample.Column("CountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Total", SqlDbType.Int, ordinal: 2),
            Sample.Column("Note", SqlDbType.NVarChar, characters: 20, nullable: true, ordinal: 3)
        ]);
        var form = FormModel.For(numbers, Project());

        var row = GridTestRow.For(form, ChosenGridColumns.For(form))!;

        Assert.AreEqual("42", row.Marker);
        Expect.Contains(row.Literal, "total: 42");
    }

    [TestMethod]
    public void A_grid_with_no_required_text_or_whole_number_column_has_no_row_to_look_for()
    {
        var flags = Sample.Table("Flag",
        [
            Sample.Column("FlagId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("IsOn", SqlDbType.Bit, ordinal: 2),
            Sample.Column("Note", SqlDbType.NVarChar, characters: 20, nullable: true, ordinal: 3)
        ]);
        var form = FormModel.For(flags, Project());

        Assert.IsNull(GridTestRow.For(form, ChosenGridColumns.For(form)));
    }

    [TestMethod]
    public async Task The_angular_spec_opens_the_row_named_by_the_edit_query_parameter()
    {
        string spec = await Render("TS_Component_v1.tt");

        Expect.Contains(spec, "describe('AccountComponent opened from another page', () => {");
        Expect.Contains(spec, "convertToParamMap({ edit: '1' })");
        Expect.Contains(spec, "r.url.endsWith('/1')");
    }

    [TestMethod]
    public async Task The_blazor_test_has_the_row_as_json_and_signs_in_when_the_app_has_a_sign_in()
    {
        string plain = await Render("BLZ_Test_v1.tt", ("BlazorTests", "true"));

        Expect.Contains(plain, "public class AccountPageTests : PageTestContext");
        Expect.Contains(plain, "\"title\": \"Sample row\"");
        Expect.Contains(plain, "NavigateTo(\"/account?edit=1\")");
        Expect.DoesNotContain(plain, "SignIn(");

        string signedIn = await Render("BLZ_Test_v1.tt", ("BlazorTests", "true"), ("Auth", "true"), ("ApiAuth", "true"), ("WriteRoles", "Administrator,Manager"));

        Expect.Contains(signedIn, "SignIn(\"Manager\");");
    }

    [TestMethod]
    public async Task The_blazor_test_adds_a_class_for_the_odata_page_when_the_table_is_served_as_odata()
    {
        string text = await Render("BLZ_Test_v1.tt", ("BlazorTests", "true"), ("OData", "true"));

        Expect.Contains(text, "public class AccountODataPageTests : PageTestContext");
        Expect.Contains(text, "FakeApi.ODataPage(Row, 1)");
        Expect.Contains(text, "Api.Answer(\"/odata/accounts/1\", Row);");
        Expect.DoesNotContain(await Render("BLZ_Test_v1.tt", ("BlazorTests", "true")), "ODataPageTests");
    }

    [TestMethod]
    public async Task The_blazor_test_project_tests_the_dashboard_only_when_the_project_has_one()
    {
        string with = await RenderProject("BLZ_EssentialTests_v1.tt", ("BlazorTests", "true"), ("Dashboard", "true"), ("CurrencyCode", "EUR"));

        Expect.Contains(with, "public class DashboardPageTests : PageTestContext");
        Expect.Contains(with, "EUR 1,234.50");
        Expect.DoesNotContain(await RenderProject("BLZ_EssentialTests_v1.tt", ("BlazorTests", "true"), ("Dashboard", "false")), "DashboardPageTests");
    }
}
