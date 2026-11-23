using CodeGenNew.Core.Settings;

namespace CodeGenNew.Core.Stock;

/// <summary> The stock box on the list page of a stock ledger table (<c>StockTables</c>): an item box and a counted box with three buttons, <c>Show stock</c> (what the item has: available, on hand, reserved, and whether it is low), <c>Stock-take</c> (the counted quantity
/// becomes what is on hand, written as an adjustment row of the ledger: the level is never edited) and <c>Low stock</c> (the items at or under their reorder level), over the table's <c>available</c>, <c>adjust</c> and <c>low</c> routes.
/// The WinUI3 list reads and writes through its own context with the same guarded update as the API. </summary>
public static class StockScreens
{
    public const string ItemHint = "Item";
    public const string CountedHint = "Counted";
    public const string ItemLabel = "Stock item";
    public const string CountedLabel = "Stock counted";
    public const string ShowCaption = "Show stock";
    public const string TakeCaption = "Stock-take";
    public const string LowCaption = "Low stock";
    public const string ResultLabel = "Stock";
    public const string NeedsItem = "Give the item number.";
    public const string NeedsCount = "Give the counted quantity.";
    public const string NoneText = "There is no such item.";
    public const string RefusedText = "The count was refused: it may be below what is reserved.";
    public const string LowFailedText = "The low stock list could not be read.";
    public const string NothingLow = "Nothing is low.";

    private const string Level = "{ item: number; onHand: number; reserved: number; available: number; reorderLevel: number; low: boolean }";
    private const string Taken = "{ item: number; counted: number; difference: number; level: " + Level + " }";
    private const string LowRow = "{ item: number; onHand: number; reserved: number; available: number; reorderLevel: number }";

    private const string LevelAnswer = "{ item: 7, onHand: 10, reserved: 4, available: 6, reorderLevel: 8, low: true }";
    private const string TakenAnswer = "{ item: 7, counted: 9, difference: -1, level: { item: 7, onHand: 9, reserved: 1, available: 8, reorderLevel: 8, low: false } }";
    private const string LowAnswer = "[{ item: 7, onHand: 5, reserved: 0, available: 5, reorderLevel: 8 }]";
    public const string ShownTest = "6 available (10 on hand, 4 reserved), low";
    public const string TakenTest = "Counted 9 (-1), 8 available";
    public const string LowTest = "Low: 7 (5)";

    private const string LevelJson = "{ \\\"item\\\": 7, \\\"onHand\\\": 10, \\\"reserved\\\": 4, \\\"available\\\": 6, \\\"reorderLevel\\\": 8, \\\"low\\\": true }";
    private const string TakenJson = "{ \\\"item\\\": 7, \\\"counted\\\": 9, \\\"difference\\\": -1, \\\"level\\\": { \\\"item\\\": 7, \\\"onHand\\\": 9, \\\"reserved\\\": 1, \\\"available\\\": 8, \\\"reorderLevel\\\": 8, \\\"low\\\": false } }";
    private const string LowJson = "[{ \\\"item\\\": 7, \\\"onHand\\\": 5, \\\"reserved\\\": 0, \\\"available\\\": 5, \\\"reorderLevel\\\": 8 }]";

    private const string ShownTs = "`${row.available} available (${row.onHand} on hand, ${row.reserved} reserved)${row.low ? ', low' : ''}`";
    private const string TakenTs = "`Counted ${row.counted} (${row.difference >= 0 ? '+' : ''}${row.difference}), ${row.level.available} available`";
    private const string LowTs = "rows.length === 0 ? '" + NothingLow + "' : 'Low: ' + rows.map((r) => `${r.item} (${r.available})`).join(', ')";

    public static bool HasBox(TableModel table, ProjectSettings project) => StockPlan.IsLedger(table.TableName, project);

    public static IReadOnlyList<string> ReactApiLines(string restPath = "${apiUrl}") =>
    [
        $"  stockAvailable: (item: string) => request<{Level}>(`{restPath}/{StockPlan.AvailableRoute}?item=${{encodeURIComponent(item)}}`),",
        $"  stockAdjust: (item: string, counted: string) => request<{Taken}>(`{restPath}/{StockPlan.AdjustRoute}`, {{ method: 'POST', body: JSON.stringify({{ item: Number(item), counted: Number(counted) }}) }}),",
        $"  stockLow: () => request<{LowRow}[]>(`{restPath}/{StockPlan.LowRoute}`),",
    ];

