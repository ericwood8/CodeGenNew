using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> A table the project keeps one row of is a form on every UI stack, unless it is served as OData (those pages are lists). </summary>
[TestClass]
public class SingleRowTableTests
{
    private static TableModel Settings() => Sample.Table("Settings",
    [
        Sample.Column("SettingsId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CompanyName", SqlDbType.NVarChar, characters: 60, ordinal: 2),
        Sample.Column("TaxRate", SqlDbType.Decimal, ordinal: 3),
        Sample.Column("Phone", SqlDbType.NVarChar, characters: 20, nullable: true, ordinal: 4),
        Sample.Column("Notes", SqlDbType.NVarChar, characters: 200, nullable: true, ordinal: 5),
        Sample.Column("Currency", SqlDbType.NVarChar, characters: 3, ordinal: 6)
    ]);

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("City", SqlDbType.NVarChar, characters: 50, ordinal: 3),
        Sample.Column("Email", SqlDbType.NVarChar, characters: 80, ordinal: 4),
        Sample.Column("CreditLimit", SqlDbType.Int, ordinal: 5)
    ]);

    private static readonly DatabaseModel Database = new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Settings(), Customer()] };

    private static ProjectSettings Project(params string[] stacks) =>
        ProjectSettings.FromValues([new("ProjectName", "Acme"), new("SingleRowTables", "settings"), new("Stacks", string.Join(",", stacks))]);

    private static HashSet<string> Given(string table, params string[] stacks)
    {
        var steps = ProjectPlan.Build(TemplateCatalog.Discover(Repo.TemplatesDirectory), Database, Project(stacks), stacks);
        return steps.Where(s => !s.IsDatabaseLevel && s.TableNames.Contains(table)).Select(s => s.Template.Name).ToHashSet();
    }

    [TestMethod]
    public void The_setting_names_tables_whatever_the_case()
    {
        var project = ProjectSettings.FromValues([new("SingleRowTables", "Settings, GeneralLedgerPostingAccount")]);

        Assert.IsTrue(project.IsSingleRow("settings"));
        Assert.IsTrue(project.IsSingleRow("GeneralLedgerPostingAccount"));
        Assert.IsFalse(project.IsSingleRow("Customer"));
    }

    [TestMethod]
    public void On_WinUI3_the_table_gets_the_form_and_its_dialog_but_no_list_page()
    {
        var settings = Given("Settings", "WinUI3");

        CollectionAssert.IsSubsetOf(new[] { "WinUI3_SingleRowScreen", "WinUI3_DetailScreen" }, settings.ToList());
        Assert.IsFalse(settings.Contains("WinUI3_MasterScreen"));
        var customer = Given("Customer", "WinUI3");
        CollectionAssert.IsSubsetOf(new[] { "WinUI3_MasterScreen", "WinUI3_DetailScreen" }, customer.ToList());
        Assert.IsFalse(customer.Contains("WinUI3_SingleRowScreen"));
    }

    [TestMethod]
    public void On_Uno_the_table_gets_the_form_page_and_its_dialog_but_no_list_page()
    {
        var settings = Given("Settings", "Uno");

        CollectionAssert.IsSubsetOf(new[] { "UNO_SingleRowScreen", "UNO_DetailScreen" }, settings.ToList());
        Assert.IsFalse(settings.Contains("UNO_MasterScreen"));
    }

    [TestMethod]
    public void On_a_web_stack_the_ordinary_page_template_writes_the_form_and_there_is_no_master_detail_page()
    {
        Assert.IsTrue(Given("Settings", "React").Contains("TSX_Page"));
        Assert.IsTrue(Given("Settings", "Angular").Contains("TS_Component"));
        Assert.IsTrue(Given("Settings", "Blazor").Contains("BLZ_Page"));
        Assert.IsFalse(Given("Settings", "React").Contains("WinUI3_SingleRowScreen"));
    }

    [TestMethod]
    public void A_table_served_as_OData_keeps_its_pages()
    {
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("SingleRowTables", "Settings"), new("OData", "true")]);

        Assert.IsFalse(SingleRowPlan.Applies(Settings(), project, "React"));
        Assert.IsTrue(SingleRowPlan.Applies(Settings(), ProjectSettings.FromValues([new("SingleRowTables", "Settings")]), "React"));
    }

    private static async Task<string> Rendered(string template, TableModel table, params string[] stacks)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project(stacks));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!;
    }

    [TestMethod]
    public async Task The_React_page_is_the_form_with_Submit_and_Reload_and_no_grid()
    {
        string page = (await Rendered("TSX_Page_v1.tt", Settings(), "React")).Split("@@@FILE ")[1];   // the page, not its test

        Expect.Contains(page, "<h1>Settings</h1>");
        Expect.Contains(page, "<section>");
        Expect.Contains(page, ">Reload</button>");
        Expect.Contains(page, "getPage(1, 1, {}, null)");
        Assert.DoesNotContain("Add New", page);
        Assert.DoesNotContain("<table>", page);
        Assert.DoesNotContain("PaginationBar", page);
        Assert.DoesNotContain("<dialog", page);
    }

    [TestMethod]
    public async Task The_Angular_screen_is_the_form_on_the_one_row_base()
    {
        string screen = await Rendered("TS_Component_v1.tt", Settings(), "Angular");

        Expect.Contains(screen, "extends SingleRowCrudScreen<Settings>");
        Expect.Contains(screen, "<section *ngIf=\"selectedRow\" #oneRowForm class=\"form-container\">");
        Expect.Contains(screen, ">Reload</button>");
        Assert.DoesNotContain("Add New", screen);
        Assert.DoesNotContain("<mat-paginator", screen);
        Assert.DoesNotContain("<table", screen);
    }

    [TestMethod]
    public async Task The_Blazor_page_is_the_form_and_reads_the_first_row()
    {
        string page = await Rendered("BLZ_Page_v1.tt", Settings(), "Blazor");

        Expect.Contains(page, "<h1>Settings</h1>");
        Expect.Contains(page, ">Reload</button>");
        Expect.Contains(page, "GetPageAsync(1, 1, new Dictionary<string, string?>(), null, false)");
        Assert.DoesNotContain("Add New", page);
        Assert.DoesNotContain("<table>", page);
        Assert.DoesNotContain("class=\"paging\"", page);
    }

    [TestMethod]
    public async Task The_Uno_page_reads_and_saves_through_the_API_client()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("UNO_SingleRowScreen_v1.tt"), Settings(), Project("Uno"));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string code = GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith("SettingsPage.xaml.cs")).Content;

        Expect.Contains(code, "_api.");
        Expect.Contains(code, "GetPageAsync(1, 1, sortBy: \"SettingsId\")");
        Expect.Contains(code, "new SettingsDetailViewModel(_api, row)");
    }

    [TestMethod]
    public async Task The_page_is_a_form_with_Save_and_Reload_and_nothing_to_list_add_or_delete()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_SingleRowScreen_v1.tt"), Settings(), Project("WinUI3"));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!);

        string xaml = files.Single(f => f.RelativePath.EndsWith("SettingsPage.xaml")).Content;
        string code = files.Single(f => f.RelativePath.EndsWith("SettingsPage.xaml.cs")).Content;
        Expect.Contains(xaml, "x:Class=\"Acme.App.Views.SettingsPage\"");
        Expect.Contains(xaml, "Click=\"OnSaveClick\"");
        Expect.Contains(xaml, "Click=\"OnReloadClick\"");
        Expect.Contains(xaml, "x:Bind ViewModel.CompanyName");
        Assert.DoesNotContain("Delete", xaml);
        Assert.DoesNotContain("Add New", xaml);
        Expect.Contains(code, "OrderBy(r => r.SettingsId).FirstOrDefaultAsync()");
        Expect.Contains(code, "new SettingsDetailViewModel(_context, row)");
        Expect.Contains(code, "ViewModelBase.InTurnAsync");
    }

    [TestMethod]
    public async Task The_menu_opens_the_page_for_a_single_row_table_and_the_list_for_the_others()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_Screens_v1.tt"), Database, Project("WinUI3"));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));

        Expect.Contains(result.GeneratedText!, "\"Settings\" => new SettingsPage(_context),");
        Expect.Contains(result.GeneratedText!, "\"Customer\" => new CustomerListPage(_context),");
    }

    [TestMethod]
    public void The_run_notes_name_the_shape_and_suggest_it_for_a_table_with_one_row()
    {
        var oneRow = Sample.Table("Branding",
        [
            Sample.Column("BrandingId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Logo", SqlDbType.NVarChar, characters: 60, ordinal: 2),
            Sample.Column("Color", SqlDbType.NVarChar, characters: 10, ordinal: 3),
            Sample.Column("Footer", SqlDbType.NVarChar, characters: 80, ordinal: 4),
            Sample.Column("Slogan", SqlDbType.NVarChar, characters: 80, ordinal: 5)
        ], rowCount: 1);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Settings(), Customer(), oneRow] };
        var project = Project("WinUI3", "React");

        var notes = TableShapeNotes.Of([], database, project, ["WinUI3", "React"]);

        CollectionAssert.Contains(notes, "Branding: holds one row; if the application keeps only one, list it in SingleRowTables to get a form instead of a list.");
        Assert.AreEqual("single-row table", TableShapeNotes.Shape(Settings(), project));
    }

    [TestMethod]
    public void The_desktop_page_xaml_is_one_text_for_both_stacks_with_the_stack_differences_as_switches()
    {
        var form = FormModel.For(Settings(), Project("WinUI3"));

        string plain = DesktopSingleRowPage.Xaml(form, Project("WinUI3"), Settings(), "Acme.Views", withGuards: false, loadErrorBar: false, gatedSave: false);
        string uno = DesktopSingleRowPage.Xaml(form, Project("Uno"), Settings(), "Acme.Views", withGuards: true, loadErrorBar: true, gatedSave: true);

        Expect.Contains(plain, "x:Class=\"Acme.Views.SettingsPage\"");
        Expect.Contains(plain, "Click=\"OnSaveClick\" />");
        Assert.DoesNotContain("LoadError", plain);
        Expect.Contains(uno, "x:Name=\"LoadError\"");
        Expect.Contains(uno, "Click=\"OnSaveClick\" Visibility=\"{x:Bind WriteVisibility}\" />");
    }

    [TestMethod]
    public void The_desktop_handlers_save_and_reload_and_clear_a_nullable_date()
    {
        var table = Sample.Table("Settings",
        [
            Sample.Column("SettingsId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("CompanyName", SqlDbType.NVarChar, characters: 60, ordinal: 2),
            Sample.Column("Opened", SqlDbType.Date, nullable: true, ordinal: 3)
        ]);
        var form = FormModel.For(table, Project("WinUI3"));

        string handlers = DesktopSingleRowPage.Handlers(form, "Settings");

        Expect.Contains(handlers, "private async void OnSaveClick(");
        Expect.Contains(handlers, "private async void OnReloadClick(");
        Expect.Contains(handlers, "private void OnClearOpenedClick(");
    }

    [TestMethod]
    public void A_web_test_row_holds_the_key_and_the_starting_values_and_a_heading_names_one_record()
    {
        var form = FormModel.For(Settings(), Project("React"));

        string row = SingleRowPlan.TypeScriptRow(form);

        StringAssert.StartsWith(row, "{ settingsId: 1, ");
        Expect.Contains(row, "companyName: ''");
        Assert.AreEqual("Setting", SingleRowPlan.Heading("Setting", true));
        Assert.AreEqual("Settings", SingleRowPlan.Heading("Setting", false));
    }
}
