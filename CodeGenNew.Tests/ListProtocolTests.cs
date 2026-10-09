using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The list protocol the Angular client speaks (ListProtocol=Columns or Search), the names it can be told, and the default sort of a screen. </summary>
[TestClass]
public class ListProtocolTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<Dictionary<string, string>> Run(string template, TableModel? table, ProjectSettings project)
    {
        var result = table is null
            ? await Repo.Cache.RunAsync(Repo.Template(template), project)
            : await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    private static readonly (string, string) Search = ("ListProtocol", "Search");

    [TestMethod]
    public void The_settings_default_to_the_common_names_and_a_zero_based_page()
    {
        var none = Project();
        Assert.IsFalse(none.ListSearchProtocol);
        var search = Project(Search);
        Assert.IsTrue(search.ListSearchProtocol);
        Assert.AreEqual("pageIndex", search.PageParameter);
        Assert.AreEqual("pageSize", search.PageSizeParameter);
        Assert.AreEqual("sort", search.SortParameter);
        Assert.AreEqual("search", search.SearchParameter);
        Assert.AreEqual("data", search.ItemsMember);
        Assert.AreEqual("count", search.TotalMember);
        Assert.AreEqual(0, search.PageBase);
        Assert.AreEqual("pageNumber", Project(Search, ("PageBase", "1")).PageParameter);
        Assert.AreEqual("p", Project(Search, ("PageParameter", "p")).PageParameter);
    }

    [TestMethod]
    public void A_default_sort_is_a_column_and_an_optional_direction_per_table()
    {
        var project = Project(("DefaultSorts", "TimeSheet=WhenEntered:desc, Holiday=Name,Bad=,=Bad"));

        Assert.AreEqual(("WhenEntered", true), project.DefaultSort("TimeSheet"));
        Assert.AreEqual(("Name", false), project.DefaultSort("holiday"));
        Assert.IsNull(project.DefaultSort("Other"));
        Assert.IsNull(project.DefaultSort("Bad"));
    }

    [TestMethod]
    public async Task Columns_is_what_the_service_base_has_always_written()
    {
        string service = (await Run("TS_EssentialCrud_v1.tt", null, Project()))["crud.service.ts"];

        Expect.Contains(service, "`${this.apiUrl}/search`");
        Expect.Contains(service, "new HttpParams().set('pageNumber', query.pageNumber).set('pageSize', query.pageSize)");
        Expect.DoesNotContain(service, "map(");
    }

    [TestMethod]
    public async Task Search_asks_the_list_route_with_a_page_index_one_search_text_and_a_combined_sort_and_reads_the_answers_members()
    {
        string service = (await Run("TS_EssentialCrud_v1.tt", null, Project(Search)))["crud.service.ts"];

        Expect.Contains(service, "import { Observable, map } from 'rxjs';");
        Expect.Contains(service, "new HttpParams().set('pageIndex', query.pageNumber - 1).set('pageSize', query.pageSize)");
        Expect.Contains(service, "const text = (query.filters?.['search'] ?? '').trim();");
        Expect.Contains(service, "params.set('search', text)");
        Expect.Contains(service, "params.set('sort', query.sortBy + (query.sortDescending ? ':desc' : ':asc'))");
        Expect.Contains(service, "this.http.get<Record<string, unknown>>(this.apiUrl, { params })");
        Expect.Contains(service, "body['data']");
        Expect.Contains(service, "body['count']");
        Expect.DoesNotContain(service, "/search`");
    }

    [TestMethod]
    public async Task The_names_of_the_parameters_the_page_base_and_the_answers_members_are_settings()
    {
        string service = (await Run("TS_EssentialCrud_v1.tt", null, Project(Search, ("PageBase", "1"), ("PageSizeParameter", "take"), ("SortParameter", "orderBy"),
            ("SearchParameter", "q"), ("ItemsMember", "rows"), ("TotalMember", "total"))))["crud.service.ts"];

        Expect.Contains(service, "new HttpParams().set('pageNumber', query.pageNumber).set('take', query.pageSize)");
        Expect.Contains(service, "params.set('q', text)");
        Expect.Contains(service, "params.set('orderBy', query.sortBy");
        Expect.Contains(service, "body['rows']");
        Expect.Contains(service, "body['total']");
    }

    [TestMethod]
    public async Task The_specs_of_the_bases_follow_the_protocol()
    {
        var plain = await Run("TS_EssentialCrud_v1.tt", null, Project());
        Expect.Contains(plain["crud.service.spec.ts"], "const LIST_PATH = '/search';");
        Expect.Contains(plain["crud.service.spec.ts"], "const PAGE_BASE = 1;");
        Expect.Contains(plain["crud-screen.spec.ts"], "const LIST_URL = 'api/items/search';");

        var search = await Run("TS_EssentialCrud_v1.tt", null, Project(Search));
        Expect.Contains(search["crud.service.spec.ts"], "const LIST_PATH = '';");
        Expect.Contains(search["crud.service.spec.ts"], "const PAGE_BASE = 0;");
        Expect.Contains(search["crud.service.spec.ts"], "const FILTER_PARAM = 'search';");
        Expect.Contains(search["crud.service.spec.ts"], "reads the rows and the total from the members the API answers with");
        Expect.Contains(search["crud-screen.spec.ts"], "({ 'data': items, 'count': totalCount });");
        Expect.DoesNotContain(plain["crud.service.spec.ts"], "reads the rows and the total from the members");
    }

    [TestMethod]
    public async Task The_paged_screen_starts_with_its_default_sort()
    {
        string screen = (await Run("TS_EssentialCrud_v1.tt", null, Project()))["crud-screen.ts"];

        Expect.Contains(screen, "protected readonly defaultSort: GridSort | null = null;");
        Expect.Contains(screen, "this.sort = loadSort(this.gridKey, this.sortableColumns) ?? this.defaultSort;");

        var holiday = (await Run("TS_Component_v1.tt", Sample.Holiday(), Project(("DefaultSorts", "Holiday=Name:desc"))))["holiday.component.ts"];
        Expect.Contains(holiday, "protected override readonly defaultSort = { column: 'Name', descending: true };");
        Expect.DoesNotContain((await Run("TS_Component_v1.tt", Sample.Holiday(), Project()))["holiday.component.ts"], "defaultSort");
    }

    [TestMethod]
    public async Task With_the_Search_protocol_the_service_and_the_screen_have_one_search_box()
    {
        var project = Project(Search);
        string service = (await Run("TS_Service_v1.tt", Sample.Holiday(), project))["holiday.service.ts"];
        Expect.Contains(service, "getPage(pageNumber: number, pageSize: number, search?: string, sortBy?: string, sortDescending?: boolean)");
        Expect.Contains(service, "filters: { search }");

        var files = await Run("TS_Component_v1.tt", Sample.Holiday(), project);
        Expect.Contains(files["holiday.component.ts"], "override filters = { search: '' };");
        Expect.Contains(files["holiday.component.html"], "placeholder=\"Search\" [(ngModel)]=\"filters.search\"");
        Expect.DoesNotContain(files["holiday.component.html"], "Search by");
        Expect.Contains(files["holiday.component.spec.ts"], "r.url === 'api/holidays' && r.params.get('pageIndex') === '0'");
        Expect.Contains(files["holiday.component.spec.ts"], "{ 'data': [], 'count': 25 }");

        var master = await Run("TS_DetailMasterComponent_v1.tt", Sample.TimeSheetWithEmployeeAndDetail(), project);
        Expect.Contains(master.Single(f => f.Key.EndsWith(".component.ts")).Value, "override filters = { search: '' };");
    }

    [TestMethod]
    public async Task With_the_Columns_protocol_one_box_per_searchable_column_stays()
    {
        var files = await Run("TS_Component_v1.tt", Sample.Holiday(), Project());
        Expect.Contains(files["holiday.component.ts"], "override filters = { sY_IsoCountry_Alpha3Code: '', name: '' };");
        Expect.Contains(files["holiday.component.html"], "placeholder=\"Search by Name\"");
        Expect.Contains(files["holiday.component.spec.ts"], "r.url.endsWith('/search')");
    }
}