    public static IReadOnlyList<string> ReactHandlers(string apiVar) =>
    [
        "",
        "  // a stock table (StockTables): the stock of an item, a stock-take that writes an adjustment row, and the items at or under their reorder level",
        "  const [stockItem, setStockItem] = useState('');",
        "  const [stockCounted, setStockCounted] = useState('');",
        "  const [stockText, setStockText] = useState('');",
        "  const showStock = () => {",
        "    const item = stockItem.trim();",
        $"    if (!item) {{ setStockText('{NeedsItem}'); return; }}",
        $"    {apiVar}.stockAvailable(item)",
        $"      .then((row) => setStockText({ShownTs}))",
        $"      .catch(() => setStockText('{NoneText}'));",
        "  };",
        "  const takeStock = () => {",
        "    const item = stockItem.trim();",
        "    const counted = stockCounted.trim();",
        $"    if (!item) {{ setStockText('{NeedsItem}'); return; }}",
        $"    if (!counted) {{ setStockText('{NeedsCount}'); return; }}",
        $"    {apiVar}.stockAdjust(item, counted)",
        $"      .then((row) => {{ setStockText({TakenTs}); load(page); }})",
        $"      .catch(() => setStockText('{RefusedText}'));",
        "  };",
        "  const showLow = () => {",
        $"    {apiVar}.stockLow()",
        $"      .then((rows) => setStockText({LowTs}))",
        $"      .catch(() => setStockText('{LowFailedText}'));",
        "  };",
    ];

    public static IReadOnlyList<string> ReactBox() =>
    [
        "",
        "      <div>",
        $"        <input type=\"text\" placeholder=\"{ItemHint}\" aria-label=\"{ItemLabel}\" value={{stockItem}} onChange={{(event) => setStockItem(event.target.value)}} />",
        $"        <input type=\"text\" placeholder=\"{CountedHint}\" aria-label=\"{CountedLabel}\" value={{stockCounted}} onChange={{(event) => setStockCounted(event.target.value)}} />",
        $"        <button type=\"button\" onClick={{showStock}}>{ShowCaption}</button>",
        $"        <button type=\"button\" onClick={{takeStock}}>{TakeCaption}</button>",
        $"        <button type=\"button\" onClick={{showLow}}>{LowCaption}</button>",
        $"        <span role=\"status\" aria-label=\"{ResultLabel}\">{{stockText}}</span>",
        "      </div>",
    ];

    public static IReadOnlyList<string> ReactBoxTests(string apiVar, string pageName) =>
    [
        "",
        "  it('shows the stock of an item, takes stock and lists what is low', async () => {",
        $"    const shown = vi.spyOn({apiVar}, 'stockAvailable').mockResolvedValueOnce({LevelAnswer} as never).mockRejectedValueOnce(new Error('none'));",
        $"    const taken = vi.spyOn({apiVar}, 'stockAdjust').mockResolvedValueOnce({TakenAnswer} as never);",
        $"    vi.spyOn({apiVar}, 'stockLow').mockResolvedValueOnce({LowAnswer} as never);",
        $"    render(<{pageName} />);",
        $"    fireEvent.change(await screen.findByLabelText('{ItemLabel}'), {{ target: {{ value: ' 7 ' }} }});",
        $"    fireEvent.click(screen.getByText('{ShowCaption}'));",
        "    expect(shown).toHaveBeenCalledWith('7');",
        $"    expect(await screen.findByText('{ShownTest}')).toBeInTheDocument();",
        $"    fireEvent.click(screen.getByText('{ShowCaption}'));",
        $"    expect(await screen.findByText('{NoneText}')).toBeInTheDocument();",
        $"    fireEvent.change(screen.getByLabelText('{CountedLabel}'), {{ target: {{ value: '9' }} }});",
        $"    fireEvent.click(screen.getByText('{TakeCaption}'));",
        "    expect(taken).toHaveBeenCalledWith('7', '9');",
        $"    expect(await screen.findByText('{TakenTest}')).toBeInTheDocument();",
        $"    fireEvent.click(screen.getByText('{LowCaption}'));",
        $"    expect(await screen.findByText('{LowTest}')).toBeInTheDocument();",
        "  });",
    ];

