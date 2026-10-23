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
    public async Task The_blazor_rest_page_test_checks_the_sort_the_search_the_paging_the_columns_a_refused_save_delete_and_clone_and_the_odata_page_does_not()
    {
        string text = await Render("BLZ_Test_v1.tt", ("BlazorTests", "true"), ("OData", "true"));
        string rest = text[..text.IndexOf("public class AccountODataPageTests", StringComparison.Ordinal)];
        string odata = text[text.IndexOf("public class AccountODataPageTests", StringComparison.Ordinal)..];

        Expect.Contains(rest, "public void Sorts_by_a_header_and_flips_the_direction_when_it_is_clicked_again()");
        Expect.Contains(rest, "r.Path.Contains(\"sortBy=Title&sortDir=asc\")");
        Expect.Contains(rest, "r.Path.Contains(\"sortBy=Title&sortDir=desc\")");
        Expect.Contains(rest, "public void Searches_with_the_text_typed_and_Clear_drops_it()");
        Expect.Contains(rest, "page.Find(\"[aria-label=\\\"Search by Title\\\"]\").Input(\"zz9\");");
        Expect.Contains(rest, "r.Path.Contains(\"title=zz9\")");
        Expect.Contains(rest, "Open(FakeApi.Page(Row, 45, 1, 20))");
        Expect.Contains(rest, "\"Page 1 of 3\"");
        Expect.Contains(rest, "r.Path.Contains(\"pageNumber=2\")");
        Expect.Contains(rest, "public void Hides_a_column_unticked_in_the_Columns_list_and_Reset_shows_it_again()");
        Expect.Contains(rest, "Api.FailWrite(HttpMethod.Put, \"/1\", System.Net.HttpStatusCode.BadRequest);");
        Expect.Contains(rest, "\"Some of the values are not acceptable!\"");
        Expect.Contains(rest, "\"Problem while saving!\"");
        Expect.Contains(rest, "public void Deletes_a_row_only_after_the_person_confirms()");
        Expect.Contains(rest, "JSInterop.Setup<bool>(\"confirm\", _ => true).SetResult(true);");
        Expect.Contains(rest, "r.Method == HttpMethod.Delete && r.Path == \"/api/accounts/1\"");
        Expect.Contains(rest, "\"It cannot be deleted because it is in use!\"");
        Expect.Contains(rest, "Api.AnswerWrite(HttpMethod.Post, \"/1/clone\", Row);");
        Expect.Contains(rest, "r.Path == \"/api/accounts/1/clone\"");
        Expect.DoesNotContain(odata, "Sorts_by_a_header");
        Expect.DoesNotContain(odata, "Deletes_a_row_only_after");
        Expect.DoesNotContain(odata, "Pages_forward");
    }

    [TestMethod]
    public async Task The_blazor_fake_api_can_answer_and_fail_a_write_and_pages_a_search()
    {
        string fake = await RenderProject("BLZ_EssentialTests_v1.tt", ("BlazorTests", "true"));

        Expect.Contains(fake, "public FakeApi AnswerWrite(HttpMethod method, string pathEnd, string json)");
        Expect.Contains(fake, "public FakeApi FailWrite(HttpMethod method, string pathEnd, HttpStatusCode status)");
        Expect.Contains(fake, "public static string Page(string rowsJson, int count, int page = 1, int pageSize = 10)");
        Expect.Contains(fake, "Math.Max(1, (count + pageSize - 1) / pageSize)");
    }

    [TestMethod]
    public async Task The_blazor_menu_test_lists_a_link_for_every_screen_and_with_a_sign_in_the_links_each_role_may_read()
    {
        var table = Sample.Table("Account",
        [
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 40, ordinal: 2),
            Sample.Column("Note", SqlDbType.NVarChar, characters: 20, nullable: true, ordinal: 3),
            Sample.Column("Code", SqlDbType.NVarChar, characters: 10, nullable: true, ordinal: 4),
            Sample.Column("Size", SqlDbType.Int, nullable: true, ordinal: 5)
        ]);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "main", Dialect = SqlDialect.Sqlite, Tables = [table] };

        async Task<string> Menu(params (string Key, string Value)[] values)
        {
            var result = await Repo.Cache.RunAsync(Repo.Template("BLZ_TestMenu_v1.tt"), database, ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme"))));
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            return result.GeneratedText!.ReplaceLineEndings("\n");
        }

        string plain = await Menu(("BlazorTests", "true"));
        Expect.Contains(plain, "public class MenuTests : PageTestContext");
        Expect.Contains(plain, "\"account|Account\"");
        Expect.Contains(plain, "The_layout_shows_the_page_beside_the_menu_and_the_hamburger_hides_and_shows_the_menu");
        Expect.DoesNotContain(plain, "SignIn(");

        string signedIn = await Menu(("BlazorTests", "true"), ("Auth", "true"), ("ApiAuth", "true"), ("ReadRoles", "Administrator,Clerk"), ("TableReadRoles", "Account:Administrator"));
        Expect.Contains(signedIn, "[DataRow(\"Administrator\", \"dashboard|Dashboard;account|Account\")]");
        Expect.Contains(signedIn, "[DataRow(\"NoSuchRole\", \"dashboard|Dashboard\")]");
        Expect.Contains(signedIn, "A_person_who_is_not_signed_in_sees_no_menu_and_no_hamburger");
        Expect.Contains(signedIn, "SignIn(role);");

        Assert.AreEqual("", (await Menu(("BlazorTests", "false"))).Trim());
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
