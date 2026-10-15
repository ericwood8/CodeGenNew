using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The React half of item 114: the OData essentials, the api module of a table, the page over it (TSX_ODataPage is TSX_Page run as its OData form), the master-detail form, the routes and the proxy. </summary>
[TestClass]
public class ODataReactTests
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
    public void The_flag_adds_the_React_templates_to_the_plan_and_the_OData_essentials_group_serves_both_web_stacks()
    {
        CollectionAssert.IsSubsetOf(new[] { "TSX_ODataApi", "TSX_ODataPage", "TSX_ODataDetailMasterPage" }, Project(On).ImpliedPlanTemplates.ToArray());
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToArray(), "TSX_ODataPage");
        foreach (string template in new[] { "TSX_ODataApi_v1.tt.config", "TSX_ODataPage_v1.tt.config", "TSX_ODataDetailMasterPage_v1.tt.config" })
            Assert.IsFalse(TemplateConfig.Load(Repo.Template(template)).InPlan, template);
        Assert.AreEqual("TSX_Page_v1.tt", TemplateConfig.Load(Repo.Template("TSX_ODataPage_v1.tt.config")).Base);
        Assert.AreEqual("TSX_DetailMasterPage_v1.tt", TemplateConfig.Load(Repo.Template("TSX_ODataDetailMasterPage_v1.tt.config")).Base);
        Assert.AreEqual("OData", TemplateConfig.Load(Repo.Template("TSX_EssentialOData_v1.tt.config")).EssentialsGroup);
    }

    [TestMethod]
    public async Task The_essentials_write_the_query_helper_the_api_factory_and_the_screen_helpers_with_their_tests()
    {
        string essentials = await Run("TSX_EssentialOData_v1.tt", On);

        foreach (string file in new[] { "src/api/odataQuery.ts", "src/api/__tests__/odataQuery.test.ts", "src/api/odataClient.ts", "src/api/__tests__/odataClient.test.ts",
                     "src/components/odataScreen.tsx", "src/components/odataGrid.css", "src/components/__tests__/odataScreen.test.tsx" })
            Expect.Contains(essentials, "@@@FILE " + file + "@@@");
        Expect.Contains(essentials, "return `'${value.replace(/'/g, \"''\")}'`;");
        Expect.Contains(essentials, "export function createODataApi<T, K extends number | string = number>(set: string, table: ODataTable, restPath: string) {");
        Expect.Contains(essentials, "export async function readOData<R>(path: string): Promise<R[]> {");
        Expect.Contains(essentials, "export function problemText(error: unknown): string | undefined {");
        Expect.Contains(essentials, "return url.pathname + url.search;");
        Expect.Contains(essentials, "export function nextSorts(sorts: GridSort[], column: string, shift: boolean): GridSort[] {");
        Expect.Contains(essentials, "export function FilterCell(");
        Expect.Contains(essentials, ".filter-row td {");
        Expect.Contains(essentials, ".sort-header {");
    }

    [TestMethod]
    public async Task Each_served_table_gets_an_api_module_named_after_the_REST_one()
    {
        string api = await Run("TSX_ODataApi_v1.tt", Database(Sample.Holiday(), Sample.NaturalKey()), On);

        Expect.Contains(api, "@@@FILE api/holidayODataApi.ts@@@");
        Expect.Contains(api, "export const holidayODataApi = createODataApi<Holiday>('holidays', {");
        Expect.Contains(api, "key: 'holidayId',");
        Expect.Contains(api, "defaultOrder: ['name', 'holidayId'],");
        Expect.Contains(api, "lookup: { name: 'name' },");
        Expect.Contains(api, "}, '/api/holidays');");
        Expect.Contains(api, "@@@FILE api/countryODataApi.ts@@@");
        Expect.Contains(api, "createODataApi<Country, string>('countries', {");
    }

    [TestMethod]
    public async Task The_OData_page_asks_the_server_for_several_sorts_a_filter_row_and_only_the_columns_the_grid_shows()
    {
        string page = await Run("TSX_ODataPage_v1.tt", Sample.Holiday(), On);

        Expect.Contains(page, "@@@FILE pages/HolidayODataPage.tsx@@@");
        Expect.Contains(page, "@@@FILE pages/__tests__/HolidayODataPage.test.tsx@@@");
        Expect.Contains(page, "import { holidayODataApi } from '../api/holidayODataApi';");
        Expect.Contains(page, "export function HolidayODataPage() {");
        Expect.Contains(page, "const gridKey = 'holiday-odata';");
        Expect.Contains(page, "const filterFields: ColumnFilterField[] = [");
        Expect.Contains(page, "{ name: 'Date', property: 'date', kind: 'date', nullable: false },");
        Expect.Contains(page, "const selectColumns = ['name', 'sY_IsoCountry_Alpha3Code', 'date', 'sY_DisplayId'];");
        Expect.Contains(page, "holidayODataApi.queryPage({");
        Expect.Contains(page, "select: selectColumns,");
        Expect.Contains(page, "const sortBy = (column: string, shift: boolean) => {");
        Expect.Contains(page, "<ODataSortHeader label=\"Name\" column=\"Name\" sorts={sorts} onSort={sortBy} />");
        Expect.Contains(page, "<tr className=\"filter-row\">");
        Expect.Contains(page, "<FilterCell field={filterFields[0]} label=\"Name\"");
        Expect.Contains(page, "onClick={() => open(holiday)}>Edit</button>");
        Expect.Contains(page, "holidayODataApi.getById(holiday.holidayId!).then(edit)");
        Expect.Contains(page, "(err.status === 400 || err.status === 409)");
        Expect.Contains(page, "setError(problemText(err) ?? 'A row with this value already exists!');");
        Expect.Contains(page, "vi.spyOn(holidayODataApi, 'queryPage')");
        Expect.DoesNotContain(page, "getPage(");
        Expect.DoesNotContain(page, "<SortHeader label");
    }

    [TestMethod]
    public async Task The_REST_page_is_not_touched_by_the_variant()
    {
        string rest = await Run("TSX_Page_v1.tt", Sample.Holiday());

        Expect.Contains(rest, "export function HolidayPage() {");
        Expect.Contains(rest, "if (err instanceof ApiError && err.status === 400) {");
        Expect.Contains(rest, "holidayApi.getPage(targetPage, sizeValue");
        Expect.Contains(rest, "<SortHeader label=\"Name\" column=\"Name\" sort={sort} onSort={sortBy} />");
        Expect.DoesNotContain(rest, "OData");
        Expect.DoesNotContain(rest, "filter-row");
    }

    [TestMethod]
    public async Task The_OData_page_is_written_only_for_a_table_the_project_serves()
    {
        Assert.AreEqual("", (await Run("TSX_ODataPage_v1.tt", Sample.Holiday(), On, ("ODataTables", "Country"))).Trim());
        Assert.AreEqual("", (await Run("TSX_ODataPage_v1.tt", Sample.Holiday())).Trim());
    }

    [TestMethod]
    public async Task The_one_search_box_of_the_Search_list_protocol_stays_above_the_grid_and_searches_on_Enter()
    {
        string page = await Run("TSX_ODataPage_v1.tt", Sample.Holiday(), On, ("ListProtocol", "Search"));

        Expect.Contains(page, "const [filters, setFilters] = useState({ search: '' });");
        Expect.Contains(page, "filters: over.filters ?? filters,");
        Expect.Contains(page, "onKeyUp={(event) => { if (event.key === 'Enter') search(); }}");
        Expect.Contains(page, "load(1, { filters: cleared, columnFilters: {} });");

        string plain = await Run("TSX_ODataPage_v1.tt", Sample.Holiday(), On);
        Expect.DoesNotContain(plain, "setFilters");
    }

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("AccountNumber", SqlDbType.NVarChar, characters: 20, ordinal: 2)
    ], childForeignKeys:
    [
        new ChildForeignKeyModel
        {
            ConstraintName = "FK_CustomerItem_Customer",
            ReferencingSchema = "dbo",
            ReferencingTable = "CustomerItem",
            ReferencingColumns = ["CustomerId"],
            ReferencedColumns = ["CustomerId"],
            ReferencingPrimaryKeyColumns = ["CustomerItemId"],
            ReferencingTableColumns =
            [
                Sample.Column("CustomerItemId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2),
                Sample.Column("Quantity", SqlDbType.Int, ordinal: 3)
            ],
            ReferencingTableForeignKeys = [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "AccountNumber")]
        }
    ]);

    [TestMethod]
    public async Task A_master_detail_page_reads_a_child_served_as_OData_by_the_parent_key_and_goes_back_to_the_OData_page()
    {
        string page = await Run("TSX_ODataDetailMasterPage_v1.tt", Customer(), On);

        Expect.Contains(page, "@@@FILE pages/CustomerODataDetailMasterPage.tsx@@@");
        Expect.Contains(page, "export function CustomerODataDetailMasterPage() {");
        Expect.Contains(page, "customerODataApi.queryPage({");
        Expect.Contains(page, "import { readOData, problemText } from '../api/odataClient';");
        Expect.Contains(page, "import { odataLiteral, queryString } from '../api/odataQuery';");
        Expect.Contains(page, "readOData<any>('/odata/customeritems' + queryString({ $filter: `customerId eq ${odataLiteral(parentId, 'number')}`, $orderby: 'customerItemId' }))");
        Expect.Contains(page, "customer-odata?edit=");   // a child page sends the person back to the OData page it came from

        string replaced = await Run("TSX_ODataDetailMasterPage_v1.tt", Customer(), On, ("ODataScreens", "Replace"));
        Expect.Contains(replaced, "/customer?edit=");
        Expect.DoesNotContain(replaced, "customer-odata?edit=");

        string unserved = await Run("TSX_ODataDetailMasterPage_v1.tt", Customer(), On, ("ODataTables", "Customer"));
        Expect.DoesNotContain(unserved, "readOData<any>");
        Expect.Contains(unserved, "request<any[]>('/api/customeritems')");

        string rest = await Run("TSX_DetailMasterPage_v1.tt", Customer());
        Expect.Contains(rest, "export function CustomerDetailMasterPage() {");
        Expect.Contains(rest, "if (err instanceof ApiError && err.status === 400) {");
        Expect.DoesNotContain(rest, "OData");
    }

    [TestMethod]
    public async Task The_routes_list_the_OData_pages_as_lazy_entries_after_the_REST_ones_and_Replace_puts_them_in_place()
    {
        var database = Database(Sample.Holiday(), Sample.NaturalKey());
        string added = await Run("TSX_Screens_v1.tt", database, On);

        Expect.Contains(added, "import { Suspense, lazy } from 'react';");
        Expect.Contains(added, "const HolidayODataPage = lazy(() => import('./pages/HolidayODataPage').then((m) => ({ default: m.HolidayODataPage })));");
        Expect.Contains(added, "{ path: 'holiday', label: 'Holiday', element: <HolidayPage /> },");
        Expect.Contains(added, "{ path: 'holiday-odata', label: 'Holiday (OData)', element: <Suspense fallback={null}><HolidayODataPage /></Suspense> },");
        Assert.IsLessThan(added.IndexOf("label: 'Holiday (OData)'", StringComparison.Ordinal), added.IndexOf("label: 'Holiday'", StringComparison.Ordinal), "the REST page comes first");

        string replaced = await Run("TSX_Screens_v1.tt", database, On, ("ODataScreens", "Replace"));
        Expect.Contains(replaced, "{ path: 'holiday', label: 'Holiday', element: <Suspense fallback={null}><HolidayODataPage /></Suspense> },");
        Expect.DoesNotContain(replaced, "import { HolidayPage }");
        Expect.DoesNotContain(replaced, "(OData)");

        string without = await Run("TSX_Screens_v1.tt", database);
        Expect.DoesNotContain(without, "OData");
        Expect.DoesNotContain(without, "Suspense");
    }

    [TestMethod]
    public async Task The_dev_proxy_and_the_container_forward_odata_only_with_the_flag()
    {
        string build = await Run("TSX_EssentialBuild_v1.tt", On);
        Expect.Contains(build, "'/odata': 'http://localhost:");
        Expect.DoesNotContain(await Run("TSX_EssentialBuild_v1.tt"), "'/odata'");

        string docker = await Run("TSX_EssentialDocker_v1.tt", On);
        Expect.Contains(docker, "location /odata/ {");
        Expect.DoesNotContain(await Run("TSX_EssentialDocker_v1.tt"), "/odata/");
    }

    [TestMethod]
    public void A_table_with_child_lists_edited_in_its_form_is_refused_without_the_columns_of_the_children()
    {
        var parent = Sample.TimeSheetWithEmployeeAndDetail();
        var project = Project(On, ("EditableChildren", "TimeSheet.TimeSheetDetail"));

        Assert.IsTrue(EditableChild.Lists(parent, project));
        Assert.IsFalse(EditableChild.Lists(parent, Project(On)));
        Assert.IsNotNull(ODataPlan.RefusalOf(parent, project));
    }
}
