using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Price;

/// <summary> What the list screen of a price table (<c>PriceListTables</c>) adds: boxes for the item, the quantity, the currency, the day and, when the lists have a customer group, the group, and a button that asks
/// <c>GET price</c> for the price in force and shows it. The words are here so every front end agrees. </summary>
public static class PriceScreens
{
    public const string RouteName = "price";

    /// <summary> The method a TypeScript client calls it by. </summary>
    public const string Route = "priceOn";

    public const string ItemLabel = "Price item";

    public const string QuantityLabel = "Price quantity";

    public const string CurrencyLabel = "Price currency";

    public const string DayLabel = "Price on";

    public const string GroupLabel = "Price customer group";

    public const string ItemHint = "Item";

    public const string QuantityHint = "Quantity";

    public const string CurrencyHint = "Currency";

    public const string GroupHint = "Customer group";

    public const string DayHint = "Day (yyyy-mm-dd)";

    /// <summary> The caption beside a date box, which shows no placeholder. </summary>
    public const string DayCaption = "Price on";

    public const string FindCaption = "Find price";

    public const string ResultLabel = "Price found";

    public const string NoneText = "No price for that item, quantity, currency and day.";

    public const string NeedsText = "Give the item and the currency.";

    public const string DayNeedsText = "Give the day as yyyy-mm-dd.";

    public const string NumberNeedsText = "Give the item and the quantity as numbers.";

    /// <summary> Whether the table's list screen gets the box. </summary>
    public static bool HasBox(TableModel table, ProjectSettings project) => PriceListPlan.IsListed(table.TableName, project);

    /// <summary> The names of the price, the minimum quantity and the list of a price table's rows; <c>Group</c> is true when the lists have a customer group, so the box has a group field. </summary>
    public readonly record struct PriceNames(string Price, string Min, string List, bool Group);

    /// <summary> The JSON names of the price table's rows, or null when the table is not a price table. </summary>
    public static PriceNames? Names(TableModel table, ProjectSettings project) =>
        PriceListPlan.For(table, project) is { } shape ? new PriceNames(JsonNames.Camel(shape.Price.Name), JsonNames.Camel(shape.MinQuantity.Name), JsonNames.Camel(shape.List.Name), shape.Group is not null) : null;

    /// <summary> The C# names of the price, the minimum quantity and the list, or null when the table is not a price table. </summary>
    public static PriceNames? CsNames(TableModel table, ProjectSettings project) =>
        PriceListPlan.For(table, project) is { } shape ? new PriceNames(shape.Price.CSharpName(), shape.MinQuantity.CSharpName(), shape.List.CSharpName(), shape.Group is not null) : null;

    private const string Signature = "item: string, currency: string, quantity?: string, on?: string, group?: string";

    /// <summary> The member of the React api module. </summary>
    public static string ReactApiLine(string rowType) => $"  {Route}: ({Signature}) => request<{rowType}>(`${{apiUrl}}/{RouteName}?${{new URLSearchParams({{ item, currency, ...(quantity ? {{ quantity }} : {{}}), ...(on ? {{ on }} : {{}}), ...(group ? {{ group }} : {{}}) }})}}`),";

    /// <summary> The member of the React OData api module: the lookup is the table's REST route (OData has no such query). </summary>
    public static string ReactODataApiLine(string rowType, string restPath) => $"  {Route}: ({Signature}) => request<{rowType}>(`{restPath}/{RouteName}?${{new URLSearchParams({{ item, currency, ...(quantity ? {{ quantity }} : {{}}), ...(on ? {{ on }} : {{}}), ...(group ? {{ group }} : {{}}) }})}}`),";

    private static string TsText(PriceNames n) => $"Price: ${{row.{n.Price}}} (from ${{row.{n.Min}}}, list ${{row.{n.List}}})";