    public static IReadOnlyList<string> AngularServiceLines(string urlMember) =>
    [
        "",
        "    // a stock table (StockTables): the stock of an item, a stock-take that writes an adjustment row, and the items at or under their reorder level",
        $"    stockAvailable(item: string): Observable<{Level}> {{",
        $"        return this.http.get<{Level}>(`${{this.{urlMember}}}/{StockPlan.AvailableRoute}?item=${{encodeURIComponent(item)}}`);",
        "    }",
        "",
        $"    stockAdjust(item: string, counted: string): Observable<{Taken}> {{",
        $"        return this.http.post<{Taken}>(`${{this.{urlMember}}}/{StockPlan.AdjustRoute}`, {{ item: Number(item), counted: Number(counted) }});",
        "    }",
        "",
        $"    stockLow(): Observable<{LowRow}[]> {{",
        $"        return this.http.get<{LowRow}[]>(`${{this.{urlMember}}}/{StockPlan.LowRoute}`);",
        "    }",
    ];

    public static IReadOnlyList<string> AngularBox() =>
    [
        "",
        $"    <!-- {ItemLabel} -->",
        "    <div class=\"form-group form-group-search dept\">",
        $"        <input type=\"text\" class=\"form-control\" placeholder=\"{ItemHint}\" aria-label=\"{ItemLabel}\" [(ngModel)]=\"stockItem\" />",
        $"        <input type=\"text\" class=\"form-control\" placeholder=\"{CountedHint}\" aria-label=\"{CountedLabel}\" [(ngModel)]=\"stockCounted\" />",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"showStock()\"> {ShowCaption} </button>",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"takeStock()\"> {TakeCaption} </button>",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"showLow()\"> {LowCaption} </button>",
        $"        <span role=\"status\" aria-label=\"{ResultLabel}\">{{{{ stockText }}}}</span>",
        "    </div>",
    ];

    public static IReadOnlyList<string> AngularHandlers() =>
    [
        "",
        "  // a stock table (StockTables): the stock of an item, a stock-take that writes an adjustment row, and the items at or under their reorder level",
        "  stockItem = '';",
        "  stockCounted = '';",
        "  stockText = '';",
        "",
        "  showStock(): void {",
        "    const item = this.stockItem.trim();",
        $"    if (!item) {{ this.stockText = '{NeedsItem}'; return; }}",
        "    this.service.stockAvailable(item).subscribe({",
        $"      next: (row) => {{ this.stockText = {ShownTs}; }},",
        $"      error: () => {{ this.stockText = '{NoneText}'; }}",
        "    });",
        "  }",
        "",
        "  takeStock(): void {",
        "    const item = this.stockItem.trim();",
        "    const counted = this.stockCounted.trim();",
        $"    if (!item) {{ this.stockText = '{NeedsItem}'; return; }}",
        $"    if (!counted) {{ this.stockText = '{NeedsCount}'; return; }}",
        "    this.service.stockAdjust(item, counted).subscribe({",
        $"      next: (row) => {{ this.stockText = {TakenTs}; this.load(); }},",
        $"      error: () => {{ this.stockText = '{RefusedText}'; }}",
        "    });",
        "  }",
        "",
        "  showLow(): void {",
        "    this.service.stockLow().subscribe({",
        $"      next: (rows) => {{ this.stockText = {LowTs}; }},",
        $"      error: () => {{ this.stockText = '{LowFailedText}'; }}",
        "    });",
        "  }",
    ];

    public static IReadOnlyList<string> CsClientLines(string route) =>
    [
        "",
        "    // A stock table (StockTables): the stock of an item, a stock-take that writes an adjustment row, and the items at or under their reorder level.",
        $"    public async Task<System.Text.Json.JsonElement> StockAvailableAsync(string item) => await (await http.GetAsync($\"{route}/{StockPlan.AvailableRoute}?item={{Uri.EscapeDataString(item)}}\")).ReadAsync<System.Text.Json.JsonElement>();",
        "",
        $"    public async Task<System.Text.Json.JsonElement> StockAdjustAsync(string item, string counted) => await (await http.PostAsJsonAsync($\"{route}/{StockPlan.AdjustRoute}\", new {{ item = long.Parse(item), counted = long.Parse(counted) }})).ReadAsync<System.Text.Json.JsonElement>();",
        "",
        $"    public async Task<System.Text.Json.JsonElement> StockLowAsync() => await (await http.GetAsync($\"{route}/{StockPlan.LowRoute}\")).ReadAsync<System.Text.Json.JsonElement>();",
    ];

