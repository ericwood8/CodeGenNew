using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The child grids under a master's form: a drop-down for each foreign key the grid shows as a name, listing the names its rows have. The grid holds every row of the child for the row being
/// edited, so each stack filters those rows itself. </summary>
[TestClass]
public class ChildGridFilterTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Invoice() => Sample.Table("Invoice",
    [
        Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2)
    ], childForeignKeys: [Lines()]);

    private static ChildForeignKeyModel Lines(bool withNames = true) => new()
    {
        ConstraintName = "FK_InvoiceLine_Invoice",
        ReferencingSchema = "dbo",
        ReferencingTable = "InvoiceLine",
        ReferencingColumns = ["InvoiceId"],
        ReferencedColumns = ["InvoiceId"],
        ReferencingPrimaryKeyColumns = ["InvoiceLineId"],
        ReferencingTableColumns =
        [
            Sample.Column("InvoiceLineId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("InvoiceId", SqlDbType.Int, ordinal: 2),
            Sample.Column("ItemId", SqlDbType.Int, ordinal: 3),
            Sample.Column("LineStatusId", SqlDbType.Int, ordinal: 4),
            Sample.Column("Quantity", SqlDbType.Int, ordinal: 5)
        ],
        ReferencingTableForeignKeys = withNames
            ?
            [
                Sample.ForeignKey("InvoiceId", "Invoice", "InvoiceId", "Title"),
                Sample.ForeignKey("ItemId", "Item", "ItemId", "Name"),
                Sample.ForeignKey("LineStatusId", "LineStatus", "LineStatusId", "Description")
            ]
            : [Sample.ForeignKey("InvoiceId", "Invoice", "InvoiceId", "Title")]
    };

    private static async Task<Dictionary<string, string>> Run(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.ReplaceLineEndings("\n");
        return text.Contains("@@@FILE") ? GeneratedFiles.Split(text).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n")) : new() { ["page"] = text };
    }

    [TestMethod]
    public void A_child_grid_filters_by_each_name_it_shows_up_to_three()
    {
        var grid = ChildGrid.For(Invoice(), Project()).Single();

        CollectionAssert.AreEqual(new[] { "ItemId", "LineStatusId" }, grid.Filters.Select(f => f.ColumnName).ToList());
        Assert.AreEqual("Item", grid.Filters[0].Label);
    }

    [TestMethod]
    public async Task The_react_dialog_filters_the_rows_it_loaded()
    {
        string page = (await Run("TSX_DetailMasterPage_v1.tt", Invoice(), Project())).Single(f => f.Key.EndsWith("Page.tsx")).Value;

        Expect.Contains(page, "const [invoiceLineFilter, setInvoiceLineFilter] = useState<Record<string, string>>({});");
        Expect.Contains(page, "<select aria-label=\"Filter Invoice Line by Item\" value={invoiceLineFilter['itemId'] ?? ''} onChange={(event) => setInvoiceLineFilter((current) => ({ ...current, itemId: event.target.value }))}>");
        Expect.Contains(page, "<option value=\"\">All Items</option>");
        Expect.Contains(page, "{childChoices(invoiceLineRows, 'itemId', invoiceLineNames['itemId']).map((choice) => <option key={choice.value} value={choice.value}>{choice.name}</option>)}");
        Expect.Contains(page, "sortRows(filterChildRows(invoiceLineRows, invoiceLineFilter), invoiceLineSort");
        Expect.Contains(page, "function filterChildRows(rows: any[], filter: Record<string, string>): any[] {");
        Expect.Contains(page, "    setInvoiceLineFilter({});");
    }

    [TestMethod]
    public async Task The_angular_dialog_filters_the_rows_it_loaded()
    {
        var files = await Run("TS_DetailMasterComponent_v1.tt", Invoice(), Project());
        string html = files.Single(f => f.Key.EndsWith(".component.html")).Value;
        string ts = files.Single(f => f.Key.EndsWith(".component.ts")).Value;

        Expect.Contains(html, "aria-label=\"Filter Invoice Line by Item\" [(ngModel)]=\"childFilters['invoiceLine']['itemId']\"");
        Expect.Contains(html, "<option value=\"\">All Items</option>");
        Expect.Contains(html, "*ngFor=\"let choice of childChoices(invoiceLineRows, 'itemId', invoiceLineNames['itemId'])\"");
        Expect.DoesNotContain(html, "trackBy");
        Expect.Contains(ts, "childFilters: Record<string, Record<string, string>> = { invoiceLine: { itemId: '', lineStatusId: '' } };");
        Expect.Contains(ts, "const shown = rows.filter((r) => filter.every(([column, value]) => !value || String(r[column] ?? '') === value));");
        Expect.Contains(ts, "this.childFilters['invoiceLine'] = { itemId: '', lineStatusId: '' };");
    }

    [TestMethod]
    public async Task The_blazor_page_filters_the_rows_it_loaded()
    {
        string page = (await Run("BLZ_Page_v1.tt", Invoice(), Project())).Single(f => f.Key.EndsWith(".razor")).Value;

        Expect.Contains(page, "<select aria-label=\"Filter Invoice Line by Item\" @bind=\"childInvoiceLineFilterItemId\">");
        Expect.Contains(page, "<option value=\"\">All Items</option>");
        Expect.Contains(page, "@foreach (var line in childInvoiceLineRows.Where(line => (string.IsNullOrEmpty(childInvoiceLineFilterItemId) || Convert.ToString(line.ItemId) == childInvoiceLineFilterItemId)");
        Expect.Contains(page, "private string? childInvoiceLineFilterItemId;");
        Expect.Contains(page, "childInvoiceLineFilterItemId = null;");
    }

    [TestMethod]
    public async Task The_winui3_dialog_filters_the_rows_it_loaded()
    {
        var files = await Run("WinUI3_DetailMasterScreen_v1.tt", Invoice(), Project());
        string xaml = files.Single(f => f.Key.EndsWith("DetailMasterDialog.xaml")).Value;
        string vm = files.Single(f => f.Key.EndsWith("DetailMasterViewModel.cs")).Value;

        Expect.Contains(xaml, "AutomationProperties.Name=\"Filter Invoice Line by Item\"");
        Expect.Contains(xaml, "SelectedItem=\"{x:Bind ViewModel.InvoiceLineItemIdFilter, Mode=TwoWay}\"");
        Expect.Contains(vm, "public ObservableCollection<string> InvoiceLineItemIdChoices { get; } = [];");
        Expect.Contains(vm, "ordered = ordered.Where(r => r.Cells[invoiceLineItemIdChoicesIndex] == pickedItemId);");
        Expect.Contains(vm, "InvoiceLineItemIdChoices.Add(\"All Items\");");
    }

    [TestMethod]
    public async Task A_master_without_a_name_in_its_child_grid_has_no_filter()
    {
        var plain = Lines(withNames: false);
        var table = Sample.Table("Invoice",
        [
            Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2)
        ], childForeignKeys: [plain]);

        string page = (await Run("TSX_DetailMasterPage_v1.tt", table, Project())).Single(f => f.Key.EndsWith("Page.tsx")).Value;

        Expect.DoesNotContain(page, "childChoices");
        Expect.DoesNotContain(page, "filterChildRows");
    }
}