    public static IReadOnlyList<string> ReactHandlers(string apiVar, PriceNames names) =>
    [
        "",
        "  // a price table (PriceListTables): the price in force for an item and a quantity",
        "  const [priceItem, setPriceItem] = useState('');",
        "  const [priceQuantity, setPriceQuantity] = useState('');",
        "  const [priceCurrency, setPriceCurrency] = useState('');",
        "  const [priceDay, setPriceDay] = useState('');",
        "  const [priceGroup, setPriceGroup] = useState('');",
        "  const [priceText, setPriceText] = useState('');",
        "  const findPrice = () => {",
        "    const item = priceItem.trim();",
        "    const currency = priceCurrency.trim();",
        $"    if (!item || !currency) {{ setPriceText('{NeedsText}'); return; }}",
        $"    {apiVar}.{Route}(item, currency, priceQuantity.trim() || undefined, priceDay || undefined, priceGroup.trim() || undefined)",
        $"      .then((row) => setPriceText(`{TsText(names)}`))",
        $"      .catch(() => setPriceText('{NoneText}'));",
        "  };",
    ];

    /// <summary> The boxes and the button above the grid of the React page. </summary>
    public static IReadOnlyList<string> ReactBox(PriceNames names)
    {
        var lines = new List<string>
        {
            "",
            "      <div>",
            $"        <input type=\"text\" placeholder=\"{ItemHint}\" aria-label=\"{ItemLabel}\" value={{priceItem}} onChange={{(event) => setPriceItem(event.target.value)}} />",
            $"        <input type=\"text\" placeholder=\"{QuantityHint}\" aria-label=\"{QuantityLabel}\" value={{priceQuantity}} onChange={{(event) => setPriceQuantity(event.target.value)}} />",
            $"        <input type=\"text\" placeholder=\"{CurrencyHint}\" aria-label=\"{CurrencyLabel}\" value={{priceCurrency}} onChange={{(event) => setPriceCurrency(event.target.value)}} />",
            $"        <label>{DayCaption} <input type=\"date\" aria-label=\"{DayLabel}\" value={{priceDay}} onChange={{(event) => setPriceDay(event.target.value)}} /></label>",
        };
        if (names.Group)
            lines.Add($"        <input type=\"text\" placeholder=\"{GroupHint}\" aria-label=\"{GroupLabel}\" value={{priceGroup}} onChange={{(event) => setPriceGroup(event.target.value)}} />");
        lines.Add($"        <button type=\"button\" onClick={{findPrice}}>{FindCaption}</button>");
        lines.Add($"        <span role=\"status\" aria-label=\"{ResultLabel}\">{{priceText}}</span>");
        lines.Add("      </div>");
        return lines;
    }

    public static IReadOnlyList<string> ReactBoxTests(string apiVar, string pageName, PriceNames names) =>
    [
        "",
        "  it('asks for the price in force and shows it, or says there is none', async () => {",
        $"    const found = vi.spyOn({apiVar}, '{Route}').mockResolvedValueOnce({{ {names.List}: 3, {names.Min}: 10, {names.Price}: 9 }} as never).mockRejectedValueOnce(new Error('none'));",
        $"    render(<MemoryRouter><{pageName} /></MemoryRouter>);",
        $"    fireEvent.change(await screen.findByLabelText('{ItemLabel}'), {{ target: {{ value: ' 5 ' }} }});",
        $"    fireEvent.change(screen.getByLabelText('{QuantityLabel}'), {{ target: {{ value: '12' }} }});",
        $"    fireEvent.change(screen.getByLabelText('{CurrencyLabel}'), {{ target: {{ value: 'USD' }} }});",
        $"    fireEvent.change(screen.getByLabelText('{DayLabel}'), {{ target: {{ value: '2024-03-15' }} }});",
        $"    fireEvent.click(screen.getByText('{FindCaption}'));",
        "    expect(found).toHaveBeenCalledWith('5', 'USD', '12', '2024-03-15', undefined);",
        "    expect(await screen.findByText(/Price: 9 \\(from 10, list 3\\)$/)).toBeInTheDocument();",
        $"    fireEvent.click(screen.getByText('{FindCaption}'));",
        $"    expect(await screen.findByText('{NoneText}')).toBeInTheDocument();",
        "  });",
    ];

    /// <summary> The method of the Angular service. </summary>
    public static IReadOnlyList<string> AngularServiceLines(string rowType, string urlMember) =>
    [
        "",
        "    // a price table (PriceListTables): the price in force for an item and a quantity",
        $"    {Route}({Signature}): Observable<{rowType}> {{",
        "        const query = new URLSearchParams({ item, currency, ...(quantity ? { quantity } : {}), ...(on ? { on } : {}), ...(group ? { group } : {}) });",
        $"        return this.http.get<{rowType}>(`${{this.{urlMember}}}/{RouteName}?${{query}}`);",
        "    }",
    ];

