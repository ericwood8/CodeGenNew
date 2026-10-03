using System.Data;
using System.Text.RegularExpressions;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> Runs the SHIPPED templates (the files in Templates\) against hand-built tables -- no database -- and checks
/// the text they produce. These are the guard rails for the templates' rules, refusals and project settings. </summary>
[TestClass]
public class TemplateRenderingTests
{
    private static async Task<TemplateResult> Run(string templateFile, TableModel model) =>
        await TemplateRunner.RunAsync(Repo.Template(templateFile), model);

    private static async Task<string> Render(string templateFile, TableModel model)
    {
        var result = await Run(templateFile, model);
        Assert.IsTrue(result.Success, $"{templateFile} failed for {model.TableName}: {string.Join(" | ", result.Errors)}");
        // A template checked out with CRLF line endings (git autocrlf) renders CRLF; the assertions below spell line breaks as LF.
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    private static async Task<string> Refusal(string templateFile, TableModel model)
    {
        var result = await Run(templateFile, model);
        Assert.IsFalse(result.Success, $"{templateFile} should have refused {model.TableName}");
        return string.Join(" | ", result.Errors);
    }

    // ------------------------------------------------------------------ every template

    [TestMethod]
    public async Task Every_shipped_template_compiles_and_runs()
    {
        // A template that does not compile reports "error CS...."; a deliberate refusal is a plain message. Neither may be the former.
        var table = Sample.DonateLeave();
        foreach (var template in TemplateCatalog.Discover(Repo.TemplatesDirectory))
        {
            // A database-level template is given every table instead of one.
            var result = template.Config.DatabaseOnly
                ? await TemplateRunner.RunAsync(template.FilePath, new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [table] })
                : await Run(template.FileStem + ".tt", template.Config.NeedsRowData ? Sample.Roles() : table);
            string errors = string.Join(" | ", result.Errors);
            Assert.DoesNotContain("error CS", errors, $"{template.FileStem} does not compile: {errors}");
        }
    }

    // ------------------------------------------------------------------ stored procedures

    [TestMethod]
    [DataRow("SP_Insert_v1.tt", "E_DonateLeave_Insert")]
    [DataRow("SP_Update_v1.tt", "E_DonateLeave_Update")]
    [DataRow("SP_Delete_v1.tt", "E_DonateLeave_Delete")]
    [DataRow("SP_Save_v1.tt", "E_DonateLeave_Save")]
    [DataRow("SP_Lookup_v1.tt", "E_DonateLeave_Lookup")]
    [DataRow("SP_Clone_v1.tt", "E_DonateLeave_Clone")]
    [DataRow("SP_Search_v1.tt", "E_DonateLeave_Search")]
    public async Task Each_stored_procedure_template_writes_its_procedure(string template, string procedure)
    {
        string sql = await Render(template, Sample.DonateLeave());

        Expect.Contains(sql, $"CREATE OR ALTER PROCEDURE [dbo].[{procedure}]");
    }

    [TestMethod]
    public async Task The_load_procedure_carries_the_tables_rows()
    {
        string sql = await Render("SP_Load_v1.tt", Sample.Roles());

        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[SY_Role_Load]");
        Expect.Contains(sql, "N'Human Resources'");
    }

    // ------------------------------------------------------------------ SP_Search

    [TestMethod]
    public async Task SP_Search_generates_with_no_filter_parameters_when_the_table_has_no_searchable_columns()
    {
        // Pagination and searchability are separate concerns (found live needing pagination alone on such a
        // table, 2026-09-28): a table with no searchable column still gets both procedures, just with no
        // filter parameters and no WHERE clause at all.
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        string sql = await Render("SP_Search_v1.tt", allNumeric);

        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[Metric_Search]");
        Expect.Contains(sql, "@PageNumber INT = 1,");
        Expect.Contains(sql, "@PageSize INT = 100");
        Expect.DoesNotContain(sql, "WHERE");
        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[Metric_SearchCount]");
        Expect.Contains(sql, "SELECT COUNT(*)");
        Expect.Contains(sql, "FROM [dbo].[Metric];");
        // Empty "( )" is invalid T-SQL -- CREATE PROCEDURE with no parameters must omit the parens
        // entirely, not emit an empty pair (found live deploying this exact zero-column case, 2026-09-28).
        Expect.DoesNotContain(sql, "[dbo].[Metric_SearchCount]\r\n(\r\n)");
        Expect.DoesNotContain(sql, "[dbo].[Metric_SearchCount]\n(\n)");
    }

    [TestMethod]
    public async Task SP_Search_writes_one_optional_parameter_per_searchable_column()
    {
        string sql = await Render("SP_Search_v1.tt", Sample.Holiday());

        Expect.Contains(sql, "@pSY_IsoCountry_Alpha3Code char(3) = NULL");
        Expect.Contains(sql, "@pName nvarchar(50) = NULL");
    }

    [TestMethod]
    public async Task SP_Search_ANDs_a_like_filter_per_supplied_column()
    {
        string sql = await Render("SP_Search_v1.tt", Sample.Holiday());

        Expect.Contains(sql, "(@pName IS NULL OR [Name] LIKE '%' + LTRIM(RTRIM(@pName)) + '%')");
        Expect.Contains(sql, "AND (@pName IS NULL OR");
    }

    // ------------------------------------------------------------------ PostgreSQL

    private static TableModel AsPostgres(TableModel t) => new()
    {
        SchemaName = "public", TableName = t.TableName, QuotedName = $"\"public\".\"{t.TableName}\"", Dialect = SqlDialect.PostgreSql,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys, ChildForeignKeys = t.ChildForeignKeys,
        DisplayColumns = t.DisplayColumns, HasReferencedDisplayColumns = t.HasReferencedDisplayColumns, Rows = t.Rows
    };

    [TestMethod]
    public async Task SP_Search_on_PostgreSQL_writes_a_function_returning_the_tables_rows()
    {
        string sql = (await Render("SP_Search_v1.tt", AsPostgres(Sample.Holiday())));

        Expect.Contains(sql, "CREATE OR REPLACE FUNCTION \"public\".\"Holiday_Search\"(");
        Expect.Contains(sql, "RETURNS SETOF \"public\".\"Holiday\"");
        Expect.Contains(sql, "(\"pName\" IS NULL OR \"Name\" ILIKE '%' || btrim(\"pName\") || '%')");
        Expect.Contains(sql, "OFFSET (\"PageNumber\" - 1) * \"PageSize\" LIMIT \"PageSize\"");
        Expect.Contains(sql, "CREATE OR REPLACE FUNCTION \"public\".\"Holiday_SearchCount\"(");
        Expect.Contains(sql, "RETURNS integer");
        Expect.DoesNotContain(sql, "EXEC");
        Expect.DoesNotContain(sql, "@p");
    }

    [TestMethod]
    [DataRow("SP_Insert_v1.tt", "CREATE OR REPLACE FUNCTION \"public\".\"Holiday_Insert\"(")]
    [DataRow("SP_Update_v1.tt", "CREATE OR REPLACE FUNCTION \"public\".\"Holiday_Update\"(")]
    [DataRow("SP_Save_v1.tt", "CREATE OR REPLACE FUNCTION \"public\".\"Holiday_Save\"(")]
    [DataRow("SP_Delete_v1.tt", "CREATE OR REPLACE FUNCTION \"public\".\"Holiday_Delete\"(")]
    [DataRow("SP_Clone_v1.tt", "CREATE OR REPLACE FUNCTION \"public\".\"Holiday_Clone\"(")]
    public async Task The_SP_templates_write_a_PostgreSQL_function_for_a_PostgreSQL_table(string template, string expected)
    {
        string sql = await Render(template, AsPostgres(Sample.Holiday()));

        Expect.Contains(sql, expected);
        Expect.DoesNotContain(sql, "CREATE OR ALTER PROCEDURE");
    }

    [TestMethod]
    public async Task SP_Search_on_PostgreSQL_with_nothing_to_filter_has_no_where_clause()
    {
        var allNumeric = Sample.Table("Counter", [Sample.Column("CounterId", SqlDbType.Int, primaryKey: true)]);

        string sql = (await Render("SP_Search_v1.tt", AsPostgres(allNumeric)));

        Expect.DoesNotContain(sql, "WHERE");
        Expect.Contains(sql, "SELECT count(*)::integer FROM \"public\".\"Counter\";");
    }

    [TestMethod]
    public async Task The_search_endpoint_and_repository_call_a_function_with_Npgsql_parameters_on_PostgreSQL()
    {
        string repo = await Render("CS_Repo_v1.tt", AsPostgres(Sample.Holiday()));
        string api = await Render("API_Search_v1.tt", AsPostgres(Sample.Holiday()));

        foreach (string code in new[] { repo, api })
        {
            Expect.Contains(code, "SELECT * FROM \\\"public\\\".\\\"Holiday_Search\\\"(@pSY_IsoCountry_Alpha3Code, @pName, @PageNumber, @PageSize, @SortColumn, @SortDescending)");
            Expect.Contains(code, "SELECT \\\"public\\\".\\\"Holiday_SearchCount\\\"(@pSY_IsoCountry_Alpha3Code, @pName) AS \\\"Value\\\"");
            Expect.Contains(code, "NpgsqlParameter(\"@pName\", NpgsqlTypes.NpgsqlDbType.Text)");
            Expect.DoesNotContain(code, "SqlParameter(");
            Expect.DoesNotContain(code, "EXEC [");
        }
        Expect.Contains(api, "using Npgsql;");
    }

    [TestMethod]
    public async Task SP_Search_excludes_audit_columns_from_the_filter()
    {
        string sql = await Render("SP_Search_v1.tt", Sample.WithModifiedByColumn());

        Expect.Contains(sql, "@pSubject nvarchar(100) = NULL");
        Expect.DoesNotContain(sql, "@pModifiedBy");
    }

    [TestMethod]
    public async Task SP_Search_returns_every_column_not_just_the_display_columns()
    {
        string sql = await Render("SP_Search_v1.tt", Sample.DonateLeave());

        Expect.Contains(sql, "[DonateLeaveId]");
        Expect.Contains(sql, "[HoursDonated]");
        Expect.Contains(sql, "[WhenDonated]");
        Expect.Contains(sql, "[Note]");
    }

    [TestMethod]
    public async Task SP_Search_orders_by_the_best_display_column_then_the_primary_key()
    {
        string sql = await Render("SP_Search_v1.tt", Sample.Holiday());

        Expect.Contains(sql, "[Name] ASC, [HolidayId] ASC");
    }

    [TestMethod]
    public async Task SP_Search_pages_its_results_with_offset_fetch()
    {
        string sql = await Render("SP_Search_v1.tt", Sample.Holiday());

        Expect.Contains(sql, "@PageNumber INT = 1,");
        Expect.Contains(sql, "@PageSize INT = 100");
        Expect.Contains(sql, "OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;");
    }

    [TestMethod]
    public async Task SP_Search_also_writes_a_matching_SearchCount_procedure_with_the_same_filter_and_no_paging()
    {
        string sql = await Render("SP_Search_v1.tt", Sample.Holiday());

        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[Holiday_SearchCount]");
        Expect.Contains(sql, "SELECT COUNT(*)");
        Expect.DoesNotContain(sql.Substring(sql.IndexOf("Holiday_SearchCount")), "@PageNumber");
        Expect.DoesNotContain(sql.Substring(sql.IndexOf("Holiday_SearchCount")), "OFFSET");
    }

    // ------------------------------------------------------------------ SP_Junction (many-to-many junction tables)

    [TestMethod]
    public async Task SP_Junction_refuses_a_table_that_is_not_a_junction_table()
    {
        string message = await Refusal("SP_Junction_v1.tt", Sample.DonateLeave());

        StringAssert.Contains(message, "IsJunctionTable");
    }

    [TestMethod]
    public async Task SP_Junction_writes_list_link_and_unlink_for_the_surrogate_key_shape()
    {
        // The real-world shape (a second production database's dbo.NameBaseGroupXref table): ID is the PK,
        // NameBaseID/GroupID are plain FK columns -- confirmed against a live database, 2026-09-25.
        string sql = await Render("SP_Junction_v1.tt", Sample.JunctionWithSurrogateKey());

        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[NameBaseGroupXref_List]");
        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[NameBaseGroupXref_Link]");
        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[NameBaseGroupXref_Unlink]");
        Expect.Contains(sql, "@AnchorNameBaseID int");
        Expect.Contains(sql, "@TargetGroupID int");
        Expect.Contains(sql, "[t].[ID] AS TargetId");
        Expect.Contains(sql, "[t].[ShortDescr]"); // Groups' display column
        Expect.Contains(sql, "ORDER BY [t].[ShortDescr]");
        // CreateDate/CreateUser are audit columns on this fixture: Link should set/accept them, not treat
        // them as a third "structural" association column.
        Expect.Contains(sql, "GETDATE()");
        Expect.Contains(sql, "@CreateUser varchar(50) = NULL");
    }

    [TestMethod]
    public async Task SP_Junction_writes_list_link_and_unlink_for_the_composite_key_shape()
    {
        string sql = await Render("SP_Junction_v1.tt", Sample.CompositeKeyJunction());

        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[UserRole_List]");
        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[UserRole_Link]");
        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[UserRole_Unlink]");
        Expect.Contains(sql, "@AnchorUserId int");
        Expect.Contains(sql, "@TargetRoleId int");
        // No audit columns on this fixture, so Link's parameter list is exactly the two association columns.
        Expect.DoesNotContain(sql, "@CreateUser");
    }

    [TestMethod]
    public async Task SP_Junction_link_only_inserts_when_the_pair_does_not_already_exist()
    {
        string sql = await Render("SP_Junction_v1.tt", Sample.JunctionWithSurrogateKey());

        Expect.Contains(sql, "IF NOT EXISTS (");
        Expect.Contains(sql, "INSERT INTO [dbo].[NameBaseGroupXref]");
    }

    // ------------------------------------------------------------------ WinUI3_JunctionEditor

    [TestMethod]
    public async Task WinUI3_JunctionEditor_refuses_a_table_that_is_not_a_junction_table()
    {
        string message = await Refusal("WinUI3_JunctionEditor_v1.tt", Sample.DonateLeave());

        StringAssert.Contains(message, "IsJunctionTable");
    }

