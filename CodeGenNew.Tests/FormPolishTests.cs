using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> What a form shows: the columns the system writes (CreateDate, CreateUser) as text, a money column in a money box, a short whole number in a short box, every button's caption bold, and a child with many
/// columns as blocks of fields instead of a table of narrow boxes. Run live in the React and Angular samples of InvoiceSystem (the invoice's lines, 27 columns, as blocks; a money box showing $1,234.50). </summary>
[TestClass]
public class FormPolishTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Invoice(params ChildForeignKeyModel[] children) => Sample.Table("Invoice",
    [
        Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("MonthNumber", SqlDbType.Int, numericKind: NumericKind.Month, ordinal: 3),
        Sample.Column("Freight", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 4, currency: true),
        Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true, ordinal: 5),
        Sample.Column("CreateUser", SqlDbType.VarChar, characters: 30, createUserColumn: true, ordinal: 6)
    ], childForeignKeys: [.. children]);

    private static ChildForeignKeyModel Lines(int columns) => new()
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
            .. Enumerable.Range(1, columns).Select(i => i == 1
                ? Sample.Column("ItemPrice", SqlDbType.Decimal, precision: 18, scale: 8, ordinal: 2 + i, currency: true)
                : i == columns
                    ? Sample.Column("LineNote", SqlDbType.NVarChar, characters: 300, nullable: true, ordinal: 2 + i)
                    : Sample.Column("Field" + i, SqlDbType.NVarChar, characters: 20, ordinal: 2 + i))
        ],
        ReferencingTableForeignKeys = [Sample.ForeignKey("InvoiceId", "Invoice", "InvoiceId", "Title")]
    };

    private static async Task<Dictionary<string, string>> Run(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.ReplaceLineEndings("\n");
        return text.Contains("@@@FILE") ? GeneratedFiles.Split(text).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n")) : new() { ["page"] = text };
    }

    private static TableModel WideWithParent(int parentOrdinal) => Sample.Table("Wide",
    [
        Sample.Column("WideId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        .. Enumerable.Range(2, 22).Select(i => i == parentOrdinal
            ? Sample.Column("KindId", SqlDbType.Int, ordinal: i)
            : Sample.Column("Field" + i, SqlDbType.NVarChar, characters: 20, ordinal: i))
    ], [Sample.ForeignKey("KindId", "Kind", "KindId", "Name")]);

    [TestMethod]
    [DataRow("TSX_Page_v1.tt")]
    public async Task A_parent_name_helper_is_written_for_every_parent_the_grid_can_show(string template)
    {
        string shown = (await Run(template, WideWithParent(2), Project())).Single(f => f.Key.EndsWith("Page.tsx")).Value;
        Expect.Contains(shown, "const kindName = (id?: number): string");
        Expect.Contains(shown, "{kindName(");

        // the 23rd column is past the 18 the grid starts with, but the Columns list can show it, so its name helper is there
        string late = (await Run(template, WideWithParent(23), Project())).Single(f => f.Key.EndsWith("Page.tsx")).Value;
        Expect.Contains(late, "const kindName = (id?: number): string");
        Expect.DoesNotContain(late.Split('\n').Single(line => line.StartsWith("const initialColumns = [")), "'KindId'");

        // a parent the project hides is not a column of the screen at all
        string hidden = (await Run(template, WideWithParent(23), Project(("HiddenParents", "Kind")))).Single(f => f.Key.EndsWith("Page.tsx")).Value;
        Expect.DoesNotContain(hidden, "const kindName");
        Expect.DoesNotContain(hidden, "{kindName(");
    }

    [TestMethod]
    public async Task The_react_form_shows_the_stamp_as_text_the_money_in_a_money_box_and_a_month_in_a_short_box()
    {
        string page = (await Run("TSX_Page_v1.tt", Invoice(), Project())).Single(f => f.Key.EndsWith("Page.tsx")).Value;

        Expect.Contains(page, "{selectedRow.invoiceId && (");
        Expect.Contains(page, "<span id=\"invoiceCreateDate\">{selectedRow.createDate ? new Date(selectedRow.createDate).toLocaleString() : ''}</span>");
        Expect.Contains(page, "<span id=\"invoiceCreateUser\">{selectedRow.createUser ?? ''}</span>");
        Expect.DoesNotContain(page, "aria-label=\"Create Date\"");
        Expect.DoesNotContain(page, "aria-label=\"Create User\"");
        Expect.Contains(page, "<CurrencyInput");
        Expect.Contains(page, "import { CurrencyInput } from '../components/CurrencyInput';");
        Expect.Contains(page, "style={{ width: '7ch' }}");
        // the timestamp the API sent is not cut to a date when the row is opened
        Expect.DoesNotContain(page, "row.createDate = row.createDate.substring");
    }

    [TestMethod]
    public async Task The_angular_form_shows_the_stamp_as_text_and_the_money_in_a_money_box()
    {
        var files = await Run("TS_Component_v1.tt", Invoice(), Project(("AngularVersion", "22")));
        string html = files["invoice.component.html"];
        string ts = files["invoice.component.ts"];

        Expect.Contains(html, "<span id=\"invoiceCreateDate\">{{ selectedRow.createDate | date:'medium' }}</span>");
        Expect.Contains(html, "<span id=\"invoiceCreateUser\">{{ selectedRow.createUser }}</span>");
        Expect.DoesNotContain(html, "[(ngModel)]=\"selectedRow.createDate\"");
        Expect.Contains(html, "appCurrency currencyCode=\"USD\" [digits]=\"2\" [(ngModel)]=\"selectedRow.freight\"");
        Expect.Contains(html, "style=\"width: 7ch\"");
        Expect.Contains(ts, "import { CurrencyInputDirective } from '../currency-input';");
        Expect.Contains(ts, "imports: [ CommonModule, FormsModule, CurrencyInputDirective, MatPaginatorModule ],");
    }

    [TestMethod]
    public async Task The_blazor_form_shows_the_stamp_as_text()
    {
        string page = (await Run("BLZ_Page_v1.tt", Invoice(), Project())).Single(f => f.Key.EndsWith(".razor")).Value;
        Expect.Contains(page, "@if (!isNew) { <div class=\"field\"><label for=\"invoiceCreateDate\">Create Date</label><span id=\"invoiceCreateDate\">@row.CreateDate.ToString(\"g\")</span></div> }");
        Expect.DoesNotContain(page, "<InputDate id=\"invoiceCreateDate\"");
        Expect.Contains(page, "<InputNumber id=\"invoiceMonthNumber\" @bind-Value=\"row.MonthNumber\" min=\"@(stored is null || row.MonthNumber != stored.MonthNumber ? \"1\" : null)\" max=\"@(stored is null || row.MonthNumber != stored.MonthNumber ? \"12\" : null)\" style=\"width: 7ch\" required />");
    }

    [TestMethod]
    public async Task The_blazor_grid_shows_money_with_the_currency_symbol_and_a_tinyint_is_a_short()
    {
        var table = Sample.Table("Invoice",
        [
            Sample.Column("InvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Freight", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 2, currency: true),
            Sample.Column("Quarter", SqlDbType.TinyInt, nullable: true, ordinal: 3)
        ]);
        string page = (await Run("BLZ_Page_v1.tt", table, Project())).Single(f => f.Key.EndsWith(".razor")).Value;
        Expect.Contains(page, "<td>@Money(item.Freight, 2)</td>");
        Expect.Contains(page, "<CurrencyInput id=\"invoiceFreight\" @bind-Value=\"row.Freight\" Digits=\"2\" Symbol=\"$\" required />");
        Expect.Contains(page, "<ValidationSummary />");
        Expect.Contains(page, "private static string Money(decimal? value, int digits)");
        Expect.Contains(page, "\"$\" + Math.Abs(value.Value)");

        string model = (await Run("BLZ_Model_v1.tt", table, Project())).Single(f => f.Key.EndsWith("Invoice.cs")).Value;
        Expect.Contains(model, "public short? Quarter { get; set; }");   // InputNumber has no byte
    }

    [TestMethod]
    public async Task The_blazor_page_of_a_master_shows_a_read_only_grid_of_each_child_under_the_form()
    {
        string page = (await Run("BLZ_Page_v1.tt", Invoice(Lines(4)), Project())).Single(f => f.Key.EndsWith(".razor")).Value;
        Expect.Contains(page, "@inject InvoiceLineClient childInvoiceLineClient");
        Expect.Contains(page, "<h3>Invoice Line</h3>");
        Expect.Contains(page, "<th>Item Price</th>");
        Expect.Contains(page, "<td>@Money(line.ItemPrice, 8)</td>");
        Expect.Contains(page, "(await childInvoiceLineClient.GetAllAsync()).Where(line => line.InvoiceId == rowKey).ToList()");
        Expect.Contains(page, "if (!add) await LoadChildrenAsync();");
        Expect.Contains(page, "<button type=\"button\" @onclick=\"() => EditchildInvoiceLine(line)\">Edit</button>");
        Expect.Contains(page, "<button type=\"button\" @onclick=\"() => DeletechildInvoiceLine(line)\">Delete</button>");
        Expect.Contains(page, "await childInvoiceLineClient.DeleteAsync(line.InvoiceLineId);");
        Expect.Contains(page, "Navigation.NavigateTo(\"/invoice-line?edit=\" + Uri.EscapeDataString(Convert.ToString(line.InvoiceLineId)!) + \"&back=\" + Uri.EscapeDataString(back));");
        Expect.Contains(page, "[Parameter, SupplyParameterFromQuery(Name = \"edit\")] public string? EditId { get; set; }");
        Expect.Contains(page, "Edit(await Client.GetAsync(int.Parse(EditId)))");
        Expect.Contains(page, "if (Back is not null) Navigation.NavigateTo(Back);");
        Expect.DoesNotContain(page, "<th>Invoice Line Id</th>");   // the child's own key and the foreign key back to this row are left out

        string plain = (await Run("BLZ_Page_v1.tt", Invoice(), Project())).Single(f => f.Key.EndsWith(".razor")).Value;
        Expect.DoesNotContain(plain, "LoadChildrenAsync");
    }

    [TestMethod]
    public async Task The_winui3_dialog_shows_the_stamp_as_text_and_has_bold_buttons()
    {
        string xaml = (await Run("WinUI3_DetailScreen_v1.tt", Invoice(), Project())).Single(f => f.Key.EndsWith("DetailDialog.xaml")).Value;
        Expect.Contains(xaml, "<TextBlock Text=\"Create Date\" FontWeight=\"SemiBold\" />");
        Expect.Contains(xaml, "<TextBlock Text=\"{x:Bind ViewModel.CreateDate, Mode=OneWay}\" IsTextSelectionEnabled=\"True\" />");
        Expect.DoesNotContain(xaml, "Header=\"Create Date\"");
        Expect.Contains(xaml, "<ContentDialog.PrimaryButtonStyle>\n        <StaticResource ResourceKey=\"BoldAccentDialogButton\" />");
        Expect.Contains(xaml, "<ContentDialog.CloseButtonStyle>\n        <StaticResource ResourceKey=\"BoldDialogButton\" />");
        Expect.DoesNotContain(xaml, "PrimaryButtonStyle=\"");
        Expect.Contains(xaml, "<Setter Property=\"FontWeight\" Value=\"Bold\" />");
        System.Xml.Linq.XDocument.Parse(xaml);
    }

    [TestMethod]
    public async Task Every_stack_styles_every_button_bold()
    {
        Expect.Contains((await RunEssentials("TSX_EssentialStyles_v1.tt"))["index.css"], "button, [role='tab'] { font-weight: 700; }");
        Expect.Contains((await RunEssentials("TS_EssentialStyles_v1.tt"))["styles.css"], "button { font-weight: 700; }");
        Expect.Contains((await RunEssentials("BLZ_EssentialLayout_v1.tt")).Single(f => f.Key.EndsWith(".css")).Value, "button { font-weight: 700; }");
        Expect.Contains((await RunEssentials("WinUI3_EssentialApp_v1.tt")).Single(f => f.Key.EndsWith("App.xaml")).Value, "<Setter Property=\"FontWeight\" Value=\"Bold\" />");
    }

    private static async Task<Dictionary<string, string>> RunEssentials(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public async Task A_child_with_many_columns_is_blocks_of_fields_and_one_with_few_is_a_table()
    {
        string wide = (await Run("TSX_DetailMasterPage_v1.tt", Invoice(Lines(10)), Project(("EditableChildren", "InvoiceLine")))).Single(f => f.Key.EndsWith("DetailMasterPage.tsx")).Value;
        Expect.Contains(wide, "<strong>Invoice Line {index + 1}</strong>");
        Expect.Contains(wide, "gridTemplateColumns: 'repeat(auto-fill, minmax(11rem, 1fr))'");
        Expect.Contains(wide, "<label style={{ display: 'flex', flexDirection: 'column', gridColumn: '1 / -1' }}><span>Line Note</span>");
        Expect.Contains(wide, "<CurrencyInput label=\"Item Price\" value={row.itemPrice}");
        Expect.DoesNotContain(wide, "<table>\n                <thead>\n                  <tr><th>Item Price");

        string narrow = (await Run("TSX_DetailMasterPage_v1.tt", Invoice(Lines(4)), Project(("EditableChildren", "InvoiceLine")))).Single(f => f.Key.EndsWith("DetailMasterPage.tsx")).Value;
        Expect.Contains(narrow, "<th>Item Price" + RequiredMark.Jsx(true) + "</th>");
        Expect.DoesNotContain(narrow, "gridTemplateColumns");

        var angular = await Run("TS_DetailMasterComponent_v1.tt", Invoice(Lines(10)), Project(("EditableChildren", "InvoiceLine"), ("AngularVersion", "22")));
        Expect.Contains(angular["invoice-detail-master.component.html"], "child-card-fields");
        Expect.Contains(angular["invoice-detail-master.component.html"], "<label class=\"child-field child-field-wide\"><span>Line Note</span>");
        Expect.Contains(angular["invoice-detail-master.component.html"], "appCurrency currencyCode=\"USD\" [digits]=\"8\"");
        Expect.Contains(angular["invoice-detail-master.component.css"], ".child-card-fields { display: grid;");
    }

    [TestMethod]
    public void A_stamp_is_what_the_system_writes_and_a_short_whole_number_has_a_short_box()
    {
        Assert.IsTrue(Sample.Column("CreateDate", SqlDbType.DateTime, createDateColumn: true).IsSystemStampColumn);
        Assert.IsTrue(Sample.Column("ModifiedBy", SqlDbType.VarChar, modifiedUserColumn: true).IsSystemStampColumn);
        Assert.IsFalse(Sample.Column("Title", SqlDbType.VarChar).IsSystemStampColumn);

        var project = Project(("MinYear", "1990"), ("MaxYear", "2040"));
        Assert.AreEqual(7, project.NumberBoxCharacters(Sample.Column("MonthNumber", SqlDbType.Int, numericKind: NumericKind.Month)));
        Assert.AreEqual(9, project.NumberBoxCharacters(Sample.Column("Year", SqlDbType.Int, numericKind: NumericKind.Year)));
        Assert.AreEqual(8, project.NumberBoxCharacters(Sample.Column("Flags", SqlDbType.TinyInt)), "0 to 255");
        Assert.IsNull(project.NumberBoxCharacters(Sample.Column("Quantity", SqlDbType.Int)), "a plain int keeps the form's width");
        Assert.IsNull(project.NumberBoxCharacters(Sample.Column("Price", SqlDbType.Decimal)));
    }
}