    /// <summary> The boxes and the button above the grid of the Angular screen. </summary>
    public static IReadOnlyList<string> AngularBox(PriceNames names)
    {
        var lines = new List<string>
        {
            "",
            $"    <!-- {DayLabel} -->",
            "    <div class=\"form-group form-group-search dept\">",
            $"        <input type=\"text\" class=\"form-control\" placeholder=\"{ItemHint}\" aria-label=\"{ItemLabel}\" [(ngModel)]=\"priceItem\" />",
            $"        <input type=\"text\" class=\"form-control\" placeholder=\"{QuantityHint}\" aria-label=\"{QuantityLabel}\" [(ngModel)]=\"priceQuantity\" />",
            $"        <input type=\"text\" class=\"form-control\" placeholder=\"{CurrencyHint}\" aria-label=\"{CurrencyLabel}\" [(ngModel)]=\"priceCurrency\" />",
            $"        <label>{DayCaption} <input type=\"date\" class=\"form-control\" aria-label=\"{DayLabel}\" [(ngModel)]=\"priceDay\" /></label>",
        };
        if (names.Group)
            lines.Add($"        <input type=\"text\" class=\"form-control\" placeholder=\"{GroupHint}\" aria-label=\"{GroupLabel}\" [(ngModel)]=\"priceGroup\" />");
        lines.Add($"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"findPrice()\"> {FindCaption} </button>");
        lines.Add($"        <span role=\"status\" aria-label=\"{ResultLabel}\">{{{{ priceText }}}}</span>");
        lines.Add("    </div>");
        return lines;
    }

    public static IReadOnlyList<string> AngularHandlers(PriceNames names) =>
    [
        "",
        "  // a price table (PriceListTables): the price in force for an item and a quantity",
        "  priceItem = '';",
        "  priceQuantity = '';",
        "  priceCurrency = '';",
        "  priceDay = '';",
        "  priceGroup = '';",
        "  priceText = '';",
        "",
        "  findPrice(): void {",
        "    const item = this.priceItem.trim();",
        "    const currency = this.priceCurrency.trim();",
        $"    if (!item || !currency) {{ this.priceText = '{NeedsText}'; return; }}",
        $"    this.service.{Route}(item, currency, this.priceQuantity.trim() || undefined, this.priceDay || undefined, this.priceGroup.trim() || undefined).subscribe({{",
        $"      next: (row) => {{ this.priceText = `{TsText(names)}`; }},",
        $"      error: () => {{ this.priceText = '{NoneText}'; }}",
        "    });",
        "  }",
    ];

    /// <summary> The Jasmine test of the box: the item and the currency are asked for trimmed, the quantity and the day go along and the answer is shown. </summary>
    public static string AngularBoxTest(PriceNames names) =>
        "  it('asks for the price in force and shows it', () => {\n"
        + "    const http = TestBed.inject(HttpTestingController);\n"
        + "    component.priceItem = ' 5 ';\n"
        + "    component.priceQuantity = '12';\n"
        + "    component.priceCurrency = 'USD';\n"
        + "    component.priceDay = '2024-03-15';\n"
        + "    component.findPrice();\n"
        + $"    const request = http.expectOne((r) => r.urlWithParams.endsWith('/{RouteName}?item=5&currency=USD&quantity=12&on=2024-03-15'));\n"
        + $"    request.flush({{ {names.List}: 3, {names.Min}: 10, {names.Price}: 9 }});\n"
        + "    expect(component.priceText).toContain('Price: 9 (from 10, list 3)');\n"
        + "  });\n";

    /// <summary> The method a C# client and a Blazor page call it by. </summary>
    public const string CsMethod = "PriceOnAsync";

    /// <summary> The method of the C# client; <paramref name="route"/> is the REST route of the table (the OData client asks it too). </summary>
    public static IReadOnlyList<string> CsClientLines(string rowType, string route) =>
    [
        "",
        "    // A price table (PriceListTables): the price in force for an item and a quantity.",
        $"    public async Task<{rowType}> {CsMethod}(string item, string currency, string? quantity = null, string? on = null, string? group = null) => await (await http.GetAsync($\"{route}/{RouteName}?item={{Uri.EscapeDataString(item)}}&currency={{Uri.EscapeDataString(currency)}}{{(string.IsNullOrEmpty(quantity) ? \"\" : \"&quantity=\" + Uri.EscapeDataString(quantity))}}{{(string.IsNullOrEmpty(on) ? \"\" : \"&on=\" + Uri.EscapeDataString(on))}}{{(string.IsNullOrEmpty(group) ? \"\" : \"&group=\" + Uri.EscapeDataString(group))}}\")).ReadAsync<{rowType}>();",
    ];

