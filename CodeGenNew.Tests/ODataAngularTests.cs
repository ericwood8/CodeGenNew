using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The Angular half of item 114: the OData service of a table, the screen over it (TS_ODataComponent is TS_Component run as its OData form), the routes, the proxy and the essentials. </summary>
[TestClass]
public class ODataAngularTests
{
    private static readonly (string, string) On = ("OData", "true");

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = [.. tables] };

    private static async Task<string> Run(string template, DatabaseModel database, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), database, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static async Task<string> Run(string template, TableModel table, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static async Task<string> Run(string template, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void A_foreign_key_sorts_by_the_display_column_of_a_parent_that_is_served_and_by_its_own_value_otherwise()
    {
        var employee = Sample.Employee();
        var timeSheet = Sample.TimeSheetWithEmployeeAndDetail();
        var settings = Project(On);

        var served = new Dictionary<string, TableModel> { ["Employee"] = employee, ["TimeSheet"] = timeSheet };
        var plan = ODataScreenPlan.For(timeSheet, settings, served);
        Assert.AreEqual("employee/name", plan.Sorts.Single(s => s.Name == "EmployeeId").Path);
        Assert.AreEqual("whenEntered", plan.Sorts.Single(s => s.Name == "WhenEntered").Path);
        Assert.AreEqual("timeSheetId", plan.KeyProperty);
        Assert.AreEqual(ODataNames.SetName("TimeSheet"), plan.Set);

        var alone = ODataScreenPlan.For(timeSheet, settings, new Dictionary<string, TableModel> { ["TimeSheet"] = timeSheet });
        Assert.AreEqual("employeeId", alone.Sorts.Single(s => s.Name == "EmployeeId").Path, "the parent is not served, so $orderby cannot name it");
    }

    [TestMethod]
    public void The_plan_names_the_boxes_the_order_and_the_drop_down_columns_in_the_JSON_names()
    {
        var holiday = Sample.Holiday();
        var plan = ODataScreenPlan.For(holiday, Project(On), new Dictionary<string, TableModel> { ["Holiday"] = holiday });
        CollectionAssert.AreEqual(new[] { "sY_IsoCountry_Alpha3Code", "name" }, plan.Filters.Select(f => f.Key).ToArray(), "the keys are the filter names the REST screen uses");
        Assert.AreEqual("name", plan.Filters.Single(f => f.Key == "name").Property);
        Assert.AreEqual("name", plan.LookupName);
        Assert.IsNull(plan.LookupActive);
        Assert.AreEqual("holidayId", plan.DefaultOrder[^1]);
    }

    [TestMethod]
    public void A_stub_template_runs_the_text_of_its_base_with_its_variant()
    {
        var config = TemplateConfig.Load(Repo.Template("TS_ODataComponent_v1.tt.config"));
        Assert.AreEqual("TS_Component_v1.tt", config.Base);
        Assert.AreEqual("OData", config.Variant);
        Assert.IsFalse(config.InPlan);

        var (path, variant) = TemplateAlias.Resolve(Repo.Template("TS_ODataComponent_v1.tt"));
        Assert.AreEqual(Repo.Template("TS_Component_v1.tt"), path);
        Assert.AreEqual("OData", variant);

        var (own, none) = TemplateAlias.Resolve(Repo.Template("TS_Component_v1.tt"));
        Assert.AreEqual(Repo.Template("TS_Component_v1.tt"), own);
        Assert.IsNull(none);
    }

    [TestMethod]
    public async Task The_OData_screen_is_the_REST_screen_over_the_OData_service_with_names_of_its_own()
    {
        var holiday = Sample.Holiday();
        string odata = await Run("TS_ODataComponent_v1.tt", holiday, On);
        Expect.Contains(odata, "@@@FILE components/holiday-odata/holiday-odata.component.ts@@@");
        Expect.Contains(odata, "import { HolidayODataService } from '../../services/holiday-odata.service';");
        Expect.Contains(odata, "protected readonly service = inject(HolidayODataService);");
        Expect.Contains(odata, "selector: 'app-holiday-odata',");
        Expect.Contains(odata, "export class HolidayODataComponent extends ODataPagedScreen<Holiday>");
        Expect.Contains(odata, "import { ColumnFilterField, ODataPagedScreen } from '../odata-screen';");
        Expect.Contains(odata, "(click)=\"sortBy('Name', $event)\"");
        Expect.Contains(odata, "<tr class=\"filter-row\">");
        Expect.Contains(odata, "[(ngModel)]=\"columnFilters['Name'].text\"");
        Expect.Contains(odata, "(click)=\"open(holiday)\"");
        Expect.Contains(odata, "{ name: 'Date', property: 'date', kind: 'date', nullable: false }");
        Expect.Contains(odata, "protected override readonly selectColumns = ['");
        Expect.Contains(odata, ".sort-order {");
        Expect.Contains(odata, "protected readonly gridKey = 'holiday-odata';");
        Expect.Contains(odata, "r.urlWithParams.startsWith('odata/holidays?')");
        Expect.Contains(odata, "'@odata.count': 0");
        Expect.Contains(odata, "date:'MM/dd/yyyy':'UTC'");

        string rest = await Run("TS_Component_v1.tt", holiday, On);
        Expect.Contains(rest, "@@@FILE components/holiday/holiday.component.ts@@@");
        Expect.Contains(rest, "protected readonly service = inject(HolidayService);");
        Expect.DoesNotContain(rest, "odata");
        Expect.DoesNotContain(rest, "'UTC'");
    }

    [TestMethod]
    public async Task The_OData_screen_is_written_only_for_the_tables_served_as_OData()
    {
        Assert.AreEqual("", (await Run("TS_ODataComponent_v1.tt", Sample.Holiday())).Trim(), "OData is off");
        Assert.AreEqual("", (await Run("TS_ODataComponent_v1.tt", Sample.Holiday(), On, ("ODataTables", "Other"))).Trim(), "the table is not in the list");
        Assert.AreEqual("", (await Run("TS_ODataComponent_v1.tt", Sample.Holiday(), On, ("NoApiTables", "Holiday"))).Trim());
    }

    [TestMethod]
    public async Task The_OData_service_names_the_set_the_key_the_boxes_and_the_sorts_of_each_served_table()
    {
        var holiday = Sample.Holiday();
        var country = Sample.NaturalKey();
        string services = await Run("TS_ODataService_v1.tt", Database(holiday, country), On);

        Expect.Contains(services, "@@@FILE services/holiday-odata.service.ts@@@");
        Expect.Contains(services, "export class HolidayODataService extends ODataCrudService<Holiday> {");
        Expect.Contains(services, "super(http, 'odata/holidays', {");
        Expect.Contains(services, "key: 'holidayId',");
        Expect.Contains(services, "{ key: 'name', property: 'name' }");
        Expect.Contains(services, "}, 'api/holidays');");
        Expect.Contains(services, "export class CountryODataService extends ODataCrudService<Country, string> {");
        Expect.Contains(services, "lookup: { name: 'label' }");
    }

    [TestMethod]
    public async Task The_routes_list_the_OData_screen_after_the_REST_ones_only_with_the_flag()
    {
        var database = Database(Sample.Holiday());
        string with = await Run("TS_Screens_v1.tt", database, On);
        Expect.DoesNotContain(with, "import { HolidayODataComponent }");
        Expect.Contains(with, "{ path: 'holiday-odata', label: 'Holiday (OData)', load: () => import('./components/holiday-odata/holiday-odata.component').then((m) => m.HolidayODataComponent) },");
        Expect.Contains(with, "...screens.map((s) => (s.load ? { path: s.path, loadComponent: s.load } : { path: s.path, component: s.component })),");
        Assert.IsLessThan(with.IndexOf("label: 'Holiday (OData)'", StringComparison.Ordinal), with.IndexOf("component: HolidayComponent", StringComparison.Ordinal), "the REST screen comes first");

        string without = await Run("TS_Screens_v1.tt", database);
        Expect.DoesNotContain(without, "OData");
    }

    [TestMethod]
    public async Task ODataScreens_Replace_puts_the_OData_screen_in_the_REST_screens_place()
    {
        var database = Database(Sample.Holiday(), Sample.NaturalKey());
        string replaced = await Run("TS_Screens_v1.tt", database, On, ("ODataScreens", "Replace"));

        Expect.Contains(replaced, "{ path: 'holiday', label: 'Holiday', load: () => import('./components/holiday-odata/holiday-odata.component').then((m) => m.HolidayODataComponent) },");
        Expect.DoesNotContain(replaced, "import { HolidayComponent }");
        Expect.DoesNotContain(replaced, "holiday-odata', label");
        Expect.DoesNotContain(replaced, "(OData)");
        Expect.DoesNotContain(replaced, "component: HolidayComponent");

        string added = await Run("TS_Screens_v1.tt", database, On, ("ODataScreens", "Add"));
        Expect.Contains(added, "component: HolidayComponent");
        Expect.Contains(added, "label: 'Holiday (OData)'");
    }

    [TestMethod]
    public async Task The_proxy_forwards_odata_next_to_api_only_with_the_flag()
    {
        string with = await Run("TS_EssentialConfig_v1.tt", On, ("ApiPort", "5090"));
        Expect.Contains(with, "\"/odata\": {");
        Expect.Contains(with, "\"target\": \"http://localhost:5090\"");
        Expect.DoesNotContain(await Run("TS_EssentialConfig_v1.tt"), "odata");

        string docker = await Run("TS_EssentialDocker_v1.tt", On);
        Expect.Contains(docker, "location /odata/ {");
        Expect.DoesNotContain(await Run("TS_EssentialDocker_v1.tt"), "location /odata/");
    }

    [TestMethod]
    public async Task The_essentials_write_the_query_helper_and_the_service_base_and_the_flag_turns_the_group_on()
    {
        string essentials = await Run("TS_EssentialOData_v1.tt");
        Expect.Contains(essentials, "@@@FILE src/app/services/odata-query.ts@@@");
        Expect.Contains(essentials, "export function odataString(value: string): string {");
        Expect.Contains(essentials, "return `'${value.replace(/'/g, \"''\")}'`;");
        Expect.Contains(essentials, "export abstract class ODataCrudService<T, K extends number | string = number> extends CrudService<T, K> {");
        Expect.Contains(essentials, "@@@FILE src/app/services/odata-crud.service.spec.ts@@@");

        CollectionAssert.Contains(Project(On).ImpliedEssentialsGroups.ToArray(), "OData");
        CollectionAssert.DoesNotContain(Project().ImpliedEssentialsGroups.ToArray(), "OData");
        CollectionAssert.IsSubsetOf(new[] { "API_OData", "TS_ODataService", "TS_ODataComponent" }, Project(On).ImpliedPlanTemplates.ToArray());
    }

    private static TableModel Ledger() => Sample.Table("Ledger",
    [
        Sample.Column("LedgerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Reference", SqlDbType.NVarChar, characters: 30, nullable: true, ordinal: 2),
        Sample.Column("Notes", SqlDbType.NVarChar, characters: 4000, ordinal: 3),
        Sample.Column("Amount", SqlDbType.Money, ordinal: 4, currency: true),
        Sample.Column("Quantity", SqlDbType.Int, ordinal: 5),
        Sample.Column("Posted", SqlDbType.Date, nullable: true, ordinal: 6),
        Sample.Column("IsOpen", SqlDbType.Bit, ordinal: 7),
        Sample.Column("Kind", SqlDbType.NVarChar, characters: 10, ordinal: 8, choices: ["Debit", "Credit"]),
        Sample.Column("Token", SqlDbType.UniqueIdentifier, ordinal: 9)
    ]);

    [TestMethod]
    public void Each_grid_column_gets_the_filter_control_of_its_kind_and_a_column_with_no_control_gets_none()
    {
        var form = FormModel.For(Ledger(), Project(On));
        var fields = ODataFilterFields.For(form).Where(f => f is not null).Select(f => f!).ToDictionary(f => f.Name);

        Assert.AreEqual(ODataFilterKind.Text, fields["Reference"].Kind);
        Assert.IsTrue(fields["Reference"].Nullable, "a column that can be empty gets the empty check box");
        Assert.AreEqual(ODataFilterKind.Number, fields["Amount"].Kind);
        Assert.AreEqual(ODataFilterKind.Number, fields["Quantity"].Kind);
        Assert.AreEqual(ODataFilterKind.Date, fields["Posted"].Kind);
        Assert.AreEqual(ODataFilterKind.Boolean, fields["IsOpen"].Kind);
        Assert.AreEqual(ODataFilterKind.Choice, fields["Kind"].Kind);
        CollectionAssert.AreEqual(new[] { "Debit", "Credit" }, fields["Kind"].Choices.ToArray());
        Assert.AreEqual("reference", fields["Reference"].Property);
        Assert.IsFalse(fields.ContainsKey("Notes"), "long text has no control");
        Assert.IsFalse(fields.ContainsKey("Token"), "a guid is not text and has no control");
    }

    [TestMethod]
    public async Task The_OData_screen_has_the_filter_row_the_sort_titles_and_the_order_badges()
    {
        string screen = await Run("TS_ODataComponent_v1.tt", Ledger(), On);

        Expect.Contains(screen, "title=\"Click to sort by Reference. Shift-click to add it to the sort.\"");
        Expect.Contains(screen, "<span class=\"sort-order\" *ngIf=\"sortOrder('Reference')\">{{ sortOrder('Reference') }}</span>");
        Expect.Contains(screen, "<input type=\"text\" class=\"form-control\" placeholder=\"contains\" aria-label=\"Filter by Reference\" [(ngModel)]=\"columnFilters['Reference'].text\" (keyup.enter)=\"search()\" />");
        Expect.Contains(screen, "<label class=\"empty-toggle\" title=\"Only the rows where Reference is empty\"><input type=\"checkbox\" [(ngModel)]=\"columnFilters['Reference'].empty\" /> empty</label>");
        Expect.Contains(screen, "<input type=\"number\" placeholder=\"from\" aria-label=\"Amount from\" [(ngModel)]=\"columnFilters['Amount'].from\" (keyup.enter)=\"search()\" />");
        Expect.Contains(screen, "<option [ngValue]=\"undefined\">Any</option><option value=\"true\">Yes</option><option value=\"false\">No</option>");
        Expect.Contains(screen, "<option value=\"Debit\">Debit</option>");
        Expect.Contains(screen, "<button class=\"btn btn-outline-secondary\" type=\"button\" title=\"Remove every filter\" (click)=\"clearSearch()\">Clear</button>");
        Expect.Contains(screen, "{ name: 'Posted', property: 'posted', kind: 'date', nullable: true }");
        Expect.Contains(screen, "{ name: 'Kind', property: 'kind', kind: 'choice', nullable: false, literal: 'string' }");
        Expect.DoesNotContain(screen, "name: 'Notes'");

        string rest = await Run("TS_Component_v1.tt", Ledger(), On);
        Expect.DoesNotContain(rest, "filter-row");
        Expect.DoesNotContain(rest, "columnFilters");
        Expect.DoesNotContain(rest, "sortBy('Reference', $event)");
        Expect.Contains(rest, "(click)=\"edit(ledger)\"");
    }

    [TestMethod]
    public async Task The_essentials_write_the_screen_base_with_the_sort_list_the_conditions_and_the_slim_grid()
    {
        string essentials = await Run("TS_EssentialOData_v1.tt");
        Expect.Contains(essentials, "@@@FILE src/app/components/odata-screen.ts@@@");
        Expect.Contains(essentials, "export abstract class ODataPagedScreen<T, K extends number | string = number> extends PagedCrudScreen<T, K> {");
        Expect.Contains(essentials, "override sortBy(column: string, event?: { shiftKey?: boolean }): void {");
        Expect.Contains(essentials, "import { GridSort } from '../grid-sort';");
        Expect.Contains(essentials, "const sortsKey = (gridKey: string) => `gridSort:${gridKey}`;");
        Expect.Contains(essentials, "export function conditionFilter(condition: ODataCondition): string | undefined {");
        Expect.Contains(essentials, "@@@FILE src/app/components/odata-screen.spec.ts@@@");
    }

    internal static TableModel TimeSheetWithKeyedDetail()
    {
        var plain = Sample.TimeSheetWithEmployeeAndDetail();
        var detail = new ChildForeignKeyModel
        {
            ConstraintName = "FK_TimeSheetId_TimeSheetDetail", ReferencingSchema = "dbo", ReferencingTable = "TimeSheetDetail", ReferencingColumns = ["TimeSheetId"], ReferencedColumns = ["TimeSheetId"],
            ReferencingPrimaryKeyColumns = ["TimeSheetDetailId"],
            ReferencingTableColumns =
            [
                Sample.Column("TimeSheetDetailId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("TimeSheetId", SqlDbType.Int, ordinal: 2),
                Sample.Column("Hours", SqlDbType.Int, ordinal: 3)
            ]
        };
        return Sample.Table("TimeSheet", plain.Columns, plain.ForeignKeys, childForeignKeys: [detail]);
    }

    [TestMethod]
    public async Task The_master_detail_OData_screen_reads_the_child_rows_from_the_childs_entity_set_filtered_by_the_parents_key()
    {
        var timeSheet = TimeSheetWithKeyedDetail();
        string screen = await Run("TS_ODataDetailMasterComponent_v1.tt", timeSheet, On);

        Expect.Contains(screen, "@@@FILE components/timesheet-odata/timesheet-odata.component.ts@@@");
        Expect.Contains(screen, "export class TimeSheetODataComponent extends ODataPagedScreen<TimeSheet> {");
        Expect.Contains(screen, "selector: 'app-timesheet-odata',");
        Expect.Contains(screen, "protected readonly service = inject(TimeSheetODataService);");
        Expect.Contains(screen, "<tr class=\"filter-row\">");
        Expect.Contains(screen, "(click)=\"open(timeSheet)\"");
        Expect.Contains(screen, "import { readOData } from '../../services/odata-crud.service';");
        Expect.Contains(screen, "readOData<any>(this.http, 'odata/" + ODataNames.SetName("TimeSheetDetail") + "' + queryString({ $filter: `timeSheetId eq ${odataLiteral(parentId, 'number')}`");
        Expect.Contains(screen, "override edit(timeSheet: TimeSheet): void {");
        Expect.Contains(screen, "r.urlWithParams.startsWith('odata/timesheets?')");

        string rest = await Run("TS_DetailMasterComponent_v1.tt", timeSheet, On);
        Expect.Contains(rest, "@@@FILE components/timesheet-detail-master/timesheet-detail-master.component.ts@@@");
        Expect.Contains(rest, "this.http.get<any[]>('api/" + ODataNames.SetName("TimeSheetDetail") + "')");
        Expect.DoesNotContain(rest, "readOData");
        Expect.DoesNotContain(rest, "filter-row");
    }

    [TestMethod]
    public async Task A_child_that_is_not_served_as_OData_keeps_the_REST_route_and_a_table_that_is_not_served_has_no_master_detail_screen()
    {
        var timeSheet = TimeSheetWithKeyedDetail();
        string unserved = await Run("TS_ODataDetailMasterComponent_v1.tt", timeSheet, On, ("ODataTables", "TimeSheet"));
        Expect.Contains(unserved, "this.http.get<any[]>('api/" + ODataNames.SetName("TimeSheetDetail") + "')");
        Expect.DoesNotContain(unserved, "readOData");

        Assert.AreEqual("", (await Run("TS_ODataDetailMasterComponent_v1.tt", timeSheet)).Trim(), "OData is off");
        Assert.AreEqual("", (await Run("TS_ODataDetailMasterComponent_v1.tt", timeSheet, On, ("ODataTables", "Other"))).Trim());
    }

    [TestMethod]
    public async Task The_routes_list_the_master_detail_OData_screen_too()
    {
        var database = Database(Sample.TimeSheetWithEmployeeAndDetail(), Sample.Employee());
        string routes = await Run("TS_Screens_v1.tt", database, On, ("Screens", "TimeSheet"));
        Expect.Contains(routes, "{ path: 'time-sheet-odata', label: 'Time Sheet (OData)', load: () => import('./components/timesheet-odata/timesheet-odata.component').then((m) => m.TimeSheetODataComponent) },");
        Expect.DoesNotContain(routes, "import { TimeSheetODataComponent }");
    }

    [TestMethod]
    public async Task The_OData_screen_of_a_name_and_active_table_reads_the_whole_list_and_narrows_it_on_the_page()
    {
        var team = Sample.Table("Team", [Sample.Column("TeamId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2), Sample.Column("IsActive", SqlDbType.Bit, defaultSql: "((1))", ordinal: 3)]);
        string screen = await Run("TS_ODataComponent_v1.tt", team, On, ("EnumTables", "none"));

        Expect.Contains(screen, "@@@FILE components/team-odata/team-odata.component.ts@@@");
        Expect.Contains(screen, "export class TeamODataComponent extends NameActiveCrudScreen<Team> {");
        Expect.Contains(screen, "import { NameActiveCrudScreen } from '../crud-screen';");
        Expect.Contains(screen, "protected readonly service = inject(TeamODataService);");
        Expect.Contains(screen, "[(ngModel)]=\"searchText\"");
        Expect.Contains(screen, "(click)=\"edit(team)\"");
        Expect.Contains(screen, "r.urlWithParams.startsWith('odata/teams')");
        Expect.Contains(screen, "request.flush({ value: [");
        Expect.DoesNotContain(screen, "filter-row");
        Expect.DoesNotContain(screen, "ODataPagedScreen");
        Expect.DoesNotContain(screen, "mat-paginator");
    }

    [TestMethod]
    public async Task The_OData_master_detail_screen_of_a_name_and_active_table_reads_its_children_from_the_childs_entity_set()
    {
        var team = Sample.NameActiveTableWithChildren();
        string screen = await Run("TS_ODataDetailMasterComponent_v1.tt", team, On, ("EnumTables", "none"));
        Expect.Contains(screen, "export class TeamODataComponent extends NameActiveCrudScreen<Team> {");
        Expect.Contains(screen, "(click)=\"edit(team)\"");
        Expect.DoesNotContain(screen, "filter-row");
        Expect.Contains(screen, "r.urlWithParams.startsWith('odata/teams')");
    }
}
