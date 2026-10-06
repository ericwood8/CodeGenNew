using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> Click a column header to sort a grid: the routine's own safe ORDER BY, the call that passes the sort, and the three screens. </summary>
[TestClass]
public class GridSortTests
{
    // An invoice line: a parent (Product) with a display column, a plain column, a date, a long note and a name that holds a quote.
    // oddName adds a column whose name holds a quote, to prove the SQL cannot be broken out of; no real screen has one (it would break the TypeScript and C#).
    private static TableModel Line(SqlDialect dialect = SqlDialect.SqlServer, bool oddName = false)
    {
        var columns = new List<ColumnModel>
        {
            Sample.Column("InvoiceLineId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("ProductId", SqlDbType.Int, ordinal: 2),
            Sample.Column("Description", SqlDbType.NVarChar, characters: 40, ordinal: 3),   // 100 or more characters would be a multi-line field, left out of the grid
            Sample.Column("Quantity", SqlDbType.Int, ordinal: 4),
            Sample.Column("ShipDate", SqlDbType.Date, nullable: true, ordinal: 5),
            Sample.Column("Notes", SqlDbType.NVarChar, characters: 2000, ordinal: 6)
        };
        if (oddName)
            columns.Add(Sample.Column("Owner's Mark", SqlDbType.NVarChar, characters: 20, ordinal: 7));
        var t = Sample.Table("InvoiceLine", columns, [Sample.ForeignKey("ProductId", "Product", "ProductId", "ProductName")]);
        return new TableModel
        {
            SchemaName = dialect == SqlDialect.PostgreSql ? "public" : t.SchemaName, TableName = t.TableName, QuotedName = t.QuotedName, Dialect = dialect,
            Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys, ChildForeignKeys = t.ChildForeignKeys,
            DisplayColumns = t.DisplayColumns, HasReferencedDisplayColumns = true, HasRowData = t.HasRowData, Rows = t.Rows, LookupShape = t.LookupShape
        };
    }

    private static async Task<string> Render(string template, TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------ which columns, and the SQL

    [TestMethod]
    public void Every_column_but_long_text_can_be_sorted_and_a_foreign_key_sorts_by_the_parents_name()
    {
        var entries = SearchSort.Entries(Line(oddName: true));

        CollectionAssert.AreEqual(new[] { "InvoiceLineId", "ProductId", "Description", "Quantity", "ShipDate", "Owner's Mark" }, entries.Select(e => e.Name).ToArray());
        Assert.IsNotNull(entries.Single(e => e.Name == "ProductId").Parent);
        Assert.IsNull(entries.Single(e => e.Name == "Quantity").Parent);
    }

    [TestMethod]
    public async Task SQL_Server_compares_the_name_with_a_fixed_list_and_keeps_the_default_order_as_the_tie_breaker()
    {
        string sql = await Render("SP_Search_v1.tt", Line(oddName: true));

        Expect.Contains(sql, "@SortColumn NVARCHAR(128) = NULL,\n@SortDescending BIT = 0");
        Expect.Contains(sql, "CASE WHEN @SortColumn = N'Description' AND @SortDescending = 0 THEN t.[Description] END ASC,");
        Expect.Contains(sql, "CASE WHEN @SortColumn = N'Description' AND @SortDescending = 1 THEN t.[Description] END DESC,");
        Expect.Contains(sql, "(SELECT TOP 1 p.[ProductName] FROM [dbo].[Product] AS p WHERE p.[ProductId] = t.[ProductId])");
        Expect.DoesNotContain(sql, "N'Notes'");                 // long text is never sorted
        Expect.Contains(sql, "N'Owner''s Mark'");                // a quote in a name cannot end the literal
        Expect.DoesNotContain(sql, "EXEC(");                    // never dynamic SQL: the caller's text is only compared
        Assert.IsGreaterThan(sql.IndexOf("N'ShipDate'"), sql.LastIndexOf("[InvoiceLineId] ASC"), "the default order comes after the sort");
    }

    [TestMethod]
    public async Task PostgreSQL_drops_the_earlier_signature_and_compares_in_lower_case()
    {
        string sql = await Render("SP_Search_v1.tt", Line(SqlDialect.PostgreSql));

        Expect.Contains(sql, "DROP FUNCTION IF EXISTS \"public\".\"InvoiceLine_Search\"(nvarchar(40), integer, integer);");
        Expect.Contains(sql, "\"SortColumn\" text DEFAULT NULL,\n\"SortDescending\" boolean DEFAULT false");
        Expect.Contains(sql, "CASE WHEN lower(\"SortColumn\") = 'description' AND NOT \"SortDescending\" THEN t.\"Description\" END ASC,");
        Expect.Contains(sql, "(SELECT p.\"ProductName\" FROM \"dbo\".\"Product\" AS p WHERE p.\"ProductId\" = t.\"ProductId\" LIMIT 1)");
        Expect.Contains(sql, "SELECT t.* FROM \"public\".\"InvoiceLine\" AS t");
    }

    [TestMethod]
    public async Task MySQL_takes_the_sort_as_two_more_parameters()
    {
        string sql = await Render("SP_Search_v1.tt", Line(SqlDialect.MySql));

        Expect.Contains(sql, "IN `SortColumn` varchar(128)");
        Expect.Contains(sql, "IN `SortDescending` tinyint(1)");
        Expect.Contains(sql, "CASE WHEN `SortColumn` = 'Description' AND `SortDescending` = 0 THEN t.`Description` END ASC,");
        Expect.Contains(sql, "FROM `InvoiceLine` AS t");
    }

    // ------------------------------------------------------------------ the call

    [TestMethod]
    [DataRow(SqlDialect.SqlServer, "EXEC [dbo].[InvoiceLine_Search] @pDescription, @PageNumber, @PageSize, @SortColumn, @SortDescending")]
    [DataRow(SqlDialect.PostgreSql, "SELECT * FROM \\\"public\\\".\\\"InvoiceLine_Search\\\"(@pDescription, @PageNumber, @PageSize, @SortColumn, @SortDescending)")]
    [DataRow(SqlDialect.MySql, "CALL `InvoiceLine_Search`(@pDescription, @PageNumber, @PageSize, @SortColumn, @SortDescending)")]
    public async Task The_repository_and_the_api_pass_the_sort(SqlDialect dialect, string call)
    {
        string repo = await Render("CS_Repo_v1.tt", Line(dialect));
        string api = await Render("API_Search_v1.tt", Line(dialect));

        Expect.Contains(repo, call);
        Expect.Contains(repo, "string? sortColumn = null, bool sortDescending = false");
        Expect.Contains(api, call);
        Expect.Contains(api, "[FromQuery] string? sortBy = null,");
        Expect.Contains(api, "bool sortDescending = string.Equals(sortDir, \"desc\", StringComparison.OrdinalIgnoreCase);");
        Expect.Contains(api, "sortBy.Length > 128 ? null : sortBy.Trim()");
    }

    // ------------------------------------------------------------------ the front ends

    [TestMethod]
    public async Task The_web_services_send_the_sort_only_when_there_is_one()
    {
        string tsx = await Render("TSX_Api_v1.tt", Line());
        string ts = await Render("TS_Service_v1.tt", Line());

        Expect.Contains(tsx, "sort: { column: string; descending: boolean } | null = null");
        Expect.Contains(tsx, "params.set('sortBy', sort.column);");
        Expect.Contains(tsx, "params.set('sortDir', sort.descending ? 'desc' : 'asc');");
        Expect.Contains(ts, "sortBy?: string, sortDescending?: boolean");
        Expect.Contains(ts, "params = params.set('sortBy', sortBy).set('sortDir', sortDescending ? 'desc' : 'asc');");
    }

    [TestMethod]
    [DataRow("TSX_Page_v1.tt")]
    [DataRow("TSX_DetailMasterPage_v1.tt")]
    public async Task A_React_grid_sorts_from_its_headers_and_keeps_the_sort_when_paging(string template)
    {
        var table = Line();
        string tsx = await Render(template, template.Contains("DetailMaster") ? WithChild(table) : table);

        Expect.Contains(tsx, "import { GridMenu, SortHeader, loadSort, nextSort, saveSort");
        Expect.Contains(tsx, "const gridKey = 'invoiceline';");
        Expect.Contains(tsx, "const sortableColumns = [");
        Expect.Contains(tsx, "<SortHeader label=\"Description\" column=\"Description\" sort={sort} onSort={sortBy} />");
        Expect.DoesNotContain(tsx, "column=\"Notes\"");
        Expect.Contains(tsx, "const [sort, setSort] = useState<GridSort | null>(() => loadSort(gridKey, sortableColumns));");
        Expect.Contains(tsx, "sortValue: GridSort | null = sort)");   // paging and searching read the current sort
        Expect.Contains(tsx, "load(1, filters, next);");             // a click goes back to page 1 with the new sort
        Expect.Contains(tsx, "load(1, filters, null);");             // Clear sort
        Expect.Contains(tsx, "<GridMenu sort={sort} onClear={clearSort}>");
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt")]
    [DataRow("TS_DetailMasterComponent_v1.tt")]
    public async Task An_Angular_grid_sorts_from_its_headers_and_clears_from_a_right_click_menu(string template)
    {
        var table = Line();
        string files = await Render(template, template.Contains("DetailMaster") ? WithChild(table) : table);

        Expect.Contains(files, "import { GridSort, loadSort, nextSort, saveSort");
        Expect.Contains(files, "<table (contextmenu)=\"openSortMenu($event)\">");
        Expect.Contains(files, "(click)=\"sortBy('Description')\">Description{{ sortMark('Description') }}</button>");
        Expect.DoesNotContain(files, "sortBy('Notes')");
        Expect.Contains(files, "this.sort?.column, this.sort?.descending)");
        Expect.Contains(files, "@HostListener('document:click')");
        Expect.Contains(files, "(click)=\"clearSort()\">Clear sort</button>");
        Expect.Contains(files, ".sort-menu {");                      // the component's own style sheet
    }

    [TestMethod]
    public async Task The_WinUI3_grid_has_header_buttons_a_clear_menu_and_a_saved_sort()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_MasterScreen_v1.tt", Line()));
        string xaml = files.Single(f => f.RelativePath.EndsWith("ListPage.xaml")).Content;
        string page = files.Single(f => f.RelativePath.EndsWith("ListPage.xaml.cs")).Content;
        string vm = files.Single(f => f.RelativePath.EndsWith("ListViewModel.cs")).Content;
        string store = files.Single(f => f.RelativePath.EndsWith("GridSortStore.cs")).Content;

        Expect.Contains(xaml, "<Button x:Name=\"SortHeaderDescription\"");
        Expect.Contains(xaml, "Click=\"OnSortHeaderClick\"");
        Expect.Contains(xaml, "RightTapped=\"OnGridRightTapped\"");
        Expect.DoesNotContain(xaml, "SortHeaderNotes");
        Expect.Contains(page, "new MenuFlyoutItem { Text = \"Clear sort\", IsEnabled = ViewModel.HasSort }");
        Expect.Contains(page, "SortHeaderDescription.Content = \"Description\" + ViewModel.SortMark(\"Description\");");
        Expect.Contains(vm, "sortColumn: _sortColumn, sortDescending: _sortDescending);");
        Expect.Contains(vm, "GridSortStore.Save(GridKey, null);");
        Expect.Contains(store, "\"GridSorts.json\"");
        Expect.Contains(store, "File.Delete(FilePath);");       // clearing the last sort removes the file
    }

    [TestMethod]
    public async Task The_shared_support_files_are_written_by_their_own_templates()
    {
        var db = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Line()] };
        var react = await Repo.Cache.RunAsync(Repo.Template("TSX_GridSort_v1.tt"), db);
        var angular = await Repo.Cache.RunAsync(Repo.Template("TS_GridSort_v1.tt"), db);
        Assert.IsTrue(react.Success && angular.Success, string.Join(" | ", react.Errors.Concat(angular.Errors)));

        var reactFile = GeneratedFiles.Split(react.GeneratedText!).Single();
        var angularFile = GeneratedFiles.Split(angular.GeneratedText!).Single();
        Assert.AreEqual("components/gridSort.tsx", reactFile.RelativePath);
        Assert.AreEqual("grid-sort.ts", angularFile.RelativePath);
        foreach (string text in new[] { reactFile.Content, angularFile.Content })
        {
            Expect.Contains(text, "export function loadSort(");
            Expect.Contains(text, "export function nextSort(");
            Expect.Contains(text, "localStorage.removeItem(");       // clearing forgets the saved sort
            Expect.Contains(text, "JSON.stringify([sort])");         // a list, so several columns can be added later
        }
        Expect.Contains(reactFile.Content, "export function SortHeader(");
        Expect.Contains(reactFile.Content, "Clear sort");
    }

