using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The one search box of ListProtocol=Search: which columns it matches, the API that answers it (CrudApi and the table's class) and the React api module and page that ask it. Run live against a
/// scratch SQLite database: paging from 0, a search that finds a row by its parent's name, a sort by a foreign key that orders by the parent's name, a bad sort name, a negative page. </summary>
[TestClass]
public class SearchBoxTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static readonly (string, string) Search = ("ListProtocol", "Search");

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("StatusId", SqlDbType.Int, ordinal: 2),
        Sample.Column("AccountNumber", SqlDbType.VarChar, characters: 20, ordinal: 3),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 4),
        Sample.Column("Email", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 5),
        Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 6),
        Sample.Column("Notes", SqlDbType.VarChar, characters: 1000, nullable: true, ordinal: 7)
    ],
    [Sample.ForeignKey("StatusId", "Status", "StatusId", "Description")]);

    private static List<SearchBoxTerm> Terms(ProjectSettings project)
    {
        var table = Customer();
        return SearchBox.For(table, project, EntityNavigations.Of(table, fk => false));
    }

    private static async Task<string> Render(string template, TableModel? table, ProjectSettings project, string file)
    {
        var result = table is null
            ? await Repo.Cache.RunAsync(Repo.Template(template), project)
            : await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        if (!result.GeneratedText!.Contains("@@@FILE"))
            return result.GeneratedText.ReplaceLineEndings("\n");
        return GeneratedFiles.Split(result.GeneratedText!).First(f => Path.GetFileName(f.RelativePath) == file).Content.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public void The_box_matches_the_text_columns_and_the_parents_display_column_unless_the_project_lists_them()
    {
        var conditions = Terms(Project(Search)).Select(t => t.Condition("t", "text")).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "t.AccountNumber.ToLower().Contains(text)",
            "t.Name.ToLower().Contains(text)",
            "t.Email != null && t.Email.ToLower().Contains(text)",
            "t.Status != null && t.Status.Description.ToLower().Contains(text)"
        }, conditions, "long text (Notes) and audit columns are left out, a nullable column and a parent are guarded");

        var listed = Terms(Project(Search, ("SearchColumns", "Customer=Name, Customer=Status.Description,Customer=Notes,Customer=Nope.Name,Customer=Missing,Other=Email")))
            .Select(t => t.Condition("t", "text")).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "t.Name.ToLower().Contains(text)",
            "t.Status != null && t.Status.Description != null && t.Status.Description.ToLower().Contains(text)",
            "t.Notes != null && t.Notes.ToLower().Contains(text)"
        }, listed, "the project's list wins; a column or parent that is not there is skipped");
    }

    [TestMethod]
    public void A_list_of_search_columns_is_read_per_table()
    {
        var project = Project(("SearchColumns", "Customer=Name,customer=Status.Description,Bad=,=Bad,Holiday=Name"));

        CollectionAssert.AreEqual(new[] { "Name", "Status.Description" }, project.SearchColumnsOf("Customer")!);
        CollectionAssert.AreEqual(new[] { "Name" }, project.SearchColumnsOf("Holiday")!);
        Assert.IsNull(project.SearchColumnsOf("Other"));
        Assert.IsNull(project.SearchColumnsOf("Bad"));
    }

    [TestMethod]
    public async Task The_table_class_says_what_is_searched_and_what_can_be_sorted_only_in_the_Search_protocol()
    {
        string api = await Render("API_Crud_v1.tt", Customer(), Project(Search), "CustomerApi.cs");

        Expect.Contains(api, "protected override IQueryable<Customer> SearchRows(IQueryable<Customer> rows, string text) =>");
        Expect.Contains(api, "rows.Where(t => (t.AccountNumber.ToLower().Contains(text))");
        Expect.Contains(api, "|| (t.Status != null && t.Status.Description.ToLower().Contains(text)));");
        Expect.Contains(api, "protected override IOrderedQueryable<Customer>? SortRows(IQueryable<Customer> rows, string column, bool descending)");
        Expect.Contains(api, "case \"statusid\":");
        Expect.Contains(api, "rows.OrderByDescending(t => t.Status!.Description) : rows.OrderBy(t => t.Status!.Description)");
        Expect.DoesNotContain(api, "case \"notes\":");

        string plain = await Render("API_Crud_v1.tt", Customer(), Project(), "CustomerApi.cs");
        Expect.DoesNotContain(plain, "SearchRows");
        Expect.DoesNotContain(plain, "SortRows");
    }

    [TestMethod]
    public async Task The_list_route_takes_the_protocols_names_and_answers_the_members_it_is_told()
    {
        string api = await Render("API_EssentialCrudApi_v1.tt", null, Project(Search), "CrudApi.cs");

        Expect.Contains(api, "[FromQuery(Name = \"pageIndex\")] int? pageNumber, [FromQuery(Name = \"pageSize\")] int? pageSize,");
        Expect.Contains(api, "[FromQuery(Name = \"sort\")] string? sort, [FromQuery(Name = \"search\")] string? search) => GetAllAsync(context, pageNumber, pageSize, sort, search))");
        Expect.Contains(api, "rows = SearchRows(rows, search.Trim().ToLower());");
        Expect.Contains(api, "int number = pageNumber ?? 0;");
        Expect.Contains(api, "new Dictionary<string, object?> { [\"data\"] = page, [\"count\"] = totalCount }");
        Expect.Contains(api, "protected virtual IQueryable<TEntity> SearchRows(");
        Expect.Contains(api, "return SortRows(rows, column, descending)?.ThenBy(Key);");
        Expect.DoesNotContain(api, "CrudPage<TEntity>>()");

        string named = await Render("API_EssentialCrudApi_v1.tt", null,
            Project(Search, ("PageBase", "1"), ("PageSizeParameter", "take"), ("SortParameter", "orderBy"), ("SearchParameter", "q"), ("ItemsMember", "rows"), ("TotalMember", "total")), "CrudApi.cs");
        Expect.Contains(named, "[FromQuery(Name = \"pageNumber\")] int? pageNumber, [FromQuery(Name = \"take\")] int? pageSize,");
        Expect.Contains(named, "[FromQuery(Name = \"orderBy\")] string? sort, [FromQuery(Name = \"q\")] string? search)");
        Expect.Contains(named, "int number = pageNumber ?? 1;");
        Expect.Contains(named, "(number - 1) * size");
        Expect.Contains(named, "[\"rows\"] = page, [\"total\"] = totalCount");

        string columns = await Render("API_EssentialCrudApi_v1.tt", null, Project(), "CrudApi.cs");
        Expect.Contains(columns, "[FromQuery] int? pageNumber, [FromQuery] int? pageSize) => GetAllAsync(context, pageNumber, pageSize))");
        Expect.Contains(columns, "CrudPage<TEntity>>()");
        Expect.DoesNotContain(columns, "SearchRows");
        Expect.DoesNotContain(columns, "SortedRows");
    }

    [TestMethod]
    public async Task The_react_api_module_and_page_ask_the_list_route_and_have_one_search_box()
    {
        var table = Sample.Holiday();
        string api = await Render("TSX_Api_v1.tt", table, Project(Search), "holidayApi.ts");
        Expect.Contains(api, "getPage: (pageNumber: number, pageSize: number, filters: { search?: string } = {}, sort:");
        Expect.Contains(api, "params.set('pageIndex', String(pageNumber - 1));");
        Expect.Contains(api, "if (filters.search) params.set('search', filters.search);");
        Expect.Contains(api, "params.set('sort', sort.column + (sort.descending ? ':desc' : ':asc'));");
        Expect.Contains(api, "request<Record<string, unknown>>(`${apiUrl}?${params.toString()}`)");
        Expect.Contains(api, "body['data']");
        Expect.Contains(api, "body['count']");
        Expect.DoesNotContain(api, "/search?");

        string based = await Render("TSX_Api_v1.tt", table, Project(Search, ("PageBase", "1"), ("PageParameter", "p"), ("ItemsMember", "rows")), "holidayApi.ts");
        Expect.Contains(based, "params.set('p', String(pageNumber));");
        Expect.Contains(based, "body['rows']");

        string columns = await Render("TSX_Api_v1.tt", table, Project(), "holidayApi.ts");
        Expect.Contains(columns, "params.set('pageNumber', String(pageNumber));");
        Expect.Contains(columns, "`${apiUrl}/search?${params.toString()}`");

        string page = await Render("TSX_Page_v1.tt", table, Project(Search), "HolidayPage.tsx");
        Expect.Contains(page, "useState({ search: '' })");
        Expect.Contains(page, "placeholder=\"Search\"");
        Expect.Contains(page, "value={filters.search}");
        Expect.DoesNotContain(page, "Search by");

        string byColumn = await Render("TSX_Page_v1.tt", table, Project(), "HolidayPage.tsx");
        Expect.Contains(byColumn, "placeholder=\"Search by Name\"");
    }
}