    /// <summary> The boxes and the button above the grid of the Blazor page. </summary>
    public static IReadOnlyList<string> BlazorBox(PriceNames names)
    {
        var lines = new List<string>
        {
            "",
            "<div class=\"search\">",
            $"    <input type=\"text\" placeholder=\"{ItemHint}\" aria-label=\"{ItemLabel}\" @bind=\"priceItem\" @bind:event=\"oninput\" />",
            $"    <input type=\"text\" placeholder=\"{QuantityHint}\" aria-label=\"{QuantityLabel}\" @bind=\"priceQuantity\" @bind:event=\"oninput\" />",
            $"    <input type=\"text\" placeholder=\"{CurrencyHint}\" aria-label=\"{CurrencyLabel}\" @bind=\"priceCurrency\" @bind:event=\"oninput\" />",
            $"    <input type=\"text\" placeholder=\"{DayHint}\" aria-label=\"{DayLabel}\" value=\"@priceDay\" @oninput=\"e => priceDay = e.Value?.ToString()\" />",
        };
        if (names.Group)
            lines.Add($"    <input type=\"text\" placeholder=\"{GroupHint}\" aria-label=\"{GroupLabel}\" @bind=\"priceGroup\" @bind:event=\"oninput\" />");
        lines.Add($"    <button type=\"button\" @onclick=\"FindPriceAsync\">{FindCaption}</button>");
        lines.Add($"    <span role=\"status\" aria-label=\"{ResultLabel}\">@priceText</span>");
        lines.Add("</div>");
        return lines;
    }

    /// <summary> The fields and the method of the Blazor page. </summary>
    public static IReadOnlyList<string> BlazorHandlers(string client, PriceNames names) =>
    [
        "",
        "    // A price table (PriceListTables): the price in force for an item and a quantity.",
        "    private string? priceItem;",
        "    private string? priceQuantity;",
        "    private string? priceCurrency;",
        "    private string? priceDay;",
        "    private string? priceGroup;",
        "    private string? priceText;",
        "",
        "    private async Task FindPriceAsync()",
        "    {",
        "        string item = (priceItem ?? \"\").Trim(), currency = (priceCurrency ?? \"\").Trim();",
        $"        if (item.Length == 0 || currency.Length == 0) {{ priceText = \"{NeedsText}\"; return; }}",
        "        try",
        "        {",
        $"            var row = await {client}.{CsMethod}(item, currency, priceQuantity?.Trim(), priceDay, priceGroup?.Trim());",
        $"            priceText = $\"Price: {{row.{names.Price}}} (from {{row.{names.Min}}}, list {{row.{names.List}}})\";",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ priceText = \"{NoneText}\"; }}",
        "    }",
    ];

    /// <summary> The boxes, the button and the answer in the top bar of a WinUI3 or Uno list (sixteen spaces in). </summary>
    public static string DesktopBox(PriceNames names) =>
        $"                <TextBox Width=\"90\" PlaceholderText=\"{ItemHint}\" AutomationProperties.Name=\"{ItemLabel}\" Text=\"{{x:Bind ViewModel.PriceItem, Mode=TwoWay}}\" />\n"
        + $"                <TextBox Width=\"90\" PlaceholderText=\"{QuantityHint}\" AutomationProperties.Name=\"{QuantityLabel}\" Text=\"{{x:Bind ViewModel.PriceQuantity, Mode=TwoWay}}\" />\n"
        + $"                <TextBox Width=\"90\" PlaceholderText=\"{CurrencyHint}\" AutomationProperties.Name=\"{CurrencyLabel}\" Text=\"{{x:Bind ViewModel.PriceCurrency, Mode=TwoWay}}\" />\n"
        + $"                <TextBox Width=\"150\" PlaceholderText=\"{DayHint}\" AutomationProperties.Name=\"{DayLabel}\" Text=\"{{x:Bind ViewModel.PriceDay, Mode=TwoWay}}\" />\n"
        + (names.Group ? $"                <TextBox Width=\"130\" PlaceholderText=\"{GroupHint}\" AutomationProperties.Name=\"{GroupLabel}\" Text=\"{{x:Bind ViewModel.PriceGroup, Mode=TwoWay}}\" />\n" : "")
        + $"                <Button Content=\"{FindCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnFindPriceClick\" />\n"
        + $"                <TextBlock VerticalAlignment=\"Center\" Text=\"{{x:Bind ViewModel.PriceText, Mode=OneWay}}\" />\n";