    // ------------------------------------------------------------------ the child grids in a master dialog

    [TestMethod]
    public async Task A_React_child_grid_sorts_its_rows_in_the_browser_and_remembers_its_own_sort()
    {
        string tsx = await Render("TSX_DetailMasterPage_v1.tt", WithChild(Line()));

        Expect.Contains(tsx, "loadSort(gridKey + '.invoiceLineNote')");                  // its own saved sort, apart from the main grid's
        Expect.Contains(tsx, "const sortInvoiceLineNoteBy = (column: string) => {");
        Expect.Contains(tsx, "saveSort(gridKey + '.invoiceLineNote', next);");
        Expect.Contains(tsx, "<GridMenu sort={invoiceLineNoteSort} onClear={clearInvoiceLineNoteSort}>");
        Expect.Contains(tsx, "<SortHeader key={col} label={caption(col)} column={col} sort={invoiceLineNoteSort} onSort={sortInvoiceLineNoteBy} />");
        Expect.Contains(tsx, "sortRows(invoiceLineNoteRows, invoiceLineNoteSort).map((row, index) => (");
    }

    [TestMethod]
    public async Task An_Angular_child_grid_sorts_its_rows_in_the_browser_and_remembers_its_own_sort()
    {
        string files = await Render("TS_DetailMasterComponent_v1.tt", WithChild(Line()));

        Expect.Contains(files, "invoiceLineNote: loadSort(this.gridKey + '.invoiceLineNote')");
        Expect.Contains(files, "<table (contextmenu)=\"openChildSortMenu($event, 'invoiceLineNote')\">");
        Expect.Contains(files, "(click)=\"sortChildBy('invoiceLineNote', col)\">{{caption(col)}}{{ childSortMark('invoiceLineNote', col) }}</button>");
        Expect.Contains(files, "*ngFor=\"let row of sortedChildRows('invoiceLineNote', invoiceLineNoteRows)\"");
        Expect.Contains(files, "(click)=\"clearChildSort('invoiceLineNote')\">Clear sort</button>");
        Expect.Contains(files, "return sortRows(rows, sort, sort ? names?.[sort.column] : undefined);");
    }

