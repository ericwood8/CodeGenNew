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
        return result.GeneratedText!;
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
            var model = template.Config.NeedsRowData ? Sample.Roles() : table;
            var result = await Run(template.FileStem + ".tt", model);
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
    public async Task SP_Search_refuses_a_table_with_no_searchable_columns()
    {
        string message = await Refusal("SP_Search_v1.tt", Sample.CompositeKey());

        StringAssert.Contains(message, "searchable");
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

        Expect.Contains(sql, "ORDER BY [Name] ASC, [HolidayId] ASC");
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
        Expect.Contains(xaml, "Text=\"{x:Bind ViewModel.WhenDonated, Mode=TwoWay}\"");
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
        Expect.Contains(viewModel, "if (!int.TryParse(HoursDonated, out var HoursDonatedValue)) { ErrorMessage = \"Hours Donated is not a valid number.\"; return false; }");
        Expect.Contains(viewModel, "if (!DateTime.TryParse(WhenDonated, out var WhenDonatedValue)) { ErrorMessage = \"When Donated is not a valid date.\"; return false; }");
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

        Assert.HasCount(3, files);
        string xaml = files["E_DonateLeaveListPage.xaml"].Content;
        string codeBehind = files["E_DonateLeaveListPage.xaml.cs"].Content;
        string viewModel = files["E_DonateLeaveListViewModel.cs"].Content;

        Expect.Contains(xaml, "x:Class=\"TimeEntry.Desktop.Views.E_DonateLeaveListPage\"");
        Expect.Contains(xaml, "Text=\"Donate From Employee\"");
        Expect.Contains(xaml, "ItemsSource=\"{x:Bind ViewModel.Rows}\"");
        Expect.Contains(xaml, "Text=\"{x:Bind Cells[0]}\"");

        Expect.Contains(codeBehind, "public sealed partial class E_DonateLeaveListPage : Page");
        Expect.Contains(codeBehind, "new E_DonateLeaveDetailDialog(_context)");
        Expect.Contains(codeBehind, "new E_DonateLeaveDetailDialog(_context, entity)");

        Expect.Contains(viewModel, "public class E_DonateLeaveListRow");
        Expect.Contains(viewModel, "var employeeNames = await _context.Set<Employee>().ToDictionaryAsync(r => r.EmployeeId, r => r.Name?.ToString() ?? \"\");");
        Expect.Contains(viewModel, "cells.Add(employeeNames.TryGetValue(e.DonateFrom_EmployeeId, out var donateFrom_EmployeeIdName) ? donateFrom_EmployeeIdName : e.DonateFrom_EmployeeId.ToString());");
        Expect.Contains(viewModel, "int result = await _repo.DeleteAsync(\"E_DonateLeave\", id);");
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

        Assert.HasCount(3, files);
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
        Expect.Contains(viewModel, "public ObservableCollection<string> departmentTeamColumnHeaders { get; } = [];");
        Expect.Contains(viewModel, "public ObservableCollection<DepartmentChildGridRow> departmentTeamRows { get; } = [];");
        Expect.Contains(viewModel, "var entityType = _context.Model.FindEntityType(typeof(DepartmentTeam))!;");
        Expect.Contains(viewModel, "EF.Property<int>(c, \"DepartmentId\") == _editing!.DepartmentId");
        Expect.Contains(viewModel, "if (_editing is null)\n            return; // no child rows to show until this Department has been saved once");
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
        Expect.Contains(html, "*ngFor=\"let row of departmentTeamRows\"");
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

        Expect.Contains(cs, "public class HolidayRepo : GenericRepo<Holiday>");
        Expect.Contains(cs, "public HolidayRepo(TimeEntryContext context) : base(context)");
        Expect.Contains(cs, "public async Task<List<Holiday>> GetByName(string name)");
    }

    [TestMethod]
    public async Task A_name_and_active_child_table_gets_its_rows_by_parent()
    {
        string cs = await Render("CS_Repo_v1.tt", Sample.DepartmentTeam());

        Expect.Contains(cs, "public class DepartmentTeamRepo : NameActiveRepo<DepartmentTeam>");
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
    public async Task No_has_duplicate_check_is_written_when_no_column_is_in_a_unique_index()
    {
        string cs = await Render("CS_Repo_v1.tt", Sample.Holiday());

        Expect.DoesNotContain(cs, "HasDuplicate");
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

        Expect.Contains(html, "<th>Full Name</th>");
        Expect.Contains(html, "<th>List ID</th>");
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
        Assert.AreEqual("", files[0].Content, "the stylesheet is meant to be empty");
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
    public async Task A_component_has_a_search_box_only_for_a_table_with_a_name()
    {
        var holiday = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.Holiday())).ToDictionary(f => Path.GetFileName(f.RelativePath));
        var donate = GeneratedFiles.Split(await Render("TS_Component_v1.tt", Sample.DonateLeave())).ToDictionary(f => Path.GetFileName(f.RelativePath));

        Expect.Contains(holiday["holiday.component.html"].Content, "Search by Name");
        Expect.Contains(holiday["holiday.component.ts"].Content, "findByName(this.searchText)");
        Expect.DoesNotContain(donate["donateleave.component.html"].Content, "Search by Name");
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
    public async Task The_folder_names_in_the_settings_decide_where_files_go_and_how_they_import()
    {
        using var temp = new TempFolder();
        string source = File.ReadAllText(Repo.Template("TS_Component_v1.tt"))
            .Replace("string componentsFolder = \"components\";", "string componentsFolder = \"screens\";")
            .Replace("string modelsFolder = \"models\";", "string modelsFolder = \"types\";");
        string custom = temp.File("TS_Component_v1.tt", source);

        var result = await TemplateRunner.RunAsync(custom, Sample.Holiday());

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = GeneratedFiles.Split(result.GeneratedText!);
        Assert.IsTrue(files.All(f => f.RelativePath.StartsWith("screens/holiday/")));
        Expect.Contains(files.Single(f => f.RelativePath.EndsWith("holiday.component.ts")).Content, "from '../../types/holiday'");
    }
}