    /// <summary> The click handler of the desktop page. </summary>
    public static string DesktopHandlers() =>
        "    private async void OnFindPriceClick(object sender, RoutedEventArgs e) => await ViewModel.FindPriceAsync();\n";

    /// <summary> The members of the desktop list's view model; <paramref name="read"/> is the body of the lookup after the item, the quantity, the currency and the day are checked. </summary>
    public static string DesktopMembers(string read) =>
        "    /// <summary> The boxes and the answer of the price lookup (PriceListTables). </summary>\n"
        + "    [ObservableProperty]\n    private string? _priceItem;\n\n"
        + "    [ObservableProperty]\n    private string? _priceQuantity;\n\n"
        + "    [ObservableProperty]\n    private string? _priceCurrency;\n\n"
        + "    [ObservableProperty]\n    private string? _priceDay;\n\n"
        + "    [ObservableProperty]\n    private string? _priceGroup;\n\n"
        + "    [ObservableProperty]\n    private string? _priceText;\n\n"
        + "    public async Task FindPriceAsync()\n"
        + "    {\n"
        + "        string currency = (PriceCurrency ?? \"\").Trim().ToUpperInvariant();\n"
        + $"        if (string.IsNullOrWhiteSpace(PriceItem) || currency.Length == 0) {{ PriceText = \"{NeedsText}\"; return; }}\n"
        + "        decimal quantity = 1;\n"
        + $"        if (!long.TryParse(PriceItem.Trim(), out long item) || (!string.IsNullOrWhiteSpace(PriceQuantity) && !decimal.TryParse(PriceQuantity.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out quantity))) {{ PriceText = \"{NumberNeedsText}\"; return; }}\n"
        + "        DateTime day = DateTime.Today;\n"
        + $"        if (!string.IsNullOrWhiteSpace(PriceDay) && !DateTime.TryParse(PriceDay.Trim(), System.Globalization.CultureInfo.InvariantCulture, out day)) {{ PriceText = \"{DayNeedsText}\"; return; }}\n"
        + "        string? group = string.IsNullOrWhiteSpace(PriceGroup) ? null : PriceGroup.Trim();\n"
        + read
        + "    }\n\n";

    /// <summary> The Uno lookup: the API's route. </summary>
    public static string UnoRead(PriceNames names) =>
        "        try\n"
        + "        {\n"
        + $"            var row = await Client.{CsMethod}(item.ToString(), currency, quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), day.ToString(\"yyyy-MM-dd\"), group);\n"
        + $"            PriceText = $\"Price: {{row.{names.Price}}} (from {{row.{names.Min}}}, list {{row.{names.List}}})\";\n"
        + "        }\n"
        + $"        catch (Exception) {{ PriceText = \"{NoneText}\"; }}\n";