    private const string ShownCs = "$\"{row.GetProperty(\"available\")} available ({row.GetProperty(\"onHand\")} on hand, {row.GetProperty(\"reserved\")} reserved){(row.GetProperty(\"low\").GetBoolean() ? \", low\" : \"\")}\"";
    private const string TakenCs = "$\"Counted {row.GetProperty(\"counted\")} ({(row.GetProperty(\"difference\").GetInt64() >= 0 ? \"+\" : \"\")}{row.GetProperty(\"difference\")}), {row.GetProperty(\"level\").GetProperty(\"available\")} available\"";
    private const string LowCs = "row.GetArrayLength() == 0 ? \"" + NothingLow + "\" : \"Low: \" + string.Join(\", \", row.EnumerateArray().Select(r => $\"{r.GetProperty(\"item\")} ({r.GetProperty(\"available\")})\"))";

    public static IReadOnlyList<string> BlazorBox() =>
    [
        "",
        "<div class=\"search\">",
        $"    <input type=\"text\" placeholder=\"{ItemHint}\" aria-label=\"{ItemLabel}\" @bind=\"StockItem\" @bind:event=\"oninput\" />",
        $"    <input type=\"text\" placeholder=\"{CountedHint}\" aria-label=\"{CountedLabel}\" @bind=\"StockCounted\" @bind:event=\"oninput\" />",
        $"    <button type=\"button\" @onclick=\"ShowStockAsync\">{ShowCaption}</button>",
        $"    <button type=\"button\" @onclick=\"TakeStockAsync\">{TakeCaption}</button>",
        $"    <button type=\"button\" @onclick=\"ShowLowAsync\">{LowCaption}</button>",
        $"    <span role=\"status\" aria-label=\"{ResultLabel}\">@StockText</span>",
        "</div>",
    ];