    [TestMethod]
    public async Task WinUI3_JunctionEditor_writes_view_codebehind_and_viewmodel()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_JunctionEditor_v1.tt", Sample.JunctionWithSurrogateKey()));

        Expect.Contains(string.Join("|", files.Select(f => f.RelativePath)), "Views/NameBaseGroupXrefJunctionEditor.xaml");
        var xaml = files.Single(f => f.RelativePath.EndsWith(".xaml")).Content;
        var codeBehind = files.Single(f => f.RelativePath.EndsWith(".xaml.cs")).Content;
        var viewModel = files.Single(f => f.RelativePath.EndsWith("JunctionEditorViewModel.cs")).Content;

        Expect.Contains(xaml, "x:Class=\"TimeEntry.Desktop.Views.NameBaseGroupXrefJunctionEditor\"");
        Expect.Contains(xaml, "Title=\"Groups for this NameBase\"");
        Expect.Contains(xaml, "ItemsSource=\"{x:Bind ViewModel.Available}\"");
        Expect.Contains(xaml, "ItemsSource=\"{x:Bind ViewModel.Selected}\"");

        Expect.Contains(codeBehind, "public sealed partial class NameBaseGroupXrefJunctionEditor : ContentDialog");
        Expect.Contains(codeBehind, "AvailableList.SelectedItems.Cast<JunctionListItem>()");