    [TestMethod]
    public async Task The_WinUI3_child_grid_sorts_its_rows_by_their_values_and_keeps_its_own_saved_sort()
    {
        var files = GeneratedFiles.Split(await Render("WinUI3_DetailMasterScreen_v1.tt", WithChild(Line())));
        string xaml = files.Single(f => f.RelativePath.EndsWith("DetailMasterDialog.xaml")).Content;
        string page = files.Single(f => f.RelativePath.EndsWith("DetailMasterDialog.xaml.cs")).Content;
        string vm = files.Single(f => f.RelativePath.EndsWith("DetailMasterViewModel.cs")).Content;

        Expect.Contains(xaml, "Click=\"OnInvoiceLineNoteHeaderClick\"");
        Expect.Contains(xaml, "RightTapped=\"OnInvoiceLineNoteGridRightTapped\"");
        Expect.Contains(page, "ViewModel.SortInvoiceLineNoteBy((string)((Button)sender).Tag)");
        Expect.Contains(page, "new MenuFlyoutItem { Text = \"Clear sort\", IsEnabled = ViewModel.HasInvoiceLineNoteSort }");
        Expect.Contains(vm, "GridSortStore.Save(GridKeyFor(\"invoiceLineNote\"), new GridSortEntry(column,");
        Expect.Contains(vm, "GridSortStore.Load(GridKeyFor(\"invoiceLineNote\"), propertyNames)");
        Expect.Contains(vm, "OrderByDescending(r => r.Values[sortIndex], SortValueComparer.Instance)");   // real values, not the text on screen
        Expect.Contains(vm, "public List<object?> Values { get; init; } = [];");
    }

    private static TableModel WithChild(TableModel t) => new()
    {
        SchemaName = t.SchemaName, TableName = t.TableName, QuotedName = t.QuotedName, Dialect = t.Dialect,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys,
        ChildForeignKeys = [Sample.ChildForeignKey("InvoiceLineNote", "InvoiceLineId", "InvoiceLineId", childOwnPrimaryKey: ["InvoiceLineNoteId"])],
        DisplayColumns = t.DisplayColumns, HasReferencedDisplayColumns = true, LookupShape = t.LookupShape
    };
}
