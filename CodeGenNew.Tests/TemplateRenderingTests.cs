using System.Data;
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