    /// <summary> The WinUI3 lookup: the same query as the API's route (the group's lists first, then the lists with no group), over the dialog's own context. </summary>
    public static string WinUI3Read(TableModel table, ProjectSettings project, string context)
    {
        if (PriceListPlan.For(table, project) is not { } s)
            return "";
        string row = table.TableName, lists = s.ListTable.TableName;
        string group = s.Group is null ? "" : $"\n            listed = held is null ? listed.Where(l => l.{s.Group.CSharpName()} == null) : listed.Where(l => l.{s.Group.CSharpName()} != null && l.{s.Group.CSharpName()}!.ToUpper() == held);";
        string names = $"Price: {{best.{s.Price.CSharpName()}}} (from {{best.{s.MinQuantity.CSharpName()}}}, list {{best.{s.List.CSharpName()}}})";
        return "        DateTime today = day.Date;\n"
            + $"        {row}? best = null;\n"
            + (s.Group is null ? "        foreach (string? held in new string?[] { null })\n" : "        foreach (string? held in group is null ? new string?[] { null } : new string?[] { group.ToUpperInvariant(), null })\n")
            + "        {\n"
            + $"            var listed = {context}.Set<{lists}>().AsNoTracking().Where(l => l.{s.Currency.CSharpName()}.ToUpper() == currency && l.{s.ValidFrom.CSharpName()} <= today && (l.{s.ValidTo.CSharpName()} == null || l.{s.ValidTo.CSharpName()} >= today));{group}\n"
            + $"            var listKeys = listed.Select(l => l.{s.ListKey.CSharpName()});\n"
            + $"            var rows = await {context}.Set<{row}>().AsNoTracking().Where(x => x.{s.Item.CSharpName()} == item && listKeys.Contains(x.{s.List.CSharpName()})).ToListAsync();\n"
            + $"            best = rows.Where(x => (decimal)x.{s.MinQuantity.CSharpName()} <= quantity).OrderByDescending(x => (decimal)x.{s.MinQuantity.CSharpName()}).ThenByDescending(x => x.{s.Key.CSharpName()}).FirstOrDefault();\n"
            + "            if (best is not null)\n"
            + "                break;\n"
            + "        }\n"
            + $"        PriceText = best is null ? \"{NoneText}\" : $\"{names}\";\n";
    }

    /// <summary> The lines a WinUI3 dialog runs before it saves a row of a price table (it writes through its own context, not through the API; the same rule as <c>CheckPriceAsync</c> of the API): a minimum quantity above
    /// zero, a price that is not negative and one row per list, item and minimum quantity. Empty when the table is not listed. </summary>
    public static string WinUI3Check(TableModel table, ProjectSettings project, string context)
    {
        if (PriceListPlan.For(table, project) is not { } s)
            return "";
        string row = table.TableName, min = s.MinQuantity.CSharpName(), key = s.Key.CSharpName();
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append("        ").Append(text).Append('\n');
        L("{");
        L($"    if ((decimal)entity.{min} <= 0)");
        L("    {");
        L($"        ErrorMessage = \"{PriceWords.QuantityNotPositive}\";");
        L("        return false;");
        L("    }");
        L($"    if ((decimal)entity.{s.Price.CSharpName()} < 0)");
        L("    {");
        L($"        ErrorMessage = \"{PriceWords.PriceNegative}\";");
        L("        return false;");
        L("    }");
        L($"    var pcHeld = await {context}.Set<{row}>().AsNoTracking().Where(x => x.{key} != entity.{key} && x.{s.List.CSharpName()} == entity.{s.List.CSharpName()} && x.{s.Item.CSharpName()} == entity.{s.Item.CSharpName()}).Select(x => x.{min}).ToListAsync();");
        L($"    if (pcHeld.Any(m => (decimal)m == (decimal)entity.{min}))");
        L("    {");
        L($"        ErrorMessage = \"{PriceWords.Taken}\";");
        L("        return false;");
        L("    }");
        L("}");
        return o.Append('\n').ToString();
    }

    public static string BlazorBoxTest(PriceNames json) =>
        "\n"
        + "    [TestMethod]\n"
        + "    public void Asks_for_the_price_in_force_and_shows_it()\n"
        + "    {\n"
        + $"        Api.Answer(\"/{RouteName}\", \"{{ \\\"{json.List}\\\": 3, \\\"{json.Min}\\\": 10, \\\"{json.Price}\\\": 9 }}\");\n"
        + "        var page = Open();\n"
        + $"        page.Find(\"[aria-label=\\\"{ItemLabel}\\\"]\").Input(\" 5 \");\n"
        + $"        page.Find(\"[aria-label=\\\"{QuantityLabel}\\\"]\").Input(\"12\");\n"
        + $"        page.Find(\"[aria-label=\\\"{CurrencyLabel}\\\"]\").Input(\"USD\");\n"
        + $"        page.Find(\"[aria-label=\\\"{DayLabel}\\\"]\").Input(\"2024-03-15\");\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{FindCaption}\").Click();\n"
        + "        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"Price: 9 (from 10, list 3)\"));\n"
        + $"        Assert.IsTrue(Api.Requests.Any(r => r.Path.EndsWith(\"/{RouteName}?item=5&currency=USD&quantity=12&on=2024-03-15\")));\n"
        + "    }\n"
        + "\n";
}