        Expect.Contains(viewModel, "public string? ShortDescr { get; set; }"); // Groups' display column
        Expect.Contains(viewModel, "public int TargetId { get; set; }");
        Expect.Contains(viewModel, "public NameBaseGroupXrefJunctionEditorViewModel(TimeEntryContext context, int anchorId)");
        Expect.Contains(viewModel, "SqlQueryRaw<JunctionListItem>(\"EXEC [dbo].[NameBaseGroupXref_List] @AnchorNameBaseID\"");
        Expect.Contains(viewModel, "$\"EXEC [dbo].[NameBaseGroupXref_Link] {_anchorId}, {item.TargetId}\"");
        Expect.Contains(viewModel, "$\"EXEC [dbo].[NameBaseGroupXref_Unlink] {_anchorId}, {item.TargetId}\"");
    }

    [TestMethod]
    public async Task WinUI3_JunctionEditor_moves_items_between_available_and_selected_on_link_and_unlink()
    {
        string viewModel = GeneratedFiles.Split(await Render("WinUI3_JunctionEditor_v1.tt", Sample.JunctionWithSurrogateKey()))
            .Single(f => f.RelativePath.EndsWith("JunctionEditorViewModel.cs")).Content;

        Expect.Contains(viewModel, "if (Available.Remove(item))");
        Expect.Contains(viewModel, "Selected.Add(item);");
        Expect.Contains(viewModel, "if (Selected.Remove(item))");
        Expect.Contains(viewModel, "Available.Add(item);");
    }

    // ------------------------------------------------------------------ WinUI3_DetailScreen

    [TestMethod]
    public async Task WinUI3_DetailScreen_refuses_a_composite_or_non_int_primary_key()
    {
        string message = await Refusal("WinUI3_DetailScreen_v1.tt", Sample.CompositeKey());

        StringAssert.Contains(message, "single int primary key");
    }

    [TestMethod]
    public async Task WinUI3_DetailScreen_refuses_a_name_active_table()
    {
        string message = await Refusal("WinUI3_DetailScreen_v1.tt", Sample.DepartmentTeam());

        StringAssert.Contains(message, "NameActiveRepo");
    }

    [TestMethod]
    public async Task WinUI3_DetailScreen_writes_dialog_codebehind_and_viewmodel()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_DetailScreen_v1.tt", Sample.DonateLeave()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));

        Assert.HasCount(3, files);
        string xaml = files["E_DonateLeaveDetailDialog.xaml"].Content;
        string codeBehind = files["E_DonateLeaveDetailDialog.xaml.cs"].Content;
        string viewModel = files["E_DonateLeaveDetailViewModel.cs"].Content;

        Expect.Contains(xaml, "x:Class=\"TimeEntry.Desktop.Views.E_DonateLeaveDetailDialog\"");
        Expect.Contains(xaml, "SelectedValue=\"{x:Bind ViewModel.DonateFrom_EmployeeId, Mode=TwoWay}\"");
        Expect.Contains(xaml, "SelectedValue=\"{x:Bind ViewModel.DonateTo_EmployeeId, Mode=TwoWay}\"");
        Expect.Contains(xaml, "<CalendarDatePicker Header=\"When Donated\" Date=\"{x:Bind ViewModel.WhenDonated, Mode=TwoWay}\"");
        Expect.Contains(xaml, "Text=\"{x:Bind ViewModel.Note, Mode=TwoWay}\"");

        Expect.Contains(codeBehind, "public sealed partial class E_DonateLeaveDetailDialog : ContentDialog");
        Expect.Contains(codeBehind, "public E_DonateLeaveDetailDialog(TimeEntryContext context, E_DonateLeave? editing = null)");

        Expect.Contains(viewModel, "private readonly E_DonateLeaveRepo _repo;");
        Expect.Contains(viewModel, "public ObservableCollection<E_DonateLeaveDetailLookupOption> EmployeeOptions { get; } = [];");
        Expect.Contains(viewModel, "private int? _donateFrom_EmployeeId;");
        Expect.Contains(viewModel, "private string? _note;");
        Expect.DoesNotContain(viewModel, "EmployeeRepo"); // lookup options come straight from the DbContext
    }

    [TestMethod]
    public async Task WinUI3_DetailScreen_save_validates_required_fields_and_parses_the_rest()
    {
        string viewModel = GeneratedFiles.Split(await Render("WinUI3_DetailScreen_v1.tt", Sample.DonateLeave()))
            .Single(f => f.RelativePath.EndsWith("DetailViewModel.cs")).Content;

        Expect.Contains(viewModel, "if (DonateFrom_EmployeeId is null) { ErrorMessage = \"Donate From Employee is required.\"; return false; }");
        // a whole-number column is a number box: a double (NaN = blank) checked for being whole and in range, then cast back
        Expect.Contains(viewModel, "private double _hoursDonated = double.NaN;");
        Expect.Contains(viewModel, "if (double.IsNaN(HoursDonated))");
        Expect.Contains(viewModel, "else if (HoursDonated != Math.Floor(HoursDonated)) { ErrorMessage = \"Hours Donated must be a whole number.\"; return false; }");
        Expect.Contains(viewModel, "else { entity.HoursDonated = (int)HoursDonated; }");
        // a date column is a calendar picker holding a nullable DateTimeOffset; saving keeps the stored time of day
        Expect.Contains(viewModel, "private DateTimeOffset? _whenDonated;");
        Expect.Contains(viewModel, "if (WhenDonated is null)\n        {\n            ErrorMessage = \"When Donated is required.\"; return false;");
        Expect.Contains(viewModel, "entity.WhenDonated = WhenDonated.Value.Date + _whenDonatedTime;");
        Expect.Contains(viewModel, "entity.Note = string.IsNullOrWhiteSpace(Note) ? null : Note;"); // nullable text
        Expect.Contains(viewModel, "await _repo.AddAsync(entity);");
        Expect.Contains(viewModel, "await _repo.UpdateAsync(entity.DonateLeaveId, entity);");
    }

    // ------------------------------------------------------------------ WinUI3_MasterScreen

    [TestMethod]
    public async Task WinUI3_MasterScreen_refuses_a_name_active_table()
    {
        string message = await Refusal("WinUI3_MasterScreen_v1.tt", Sample.DepartmentTeam());

        StringAssert.Contains(message, "NameActiveRepo");
    }

    [TestMethod]
    public async Task WinUI3_MasterScreen_writes_a_grid_page_matching_the_detail_screens_fields()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", Sample.DonateLeave()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));

        Assert.HasCount(6, files); // the page, its code-behind, its ViewModel, the shared PaginationBar (xaml + code-behind) and the saved-sort store
        string xaml = files["E_DonateLeaveListPage.xaml"].Content;
        string codeBehind = files["E_DonateLeaveListPage.xaml.cs"].Content;
        string viewModel = files["E_DonateLeaveListViewModel.cs"].Content;

        Expect.Contains(xaml, "x:Class=\"TimeEntry.Desktop.Views.E_DonateLeaveListPage\"");
        Expect.Contains(xaml, "Content=\"Donate From Employee\"");   // a sortable header is a button
        Expect.Contains(xaml, "ItemsSource=\"{x:Bind ViewModel.Rows}\"");
        Expect.Contains(xaml, "Text=\"{x:Bind Cells[0]}\"");

        Expect.Contains(codeBehind, "public sealed partial class E_DonateLeaveListPage : Page");
        Expect.Contains(codeBehind, "new E_DonateLeaveDetailDialog(_context)");
        Expect.Contains(codeBehind, "new E_DonateLeaveDetailDialog(_context, entity)");
        // The XAML's own Refresh button (Click="OnRefreshClick") had no matching handler at all until this was
        // caught by actually compiling a generated project for real (2026-09-27) -- every table was affected.
        Expect.Contains(xaml, "Button Content=\"Refresh\" Click=\"OnRefreshClick\"");
        Expect.Contains(codeBehind, "private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();");

        Expect.Contains(viewModel, "public class E_DonateLeaveListRow");
        Expect.Contains(viewModel, "var employeeNames = await _context.Set<Employee>().ToDictionaryAsync(r => r.EmployeeId, r => r.Name?.ToString() ?? \"\");");
        Expect.Contains(viewModel, "cells.Add(employeeNames.TryGetValue(e.DonateFrom_EmployeeId, out var donateFrom_EmployeeIdName) ? donateFrom_EmployeeIdName : e.DonateFrom_EmployeeId.ToString());");
        Expect.Contains(viewModel, "int result = await _repo.DeleteAsync(\"E_DonateLeave\", id);");
    }

    [TestMethod]
    public async Task WinUI3_MasterScreen_pages_the_grid_through_SearchAsync_with_a_PaginationBar_when_the_table_has_a_searchable_column()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", Sample.Holiday()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));
        string xaml = files["HolidayListPage.xaml"].Content;
        string codeBehind = files["HolidayListPage.xaml.cs"].Content;
        string viewModel = files["HolidayListViewModel.cs"].Content;

        // PaginationBar lives in the Views namespace, not the ViewModels namespace "local:" already means
        // (claimed by <Table>ListRow's own DataTemplate) -- referencing it via "local:" compiled the XAML but
        // failed at compile time with WMC0001 "Unknown type 'PaginationBar'", caught only once this template
        // was actually compiled for real (2026-09-27).
        Expect.Contains(xaml, "xmlns:views=\"using:TimeEntry.Desktop.Views\"");
        // Pagination bar is boxed in the same shaded Border as the Add New/Refresh and search bars
        // (2026-09-27 grid/toolbar styling) -- the Border immediately wraps <views:PaginationBar.
        Expect.Contains(xaml, "<Border Grid.Row=\"1\" Background=\"{ThemeResource SolidBackgroundFillColorSecondaryBrush}\"\n                    BorderBrush=\"{ThemeResource ControlStrokeColorSecondaryBrush}\" BorderThickness=\"1\" CornerRadius=\"4\" Padding=\"8\">\n                <views:PaginationBar");
        Expect.DoesNotContain(xaml, "<local:PaginationBar");
        Expect.Contains(xaml, "PageLabel=\"{x:Bind ViewModel.PageLabel, Mode=OneWay}\"");
        Expect.Contains(xaml, "PreviousClicked=\"OnPreviousClick\"");
        Expect.Contains(xaml, "NextClicked=\"OnNextClick\"");

        Expect.Contains(codeBehind, "private async void OnPreviousClick(object sender, RoutedEventArgs e) => await ViewModel.PreviousPageAsync();");
        Expect.Contains(codeBehind, "private async void OnNextClick(object sender, RoutedEventArgs e) => await ViewModel.NextPageAsync();");

        Expect.Contains(viewModel, "private const int PageSize = 20;");
        Expect.Contains(viewModel, "public bool CanGoPrevious => PageNumber > 1;");
        Expect.Contains(viewModel, "public bool CanGoNext => PageNumber < TotalPages;");
        Expect.Contains(viewModel, "var (rows, totalCount) = await _repo.SearchAsync(sY_IsoCountry_Alpha3Code: string.IsNullOrWhiteSpace(SY_IsoCountry_Alpha3CodeFilter) ? null : SY_IsoCountry_Alpha3CodeFilter, name: string.IsNullOrWhiteSpace(NameFilter) ? null : NameFilter, pageNumber: PageNumber, pageSize: PageSize, sortColumn: _sortColumn, sortDescending: _sortDescending);");
        Expect.Contains(viewModel, "public async Task PreviousPageAsync()");
        Expect.Contains(viewModel, "public async Task NextPageAsync()");
        Expect.DoesNotContain(viewModel, "_repo.GetAll()");
    }

    [TestMethod]
    public async Task WinUI3_MasterScreen_gets_a_search_bar_with_one_text_box_per_searchable_column()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", Sample.Holiday()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));
        string xaml = files["HolidayListPage.xaml"].Content;
        string codeBehind = files["HolidayListPage.xaml.cs"].Content;
        string viewModel = files["HolidayListViewModel.cs"].Content;

        Expect.Contains(xaml, "Text=\"{x:Bind ViewModel.SY_IsoCountry_Alpha3CodeFilter, Mode=TwoWay}\"");
        Expect.Contains(xaml, "Text=\"{x:Bind ViewModel.NameFilter, Mode=TwoWay}\"");
        Expect.Contains(xaml, "Button Content=\"Search\" Click=\"OnSearchClick\"");

        Expect.Contains(codeBehind, "private async void OnSearchClick(object sender, RoutedEventArgs e)");
        Expect.Contains(codeBehind, "ViewModel.PageNumber = 1;");

        Expect.Contains(viewModel, "private string _sY_IsoCountry_Alpha3CodeFilter = \"\";");
        Expect.Contains(viewModel, "private string _nameFilter = \"\";");
    }

    [TestMethod]
    public async Task WinUI3_MasterScreen_pluralizes_the_title_heading_correctly_for_a_name_ending_in_s()
    {
        // Same "literal + s" bug already fixed elsewhere (TS_Component/TSX_Page's list variable, the error
        // message), found live against the real, already-plural Movies table -- the title heading had it too.
        string xaml = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", Sample.Movies()))
            .Single(f => f.RelativePath.EndsWith("ListPage.xaml")).Content;

        Expect.Contains(xaml, "Text=\"Movies\" Style=\"{ThemeResource TitleTextBlockStyle}\"");
        Expect.DoesNotContain(xaml, "Moviess");
    }

    [TestMethod]
    public async Task WinUI3_MasterScreen_still_pages_the_grid_but_gets_no_search_bar_when_the_table_has_no_searchable_column()
    {
        // Pagination and searchability are separate concerns (found live needing pagination alone on such a
        // table, 2026-09-28): the grid still pages through SearchAsync/PaginationBar; only the search bar
        // itself (TextBoxes, Search button, filter properties) is skipped when there's nothing to search by.
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        var files = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", allNumeric))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));
        string xaml = files["MetricListPage.xaml"].Content;
        string codeBehind = files["MetricListPage.xaml.cs"].Content;
        string viewModel = files["MetricListViewModel.cs"].Content;

        Expect.Contains(xaml, "<views:PaginationBar");
        Expect.DoesNotContain(xaml, "TextBox");
        Expect.DoesNotContain(xaml, "OnSearchClick");
        Expect.Contains(codeBehind, "OnPreviousClick");
        Expect.Contains(codeBehind, "OnNextClick");
        Expect.DoesNotContain(codeBehind, "OnSearchClick");
        Expect.Contains(viewModel, "var (rows, totalCount) = await _repo.SearchAsync(pageNumber: PageNumber, pageSize: PageSize, sortColumn: _sortColumn, sortDescending: _sortDescending);");
        Expect.DoesNotContain(viewModel, "Filter");
        Expect.DoesNotContain(viewModel, "GetAll()");
    }

    // ------------------------------------------------------------------ WinUI3_DetailMasterScreen

    [TestMethod]
    public async Task WinUI3_DetailMasterScreen_refuses_a_table_with_no_child_tables()
    {
        string message = await Refusal("WinUI3_DetailMasterScreen_v1.tt", Sample.DonateLeave());

        StringAssert.Contains(message, "foreign key pointing back at");
    }

    [TestMethod]
    public async Task WinUI3_DetailMasterScreen_refuses_a_composite_or_non_int_primary_key()
    {
        var table = Sample.Table("Parent",
            [Sample.Column("LeftId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("RightId", SqlDbType.Int, primaryKey: true, ordinal: 2)],
            childForeignKeys: [Sample.ChildForeignKey("Child", "ParentId", "LeftId")]);

        string message = await Refusal("WinUI3_DetailMasterScreen_v1.tt", table);

        StringAssert.Contains(message, "single int primary key");
    }

    [TestMethod]
    public async Task WinUI3_DetailMasterScreen_writes_the_form_plus_one_grid_per_child_table()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_DetailMasterScreen_v1.tt", Sample.DepartmentWithTeams()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));

        Assert.HasCount(5, files); // the page/dialog, its code-behind, its ViewModel, and the shared PaginationBar (xaml + code-behind)
        string xaml = files["DepartmentDetailMasterDialog.xaml"].Content;
        string codeBehind = files["DepartmentDetailMasterDialog.xaml.cs"].Content;
        string viewModel = files["DepartmentDetailMasterViewModel.cs"].Content;

        Expect.Contains(xaml, "x:Class=\"TimeEntry.Desktop.Views.DepartmentDetailMasterDialog\"");
        Expect.Contains(xaml, "Text=\"{x:Bind ViewModel.Name, Mode=TwoWay}\"");
        Expect.Contains(xaml, "Text=\"Department Team\""); // the child table's grid title
        Expect.Contains(xaml, "ItemsSource=\"{x:Bind ViewModel.departmentTeamColumnHeaders}\"");
        Expect.Contains(xaml, "ItemsSource=\"{x:Bind ViewModel.departmentTeamRows}\"");

        Expect.Contains(codeBehind, "public sealed partial class DepartmentDetailMasterDialog : ContentDialog");

        Expect.Contains(viewModel, "public class DepartmentChildGridRow");
        Expect.Contains(viewModel, "public ObservableCollection<DepartmentChildGridHeader> departmentTeamColumnHeaders { get; } = [];");
        Expect.Contains(viewModel, "public ObservableCollection<DepartmentChildGridRow> departmentTeamRows { get; } = [];");
        Expect.Contains(viewModel, "var entityType = _context.Model.FindEntityType(typeof(DepartmentTeam))!;");
        Expect.Contains(viewModel, "EF.Property<int>(c, \"DepartmentId\") == _editing!.DepartmentId");
        Expect.Contains(viewModel, "if (_editing is null)\n            return; // no child rows to show until this Department has been saved once");
    }

    [TestMethod]
    public async Task WinUI3_DetailMasterScreen_child_grid_hides_internal_ids_and_resolves_a_foreign_key_with_a_display_name()
    {
        // OrderLine's own primary key (OrderLineId) and its own FK back to the parent (OrderId) have no business
        // meaning to a grid viewer and are hidden entirely; its FK to Warehouse has no display column so it's
        // hidden too; its FK to Product DOES have one, so it's resolved to ProductName instead of shown as a
        // raw id -- found live: a real user looking at a generated grid asked for exactly this (2026-09-29).
        var files = GeneratedFiles.Split(await Render("WinUI3_DetailMasterScreen_v1.tt", Sample.OrderWithLines()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));
        string viewModel = files["OrderDetailMasterViewModel.cs"].Content;

        Expect.Contains(viewModel, "private static readonly string[] HideFromOrderLineGrid = [ \"OrderId\", \"OrderLineId\", \"WarehouseId\" ];");
        Expect.Contains(viewModel, "var orderLineProductNames = await _context.Set<Product>().ToDictionaryAsync(r => r.ProductId, r => r.ProductName?.ToString() ?? \"\");");
        Expect.Contains(viewModel, ".Where(name => !HideFromOrderLineGrid.Contains(name, StringComparer.OrdinalIgnoreCase))");
        Expect.Contains(viewModel, "if (name == \"ProductId\")");
        Expect.Contains(viewModel, "orderLineProductNames.TryGetValue(rawId, out string? resolvedProductId) ? resolvedProductId : rawId.ToString()");
        // Never resolved for the unresolvable one -- no lookup dictionary, no per-column branch for it.
        Expect.DoesNotContain(viewModel, "WarehouseNames");
        Expect.DoesNotContain(viewModel, "name == \"WarehouseId\"");

        // The resolved column's header reads "Product", not the raw "ProductId" -- its name is known at
        // generation time (the same Label() this template already uses for the parent form's own field
        // headers), unlike every other column in this grid, which is only discovered later via EF reflection
        // and keeps its raw property name as the header. Found live: the Customer Monthly Summarys list
        // screen already shows "Customer" for its own resolved column; a real user asked for the same here.
        Expect.Contains(viewModel, "new(StringComparer.OrdinalIgnoreCase)\n    {\n        [\"ProductId\"] = \"Product\",\n    };");
        Expect.Contains(viewModel, "HeaderLabelForOrderLineGrid.TryGetValue(name, out string? label) ? label : SpacedHeader(name)");
    }

    // ------------------------------------------------------------------ TS_DetailMasterComponent

    [TestMethod]
    public async Task TS_DetailMasterComponent_refuses_a_table_with_no_child_tables()
    {
        string message = await Refusal("TS_DetailMasterComponent_v1.tt", Sample.DonateLeave());

        StringAssert.Contains(message, "foreign key pointing back at");
    }

    [TestMethod]
    public async Task TS_DetailMasterComponent_refuses_a_composite_primary_key()
    {
        var table = Sample.Table("Parent",
            [Sample.Column("LeftId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("RightId", SqlDbType.Int, primaryKey: true, ordinal: 2)],
            childForeignKeys: [Sample.ChildForeignKey("Child", "ParentId", "LeftId")]);

        string message = await Refusal("TS_DetailMasterComponent_v1.tt", table);

        StringAssert.Contains(message, "single int or uniqueidentifier primary key");
    }

    [TestMethod]
    public async Task TS_DetailMasterComponent_writes_the_grid_form_plus_one_child_grid_per_child_table()
    {
        var files = GeneratedFiles.Split(await Render("TS_DetailMasterComponent_v1.tt", Sample.DepartmentWithTeams()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));

        Assert.HasCount(4, files);
        string html = files["department-detail-master.component.html"].Content;
        string ts = files["department-detail-master.component.ts"].Content;
        string spec = files["department-detail-master.component.spec.ts"].Content;

        Expect.Contains(html, "<h1>Departments</h1>");
        Expect.Contains(html, "Department Team");
        Expect.Contains(html, "*ngIf=\"selectedRow.departmentId; else saveDepartmentTeamFirst\"");
        Expect.Contains(html, "*ngFor=\"let col of departmentTeamColumns\"");
        Expect.Contains(html, "*ngFor=\"let row of sortedChildRows('departmentTeam', departmentTeamRows)\"");
        Expect.Contains(html, "Save this Department first to see its Department Team rows.");

        Expect.Contains(ts, "export class DepartmentDetailMasterComponent {");
        Expect.Contains(ts, "departmentTeamRows: any[] = [];");
        Expect.Contains(ts, "departmentTeamColumns: string[] = [];");
        Expect.Contains(ts, "import { HttpClient } from '@angular/common/http';");
        Expect.Contains(ts, "private http: HttpClient");
        Expect.Contains(ts, "this.loadDepartmentTeam(department.departmentId!);");
        Expect.Contains(ts, "private loadDepartmentTeam(parentId: number): void {");
        Expect.Contains(ts, "this.http.get<any[]>('api/departmentteams').subscribe({");
        Expect.Contains(ts, "this.departmentTeamRows = rows.filter((r: any) => r['departmentId'] === parentId);");
        Expect.DoesNotContain(ts, "import { DepartmentTeam }"); // no dependency on the child's own model/service
        Expect.DoesNotContain(ts, "DepartmentTeamService");

        Expect.Contains(spec, "import { DepartmentDetailMasterComponent } from './department-detail-master.component';");
    }

    [TestMethod]
    public async Task TS_DetailMasterComponent_does_not_double_pluralize_an_already_plural_child_table_name()
    {
        var files = GeneratedFiles.Split(await Render("TS_DetailMasterComponent_v1.tt", Sample.MovieWithReviews()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));
        string ts = files["movie-detail-master.component.ts"].Content;

        Expect.Contains(ts, "this.http.get<any[]>('api/reviews').subscribe({");
        Expect.DoesNotContain(ts, "'api/reviewss'");
    }

    [TestMethod]
    public async Task TS_DetailMasterComponent_adds_es_for_a_child_table_name_ending_in_double_s()
    {
        var files = GeneratedFiles.Split(await Render("TS_DetailMasterComponent_v1.tt", Sample.CustomerWithAddressChild()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));
        string ts = files["customer-detail-master.component.ts"].Content;

        Expect.Contains(ts, "this.http.get<any[]>('api/addresses').subscribe({");
        Expect.DoesNotContain(ts, "'api/address').subscribe");
    }

    // ------------------------------------------------------------------ Cross-template consistency: a component's
    // lookup drop-down calls this.<parent>Service.<method>() by NAME (it never sees TS_Service's own render), so
    // nothing catches the two templates drifting apart except a test that renders both and compares them directly.
    // TS_JunctionComponent/TS_DetailMasterComponent's own child-grid fetch is deliberately exempt (see
    // Docs/specs.md section 11): it calls the API directly instead of assuming a parent service's shape at all.

    private static string CalledServiceMethod(string componentTs, string serviceVar)
    {
        var match = Regex.Match(componentTs, $@"this\.{Regex.Escape(serviceVar)}\.(\w+)\(\)\.subscribe");
        Assert.IsTrue(match.Success, $"expected a this.{serviceVar}.<method>().subscribe(...) call for the lookup list");
        return match.Groups[1].Value;
    }

    [TestMethod]
    public async Task TS_Component_calls_the_method_TS_Service_actually_generates_for_a_lookup_parent()
    {
        string parentServiceTs = await Render("TS_Service_v1.tt", Sample.Employee());
        string componentTs = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.DonateLeave()))
            .Single(f => f.RelativePath.EndsWith(".component.ts")).Content;

        string calledMethod = CalledServiceMethod(componentTs, "employeeService");

        Expect.Contains(parentServiceTs, $"{calledMethod}(): Observable<Employee[]>");
    }

    [TestMethod]
    public async Task TS_DetailMasterComponent_calls_the_method_TS_Service_actually_generates_for_a_lookup_parent()
    {
        string parentServiceTs = await Render("TS_Service_v1.tt", Sample.Employee());
        string componentTs = GeneratedFiles.Split(await Render("TS_DetailMasterComponent_v1.tt", Sample.TimeSheetWithEmployeeAndDetail()))
            .Single(f => f.RelativePath.EndsWith(".component.ts")).Content;

        string calledMethod = CalledServiceMethod(componentTs, "employeeService");

        Expect.Contains(parentServiceTs, $"{calledMethod}(): Observable<Employee[]>");
    }

    // ------------------------------------------------------------------ API_Junction

    [TestMethod]
    public async Task API_Junction_refuses_a_table_that_is_not_a_junction_table()
    {
        string message = await Refusal("API_Junction_v1.tt", Sample.DonateLeave());

        StringAssert.Contains(message, "IsJunctionTable");
    }

    [TestMethod]
    public async Task API_Junction_registers_list_link_and_unlink_routes_over_the_context_directly()
    {
        string cs = await Render("API_Junction_v1.tt", Sample.JunctionWithSurrogateKey());

        Expect.Contains(cs, "namespace TimeEntry.ApiService.Apis;");
        Expect.Contains(cs, "public class NameBaseGroupXrefJunctionApi<T> : BaseApi<T> where T : class");
        Expect.Contains(cs, "MapGet(_apiSubDir + \"/junction/{anchorId}\", GetJunctionList)");
        Expect.Contains(cs, "MapPost(_apiSubDir + \"/junction/link\", Link)");
        Expect.Contains(cs, "MapPost(_apiSubDir + \"/junction/unlink\", Unlink)");
        Expect.Contains(cs, "public string? ShortDescr { get; set; }"); // Groups' display column
        Expect.Contains(cs, "public record NameBaseGroupXrefJunctionLinkRequest(int AnchorId, int TargetId);");
        // No repo: the handlers call the DbContext directly, same as the WinUI3 ViewModel.
        Expect.DoesNotContain(cs, "Repo repo");
    }

    // ------------------------------------------------------------------ API_Search (the HTTP companion to SP_Search)

    [TestMethod]
    public async Task API_Search_generates_with_no_query_parameters_when_the_table_has_no_searchable_columns()
    {
        // Pagination and searchability are separate concerns (found live needing pagination alone on such a
        // table, 2026-09-28): a table with no searchable column still gets this endpoint, with only
        // pageNumber/pageSize -- and both parameter arrays must compile even with zero filters (an
        // implicitly-typed "new[] { }" with nothing in it is a real CS0826 compile error, also found here).
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        string cs = await Render("API_Search_v1.tt", allNumeric);

        Expect.Contains(cs, "[FromQuery] int pageNumber = 1,");
        // no filter parameter: the only string query parameters are the sort (sortBy, sortDir)
        Assert.AreEqual(2, cs.Split("[FromQuery] string?").Length - 1, "only sortBy and sortDir");
        Expect.Contains(cs, "[FromQuery] string? sortBy = null,");
        Expect.Contains(cs, "var countParameters = new SqlParameter[]");
        Expect.Contains(cs, "EXEC [dbo].[Metric_Search] @PageNumber, @PageSize, @SortColumn, @SortDescending");
        Expect.Contains(cs, "\"EXEC [dbo].[Metric_SearchCount]\",");
        Expect.Contains(cs, "countParameters)");
    }

    [TestMethod]
    public async Task API_Search_registers_a_search_route_with_one_query_parameter_per_searchable_column()
    {
        string cs = await Render("API_Search_v1.tt", Sample.Holiday());

        Expect.Contains(cs, "namespace TimeEntry.ApiService.Apis;");
        Expect.Contains(cs, "public record HolidaySearchResult(IReadOnlyList<Holiday> Items, int Page, int PageSize, int TotalCount, int TotalPages);");
        Expect.Contains(cs, "public class HolidaySearchApi<T> : BaseApi<T> where T : class");
        Expect.Contains(cs, "MapGet(_apiSubDir + \"/search\", Search)");
        Expect.Contains(cs, "[FromQuery] string? sY_IsoCountry_Alpha3Code,");
        Expect.Contains(cs, "[FromQuery] string? name,");
        Expect.Contains(cs, "[FromQuery] int pageNumber = 1,");
        Expect.Contains(cs, "[FromQuery] int pageSize = 100,");
    }

    [TestMethod]
    public async Task API_Search_calls_both_generated_procedures_through_the_context_directly()
    {
        string cs = await Render("API_Search_v1.tt", Sample.Holiday());

        // Set<Holiday>().FromSqlRaw(...), not Database.SqlQueryRaw<Holiday>(...) -- SqlQueryRaw<T> builds an
        // ad hoc EF model that rejects any navigation property T has, throwing for any table with a foreign
        // key; found live running this exact generated code against a table with a parent lookup (2026-09-28).
        Expect.Contains(cs, "context.Set<Holiday>()");
        Expect.Contains(cs, ".FromSqlRaw(");
        Expect.Contains(cs, "EXEC [dbo].[Holiday_Search] @pSY_IsoCountry_Alpha3Code, @pName, @PageNumber, @PageSize");
        Expect.Contains(cs, "SqlQueryRaw<int>(");
        Expect.Contains(cs, "EXEC [dbo].[Holiday_SearchCount] @pSY_IsoCountry_Alpha3Code, @pName");
        // No repo: same "call the DbContext directly" convention API_Junction.tt already established.
        Expect.DoesNotContain(cs, "Repo repo");
        // An EXEC call is non-composable SQL -- .SingleAsync() (which needs to compose it) throws
        // InvalidOperationException, found live running the generated code for real (2026-09-27).
        // .ToListAsync() then .Single() client-side works instead.
        Expect.Contains(cs, "int totalCount = totalCountRows.Single();");
    }

    [TestMethod]
    public async Task API_Search_rejects_a_page_number_below_one()
    {
        string cs = await Render("API_Search_v1.tt", Sample.Holiday());

        Expect.Contains(cs, "if (pageNumber < 1)");
        Expect.Contains(cs, "return Results.BadRequest(\"pageNumber must be 1 or greater.\");");
    }

    // ------------------------------------------------------------------ TS_JunctionComponent

    [TestMethod]
    public async Task TS_JunctionComponent_refuses_a_table_that_is_not_a_junction_table()
    {
        string message = await Refusal("TS_JunctionComponent_v1.tt", Sample.DonateLeave());

        StringAssert.Contains(message, "IsJunctionTable");
    }

    [TestMethod]
    public async Task TS_JunctionComponent_writes_the_four_files_with_a_shuttle_control()
    {
        var files = GeneratedFiles.Split(await Render("TS_JunctionComponent_v1.tt", Sample.JunctionWithSurrogateKey()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));

        Assert.HasCount(4, files);
        Expect.Contains(files["namebasegroupxref-junction.component.html"].Content, "[(ngModel)]=\"availableSelectionIds\"");
        Expect.Contains(files["namebasegroupxref-junction.component.html"].Content, "[(ngModel)]=\"selectedSelectionIds\"");

        string ts = files["namebasegroupxref-junction.component.ts"].Content;
        Expect.Contains(ts, "export class NameBaseGroupXrefJunctionComponent implements OnInit");
        Expect.Contains(ts, "@Input({ required: true }) anchorId!: number;");
        Expect.Contains(ts, "shortDescr?: string;"); // Groups' display column, camelCased like TS_Model
        Expect.Contains(ts, "private apiUrl = 'api/namebasegroupxrefs/junction';");
        Expect.Contains(ts, "this.http.get<NameBaseGroupXrefJunctionItem[]>(`${this.apiUrl}/${this.anchorId}`)");
        Expect.Contains(ts, "this.http.post(`${this.apiUrl}/link`, { anchorId: this.anchorId, targetId: item.targetId })");
        Expect.Contains(ts, "this.http.post(`${this.apiUrl}/unlink`, { anchorId: this.anchorId, targetId: item.targetId })");

        Expect.Contains(files["namebasegroupxref-junction.component.spec.ts"].Content, "provideHttpClient(), provideHttpClientTesting()");
    }

    [TestMethod]
    public async Task TS_JunctionComponent_does_not_double_pluralize_an_already_plural_table_name()
    {
        var files = GeneratedFiles.Split(await Render("TS_JunctionComponent_v1.tt", Sample.JunctionWithPluralName()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));

        Expect.Contains(files["ratings-junction.component.ts"].Content, "private apiUrl = 'api/ratings/junction';");
        Expect.DoesNotContain(files["ratings-junction.component.ts"].Content, "'api/ratingss/junction'");
    }

    [TestMethod]
    public async Task TS_JunctionComponent_adds_es_for_a_singular_table_name_ending_in_double_s()
    {
        var files = GeneratedFiles.Split(await Render("TS_JunctionComponent_v1.tt", Sample.JunctionWithDoubleSName()))
            .ToDictionary(f => Path.GetFileName(f.RelativePath));

        Expect.Contains(files["class-junction.component.ts"].Content, "private apiUrl = 'api/classes/junction';");
        Expect.DoesNotContain(files["class-junction.component.ts"].Content, "'api/class/junction'");
    }

    // ------------------------------------------------------------------ ModifiedUserColumn (e.g. ModifiedBy, UpdatedBy)

    [TestMethod]
    public async Task A_modified_user_column_is_left_out_of_insert_but_is_a_normal_update_parameter()
    {
        var table = Sample.WithModifiedByColumn();

        string insertSql = await Render("SP_Insert_v1.tt", table);
        Expect.DoesNotContain(insertSql, "ModifiedBy");

        string updateSql = await Render("SP_Update_v1.tt", table);
        Expect.Contains(updateSql, "@pModifiedBy");
    }

    [TestMethod]
    public async Task A_modified_user_column_is_only_written_by_saves_update_branch()
    {
        string sql = await Render("SP_Save_v1.tt", Sample.WithModifiedByColumn());

        // Split on the UPDATE/INSERT branches themselves rather than the first "ELSE" in the file: SP_Save's
        // own transaction-ownership boilerplate ("CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END") has an ELSE
        // of its own, ahead of the branches this test actually cares about.
        int updateStart = sql.IndexOf("UPDATE [dbo].[Ticket] SET", StringComparison.Ordinal);
        int insertStart = sql.IndexOf("INSERT INTO [dbo].[Ticket]", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, updateStart, "expected an UPDATE branch");
        Assert.IsGreaterThan(updateStart, insertStart, "expected the INSERT branch after the UPDATE branch");

        Expect.Contains(sql[updateStart..insertStart], "[ModifiedBy]"); // the UPDATE SET list
        Expect.DoesNotContain(sql[insertStart..], "[ModifiedBy]"); // not in the INSERT column/VALUES lists
    }

    [TestMethod]
    public async Task A_modified_user_column_is_left_null_by_clone()
    {
        string sql = await Render("SP_Clone_v1.tt", Sample.WithModifiedByColumn());

        Expect.DoesNotContain(sql, "[ModifiedBy]");
    }

    // ------------------------------------------------------------------ API_Crud

    [TestMethod]
    public async Task The_api_class_is_named_for_the_table_and_uses_its_repository()
    {
        string cs = await Render("API_Crud_v1.tt", Sample.DonateLeave());

        Expect.Contains(cs, "namespace TimeEntry.ApiService.Apis;");
        Expect.Contains(cs, "public class E_DonateLeaveApi<T> : BaseApi<T> where T : class");
        Expect.Contains(cs, "E_DonateLeaveRepo repo = new(context);");
        Expect.DoesNotContain(cs, "GenericRepo<");
    }

    [TestMethod]
    public async Task The_api_lists_newest_first_by_the_first_not_null_date_column()
    {
        string cs = await Render("API_Crud_v1.tt", Sample.DonateLeave());

        Expect.Contains(cs, "repo.GetAllOrderByDescending(c => c.WhenDonated)");
    }

    [TestMethod]
    public async Task A_table_with_no_date_column_lists_everything_unordered()
    {
        var table = Sample.Table("Thing", [Sample.Column("ThingId", SqlDbType.Int, primaryKey: true), Sample.Column("Label", SqlDbType.NVarChar)]);

        string cs = await Render("API_Crud_v1.tt", table);

        Expect.Contains(cs, "var rows = await repo.GetAll();");
    }

    [TestMethod]
    public async Task Update_checks_the_id_and_that_the_row_exists_and_get_by_id_can_be_404()
    {
        string cs = await Render("API_Crud_v1.tt", Sample.DonateLeave());

        Expect.Contains(cs, "if (updatedRow.DonateLeaveId != id)");
        Expect.Contains(cs, "if (!await repo.ExistsAsync(id))");
        Expect.Contains(cs, "return row != null ? Results.Ok(row) : Results.NotFound();");
        Expect.Contains(cs, "repo.DeleteAsync(\"E_DonateLeave\", id)");
    }

    [TestMethod]
    public async Task The_api_refuses_what_it_cannot_write_and_says_why()
    {
        StringAssert.Contains(await Refusal("API_Crud_v1.tt", Sample.CompositeKey()), "single int primary key");
        StringAssert.Contains(await Refusal("API_Crud_v1.tt", Sample.Roles()), "noRepositoryTables");
        StringAssert.Contains(await Refusal("API_Crud_v1.tt", Sample.DepartmentTeam()), "IsActive");
    }

    // ------------------------------------------------------------------ project settings: the usings list

    [TestMethod]
    [DataRow("API_Crud_v1.tt")]
    [DataRow("CS_Repo_v1.tt")]
    [DataRow("CS_Entity_v1.tt")]
    [DataRow("CS_Validation_v1.tt")]
    public async Task Namespaces_named_in_the_settings_block_become_using_lines(string template)
    {
        // The settings block at the top of the template is how a project says which namespaces its generated files need.
        using var temp = new TempFolder();
        string source = File.ReadAllText(Repo.Template(template));
        Assert.Contains("string[] usings = { };", source, $"{template} has no usings setting");
        string custom = temp.File(template, source.Replace("string[] usings = { };", "string[] usings = { \"MyApp.Entities\", \"MyApp.Data\" };"));

        var result = await TemplateRunner.RunAsync(custom, template == "API_Crud_v1.tt" ? Sample.DonateLeave() : Sample.Holiday());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "using MyApp.Entities;");
        Expect.Contains(result.GeneratedText!, "using MyApp.Data;");
    }

    [TestMethod]
    public async Task With_the_default_settings_no_extra_using_is_written()
    {
        string cs = await Render("CS_Repo_v1.tt", Sample.Holiday());

        Expect.DoesNotContain(cs, "using ");
    }

    [TestMethod]
    public async Task The_namespace_setting_is_what_the_file_declares()
    {
        using var temp = new TempFolder();
        string source = File.ReadAllText(Repo.Template("CS_Enum_v1.tt"));
        string custom = temp.File("CS_Enum_v1.tt", source.Replace("\"TimeEntry.Common.Enums\"", "\"MyApp.Lookups\""));

        var result = await TemplateRunner.RunAsync(custom, Sample.Roles());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "namespace MyApp.Lookups;");
    }

    // ------------------------------------------------------------------ CS_Enum

    [TestMethod]
    public async Task An_enum_takes_its_values_from_the_key_and_its_names_from_the_rows()
    {
        string cs = await Render("CS_Enum_v1.tt", Sample.Roles());

        Expect.Contains(cs, "namespace TimeEntry.Common.Enums;");
        Expect.Contains(cs, "public enum SY_Role");
        Expect.Contains(cs, "    Admin = 1,");
        Expect.Contains(cs, "    HumanResources = 2,");
        Expect.Contains(cs, "    TimeOffInLieu = 3\n");   // no comma after the last member
    }

    [TestMethod]
    public async Task An_enum_refuses_a_table_it_cannot_read_meaningfully()
    {
        StringAssert.Contains(await Refusal("CS_Enum_v1.tt", Sample.CompositeKey()), "single integer primary key");
        var noName = Sample.Table("Numbers", [Sample.Column("NumberId", SqlDbType.Int, primaryKey: true), Sample.Column("Value", SqlDbType.Int)], rows: []);
        StringAssert.Contains(await Refusal("CS_Enum_v1.tt", noName), "no name column");
    }

    private static TableModel Lookup(string table, string keyColumn, string nameColumn, List<object?[]> rows) => Sample.Table(table,
    [
        Sample.Column(keyColumn, SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column(nameColumn, SqlDbType.VarChar, characters: 50, ordinal: 2)
    ],
    rows: rows);

    [TestMethod]
    public async Task The_name_column_may_be_called_TableName_as_in_Company_and_CompanyName()
    {
        // No DisplayRank is set on these columns (as when SpecialLogicColumns.config lacks the DisplayColumn rule): the
        // template must find the name by the <Table>Name pattern on its own.
        string company = await Render("CS_Enum_v1.tt", Lookup("Company", "CompanyID", "CompanyName", [[1, "Acme Corp"], [2, "Globex"]]));
        string corporation = await Render("CS_Enum_v1.tt", Lookup("Corporation", "CorporationId", "CorporationName", [[7, "Initech"]]));

        Expect.Contains(company, "public enum Company");
        Expect.Contains(company, "    AcmeCorp = 1,");
        Expect.Contains(company, "    Globex = 2\n");
        Expect.Contains(corporation, "public enum Corporation");
        Expect.Contains(corporation, "    Initech = 7\n");
    }

    [TestMethod]
    public async Task A_prefix_on_the_table_name_is_ignored_when_looking_for_the_name_column()
    {
        // SY_Role has RoleName, E_Thing would have ThingName
        string cs = await Render("CS_Enum_v1.tt", Lookup("SY_Role", "SY_RoleId", "RoleName", [[1, "Admin"]]));

        Expect.Contains(cs, "    Admin = 1\n");
    }

    [TestMethod]
    public async Task A_column_called_Name_wins_over_TableName()
    {
        var table = Sample.Table("Company",
        [
            Sample.Column("CompanyID", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("CompanyName", SqlDbType.VarChar, characters: 50, ordinal: 2),
            Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 3)
        ],
        rows: [[1, "From CompanyName", "From Name"]]);

        string cs = await Render("CS_Enum_v1.tt", table);

        Expect.Contains(cs, "FromName = 1");
        Expect.DoesNotContain(cs, "FromCompanyName");
    }

    [TestMethod]
    public async Task An_empty_lookup_table_is_refused_with_a_message_that_says_to_load_the_data()
    {
        string message = await Refusal("CS_Enum_v1.tt", Lookup("Company", "CompanyID", "CompanyName", []));

        StringAssert.Contains(message, "the table is empty");
        StringAssert.Contains(message, "load the data first");
        Assert.DoesNotContain("no name column", message, "the name column IS found; only the missing rows are the problem");
    }

    // ------------------------------------------------------------------ CS_Entity

    [TestMethod]
    public async Task An_entity_lists_key_and_foreign_keys_then_navigations_then_the_other_columns()
    {
        string cs = await Render("CS_Entity_v1.tt", Sample.DonateLeave());

        Expect.Contains(cs, "using System.ComponentModel.DataAnnotations.Schema;");
        Expect.Contains(cs, "public class E_DonateLeave : BaseEntity");
        Expect.Contains(cs, "    #region Omitted");
        Expect.Contains(cs, "    [Key]");
        Expect.Contains(cs, "    [ForeignKey(nameof(DonateFrom_Employee))]");
        Expect.Contains(cs, "    public Employee? DonateFrom_Employee { get; set; }");
        Expect.Contains(cs, "    public required DateTime WhenDonated { get; set; }");
        Expect.Contains(cs, "    [StringLength(100)]");   // 200 bytes of nvarchar are 100 characters
        Expect.Contains(cs, "    [DataType(DataType.MultilineText)]");
        Expect.Contains(cs, "    public string? Note { get; set; }");
    }

    [TestMethod]
    public async Task A_foreign_key_to_a_lookup_table_stays_a_plain_column_and_a_named_table_gets_ToString()
    {
        string cs = await Render("CS_Entity_v1.tt", Sample.Holiday());

        Expect.Contains(cs, "    public required string SY_IsoCountry_Alpha3Code { get; set; }");
        Expect.DoesNotContain(cs, "SY_ISOCountry?");
        Expect.Contains(cs, "    public override string? ToString() => Name;");
    }

    [TestMethod]
    public async Task A_name_and_active_table_derives_from_the_shared_base_and_does_not_repeat_its_properties()
    {
        string cs = await Render("CS_Entity_v1.tt", Sample.DepartmentTeam());

        Expect.Contains(cs, "public class DepartmentTeam : BaseNameActiveEntity");
        Expect.DoesNotContain(cs, "bool IsActive");
        Expect.Contains(cs, "    public Department? Department { get; set; }");
    }

    [TestMethod]
    public async Task An_entity_refuses_a_composite_key()
    {
        StringAssert.Contains(await Refusal("CS_Entity_v1.tt", Sample.CompositeKey()), "composite primary key");
    }

    // ------------------------------------------------------------------ CS_Validation

    [TestMethod]
    public async Task Validation_writes_a_metadata_buddy_class_attached_to_a_partial_entity()
    {
        string cs = await Render("CS_Validation_v1.tt", Sample.DonateLeave());

        Expect.Contains(cs, "[MetadataType(typeof(E_DonateLeaveMetadata))]");
        Expect.Contains(cs, "public partial class E_DonateLeave");
        Expect.Contains(cs, "public class E_DonateLeaveMetadata");
        Expect.Contains(cs, "    [Required]\n    [Display(Name = \"When Donated\", Description = \"When Donated\")]\n    public DateTime WhenDonated { get; set; }");
        Expect.Contains(cs, "    [StringLength(100)]");   // 200 bytes of nvarchar are 100 characters
        Expect.Contains(cs, "    [DataType(DataType.MultilineText)]");
        Expect.Contains(cs, "    public string? Note { get; set; }");
    }

    [TestMethod]
    public async Task An_identity_primary_key_is_not_validated_but_a_natural_key_is()
    {
        string identity = await Render("CS_Validation_v1.tt", Sample.DonateLeave());
        Expect.DoesNotContain(identity, "DonateLeaveId");

        string natural = await Render("CS_Validation_v1.tt", Sample.NaturalKey());
        Expect.Contains(natural, "    [Required]\n    [Display(Name = \"Code\", Description = \"Code\")]\n    [StringLength(3)]\n    public string Code { get; set; }");
    }

    [TestMethod]
    public async Task Validation_excludes_audit_columns()
    {
        string cs = await Render("CS_Validation_v1.tt", Sample.WithModifiedByColumn());

        Expect.Contains(cs, "public string Subject { get; set; }");
        Expect.DoesNotContain(cs, "ModifiedBy");
    }

    [TestMethod]
    public async Task Validation_picks_a_semantic_data_type_from_the_column_name()
    {
        var table = Sample.Table("Contact",
        [
            Sample.Column("ContactId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("EmailAddress", SqlDbType.NVarChar, characters: 100, ordinal: 2),
            Sample.Column("AccountPassword", SqlDbType.NVarChar, characters: 50, ordinal: 3)
        ]);

        string cs = await Render("CS_Validation_v1.tt", table);

        Expect.Contains(cs, "[DataType(DataType.EmailAddress)]");
        Expect.Contains(cs, "[DataType(DataType.Password)]");
    }

    [TestMethod]
    public async Task Validation_refuses_a_table_with_nothing_left_to_validate()
    {
        var table = Sample.Table("Empty", [Sample.Column("EmptyId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)]);

        StringAssert.Contains(await Refusal("CS_Validation_v1.tt", table), "nothing to validate");
    }

    [TestMethod]
    public async Task Unlike_the_entity_validation_does_not_require_a_single_column_key()
    {
        string cs = await Render("CS_Validation_v1.tt", Sample.CompositeKey());

        Expect.Contains(cs, "public int LeftId { get; set; }");
        Expect.Contains(cs, "public int RightId { get; set; }");
    }

    // ------------------------------------------------------------------ CS_Repo

    [TestMethod]
    public async Task A_plain_table_gets_a_generic_repository_and_a_named_table_a_name_search()
    {
        string cs = await Render("CS_Repo_v1.tt", Sample.Holiday());

        // partial: a WinUI3/Windows App SDK target project's CsWinRT source generator needs it, since
        // GenericRepo<T> implements IDisposable (WinRT's IClosable) -- see the template's own comment.
        Expect.Contains(cs, "public partial class HolidayRepo : GenericRepo<Holiday>");
        Expect.Contains(cs, "public HolidayRepo(TimeEntryContext context) : base(context)");
        Expect.Contains(cs, "public async Task<List<Holiday>> GetByName(string name)");
    }

    [TestMethod]
    public async Task A_name_and_active_child_table_gets_its_rows_by_parent()
    {
        string cs = await Render("CS_Repo_v1.tt", Sample.DepartmentTeam());

        Expect.Contains(cs, "public partial class DepartmentTeamRepo : NameActiveRepo<DepartmentTeam>");
        Expect.Contains(cs, "public async Task<List<DepartmentTeam>> GetAllOfDepartment(int id)");
        Expect.Contains(cs, "t.DepartmentId.Equals(id) && t.IsActive");
    }

    [TestMethod]
    public async Task No_repository_is_written_for_a_lookup_table()
    {
        StringAssert.Contains(await Refusal("CS_Repo_v1.tt", Sample.Roles()), "noRepositoryTables");
    }

    [TestMethod]
    public async Task A_column_in_a_unique_index_gets_a_has_duplicate_check()
    {
        var table = Sample.Table("Account",
        [
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("AccountNumber", SqlDbType.NVarChar, characters: 20, ordinal: 2, inUniqueIndex: true),
            Sample.Column("Description", SqlDbType.NVarChar, characters: 100, ordinal: 3)
        ]);

        string cs = await Render("CS_Repo_v1.tt", table);

        Expect.Contains(cs, "public async Task<bool> HasDuplicateAccountNumber(string accountNumber, int excludeId)");
        Expect.Contains(cs, "t.AccountNumber == accountNumber && t.AccountId != excludeId");
        Expect.DoesNotContain(cs, "HasDuplicateDescription"); // not in a unique index
    }

    [TestMethod]
    public async Task Each_column_in_a_unique_index_gets_its_own_has_duplicate_check()
    {
        var table = Sample.Table("Account",
        [
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("AccountNumber", SqlDbType.NVarChar, characters: 20, ordinal: 2, inUniqueIndex: true),
            Sample.Column("TaxId", SqlDbType.VarChar, characters: 15, nullable: true, ordinal: 3, inUniqueIndex: true)
        ]);

        string cs = await Render("CS_Repo_v1.tt", table);

        Expect.Contains(cs, "public async Task<bool> HasDuplicateAccountNumber(string accountNumber, int excludeId)");
        Expect.Contains(cs, "public async Task<bool> HasDuplicateTaxId(string? taxId, int excludeId)");
    }

    [TestMethod]
    public async Task A_unique_string_column_gets_a_suggest_unique_method()
    {
        var table = Sample.Table("Account",
        [
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("AccountNumber", SqlDbType.NVarChar, characters: 20, ordinal: 2, inUniqueIndex: true)
        ]);

        string cs = await Render("CS_Repo_v1.tt", table);

        Expect.Contains(cs, "public async Task<string> SuggestUniqueAccountNumber(string desired)");
        Expect.Contains(cs, "string candidate = desired;");
        Expect.Contains(cs, "for (int suffix = 2; await _dbSet.AnyAsync(t => t.AccountNumber == candidate); suffix++)");
        Expect.Contains(cs, "candidate = desired + suffix;");
    }

    [TestMethod]
    public async Task A_unique_non_string_column_gets_no_suggest_unique_method()
    {
        var table = Sample.Table("Account",
        [
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("ExternalRefNumber", SqlDbType.Int, ordinal: 2, inUniqueIndex: true)
        ]);

        string cs = await Render("CS_Repo_v1.tt", table);

        Expect.Contains(cs, "public async Task<bool> HasDuplicateExternalRefNumber(int externalRefNumber, int excludeId)");
        Expect.DoesNotContain(cs, "SuggestUnique"); // appending a numeric suffix to a non-text column makes no sense
    }

    [TestMethod]
    public async Task No_has_duplicate_or_suggest_unique_is_written_when_no_column_is_in_a_unique_index()
    {
        string cs = await Render("CS_Repo_v1.tt", Sample.Holiday());

        Expect.DoesNotContain(cs, "HasDuplicate");
        Expect.DoesNotContain(cs, "SuggestUnique");
    }

    [TestMethod]
    public async Task A_table_with_a_searchable_column_gets_a_SearchAsync_method_calling_the_same_procedures_API_Search_calls()
    {
        string cs = await Render("CS_Repo_v1.tt", Sample.Holiday());

        Expect.Contains(cs, "public async Task<(List<Holiday> Items, int TotalCount)> SearchAsync(string? sY_IsoCountry_Alpha3Code = null, string? name = null, int pageNumber = 1, int pageSize = 100, string? sortColumn = null, bool sortDescending = false)");
        // Set<Holiday>().FromSqlRaw(...), not Database.SqlQueryRaw<Holiday>(...) -- SqlQueryRaw<T> builds an
        // ad hoc EF model that rejects any navigation property T has, throwing for any table with a foreign
        // key; found live running this exact generated code against a table with a parent lookup (2026-09-28).
        Expect.Contains(cs, "await _context.Set<Holiday>()");
        Expect.Contains(cs, ".FromSqlRaw(\"EXEC [dbo].[Holiday_Search] @pSY_IsoCountry_Alpha3Code, @pName, @PageNumber, @PageSize, @SortColumn, @SortDescending\", parameters)");
        Expect.Contains(cs, ".SqlQueryRaw<int>(\"EXEC [dbo].[Holiday_SearchCount] @pSY_IsoCountry_Alpha3Code, @pName\", countParameters)");
        Expect.Contains(cs, "return (items, totalCount);");
        // An EXEC call is non-composable SQL -- .SingleAsync() (which needs to compose it) throws
        // InvalidOperationException, found live running the generated code for real (2026-09-27).
        // .ToListAsync() then .Single() client-side works instead.
        Expect.Contains(cs, "int totalCount = totalCountRows.Single();");
    }

    [TestMethod]
    public async Task SearchAsync_still_gets_written_with_no_filter_parameters_when_the_table_has_no_searchable_column()
    {
        // Pagination and searchability are separate concerns (found live needing pagination alone on such a
        // table, 2026-09-28): SearchAsync always exists once CS_Repo.tt generates at all, just with zero
        // filter parameters for a table with no searchable column -- and the countParameters array must still
        // compile with zero elements (an implicitly-typed "new[] { }" with nothing in it is a real CS0826
        // compile error, also found here).
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        string cs = await Render("CS_Repo_v1.tt", allNumeric);

        Expect.Contains(cs, "public async Task<(List<Metric> Items, int TotalCount)> SearchAsync(int pageNumber = 1, int pageSize = 100, string? sortColumn = null, bool sortDescending = false)");
        Expect.Contains(cs, "var countParameters = new Microsoft.Data.SqlClient.SqlParameter[]");
        Expect.Contains(cs, "EXEC [dbo].[Metric_Search] @PageNumber, @PageSize");
        Expect.Contains(cs, "EXEC [dbo].[Metric_SearchCount]\", countParameters)");
    }

    // ------------------------------------------------------------------ TS_Model

    [TestMethod]
    public async Task A_model_names_properties_the_way_the_server_serializes_them()
    {
        string text = await Render("TS_Model_v1.tt", Sample.Holiday());
        var file = GeneratedFiles.Split(text).Single();

        Assert.AreEqual("models/holiday.ts", file.RelativePath);
        Expect.Contains(file.Content, "export interface Holiday {");
        Expect.Contains(file.Content, "    holidayId?: number; // Optional for new ones");
        Expect.Contains(file.Content, "    sY_IsoCountry_Alpha3Code: string;");   // .NET only lower-cases the leading S
        Expect.Contains(file.Content, "    date: string;");                        // JSON has no date type
        Expect.Contains(file.Content, "    sY_DisplayId?: number;");               // nullable -> optional
    }

    [TestMethod]
    public async Task A_model_keeps_the_underscore_case_the_server_sends_and_drops_the_E_prefix()
    {
        var table = Sample.Table("E_TimeSheetDetail",
        [
            Sample.Column("TimeSheetDetailId", SqlDbType.Int, primaryKey: true),
            Sample.Column("E_TimeSheetId", SqlDbType.Int)
        ],
        [Sample.ForeignKey("E_TimeSheetId", "E_TimeSheet", "TimeSheetId", "Notes")]);

        var file = GeneratedFiles.Split(await Render("TS_Model_v1.tt", table)).Single();

        Assert.AreEqual("models/timesheetdetail.ts", file.RelativePath);
        Expect.Contains(file.Content, "export interface TimeSheetDetail {");
        Expect.Contains(file.Content, "    e_TimeSheetId: number;");
        Expect.Contains(file.Content, "    e_TimeSheet?: TimeSheet;");
        Expect.Contains(file.Content, "import { TimeSheet } from \"./timesheet\";");
    }

    [TestMethod]
    public async Task A_model_refuses_tables_the_app_never_receives()
    {
        StringAssert.Contains(await Refusal("TS_Model_v1.tt", Sample.Roles()), "noApiTables");
    }

    // ------------------------------------------------------------------ TS_Service

    [TestMethod]
    public async Task A_service_calls_the_route_the_api_registers_and_searches_by_name_only_when_there_is_one()
    {
        var holiday = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Sample.Holiday())).Single();
        var donate = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Sample.DonateLeave())).Single();

        Assert.AreEqual("services/holiday.service.ts", holiday.RelativePath);
        Expect.Contains(holiday.Content, "private apiUrl = 'api/holidays';");
        Expect.Contains(holiday.Content, "findByName(name: string): Observable<Holiday[]>");
        Assert.AreEqual("services/donateleave.service.ts", donate.RelativePath);
        Expect.Contains(donate.Content, "private apiUrl = 'api/donateleaves';");
        Expect.DoesNotContain(donate.Content, "findByName");
    }

    [TestMethod]
    public async Task Every_service_has_the_same_method_names()
    {
        var file = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Sample.DonateLeave())).Single();

        foreach (string method in new[] { "getAll()", "getById(id: number)", "create(", "update(id: number", "delete(id: number)" })
            Expect.Contains(file.Content, method);
    }

    [TestMethod]
    public async Task TS_Service_pluralizes_the_route_correctly_for_names_ending_in_s()
    {
        var movies = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Sample.Movies())).Single();
        var address = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Sample.Address())).Single();
        var settingsSales = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Sample.SettingsSales())).Single();

        Expect.Contains(movies.Content, "private apiUrl = 'api/movies';"); // already plural: left alone
        Expect.DoesNotContain(movies.Content, "'api/moviess'");
        Expect.Contains(address.Content, "private apiUrl = 'api/addresses';"); // singular ending in "ss": gets "es"
        Expect.DoesNotContain(address.Content, "'api/address'");
        Expect.Contains(settingsSales.Content, "private apiUrl = 'api/settingssales';"); // bare trailing "s": left alone
        Expect.DoesNotContain(settingsSales.Content, "'api/settingssaless'");
    }

    [TestMethod]
    public async Task TS_Service_adds_getPage_only_when_the_table_has_a_searchable_column()
    {
        var withSearch = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Sample.Holiday())).Single();
        Expect.Contains(withSearch.Content, "import { HttpClient, HttpParams } from '@angular/common/http';");
        Expect.Contains(withSearch.Content, "export interface HolidayPagedResult {");
        Expect.Contains(withSearch.Content, "getPage(pageNumber: number, pageSize: number, sY_IsoCountry_Alpha3Code?: string, name?: string, sortBy?: string, sortDescending?: boolean): Observable<HolidayPagedResult> {");
        Expect.Contains(withSearch.Content, "let params = new HttpParams().set('pageNumber', pageNumber).set('pageSize', pageSize);");
        Expect.Contains(withSearch.Content, "if (sY_IsoCountry_Alpha3Code) { params = params.set('sY_IsoCountry_Alpha3Code', sY_IsoCountry_Alpha3Code); }");
        Expect.Contains(withSearch.Content, "if (name) { params = params.set('name', name); }");
        Expect.Contains(withSearch.Content, "return this.http.get<HolidayPagedResult>(`${this.apiUrl}/search`, { params });");

        // Pagination and searchability are separate concerns (found live needing pagination alone on such a
        // table, 2026-09-28): getPage/PagedResult always exist, just with zero filter parameters.
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        var withoutSearch = GeneratedFiles.Split(await Render("TS_Service_v1.tt", allNumeric)).Single();
        Expect.Contains(withoutSearch.Content, "import { HttpClient, HttpParams } from '@angular/common/http';");
        Expect.Contains(withoutSearch.Content, "export interface MetricPagedResult {");
        Expect.Contains(withoutSearch.Content, "getPage(pageNumber: number, pageSize: number, sortBy?: string, sortDescending?: boolean): Observable<MetricPagedResult> {");
    }

    // ------------------------------------------------------------------ TS: a uniqueidentifier key

    [TestMethod]
    public async Task A_guid_key_is_a_string_in_the_model_and_the_service()
    {
        var model = GeneratedFiles.Split(await Render("TS_Model_v1.tt", Sample.AccountRef())).Single();
        var service = GeneratedFiles.Split(await Render("TS_Service_v1.tt", Sample.AccountRef())).Single();

        Expect.Contains(model.Content, "    accountRefID?: string; // Optional for new ones");
        Assert.AreEqual("services/accountref.service.ts", service.RelativePath);
        Expect.Contains(service.Content, "private apiUrl = 'api/accountrefs';");
        Expect.Contains(service.Content, "getById(id: string)");
        Expect.Contains(service.Content, "update(id: string, accountRef: AccountRef)");
        Expect.Contains(service.Content, "delete(id: string)");
        Expect.DoesNotContain(service.Content, "id: number");
    }

    [TestMethod]
    public async Task A_guid_key_screen_deletes_by_string_and_sends_a_new_row_with_the_empty_guid()
    {
        var ts = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.AccountRef())).Single(f => f.RelativePath.EndsWith("accountref.component.ts")).Content;

        Expect.Contains(ts, "delete(id: string): void");
        Expect.Contains(ts, "accountRef.accountRefID = '00000000-0000-0000-0000-000000000000';");
        Expect.DoesNotContain(ts, "accountRefID = 0");
    }

    [TestMethod]
    public async Task A_guid_key_is_not_shown_on_the_screen_and_the_text_columns_are()
    {
        var html = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.AccountRef())).Single(f => f.RelativePath.EndsWith(".html")).Content;

        Expect.Contains(html, ">Full{{ sortMark(");
        Expect.Contains(html, ">List ID{{ sortMark(");
        Expect.DoesNotContain(html, "accountRefID }}");     // the key is not a grid column
        Expect.DoesNotContain(html, "id=\"accountRefAccountRefID\"");   // nor a form field
    }

    [TestMethod]
    public async Task A_key_the_person_would_type_or_a_composite_key_is_still_refused_by_the_screen_and_the_service()
    {
        StringAssert.Contains(await Refusal("TS_Component_v1.tt", Sample.NaturalKey()), "int or uniqueidentifier");
        StringAssert.Contains(await Refusal("TS_Service_v1.tt", Sample.NaturalKey()), "int or uniqueidentifier");
        StringAssert.Contains(await Refusal("TS_Component_v1.tt", Sample.CompositeKey()), "composite primary key");
    }

    // ------------------------------------------------------------------ name/active tables (Name + IsActive):
    // their real API is always hand-maintained and commonly has no plain getAll() at all (see TS_Service.tt's
    // header comment and Docs/specs.md section 5.3's RequiresNotNameActiveTable), so every template that
    // assumes a plain getAll()-style backend refuses one, matching API_Crud.tt's own long-standing refusal.

    [TestMethod]
    public async Task TS_Service_refuses_a_name_active_table()
    {
        StringAssert.Contains(await Refusal("TS_Service_v1.tt", Sample.DepartmentTeam()), "NameActiveRepo");
    }

    [TestMethod]
    public async Task TS_Component_refuses_a_name_active_table()
    {
        StringAssert.Contains(await Refusal("TS_Component_v1.tt", Sample.DepartmentTeam()), "NameActiveRepo");
    }

    [TestMethod]
    public async Task TS_DetailMasterComponent_refuses_a_name_active_table_even_though_it_has_child_tables()
    {
        // Has children (so it would otherwise pass) -- confirms the name/active check is actually reached,
        // not shadowed by an earlier refusal.
        StringAssert.Contains(await Refusal("TS_DetailMasterComponent_v1.tt", Sample.NameActiveTableWithChildren()), "NameActiveRepo");
    }

    // ------------------------------------------------------------------ TS_Component

    [TestMethod]
    public async Task A_component_is_four_files_in_its_own_folder()
    {
        var files = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.DonateLeave()));

        CollectionAssert.AreEqual(new[]
        {
            "components/donateleave/donateleave.component.css",
            "components/donateleave/donateleave.component.html",
            "components/donateleave/donateleave.component.spec.ts",
            "components/donateleave/donateleave.component.ts"
        }, files.Select(f => f.RelativePath).ToArray());
        StringAssert.StartsWith(files[0].Content, ".sort-header {", "the stylesheet holds only the sort styles");
    }

    [TestMethod]
    public async Task A_component_shows_a_drop_down_for_a_foreign_key_and_a_date_box_for_a_date()
    {
        var files = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.DonateLeave())).ToDictionary(f => Path.GetFileName(f.RelativePath));
        string html = files["donateleave.component.html"].Content;
        string ts = files["donateleave.component.ts"].Content;

        Expect.Contains(html, "<select id=\"donateLeaveDonateFrom_EmployeeId\"");
        Expect.Contains(html, "let p of employees");
        Expect.Contains(html, "<input type=\"date\" id=\"donateLeaveWhenDonated\"");
        Expect.Contains(html, "| date:'MM/dd/yyyy'");
        Expect.Contains(html, "<textarea id=\"donateLeaveNote\"");     // 100 characters: long text
        Expect.Contains(ts, "private employeeService: EmployeeService");
        Expect.Contains(ts, "this.selectedRow.whenDonated = this.selectedRow.whenDonated.substring(0, 10);");
        Expect.Contains(ts, "employeeName(id?: number)");
        Expect.DoesNotContain(ts, "}, (error)");   // never the deprecated two-callback subscribe
    }

    [TestMethod]
    public async Task A_component_gets_a_search_bar_only_for_a_table_with_a_searchable_column()
    {
        // Holiday and DonateLeave both have a searchable column (Name, Note respectively) -- Search
        // (CodeGenPossibilities\Search) replaced the old single findByName-based "Search by Name" box (only
        // ever offered for a text Name column) with the fuller search bar for both, once a searchable column
        // -- of which Name is just one kind -- exists at all. Only a table with NO searchable column at all
        // (Metric, all-numeric) gets no search UI, same as before.
        var holiday = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.Holiday())).ToDictionary(f => Path.GetFileName(f.RelativePath));
        var donate = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.DonateLeave())).ToDictionary(f => Path.GetFileName(f.RelativePath));
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        var metric = GeneratedFiles.Split(await Render("TS_Component_v1.tt", allNumeric)).ToDictionary(f => Path.GetFileName(f.RelativePath));

        Expect.Contains(holiday["holiday.component.html"].Content, "Search by Name");
        Expect.DoesNotContain(holiday["holiday.component.ts"].Content, "findByName");
        Expect.DoesNotContain(donate["donateleave.component.html"].Content, "Search by Note");   // a Note column is long text: never a search box
        Expect.DoesNotContain(metric["metric.component.html"].Content, "form-group-search");
    }

    [TestMethod]
    public async Task A_component_spec_can_create_the_component_without_a_network()
    {
        var spec = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.Holiday())).Single(f => f.RelativePath.EndsWith(".spec.ts"));

        Expect.Contains(spec.Content, "provideHttpClient()");
        Expect.Contains(spec.Content, "provideHttpClientTesting()");
        Expect.Contains(spec.Content, "describe('HolidayComponent'");
    }

    [TestMethod]
    public async Task A_new_row_starts_with_todays_date_and_a_boolean_starts_at_its_database_default()
    {
        var table = Sample.Table("Thing",
        [
            Sample.Column("ThingId", SqlDbType.Int, primaryKey: true),
            Sample.Column("Started", SqlDbType.Date),
            Sample.Column("IsOpen", SqlDbType.Bit, defaultSql: "((1))"),
            Sample.Column("Count", SqlDbType.Int)
        ]);

        string ts = GeneratedFiles.Split(await Render("TS_Component_v1.tt", table)).Single(f => f.RelativePath.EndsWith(".component.ts") && !f.RelativePath.EndsWith(".spec.ts")).Content;

        Expect.Contains(ts, "started: new Date().toISOString().substring(0, 10)");
        Expect.Contains(ts, "isOpen: true");
        Expect.Contains(ts, "count: 0");
    }

    [TestMethod]
    public async Task A_column_covered_by_two_foreign_keys_to_the_same_parent_does_not_crash_the_component()
    {
        var files = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.DuplicateForeignKeyColumn()));

        Assert.IsTrue(files.Any(f => f.RelativePath.EndsWith("product.component.ts")));
    }

    [TestMethod]
    public async Task TS_Component_pages_the_grid_through_mat_paginator_when_the_table_has_a_searchable_column()
    {
        var files = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.Holiday())).ToDictionary(f => Path.GetFileName(f.RelativePath));
        string html = files["holiday.component.html"].Content;
        string ts = files["holiday.component.ts"].Content;
        string spec = files["holiday.component.spec.ts"].Content;

        Expect.Contains(html, "<mat-paginator");
        Expect.Contains(html, "[length]=\"totalCount\"");
        Expect.Contains(html, "[pageIndex]=\"pageIndex\"");
        Expect.Contains(html, "(page)=\"onPageChange($event)\"");
        Expect.Contains(ts, "import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';");
        Expect.Contains(ts, "imports: [ CommonModule, FormsModule, MatPaginatorModule ],");
        Expect.Contains(ts, "pageIndex = 0;");
        Expect.Contains(ts, "totalCount = 0;");
        Expect.Contains(ts, "this.holidayService.getPage(this.pageIndex + 1, this.pageSize, this.filters.sY_IsoCountry_Alpha3Code, this.filters.name, this.sort?.column, this.sort?.descending).subscribe((result) => {");
        Expect.Contains(ts, "onPageChange(event: PageEvent): void {");
        Expect.Contains(ts, "this.pageIndex = event.pageIndex;");
        Expect.DoesNotContain(ts, "getAll()");
        Expect.Contains(spec, "import { provideNoopAnimations } from '@angular/platform-browser/animations';");
        Expect.Contains(spec, "providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])]");
    }

    [TestMethod]
    public async Task TS_Component_gets_a_search_bar_with_one_input_per_searchable_column()
    {
        var files = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.Holiday())).ToDictionary(f => Path.GetFileName(f.RelativePath));
        string html = files["holiday.component.html"].Content;
        string ts = files["holiday.component.ts"].Content;

        Expect.Contains(html, "[(ngModel)]=\"filters.sY_IsoCountry_Alpha3Code\"");
        Expect.Contains(html, "[(ngModel)]=\"filters.name\"");
        Expect.Contains(html, "(click)=\"search()\"");
        Expect.Contains(html, "(click)=\"clearSearch()\"");
        Expect.DoesNotContain(html, "searchText");

        Expect.Contains(ts, "filters = { sY_IsoCountry_Alpha3Code: '', name: '' };");
        Expect.Contains(ts, "search(): void {");
        Expect.Contains(ts, "this.selectedRow = null;");
        Expect.Contains(ts, "this.pageIndex = 0;");
        Expect.Contains(ts, "clearSearch(): void {");
        Expect.Contains(ts, "this.filters.sY_IsoCountry_Alpha3Code = '';");
        Expect.Contains(ts, "this.filters.name = '';");
        Expect.DoesNotContain(ts, "findByName");
    }

    [TestMethod]
    public async Task TS_Component_still_pages_the_grid_but_gets_no_search_bar_when_the_table_has_no_searchable_column()
    {
        // Pagination and searchability are separate concerns (found live needing pagination alone on such a
        // table, 2026-09-28): the grid still pages through mat-paginator/getPage; only the search bar itself
        // (and its Search/Clear buttons, filters state) is skipped when there's nothing to search by.
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        var files = GeneratedFiles.Split(await Render("TS_Component_v1.tt", allNumeric)).ToDictionary(f => Path.GetFileName(f.RelativePath));
        string html = files["metric.component.html"].Content;
        string ts = files["metric.component.ts"].Content;

        Expect.Contains(html, "<mat-paginator");
        Expect.DoesNotContain(html, "form-group-search");
        Expect.Contains(ts, "import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';");
        Expect.Contains(ts, "this.metricService.getPage(this.pageIndex + 1, this.pageSize, this.sort?.column, this.sort?.descending).subscribe((result) => {");
        Expect.DoesNotContain(ts, "filters");
        Expect.DoesNotContain(ts, "clearSearch");
        Expect.DoesNotContain(ts, "getAll()");
    }

    [TestMethod]
    public async Task The_folder_names_in_the_settings_decide_where_files_go_and_how_they_import()
    {
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("ComponentsFolder", "screens"), new("ModelsFolder", "types")]);

        var result = await TemplateRunner.RunAsync(Repo.Template("TS_Component_v1.tt"), Sample.Holiday(), project);

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!);
        Assert.IsTrue(files.All(f => f.RelativePath.StartsWith("screens/holiday/")));
        Expect.Contains(files.Single(f => f.RelativePath.EndsWith("holiday.component.ts")).Content, "from '../../types/holiday'");
    }

    [TestMethod]
    public async Task TS_Component_pluralizes_the_list_variable_correctly_for_names_ending_in_s()
    {
        var movies = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.Movies())).ToDictionary(f => Path.GetFileName(f.RelativePath));
        var address = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.Address())).ToDictionary(f => Path.GetFileName(f.RelativePath));

        Expect.Contains(movies["movies.component.ts"].Content, "movies: Movies[] = [];"); // already plural: left alone
        Expect.DoesNotContain(movies["movies.component.ts"].Content, "moviess");
        Expect.Contains(address["address.component.ts"].Content, "addresses: Address[] = [];"); // singular ending in "ss": gets "es"
        Expect.DoesNotContain(address["address.component.ts"].Content, "addresss:");

        // The <h1> heading has the identical bug (a literal "+ s"), found live against the real, already-plural
        // Movies table -- fixed the same way, alongside the list variable above, not just the error message.
        Expect.Contains(movies["movies.component.html"].Content, "<h1>Movies</h1>");
        Expect.DoesNotContain(movies["movies.component.html"].Content, "Moviess");
        Expect.Contains(address["address.component.html"].Content, "<h1>Addresses</h1>");
    }

    // ------------------------------------------------------------------ TSX_Api (the React counterpart of TS_Service)

    [TestMethod]
    public async Task TSX_Api_generates_the_five_plain_crud_functions()
    {
        var holiday = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Sample.Holiday())).Single();
        var donate = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Sample.DonateLeave())).Single();

        Assert.AreEqual("api/holidayApi.ts", holiday.RelativePath);
        Expect.Contains(holiday.Content, "import { request } from './client';");
        Expect.Contains(holiday.Content, "import type { Holiday } from '../models/holiday';");
        Expect.Contains(holiday.Content, "export const holidayApi = {");
        Expect.Contains(holiday.Content, "getAll: () => request<Holiday[]>(apiUrl),");
        Expect.Contains(holiday.Content, "getById: (id: number) => request<Holiday>(`${apiUrl}/${id}`),");
        Expect.Contains(holiday.Content, "create: (holiday: Holiday) => request<Holiday>(apiUrl, { method: 'POST', body: JSON.stringify(holiday) }),");
        Expect.Contains(holiday.Content, "update: (id: number, holiday: Holiday) => request<Holiday>(`${apiUrl}/${id}`, { method: 'PUT', body: JSON.stringify(holiday) }),");
        Expect.Contains(holiday.Content, "delete: (id: number) => request<void>(`${apiUrl}/${id}`, { method: 'DELETE' }),");
        Expect.Contains(holiday.Content, "findByName: (name: string) => request<Holiday[]>(`${apiUrl}/${name}`),");

        Expect.DoesNotContain(donate.Content, "findByName"); // E_DonateLeave has no text Name column
    }

    [TestMethod]
    public async Task TSX_Api_uses_a_string_key_for_a_uniqueidentifier_primary_key()
    {
        var file = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Sample.AccountRef())).Single();

        Expect.Contains(file.Content, "getById: (id: string) => request<AccountRef>(`${apiUrl}/${id}`),");
    }

    [TestMethod]
    public async Task TSX_Api_pluralizes_the_route_correctly_for_names_ending_in_s()
    {
        var movies = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Sample.Movies())).Single();
        var address = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Sample.Address())).Single();
        var settingsSales = GeneratedFiles.Split(await Render("TSX_Api_v1.tt", Sample.SettingsSales())).Single();

        Expect.Contains(movies.Content, "const apiUrl = '/api/movies';"); // already plural: left alone
        Expect.DoesNotContain(movies.Content, "'/api/moviess'");
        Expect.Contains(address.Content, "const apiUrl = '/api/addresses';"); // singular ending in "ss": gets "es"
        Expect.DoesNotContain(address.Content, "'/api/address'");
        Expect.Contains(settingsSales.Content, "const apiUrl = '/api/settingssales';"); // bare trailing "s": left alone
        Expect.DoesNotContain(settingsSales.Content, "'/api/settingssaless'");
    }

    [TestMethod]
    public async Task TSX_Api_refuses_the_same_tables_TS_Service_refuses()
    {
        StringAssert.Contains(await Refusal("TSX_Api_v1.tt", Sample.NaturalKey()), "int or uniqueidentifier");
        StringAssert.Contains(await Refusal("TSX_Api_v1.tt", Sample.CompositeKey()), "composite primary key");
        StringAssert.Contains(await Refusal("TSX_Api_v1.tt", Sample.DepartmentTeam()), "NameActiveRepo");
        StringAssert.Contains(await Refusal("TSX_Api_v1.tt", Sample.Roles()), "noApiTables");
    }

    [TestMethod]
    public async Task TSX_Api_adds_getPage_only_when_the_table_has_a_searchable_column()
    {
        string withSearch = await Render("TSX_Api_v1.tt", Sample.Holiday());
        Expect.Contains(withSearch, "export interface HolidayPagedResult {");
        Expect.Contains(withSearch, "items: Holiday[];");
        Expect.Contains(withSearch, "getPage: (pageNumber: number, pageSize: number, filters: { sY_IsoCountry_Alpha3Code?: string; name?: string } = {}, sort: { column: string; descending: boolean } | null = null) => {");
        Expect.Contains(withSearch, "if (filters.sY_IsoCountry_Alpha3Code) params.set('sY_IsoCountry_Alpha3Code', filters.sY_IsoCountry_Alpha3Code);");
        Expect.Contains(withSearch, "if (filters.name) params.set('name', filters.name);");
        Expect.Contains(withSearch, "return request<HolidayPagedResult>(`${apiUrl}/search?${params.toString()}`);");

        // Pagination and searchability are separate concerns (found live needing pagination alone on such a
        // table, 2026-09-28): getPage/PagedResult always exist, just with an empty filters object type.
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        string withoutSearch = await Render("TSX_Api_v1.tt", allNumeric);
        Expect.Contains(withoutSearch, "export interface MetricPagedResult {");
        Expect.Contains(withoutSearch, "getPage: (pageNumber: number, pageSize: number, _filters: {} = {}, sort: { column: string; descending: boolean } | null = null) => {");
        Expect.Contains(withoutSearch, "return request<MetricPagedResult>(`${apiUrl}/search?${params.toString()}`);");
    }

    // ------------------------------------------------------------------ TSX_Page (the React counterpart of TS_Component)

    [TestMethod]
    public async Task TSX_Page_writes_a_page_component_plus_a_colocated_test_file()
    {
        var files = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.DonateLeave()));

        Assert.HasCount(2, files);
        Assert.IsTrue(files.Any(f => f.RelativePath == "pages/DonateLeavePage.tsx"));
        Assert.IsTrue(files.Any(f => f.RelativePath == "pages/__tests__/DonateLeavePage.test.tsx"));
    }

    [TestMethod]
    public async Task TSX_Page_uses_useState_and_useEffect_instead_of_a_class()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.Holiday()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Expect.Contains(tsx, "import { useEffect, useState } from 'react';");
        Expect.Contains(tsx, "export function HolidayPage() {");
        Expect.Contains(tsx, "const [holidays, setHolidays] = useState<Holiday[]>([]);");
        Expect.Contains(tsx, "useEffect(() => {");
        Expect.Contains(tsx, "<h1>Holidays</h1>");
        Expect.Contains(tsx, "<button type=\"button\" onClick={add}>Add New Holiday</button>");
        Expect.Contains(tsx, "placeholder=\"Search by Name\""); // Holiday has a text Name column
        Expect.Contains(tsx, "{error && !selectedRow && <p>{error}</p>}");   // the dialog shows the error itself while it is open
        Expect.Contains(tsx, "{error && <p role=\"alert\">{error}</p>}");
    }

    [TestMethod]
    public async Task TSX_Page_assumes_only_the_rail_class_not_TS_Components_own_btn_and_form_classes()
    {
        // CriticalViewer's real index.css/movie-viewer.css has no "btn"/"form-group"/"form-container"/"btn-action"
        // classes -- only the Angular family's reference app (TimeEntryUI) does -- so a generated page must not
        // invent them; see this template's own header comment for the inspection that found the gap.
        string tsx = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.Holiday()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Expect.Contains(tsx, "className=\"rail\"");
        Expect.DoesNotContain(tsx, "btn");
        Expect.DoesNotContain(tsx, "form-group");
        Expect.DoesNotContain(tsx, "form-container");
    }

    [TestMethod]
    public async Task TSX_Page_renders_errors_as_state_not_alert()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.Holiday()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Expect.Contains(tsx, "const [error, setError] = useState<string | null>(null);");
        Expect.DoesNotContain(tsx, "alert(");
    }

    [TestMethod]
    public async Task TSX_Page_calls_the_function_TSX_Api_actually_generates_for_a_lookup_parent()
    {
        string parentApiTs = await Render("TSX_Api_v1.tt", Sample.Employee());
        string pageTs = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.DonateLeave()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        StringAssert.Contains(parentApiTs, "export const employeeApi = {");
        Expect.Contains(pageTs, "import { employeeApi } from '../api/employeeApi';");
        Expect.Contains(pageTs, "employeeApi.getAll().then(setEmployees).catch(() => {});");
    }

    [TestMethod]
    public async Task TSX_Page_starting_values_for_add_match_each_columns_type()
    {
        var table = Sample.Table("Project",
        [
            Sample.Column("ProjectId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Started", SqlDbType.Date),
            Sample.Column("IsOpen", SqlDbType.Bit, defaultSql: "((1))"),
            Sample.Column("Count", SqlDbType.Int)
        ]);

        string tsx = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", table))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Expect.Contains(tsx, "started: new Date().toISOString().substring(0, 10)");
        Expect.Contains(tsx, "isOpen: true");
        Expect.Contains(tsx, "count: 0");
    }

    [TestMethod]
    public async Task A_column_covered_by_two_foreign_keys_to_the_same_parent_does_not_crash_TSX_Page()
    {
        var files = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.DuplicateForeignKeyColumn()));

        Assert.IsTrue(files.Any(f => f.RelativePath.EndsWith("Page.tsx")));
    }

    [TestMethod]
    public async Task TSX_Page_refuses_the_same_tables_TS_Component_refuses()
    {
        StringAssert.Contains(await Refusal("TSX_Page_v1.tt", Sample.NaturalKey()), "int or uniqueidentifier");
        StringAssert.Contains(await Refusal("TSX_Page_v1.tt", Sample.CompositeKey()), "composite primary key");
        StringAssert.Contains(await Refusal("TSX_Page_v1.tt", Sample.DepartmentTeam()), "NameActiveRepo");
    }

    [TestMethod]
    public async Task TSX_Page_pages_the_grid_through_getPage_with_a_PaginationBar_when_the_table_has_a_searchable_column()
    {
        var files = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.Holiday()));
        string tsx = files.Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;
        string test = files.Single(f => f.RelativePath.EndsWith("Page.test.tsx")).Content;

        Expect.Contains(tsx, "import { PaginationBar } from '../components/PaginationBar';");
        Expect.Contains(tsx, "const pageSize = 20;");
        Expect.Contains(tsx, "const [page, setPage] = useState(1);");
        Expect.Contains(tsx, "const [totalPages, setTotalPages] = useState(1);");
        Expect.Contains(tsx, "const load = (targetPage: number = 1, filterValues: typeof filters = filters, sortValue: GridSort | null = sort) => {");
        Expect.Contains(tsx, "holidayApi.getPage(targetPage, pageSize, filterValues, sortValue).then((result) => {");
        Expect.Contains(tsx, "<PaginationBar");
        Expect.Contains(tsx, "onPrevious={() => load(page - 1)}");
        Expect.Contains(tsx, "onNext={() => load(page + 1)}");
        Expect.DoesNotContain(tsx, "getAll()");
        Expect.Contains(test, "vi.spyOn(holidayApi, 'getPage').mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });");
    }

    [TestMethod]
    public async Task TSX_Page_gets_a_search_bar_with_one_input_per_searchable_column()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.Holiday()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Expect.Contains(tsx, "const [filters, setFilters] = useState({ sY_IsoCountry_Alpha3Code: '', name: '' });");
        Expect.Contains(tsx, "onChange={(event) => setFilters({ ...filters, sY_IsoCountry_Alpha3Code: event.target.value })}");
        Expect.Contains(tsx, "onChange={(event) => setFilters({ ...filters, name: event.target.value })}");
        Expect.Contains(tsx, "const search = () => {");
        Expect.Contains(tsx, "setSelectedRow(null);");
        Expect.Contains(tsx, "load(1);");
        Expect.Contains(tsx, "const clearSearch = () => {");
        Expect.Contains(tsx, "const cleared = { sY_IsoCountry_Alpha3Code: '', name: '' };");
        Expect.Contains(tsx, "setFilters(cleared);");
        Expect.Contains(tsx, "load(1, cleared);");
        Expect.Contains(tsx, "onClick={search}");
        Expect.Contains(tsx, "onClick={clearSearch}");
        Expect.DoesNotContain(tsx, "searchText");
        Expect.DoesNotContain(tsx, "findByName");
    }

    [TestMethod]
    public async Task TSX_Page_still_pages_the_grid_but_gets_no_search_bar_when_the_table_has_no_searchable_column()
    {
        // Pagination and searchability are separate concerns (found live needing pagination alone on such a
        // table, 2026-09-28): the grid still pages through PaginationBar/getPage; only the search bar itself
        // (and its Search/Clear buttons, filters state) is skipped when there's nothing to search by.
        var allNumeric = Sample.Table("Metric", [Sample.Column("MetricId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Value", SqlDbType.Int, ordinal: 2)]);
        var files = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", allNumeric));
        string tsx = files.Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;
        string test = files.Single(f => f.RelativePath.EndsWith("Page.test.tsx")).Content;

        Expect.Contains(tsx, "import { PaginationBar } from '../components/PaginationBar';");
        Expect.Contains(tsx, "const load = (targetPage: number = 1, sortValue: GridSort | null = sort) => {");
        Expect.Contains(tsx, "metricApi.getPage(targetPage, pageSize, {}, sortValue).then((result) => {");
        Expect.Contains(tsx, "<PaginationBar");
        Expect.DoesNotContain(tsx, "filters");
        Expect.DoesNotContain(tsx, "clearSearch");
        Expect.DoesNotContain(tsx, "getAll()");
        Expect.Contains(test, "vi.spyOn(metricApi, 'getPage').mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });");
    }

    [TestMethod]
    public async Task TSX_Page_pluralizes_the_list_variable_correctly_for_names_ending_in_s()
    {
        string movies = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.Movies()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;
        string address = GeneratedFiles.Split(await Render("TSX_Page_v1.tt", Sample.Address()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Expect.Contains(movies, "const [movies, setMovies] = useState<Movies[]>([]);"); // already plural: left alone
        Expect.DoesNotContain(movies, "setMoviess");
        Expect.Contains(address, "const [addresses, setAddresses] = useState<Address[]>([]);"); // singular ending in "ss": gets "es"
        Expect.DoesNotContain(address, "setAddress]");

        // The <h1> heading has the identical bug (a literal "+ s"), found live against the real, already-plural
        // Movies table -- fixed the same way, alongside the list variable above, not just the error message.
        Expect.Contains(movies, "<h1>Movies</h1>");
        Expect.DoesNotContain(movies, "Moviess");
        Expect.Contains(address, "<h1>Addresses</h1>");
    }

    // ------------------------------------------------------------------ TSX_DetailMasterPage

    [TestMethod]
    public async Task TSX_DetailMasterPage_refuses_a_table_with_no_child_tables()
    {
        string message = await Refusal("TSX_DetailMasterPage_v1.tt", Sample.DonateLeave());

        StringAssert.Contains(message, "TSX_Page instead");
    }

    [TestMethod]
    public async Task TSX_DetailMasterPage_writes_the_grid_form_plus_one_child_grid_per_child_table()
    {
        var files = GeneratedFiles.Split(await Render("TSX_DetailMasterPage_v1.tt", Sample.DepartmentWithTeams()));
        string tsx = files.Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Assert.IsTrue(files.Any(f => f.RelativePath == "pages/DepartmentDetailMasterPage.tsx"));
        Expect.Contains(tsx, "const [departmentTeamRows, setDepartmentTeamRows] = useState<any[]>([]);");
        Expect.Contains(tsx, "const loadDepartmentTeam = (parentId: number) => {");
        Expect.Contains(tsx, "request<any[]>('/api/departmentteams')");
        Expect.Contains(tsx, "Save this Department first to see its Department Team rows.");
    }

    [TestMethod]
    public async Task TSX_DetailMasterPage_calls_the_function_TSX_Api_actually_generates_for_a_lookup_parent()
    {
        string parentApiTs = await Render("TSX_Api_v1.tt", Sample.Employee());
        string pageTs = GeneratedFiles.Split(await Render("TSX_DetailMasterPage_v1.tt", Sample.TimeSheetWithEmployeeAndDetail()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        StringAssert.Contains(parentApiTs, "export const employeeApi = {");
        Expect.Contains(pageTs, "import { employeeApi } from '../api/employeeApi';");
        Expect.Contains(pageTs, "employeeApi.getAll().then(setEmployees).catch(() => {});");
    }

    [TestMethod]
    public async Task TSX_DetailMasterPage_does_not_double_pluralize_an_already_plural_child_table_name()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_DetailMasterPage_v1.tt", Sample.MovieWithReviews()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Expect.Contains(tsx, "request<any[]>('/api/reviews')");
        Expect.DoesNotContain(tsx, "'/api/reviewss'");
    }

    [TestMethod]
    public async Task TSX_DetailMasterPage_adds_es_for_a_child_table_name_ending_in_double_s()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_DetailMasterPage_v1.tt", Sample.CustomerWithAddressChild()))
            .Single(f => f.RelativePath.EndsWith("Page.tsx")).Content;

        Expect.Contains(tsx, "request<any[]>('/api/addresses')");
        Expect.DoesNotContain(tsx, "'/api/address')");
    }

    [TestMethod]
    public async Task TSX_DetailMasterPage_refuses_a_composite_primary_key()
    {
        var table = Sample.Table("Department",
        [
            Sample.Column("LeftId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("RightId", SqlDbType.Int, primaryKey: true, ordinal: 2)
        ], childForeignKeys: [Sample.ChildForeignKey("DepartmentTeam", "DepartmentId", "LeftId")]);

        string message = await Refusal("TSX_DetailMasterPage_v1.tt", table);

        StringAssert.Contains(message, "composite primary key");
    }

    [TestMethod]
    public async Task TSX_DetailMasterPage_refuses_a_name_active_table_even_though_it_has_child_tables()
    {
        var table = Sample.Table("Department",
        [
            Sample.Column("DepartmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2),
            Sample.Column("IsActive", SqlDbType.Bit, defaultSql: "((1))", ordinal: 3)
        ], childForeignKeys: [Sample.ChildForeignKey("DepartmentTeam", "DepartmentId", "DepartmentId")]);

        StringAssert.Contains(await Refusal("TSX_DetailMasterPage_v1.tt", table), "NameActiveRepo");
    }

    // ------------------------------------------------------------------ TSX_JunctionPage

    [TestMethod]
    public async Task TSX_JunctionPage_refuses_a_table_that_is_not_a_junction_table()
    {
        string message = await Refusal("TSX_JunctionPage_v1.tt", Sample.DonateLeave());

        StringAssert.Contains(message, "IsJunctionTable is false");
    }

    [TestMethod]
    public async Task TSX_JunctionPage_writes_a_shuttle_control_component_with_a_colocated_test()
    {
        var files = GeneratedFiles.Split(await Render("TSX_JunctionPage_v1.tt", Sample.JunctionWithSurrogateKey()));
        string tsx = files.Single(f => f.RelativePath.EndsWith(".tsx") && !f.RelativePath.Contains("__tests__")).Content;

        Assert.IsTrue(files.Any(f => f.RelativePath == "components/NameBaseGroupXrefJunction.tsx"));
        Assert.IsTrue(files.Any(f => f.RelativePath == "components/__tests__/NameBaseGroupXrefJunction.test.tsx"));
        Expect.Contains(tsx, "export interface NameBaseGroupXrefJunctionItem {");
        Expect.Contains(tsx, "export function NameBaseGroupXrefJunction({ anchorId }: { anchorId: number }) {");
        Expect.Contains(tsx, "shortDescr?: string;"); // Groups' display column, camelCased like TS_Model
        Expect.Contains(tsx, "request<NameBaseGroupXrefJunctionItem[]>(`${apiUrl}/${anchorId}`)");
        Expect.Contains(tsx, "request(`${apiUrl}/link`, { method: 'POST', body: JSON.stringify({ anchorId, targetId: item.targetId }) })");
    }

    [TestMethod]
    public async Task TSX_JunctionPage_does_not_double_pluralize_an_already_plural_table_name()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_JunctionPage_v1.tt", Sample.JunctionWithPluralName()))
            .Single(f => f.RelativePath.EndsWith(".tsx") && !f.RelativePath.Contains("__tests__")).Content;

        Expect.Contains(tsx, "const apiUrl = '/api/ratings/junction';");
        Expect.DoesNotContain(tsx, "'/api/ratingss/junction'");
    }

    [TestMethod]
    public async Task TSX_JunctionPage_adds_es_for_a_singular_table_name_ending_in_double_s()
    {
        string tsx = GeneratedFiles.Split(await Render("TSX_JunctionPage_v1.tt", Sample.JunctionWithDoubleSName()))
            .Single(f => f.RelativePath.EndsWith(".tsx") && !f.RelativePath.Contains("__tests__")).Content;

        Expect.Contains(tsx, "const apiUrl = '/api/classes/junction';");
        Expect.DoesNotContain(tsx, "'/api/class/junction'");
    }
}