    public static IReadOnlyList<string> BlazorHandlers(string client) =>
    [
        "",
        "    // A stock table (StockTables): the stock of an item, a stock-take that writes an adjustment row, and the items at or under their reorder level.",
        "    private string? StockItem;",
        "    private string? StockCounted;",
        "    private string? StockText;",
        "",
        "    private async Task ShowStockAsync()",
        "    {",
        "        string item = (StockItem ?? \"\").Trim();",
        $"        if (item.Length == 0) {{ StockText = \"{NeedsItem}\"; return; }}",
        "        try",
        "        {",
        $"            var row = await {client}.StockAvailableAsync(item);",
        $"            StockText = {ShownCs};",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ StockText = \"{NoneText}\"; }}",
        "    }",
        "",
        "    private async Task TakeStockAsync()",
        "    {",
        "        string item = (StockItem ?? \"\").Trim(), counted = (StockCounted ?? \"\").Trim();",
        $"        if (item.Length == 0) {{ StockText = \"{NeedsItem}\"; return; }}",
        $"        if (counted.Length == 0) {{ StockText = \"{NeedsCount}\"; return; }}",
        "        try",
        "        {",
        $"            var row = await {client}.StockAdjustAsync(item, counted);",
        $"            StockText = {TakenCs};",
        "            await LoadAsync(pageNumber);",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException or FormatException) {{ StockText = \"{RefusedText}\"; }}",
        "    }",
        "",
        "    private async Task ShowLowAsync()",
        "    {",
        "        try",
        "        {",
        $"            var row = await {client}.StockLowAsync();",
        $"            StockText = {LowCs};",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ StockText = \"{LowFailedText}\"; }}",
        "    }",
    ];

    public static string BlazorBoxTest() =>
        "\n"
        + "    [TestMethod]\n"
        + "    public void Shows_the_stock_of_an_item_takes_stock_and_lists_what_is_low()\n"
        + "    {\n"
        + $"        Api.Answer(\"/{StockPlan.AvailableRoute}\", \"{LevelJson}\");\n"
        + $"        Api.Answer(\"/{StockPlan.LowRoute}\", \"{LowJson}\");\n"
        + $"        Api.AnswerWrite(HttpMethod.Post, \"/{StockPlan.AdjustRoute}\", \"{TakenJson}\");\n"
        + "        var page = Open();\n"
        + $"        page.Find(\"[aria-label=\\\"{ItemLabel}\\\"]\").Input(\" 7 \");\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{ShowCaption}\").Click();\n"
        + $"        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"{ShownTest}\"));\n"
        + $"        Assert.IsTrue(Api.Requests.Any(r => r.Path.EndsWith(\"/{StockPlan.AvailableRoute}?item=7\")));\n"
        + $"        page.Find(\"[aria-label=\\\"{CountedLabel}\\\"]\").Input(\"9\");\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{TakeCaption}\").Click();\n"
        + $"        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"{TakenTest}\"));\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{LowCaption}\").Click();\n"
        + $"        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"{LowTest}\"));\n"
        + "    }\n"
        + "\n";

    /// <summary> The boxes, the buttons and the answer in the top bar of a WinUI3 or Uno list (sixteen spaces in). </summary>
    public static string DesktopBox() =>
        $"                <TextBox Width=\"90\" PlaceholderText=\"{ItemHint}\" AutomationProperties.Name=\"{ItemLabel}\" Text=\"{{x:Bind ViewModel.StockItem, Mode=TwoWay}}\" />\n"
        + $"                <TextBox Width=\"90\" PlaceholderText=\"{CountedHint}\" AutomationProperties.Name=\"{CountedLabel}\" Text=\"{{x:Bind ViewModel.StockCounted, Mode=TwoWay}}\" />\n"
        + $"                <Button Content=\"{ShowCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnShowStockClick\" />\n"
        + $"                <Button Content=\"{TakeCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnTakeStockClick\" />\n"
        + $"                <Button Content=\"{LowCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnShowLowClick\" />\n"
        + $"                <TextBlock VerticalAlignment=\"Center\" Text=\"{{x:Bind ViewModel.StockText, Mode=OneWay}}\" />\n";

    public static string DesktopHandlers() =>
        "    private async void OnShowStockClick(object sender, RoutedEventArgs e) => await ViewModel.ShowStockAsync();\n"
        + "    private async void OnTakeStockClick(object sender, RoutedEventArgs e) => await ViewModel.TakeStockAsync();\n"
        + "    private async void OnShowLowClick(object sender, RoutedEventArgs e) => await ViewModel.ShowLowAsync();\n";

    /// <summary> The members of the Uno list's view model: the three boxes of the bar, read and written through the API client. </summary>
    public static string UnoMembers() =>
        "    /// <summary> The boxes and the answer of the stock bar (StockTables). </summary>\n"
        + "    [ObservableProperty]\n    private string? _stockItem;\n\n"
        + "    [ObservableProperty]\n    private string? _stockCounted;\n\n"
        + "    [ObservableProperty]\n    private string? _stockText;\n\n"
        + "    public async Task ShowStockAsync()\n    {\n"
        + $"        if (string.IsNullOrWhiteSpace(StockItem)) {{ StockText = \"{NeedsItem}\"; return; }}\n"
        + "        try\n        {\n"
        + "            var row = await Client.StockAvailableAsync(StockItem.Trim());\n"
        + $"            StockText = {ShownCs};\n"
        + "        }\n"
        + $"        catch (Exception) {{ StockText = \"{NoneText}\"; }}\n"
        + "    }\n\n"
        + "    public async Task TakeStockAsync()\n    {\n"
        + $"        if (string.IsNullOrWhiteSpace(StockItem)) {{ StockText = \"{NeedsItem}\"; return; }}\n"
        + $"        if (string.IsNullOrWhiteSpace(StockCounted)) {{ StockText = \"{NeedsCount}\"; return; }}\n"
        + "        try\n        {\n"
        + "            var row = await Client.StockAdjustAsync(StockItem.Trim(), StockCounted.Trim());\n"
        + $"            StockText = {TakenCs};\n"
        + "            await LoadAsync();\n"
        + "        }\n"
        + $"        catch (Exception) {{ StockText = \"{RefusedText}\"; }}\n"
        + "    }\n\n"
        + "    public async Task ShowLowAsync()\n    {\n"
        + "        try\n        {\n"
        + "            var row = await Client.StockLowAsync();\n"
        + $"            StockText = {LowCs};\n"
        + "        }\n"
        + $"        catch (Exception) {{ StockText = \"{LowFailedText}\"; }}\n"
        + "    }\n\n";

    /// <summary> The members of the WinUI3 list's view model: the three boxes of the bar, read and written through the list's own context with the guarded update of the API. </summary>
    public static string WinUI3Members(StockShape s, string context)
    {
        string level = s.Level.TableName, ledger = s.Ledger.Table.TableName, items = s.ItemTable.TableName;
        string itemType = s.Item.CSharpBase(), q = s.QuantityType;
        string lvItem = s.Item.CSharpName(), lvOn = s.OnHand.CSharpName(), lvRes = s.Reserved.CSharpName(), itemKey = s.ItemKey.CSharpName();
        string reorder = s.Reorder is null ? "0" : "x." + s.Reorder.CSharpName();
        string reorderPick = s.Reorder is null ? "Reorder = 0" : "Reorder = x." + s.Reorder.CSharpName();
        var l = s.Ledger;
        string ledgerRef = l.Reference is null ? "" : $", {l.Reference.CSharpName()} = \"stock-take\"";
        return $$"""
    /// <summary> The boxes and the answer of the stock bar (StockTables). </summary>
    [ObservableProperty]
    private string? _stockItem;

    [ObservableProperty]
    private string? _stockCounted;

    [ObservableProperty]
    private string? _stockText;

    public async Task ShowStockAsync()
    {
        if (!{{itemType}}.TryParse((StockItem ?? "").Trim(), out {{itemType}} item)) { StockText = "{{NeedsItem}}"; return; }
        if (!await {{context}}.Set<{{items}}>().AsNoTracking().AnyAsync(x => x.{{itemKey}} == item)) { StockText = "{{NoneText}}"; return; }
        var row = await {{context}}.Set<{{level}}>().AsNoTracking().Where(x => x.{{lvItem}} == item).Select(x => new { x.{{lvOn}}, x.{{lvRes}}, {{reorderPick}} }).FirstOrDefaultAsync();
        long onHand = row is null ? 0 : row.{{lvOn}}, reserved = row is null ? 0 : row.{{lvRes}}, reorder = row is null ? 0 : {{(s.Reorder is null ? "0" : "row.Reorder")}};
        StockText = $"{onHand - reserved} available ({onHand} on hand, {reserved} reserved){(onHand - reserved <= reorder ? ", low" : "")}";
    }

    public async Task TakeStockAsync()
    {
        if (!{{itemType}}.TryParse((StockItem ?? "").Trim(), out {{itemType}} item)) { StockText = "{{NeedsItem}}"; return; }
        if (!{{q}}.TryParse((StockCounted ?? "").Trim(), out {{q}} counted) || counted < 0) { StockText = "{{NeedsCount}}"; return; }
        if (!await {{context}}.Set<{{items}}>().AsNoTracking().AnyAsync(x => x.{{itemKey}} == item)) { StockText = "{{NoneText}}"; return; }
        var stored = await {{context}}.Set<{{level}}>().AsNoTracking().Where(x => x.{{lvItem}} == item).Select(x => new { x.{{lvOn}}, x.{{lvRes}} }).FirstOrDefaultAsync();
        {{q}} onHand = stored is null ? 0 : stored.{{lvOn}}, reserved = stored is null ? 0 : stored.{{lvRes}};
        if (counted < reserved) { StockText = "{{RefusedText}}"; return; }
        await using var transaction = await {{context}}.Database.BeginTransactionAsync();
        try
        {
            if (stored is null)
                {{context}}.Set<{{level}}>().Add(new {{level}} { {{lvItem}} = item, {{lvOn}} = counted });
            else if (await {{context}}.Set<{{level}}>().Where(x => x.{{lvItem}} == item && x.{{lvOn}} == onHand && x.{{lvRes}} == reserved).ExecuteUpdateAsync(s => s.SetProperty(x => x.{{lvOn}}, counted)) == 0)
            {
                StockText = "{{StockWords.Conflict}}";
                return;
            }
            {{context}}.Set<{{ledger}}>().Add(new {{ledger}} { {{l.Item.CSharpName()}} = item, {{l.Kind.CSharpName()}} = "{{StockPlan.Adjustment}}", {{l.Quantity.CSharpName()}} = ({{q}})(counted - onHand), {{l.Stamp.CSharpName()}} = DateTime.Now{{ledgerRef}} });
            await {{context}}.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            StockText = "{{StockWords.Conflict}}";
            return;
        }
        StockText = $"Counted {counted} ({(counted - onHand >= 0 ? "+" : "")}{counted - onHand}), {counted - reserved} available";
        await LoadAsync();
    }

    public async Task ShowLowAsync()
    {
        var rows = await {{context}}.Set<{{level}}>().AsNoTracking().Where(x => x.{{lvOn}} - x.{{lvRes}} <= {{reorder}}).OrderBy(x => x.{{lvOn}} - x.{{lvRes}}).ThenBy(x => x.{{lvItem}}).Select(x => new { Item = x.{{lvItem}}, Available = x.{{lvOn}} - x.{{lvRes}} }).ToListAsync();
        StockText = rows.Count == 0 ? "{{NothingLow}}" : "Low: " + string.Join(", ", rows.Select(r => $"{r.Item} ({r.Available})"));
    }


""";
    }
}
