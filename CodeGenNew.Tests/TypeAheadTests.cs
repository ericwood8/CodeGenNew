using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> A parent with too many rows for a drop-down is found by typing: the setting that decides it, the lookup endpoint that narrows its list, and the Angular, React and Blazor screens that use both. </summary>
[TestClass]
public class TypeAheadTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Invoice(long customerRows) => Sample.Table("SalesInvoice",
    [
        Sample.Column("SalesInvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2),
        Sample.Column("InvoiceStatusId", SqlDbType.TinyInt, nullable: true, ordinal: 3),
        Sample.Column("CustomerPO", SqlDbType.NVarChar, characters: 30, ordinal: 4)
    ],
    [
        Sample.ForeignKeyToParentOf(customerRows, "CustomerId", "Customer", "CustomerId", "CustomerName"),
        Sample.ForeignKeyToParentOf(40, "InvoiceStatusId", "InvoiceStatus", "InvoiceStatusId", "Description")
    ], childForeignKeys: [Sample.ChildForeignKey("SalesInvoiceLine", "SalesInvoiceId", "SalesInvoiceId")]);

    private static async Task<Dictionary<string, string>> Run(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    private static async Task<Dictionary<string, string>> RunEssentials(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public void A_parent_with_more_rows_than_the_limit_is_picked_by_typing()
    {
        var form = FormModel.For(Invoice(customerRows: 5000), Project());

        Assert.IsTrue(form.Parents.Single(p => p.Table == "Customer").TypeAhead);
        Assert.IsFalse(form.Parents.Single(p => p.Table == "InvoiceStatus").TypeAhead);
    }

    [TestMethod]
    public void The_limit_can_be_moved_or_turned_off_and_a_table_can_be_listed()
    {
        Assert.IsFalse(FormModel.For(Invoice(5000), Project(("TypeAheadRows", "0"))).Parents.Any(p => p.TypeAhead), "0 turns the automatic choice off");
        Assert.IsFalse(FormModel.For(Invoice(5000), Project(("TypeAheadRows", "5000"))).Parents.Any(p => p.TypeAhead), "the limit itself is still a drop-down");
        Assert.IsTrue(FormModel.For(Invoice(300), Project(("TypeAheadRows", "100"))).Parents.Single(p => p.Table == "Customer").TypeAhead);
        Assert.IsTrue(FormModel.For(Invoice(300), Project(("TypeAheadParents", "customer"))).Parents.Single(p => p.Table == "Customer").TypeAhead, "a listed table, whatever its size");
    }

    [TestMethod]
    public async Task The_lookup_endpoint_narrows_the_rows_before_it_projects_them()
    {
        string cs = await CrudApiTests.Essential();

        Expect.Contains(cs, "[FromQuery] string? search, [FromQuery] string? ids, [FromQuery] int? take) => LookupAsync(context, search, ids, take)");
        Expect.Contains(cs, "public static class LookupFilter");
        Expect.Contains(cs, "protected virtual Expression<Func<TEntity, string>>? LookupText => null;");
        Expect.Contains(cs, "await NarrowedLookupAsync(context.Set<TEntity>().AsNoTracking(), search, ids, take);");
        Expect.Contains(cs, "Expression.Constant(LookupFilter.ParseKeys<TKey>(ids!))");
        Expect.Contains(cs, "var lower = Expression.Call(text.Body, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);");
        Expect.Contains(cs, "var items = LookupQuery(rows);\n        if (!byIds && (bySearch || take is not null))\n            items = items.Take(LookupFilter.Take(take));");
        Expect.Contains(cs, "rows = rows.Where(c => c.Name.ToLower().Contains(text));");
        Expect.Contains(cs, "rows = rows.Where(c => keys.Contains(EF.Property<int>(c, keyName)));");
    }

    [TestMethod]
    public async Task A_generated_api_says_which_text_its_lookup_searches()
    {
        var customer = Sample.Table("Customer",
        [
            Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("CustomerName", SqlDbType.NVarChar, characters: 50, ordinal: 2)
        ]);
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Crud_v1.tt"), customer, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!.ReplaceLineEndings("\n");

        Expect.Contains(cs, "protected override Expression<Func<Customer, string>>? LookupText => c => c.CustomerName;");
    }

    [TestMethod]
    public async Task The_angular_service_sends_the_text_or_the_keys_to_the_lookup()
    {
        var files = await CrudScreenTests.Essential();

        Expect.Contains(files["crud.service.ts"], "getLookup(query?: LookupQuery<K>): Observable<Lookup<K>[]> {");
        Expect.Contains(files["crud.service.ts"], "if (query?.search) { params = params.set('search', query.search); }");
        Expect.Contains(files["crud.service.ts"], "if (query?.ids?.length) { params = params.set('ids', query.ids.join(',')); }");
        Expect.Contains(files["type-ahead.ts"], "export class LookupNames<K extends number | string = number>");
        Expect.Contains(files["type-ahead.ts"], "export class TypeAhead<K extends number | string = number>");
        Expect.Contains(files["type-ahead.spec.ts"], "describe('TypeAhead'");
    }

    [TestMethod]
    public async Task The_angular_screen_types_ahead_in_the_form_and_the_filter_and_asks_for_the_names_it_shows()
    {
        foreach (string template in new[] { "TS_Component_v1.tt", "TS_DetailMasterComponent_v1.tt" })
        {
            var files = await Run(template, Invoice(5000), Project());
            string html = files.Single(f => f.Key.EndsWith(".component.html")).Value;
            string ts = files.Single(f => f.Key.EndsWith(".component.ts")).Value;

            Expect.Contains(html, "[ngModel]=\"customerIdAhead.shown(selectedRow.customerId)\" (ngModelChange)=\"customerIdAhead.type($event)\" (blur)=\"customerIdAhead.leave()\" required>");
            Expect.Contains(html, "(mousedown)=\"customerIdAhead.pick(o)\"");
            Expect.Contains(html, "[ngModel]=\"customerIdFilterAhead.shown(filters.customerId)\"");
            Expect.Contains(html, "aria-label=\"Filter by Customer\" placeholder=\"All Customers\"");
            Expect.DoesNotContain(html, "let p of customers");
            Expect.Contains(html, "let p of invoiceStatus");
            Expect.Contains(ts, "import { LookupNames, TypeAhead } from '../../services/type-ahead';");
            Expect.Contains(ts, "private readonly customerNameCache = new LookupNames<number>((query) => this.customerService.getLookup(query));");
            Expect.Contains(ts, "readonly customerIdAhead = new TypeAhead<number>((query) => this.customerService.getLookup(query), this.customerNameCache, ");
            Expect.Contains(ts, "this.filters['customerId'] = text; this.search();");
            Expect.Contains(ts, "return this.customerNameCache.name(id);");
            Expect.DoesNotContain(ts, "customers: Lookup[]");
            Expect.DoesNotContain(ts, "customerService.getLookup().subscribe");
            Expect.Contains(ts, "invoiceStatus: Lookup[] = [];");
            Expect.Contains(ts, "this.customerIdAhead.reset();");
            Expect.Contains(ts, "override clearSearch(): void {\n    super.clearSearch();\n    this.customerIdFilterAhead.reset();");
        }
    }

    [TestMethod]
    public async Task The_react_page_types_ahead_in_the_form_and_the_filter()
    {
        foreach (string template in new[] { "TSX_Page_v1.tt", "TSX_DetailMasterPage_v1.tt" })
        {
            var files = await Run(template, Invoice(5000), Project());
            string page = files.Single(f => f.Key.EndsWith("Page.tsx")).Value;

            Expect.Contains(page, "import { TypeAheadBox, useLookupNames } from '../components/TypeAhead';");
            Expect.Contains(page, "const customerNameCache = useLookupNames<number>((query) => customerApi.getLookup(query));");
            Expect.Contains(page, "<TypeAheadBox<number>\n          label=\"Filter by Customer\"\n          placeholder=\"All Customers\"\n          value={filters.customerId}");
            Expect.Contains(page, "id=\"salesInvoiceCustomerId\"\n              label=\"Customer\"\n              value={selectedRow.customerId}");
            Expect.Contains(page, "onChange={(key) => setSelectedRow({ ...selectedRow, customerId: key ?? 0 })}");
            Expect.Contains(page, "return customerNameCache.name(id);");
            Expect.DoesNotContain(page, "customers.map");
            Expect.DoesNotContain(page, "setCustomers");
            Expect.Contains(page, "invoiceStatus.map");
        }
    }

    [TestMethod]
    public async Task The_react_api_and_support_files_send_the_text_and_the_keys()
    {
        var api = await Run("TSX_Api_v1.tt", Invoice(5000), Project());
        Expect.Contains(api.Single(f => f.Key.EndsWith("salesinvoiceApi.ts")).Value, "getLookup: (query: { search?: string; ids?: number[]; take?: number } = {}) =>");

        var support = await RunEssentials("TSX_EssentialSupport_v1.tt");
        Expect.Contains(support["client.ts"], "export function lookupQuery(");
        Expect.Contains(support["TypeAhead.tsx"], "export function TypeAheadBox<K extends number | string = number>");
        Expect.Contains(support["TypeAhead.tsx"], "export function useLookupNames<K extends number | string = number>");
        Expect.Contains(support["TypeAhead.test.tsx"], "describe('TypeAheadBox'");
    }

    [TestMethod]
    public async Task The_blazor_page_types_ahead_and_keeps_only_the_names_it_has_seen()
    {
        var files = await Run("BLZ_Page_v1.tt", Invoice(5000), Project());
        string page = files.Single(f => f.Key.EndsWith(".razor")).Value;

        Expect.Contains(page, "@using Acme.Blazor.Components");
        Expect.Contains(page, "<TypeAhead TValue=\"int\" Label=\"Filter by Customer\" Placeholder=\"All Customers\" Name=\"@NameCustomer(filterCustomerIdKey)\" Find=\"@FindCustomer\" OnPick=\"@PickfilterCustomerId\" />");
        Expect.Contains(page, "<TypeAhead TValue=\"int\" Id=\"salesInvoiceCustomerId\" Label=\"Customer\" Required=\"true\" Name=\"@NameCustomer(row.CustomerId)\" Find=\"@FindCustomer\" OnPick=\"@PickCustomerId\" />");
        Expect.Contains(page, "await KnowNamesAsync(items);");
        Expect.Contains(page, "(await lookupCustomerClient.GetLookupAsync(search: text, take: 10))");
        Expect.Contains(page, "await lookupCustomerClient.GetLookupAsync(ids: missing)");
        Expect.DoesNotContain(page, "= await lookupCustomerClient.GetLookupAsync();");
        Expect.Contains(page, "= await lookupInvoiceStatusClient.GetLookupAsync();");
    }

    [TestMethod]
    public async Task The_blazor_client_and_component_take_a_search_and_keys()
    {
        var client = await Run("BLZ_Client_v1.tt", Invoice(5000), Project());
        Expect.Contains(client.Single(f => f.Key == "SalesInvoiceClient.cs").Value, "GetLookupAsync(string? search = null, IEnumerable<int>? ids = null, int? take = null)");

        var support = await RunEssentials("BLZ_EssentialSupport_v1.tt");
        Expect.Contains(support["TypeAhead.razor"], "@typeparam TValue");
        Expect.Contains(support["TypeAhead.razor"], "await OnPick.InvokeAsync(null);");
        Expect.Contains(support["TypeAheadOption.cs"], "public record TypeAheadOption<TValue>(TValue Id, string Name);");
    }

    [TestMethod]
    public async Task The_winui3_list_filters_by_typing_and_reads_only_the_names_it_shows()
    {
        var files = await Run("WinUI3_MasterScreen_v1.tt", Invoice(5000), Project());
        string xaml = files["SalesInvoiceListPage.xaml"];
        string page = files["SalesInvoiceListPage.xaml.cs"];
        string vm = files["SalesInvoiceListViewModel.cs"];

        Expect.Contains(xaml, "<AutoSuggestBox Width=\"160\" AutomationProperties.Name=\"Filter by Customer\" PlaceholderText=\"All Customers\" DisplayMemberPath=\"Name\" TextMemberPath=\"Name\"");
        Expect.Contains(xaml, "Text=\"{x:Bind ViewModel.CustomerIdFilterText, Mode=TwoWay}\" TextChanged=\"OnCustomerIdFilterTextChanged\" SuggestionChosen=\"OnCustomerIdFilterChosen\"");
        Expect.Contains(xaml, "ItemsSource=\"{x:Bind ViewModel.InvoiceStatusIdChoices}\"");
        Expect.DoesNotContain(xaml, "ViewModel.CustomerIdChoices");
        Expect.Contains(page, "private async void OnCustomerIdFilterTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)");
        Expect.Contains(page, "ViewModel.CustomerIdFilter = (SalesInvoiceKeyChoice)args.SelectedItem;");
        Expect.Contains(vm, "public async Task<IReadOnlyList<SalesInvoiceKeyChoice>> SuggestCustomerIdFilterAsync(string text)");
        Expect.Contains(vm, ".Where(r => r.CustomerName.ToString()!.ToLower().Contains(wanted))");
        Expect.Contains(vm, "Dictionary<int, string> customerNames = [];");
        Expect.Contains(vm, "var ids = rows.Select(r => r.CustomerId).OfType<int>().Distinct().ToList();");
        Expect.Contains(vm, "public async Task LoadAsync()\n    {\n        await _gate.WaitAsync();\n        try { await LoadCoreAsync(); }");
        Expect.DoesNotContain(vm, "CustomerIdChoices");
        Expect.Contains(vm, "InvoiceStatusIdChoices");
    }

    [TestMethod]
    public async Task The_winui3_dialogs_find_the_parent_by_typing()
    {
        foreach (var (template, screen, load) in new[] { ("WinUI3_DetailScreen_v1.tt", "SalesInvoiceDetail", "LoadLookupsAsync"), ("WinUI3_DetailMasterScreen_v1.tt", "SalesInvoiceDetailMaster", "LoadAsync") })
        {
            var files = await Run(template, Invoice(5000), Project());
            string xaml = files.Single(f => f.Key == screen + "Dialog.xaml").Value;
            string dialog = files.Single(f => f.Key == screen + "Dialog.xaml.cs").Value;
            string vm = files.Single(f => f.Key == screen + "ViewModel.cs").Value;

            Expect.Contains(xaml, "<AutoSuggestBox AutomationProperties.IsRequiredForForm=\"True\" AutomationProperties.Name=\"Customer\"");
            Expect.Contains(xaml, "<AutoSuggestBox.Header>\n                    <TextBlock><Run Text=\"Customer\" /><Run Text=\" *\" Foreground=\"Red\" /></TextBlock>");
            Expect.Contains(xaml, "Text=\"{x:Bind ViewModel.CustomerIdText, Mode=TwoWay}\" TextChanged=\"OnCustomerIdTextChanged\" SuggestionChosen=\"OnCustomerIdChosen\"");
            Expect.DoesNotContain(xaml, "ViewModel.CustomerOptions");
            Expect.Contains(xaml, "ViewModel.InvoiceStatusOptions");
            Expect.Contains(dialog, "ViewModel.CustomerId = null;");
            Expect.Contains(dialog, "await ViewModel.SuggestCustomerAsync(sender.Text)");
            Expect.Contains(vm, "private string _customerIdText = \"\";");
            Expect.Contains(vm, "public async Task<IReadOnlyList<" + screen + "LookupOption>> SuggestCustomerAsync(string text)");
            Expect.DoesNotContain(vm, "CustomerOptions");
            Expect.Contains(vm, "CustomerIdText = await _context.Set<Customer>()");
            Expect.Contains(vm, "public async Task " + load + "()\n    {\n        await _gate.WaitAsync();");
        }
    }

    [TestMethod]
    public async Task A_small_parent_keeps_its_drop_down()
    {
        var files = await Run("TS_Component_v1.tt", Invoice(50), Project());
        string html = files.Single(f => f.Key.EndsWith(".component.html")).Value;
        string ts = files.Single(f => f.Key.EndsWith(".component.ts")).Value;

        Expect.Contains(html, "let p of customers");
        Expect.DoesNotContain(html, "Ahead");
        Expect.DoesNotContain(ts, "type-ahead");
        Expect.Contains(ts, "customers: Lookup[] = [];");
    }
}
