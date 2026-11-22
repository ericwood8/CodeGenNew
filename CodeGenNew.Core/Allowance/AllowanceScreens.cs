using CodeGenNew.Core.Rate;

namespace CodeGenNew.Core.Allowance;

/// <summary> What the screens add for <c>AllowanceCharges</c> when the entry names the document column: on the list of the table a document box, a button and a line that says the allowances, the charges and
/// the net of that document (the answer of <c>GET allowance-charge-totals</c>), and, for WinUI3, the same rule as the API runs before a dialog saves (it writes through its own context). </summary>
public static class AllowanceScreens
{
    public const string RouteName = AllowanceChargeWords.TotalsRoute;
    public const string Route = "allowanceTotals";
    public const string CsMethod = "AllowanceChargeTotalsAsync";
    public const string DocumentLabel = "Allowance document";
    public const string ShowCaption = "Show totals";
    public const string StatusLabel = "Allowance totals";
    public const string NeedsText = "Give the document.";
    public const string NoneText = "No allowances or charges for that document.";
    public const string FailText = "Could not read the totals.";

    /// <summary> Whether the table's list screen gets the totals box: the entry names the document column. </summary>
    public static bool HasBox(TableModel table, ProjectSettings project) =>
        AllowanceChargePlan.Entries(project).Any(e => e.Table.EqualsIgnoreCase(table.TableName) && e.Document is not null);

    private const string TsRow = "{ allowances: number; charges: number; net: number; rows: number }";

    private const string TsText = "t.rows === 0 ? '" + NoneText + "' : `Allowances ${t.allowances}, charges ${t.charges}, net ${t.net} (${t.rows} rows)`";

    public static string ReactApiLine() =>
        $"  {Route}: (document: string) => request<{TsRow}>(`${{apiUrl}}/{RouteName}?document=${{encodeURIComponent(document)}}`),";

    public static string ReactODataApiLine(string restPath) =>
        $"  {Route}: (document: string) => request<{TsRow}>(`{restPath}/{RouteName}?document=${{encodeURIComponent(document)}}`),";

    public static IReadOnlyList<string> ReactHandlers(string apiVar) =>
    [
        "",
        "  // a table of AllowanceCharges: the allowances, the charges and the net of one document",
        "  const [allowanceDocument, setAllowanceDocument] = useState('');",
        "  const [allowanceText, setAllowanceText] = useState('');",
        "  const showAllowances = () => {",
        "    const document = allowanceDocument.trim();",
        $"    if (!document) {{ setAllowanceText('{NeedsText}'); return; }}",
        $"    {apiVar}.{Route}(document)",
        $"      .then((t) => setAllowanceText({TsText}))",
        $"      .catch(() => setAllowanceText('{FailText}'));",
        "  };",
    ];

    public static IReadOnlyList<string> ReactBox() =>
    [
        "",
        "      <div>",
        $"        <input type=\"text\" placeholder=\"Document\" aria-label=\"{DocumentLabel}\" value={{allowanceDocument}} onChange={{(event) => setAllowanceDocument(event.target.value)}} />",
        $"        <button type=\"button\" onClick={{showAllowances}}>{ShowCaption}</button>",
        $"        <span role=\"status\" aria-label=\"{StatusLabel}\">{{allowanceText}}</span>",
        "      </div>",
    ];

    public static IReadOnlyList<string> ReactBoxTests(string apiVar, string pageName) =>
    [
        "",
        "  it('shows the allowances and charges of a document, or says there are none', async () => {",
        $"    const found = vi.spyOn({apiVar}, '{Route}').mockResolvedValueOnce({{ allowances: 55.02, charges: 60, net: 4.98, rows: 5 }}).mockResolvedValueOnce({{ allowances: 0, charges: 0, net: 0, rows: 0 }});",
        $"    render(<MemoryRouter><{pageName} /></MemoryRouter>);",
        $"    fireEvent.change(await screen.findByLabelText('{DocumentLabel}'), {{ target: {{ value: ' 7 ' }} }});",
        $"    fireEvent.click(screen.getByText('{ShowCaption}'));",
        "    expect(found).toHaveBeenCalledWith('7');",
        "    expect(await screen.findByText('Allowances 55.02, charges 60, net 4.98 (5 rows)')).toBeInTheDocument();",
        $"    fireEvent.click(screen.getByText('{ShowCaption}'));",
        $"    expect(await screen.findByText('{NoneText}')).toBeInTheDocument();",
        "  });",
    ];

    public static IReadOnlyList<string> AngularServiceLines(string urlMember) =>
    [
        "",
        "    // a table of AllowanceCharges: the allowances, the charges and the net of one document",
        $"    {Route}(document: string): Observable<{TsRow}> {{",
        $"        return this.http.get<{TsRow}>(`${{this.{urlMember}}}/{RouteName}?document=${{encodeURIComponent(document)}}`);",
        "    }",
    ];

    public static IReadOnlyList<string> AngularBox() =>
    [
        "",
        "    <!-- the allowances, the charges and the net of one document -->",
        "    <div class=\"form-group form-group-search dept\">",
        $"        <input type=\"text\" class=\"form-control\" placeholder=\"Document\" aria-label=\"{DocumentLabel}\" [(ngModel)]=\"allowanceDocument\" />",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"showAllowances()\"> {ShowCaption} </button>",
        $"        <span role=\"status\" aria-label=\"{StatusLabel}\">{{{{ allowanceText }}}}</span>",
        "    </div>",
    ];

    public static IReadOnlyList<string> AngularHandlers() =>
    [
        "",
        "  // a table of AllowanceCharges: the allowances, the charges and the net of one document",
        "  allowanceDocument = '';",
        "  allowanceText = '';",
        "",
        "  showAllowances(): void {",
        "    const document = this.allowanceDocument.trim();",
        $"    if (!document) {{ this.allowanceText = '{NeedsText}'; return; }}",
        $"    this.service.{Route}(document).subscribe({{",
        $"      next: (t) => {{ this.allowanceText = {TsText}; }},",
        $"      error: () => {{ this.allowanceText = '{FailText}'; }}",
        "    });",
        "  }",
    ];

    public static string AngularBoxTest() =>
        "  it('shows the allowances and charges of a document', () => {\n"
        + "    const http = TestBed.inject(HttpTestingController);\n"
        + "    component.allowanceDocument = ' 7 ';\n"
        + "    component.showAllowances();\n"
        + $"    http.expectOne((r) => r.urlWithParams.endsWith('/{RouteName}?document=7')).flush({{ allowances: 55.02, charges: 60, net: 4.98, rows: 5 }});\n"
        + "    expect(component.allowanceText).toBe('Allowances 55.02, charges 60, net 4.98 (5 rows)');\n"
        + "  });\n";

    public static IReadOnlyList<string> CsClientLines() =>
    [
        "",
        "    // A table of AllowanceCharges: the allowances, the charges and the net of one document.",
        "    public sealed record AllowanceTotals(decimal Allowances, decimal Charges, decimal Net, int Rows);",
        "",
    ];

    public static IReadOnlyList<string> CsClientMethod(string route) =>
    [
        $"    public async Task<AllowanceTotals> {CsMethod}(string document) => await (await http.GetAsync($\"{route}/{RouteName}?document={{Uri.EscapeDataString(document)}}\")).ReadAsync<AllowanceTotals>();",
    ];

    public static IReadOnlyList<string> BlazorBox() =>
    [
        "",
        "<div class=\"search\">",
        $"    <input type=\"text\" placeholder=\"Document\" aria-label=\"{DocumentLabel}\" @bind=\"allowanceDocument\" @bind:event=\"oninput\" />",
        $"    <button type=\"button\" @onclick=\"ShowAllowancesAsync\">{ShowCaption}</button>",
        $"    <span role=\"status\" aria-label=\"{StatusLabel}\">@allowanceText</span>",
        "</div>",
    ];

    public static IReadOnlyList<string> BlazorHandlers(string client) =>
    [
        "",
        "    // A table of AllowanceCharges: the allowances, the charges and the net of one document.",
        "    private string? allowanceDocument;",
        "    private string? allowanceText;",
        "",
        "    private async Task ShowAllowancesAsync()",
        "    {",
        "        string document = (allowanceDocument ?? \"\").Trim();",
        $"        if (document.Length == 0) {{ allowanceText = \"{NeedsText}\"; return; }}",
        "        try",
        "        {",
        $"            var t = await {client}.{CsMethod}(document);",
        $"            allowanceText = t.Rows == 0 ? \"{NoneText}\" : $\"Allowances {{t.Allowances}}, charges {{t.Charges}}, net {{t.Net}} ({{t.Rows}} rows)\";",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ allowanceText = \"{FailText}\"; }}",
        "    }",
    ];

    public static string BlazorBoxTest() =>
        "\n"
        + "    [TestMethod]\n"
        + "    public void Shows_the_allowances_and_charges_of_a_document()\n"
        + "    {\n"
        + $"        Api.Answer(\"/{RouteName}\", \"{{ \\\"allowances\\\": 55.02, \\\"charges\\\": 60, \\\"net\\\": 4.98, \\\"rows\\\": 5 }}\");\n"
        + "        var page = Open();\n"
        + $"        page.Find(\"[aria-label=\\\"{DocumentLabel}\\\"]\").Input(\" 7 \");\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{ShowCaption}\").Click();\n"
        + "        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"Allowances 55.02, charges 60, net 4.98 (5 rows)\"));\n"
        + $"        Assert.IsTrue(Api.Requests.Any(r => r.Path.EndsWith(\"/{RouteName}?document=7\")));\n"
        + "    }\n"
        + "\n";

    /// <summary> The box, the button and the answer in the top bar of a WinUI3 or Uno list (sixteen spaces in). </summary>
    public static string DesktopBox() =>
        $"                <TextBox Width=\"90\" PlaceholderText=\"Document\" AutomationProperties.Name=\"{DocumentLabel}\" Text=\"{{x:Bind ViewModel.AllowanceDocument, Mode=TwoWay}}\" />\n"
        + $"                <Button Content=\"{ShowCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnShowAllowancesClick\" />\n"
        + $"                <TextBlock VerticalAlignment=\"Center\" Text=\"{{x:Bind ViewModel.AllowanceText, Mode=OneWay}}\" />\n";

    public static string DesktopHandlers() =>
        "    private async void OnShowAllowancesClick(object sender, RoutedEventArgs e) => await ViewModel.ShowAllowancesAsync();\n";

    /// <summary> The members of the list's view model; <paramref name="read"/> is the body after the document is checked (it sets <c>AllowanceText</c>). </summary>
    public static string DesktopMembers(string read) =>
        "    /// <summary> The box and the answer of the allowances and charges of one document (AllowanceCharges). </summary>\n"
        + "    [ObservableProperty]\n    private string? _allowanceDocument;\n\n"
        + "    [ObservableProperty]\n    private string? _allowanceText;\n\n"
        + "    public async Task ShowAllowancesAsync()\n"
        + "    {\n"
        + "        string document = (AllowanceDocument ?? \"\").Trim();\n"
        + $"        if (document.Length == 0) {{ AllowanceText = \"{NeedsText}\"; return; }}\n"
        + read
        + "    }\n\n";

    private const string FormatLine = "AllowanceText = t.Rows == 0 ? \"" + NoneText + "\" : $\"Allowances {t.Allowances}, charges {t.Charges}, net {t.Net} ({t.Rows} rows)\";";

    /// <summary> The Uno read: the API's route. </summary>
    public static string UnoRead() =>
        "        try\n"
        + "        {\n"
        + $"            var t = await Client.{CsMethod}(document);\n"
        + "            " + FormatLine + "\n"
        + "        }\n"
        + $"        catch (Exception) {{ AllowanceText = \"{FailText}\"; }}\n";

    /// <summary> The WinUI3 read: the same query as the API's route, over the dialog's own context. </summary>
    public static string WinUI3Read(TableModel table, ProjectSettings project, string context)
    {
        if (AllowanceChargePlan.For(table, project) is not { Document: not null } s)
            return "";
        return $"        if (!{s.Document.CSharpBase()}.TryParse(document, System.Globalization.CultureInfo.InvariantCulture, out var documentId)) {{ AllowanceText = \"{NeedsText}\"; return; }}\n"
            + $"        var rows = await {context}.Set<{s.Table.TableName}>().AsNoTracking().Where(x => x.{s.Document.CSharpName()} == documentId).Select(x => new {{ Charge = x.{s.Flag.CSharpName()}, Amount = x.{s.Amount.CSharpName()} }}).ToListAsync();\n"
            + "        var t = new { Allowances = rows.Where(r => !r.Charge).Sum(r => (decimal)r.Amount), Charges = rows.Where(r => r.Charge).Sum(r => (decimal)r.Amount), Rows = rows.Count };\n"
            + "        var net = t.Charges - t.Allowances;\n"
            + "        AllowanceText = t.Rows == 0 ? \"" + NoneText + "\" : $\"Allowances {t.Allowances}, charges {t.Charges}, net {net} ({t.Rows} rows)\";\n";
    }

    /// <summary> The lines a WinUI3 dialog runs before it saves a table of <c>AllowanceCharges</c> (it writes through its own context, not through the API): the same rule as <c>CheckAllowanceCharge</c> of the API.
    /// Empty when the table is not listed. </summary>
    public static string WinUI3Check(TableModel table, ProjectSettings project)
    {
        if (AllowanceChargePlan.For(table, project) is not { } s)
            return "";
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append("        ").Append(text).Append('\n');
        L("{");
        var names = new AllowanceChargeNames("entity", "allowanceBase", "allowancePercent", "allowanceAmount");
        foreach (string line in AllowanceChargeBlock.Lines(s, names, (condition, message) => [condition, "{", $"    ErrorMessage = \"{message}\";", "    return false;", "}"]))
            L("    " + line);
        L("}");
        return o.Append('\n').ToString();
    }

    /// <summary> Whether the child grid of a document's master-detail page (<paramref name="childTable"/>, its foreign key column <paramref name="fkColumn"/>) is the table of <c>AllowanceCharges</c> whose document column
    /// is that key: the page then shows a line with the allowances, the charges and the net under the grid. </summary>
    public static bool IsChild(string childTable, string fkColumn, ProjectSettings project) =>
        AllowanceChargePlan.Entries(project).Any(e => e.Table.EqualsIgnoreCase(childTable) && e.Document is not null && e.Document.EqualsIgnoreCase(fkColumn));

    public static string ReactChildState(string variable, string name) => $"  const [{variable}Allowance, set{name}Allowance] = useState('');";

    public static IReadOnlyList<string> ReactChildLoad(string name, string apiPrefix, string route) =>
    [
        $"    set{name}Allowance('');",
        $"    request<{TsRow}>(`{apiPrefix}/{route}/{RouteName}?document=${{encodeURIComponent(String(parentId))}}`).then((t) => set{name}Allowance(t.rows === 0 ? '' : `Allowances ${{t.allowances}}, charges ${{t.charges}}, net ${{t.net}} (${{t.rows}} rows)`)).catch(() => {{}});",
    ];

    public static string ReactChildText(string variable) =>
        $"              {{{variable}Allowance && <p role=\"status\" aria-label=\"{StatusLabel}\">{{{variable}Allowance}}</p>}}";

    public static string AngularChildState(string variable) => $"  {variable}Allowance = '';";

    public static IReadOnlyList<string> AngularChildLoad(string variable, string apiPrefix, string route) =>
    [
        $"    this.{variable}Allowance = '';",
        $"    this.http.get<{TsRow}>(`{apiPrefix}/{route}/{RouteName}?document=${{encodeURIComponent(String(parentId))}}`).subscribe({{ next: (t) => {{ this.{variable}Allowance = t.rows === 0 ? '' : `Allowances ${{t.allowances}}, charges ${{t.charges}}, net ${{t.net}} (${{t.rows}} rows)`; }}, error: () => {{ this.{variable}Allowance = ''; }} }});",
    ];

    public static string AngularChildText(string variable) =>
        $"            <p *ngIf=\"{variable}Allowance\" role=\"status\" aria-label=\"{StatusLabel}\">{{{{ {variable}Allowance }}}}</p>";

    public static string BlazorChildField(string variable) => $"    private string? {variable}AllowanceText;";

    public static string BlazorChildLoad(string variable) =>
        $"        try {{ var allowanceTotals = await {variable}Client.{CsMethod}(Convert.ToString(rowKey, System.Globalization.CultureInfo.InvariantCulture)!); {variable}AllowanceText = allowanceTotals.Rows == 0 ? null : $\"Allowances {{allowanceTotals.Allowances}}, charges {{allowanceTotals.Charges}}, net {{allowanceTotals.Net}} ({{allowanceTotals.Rows}} rows)\"; }} catch (Exception ex) when (ex is ApiException or HttpRequestException or System.Text.Json.JsonException) {{ {variable}AllowanceText = null; }}";

    public static string BlazorChildText(string variable) =>
        $"            @if (!string.IsNullOrEmpty({variable}AllowanceText))\n            {{\n                <p role=\"status\" aria-label=\"{StatusLabel}\">@{variable}AllowanceText</p>\n            }}";

    /// <summary> The call that fills the line after the child rows are loaded (empty when the child is not the table of <c>AllowanceCharges</c>). </summary>
    public static string DesktopLoadCall(string childTable, string fkColumn, string member, ProjectSettings project) =>
        IsChild(childTable, fkColumn, project) ? $"        await Load{member}AllowanceAsync();\n" : "";

    /// <summary> The read-only text under the child grid in the dialog's XAML (twelve spaces in). </summary>
    public static string DesktopDialogRow(string childTable, string fkColumn, string member, ProjectSettings project) =>
        IsChild(childTable, fkColumn, project) ? $"\n            <TextBlock TextWrapping=\"Wrap\" Text=\"{{x:Bind ViewModel.{member}AllowanceSummary, Mode=OneWay}}\" />\n" : "";

    private static string DesktopSummary(string member, string body, string keyExpression) =>
        "    /// <summary> The allowances, the charges and the net of the document being edited, read-only (AllowanceCharges). </summary>\n"
        + $"    [ObservableProperty]\n    private string? _{char.ToLowerInvariant(member[0])}{member[1..]}AllowanceSummary;\n\n"
        + $"    private async Task Load{member}AllowanceAsync()\n"
        + "    {\n"
        + "        if (_editing is null)\n            return;\n"
        + $"        var documentId = {keyExpression};\n"
        + body
        + "    }\n\n";

    private static string SummaryText(string member) =>
        $"        {member}AllowanceSummary = t.Rows == 0 ? \"\" : $\"Allowances {{t.Allowances}}, charges {{t.Charges}}, net {{t.Net}} ({{t.Rows}} rows)\";\n";

    /// <summary> WinUI3: the line of the document being edited, read through the dialog's own context. </summary>
    public static string WinUI3DialogMembers(string childTable, string fkColumn, string member, ProjectSettings project, string context, string keyExpression)
    {
        if (!IsChild(childTable, fkColumn, project) || RatePlan.Remembered(project)?.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(childTable)) is not { } table || AllowanceChargePlan.For(table, project) is not { } s)
            return "";
        string body = $"        var rows = await {context}.Set<{s.Table.TableName}>().AsNoTracking().Where(x => x.{s.Document!.CSharpName()} == documentId).Select(x => new {{ Charge = x.{s.Flag.CSharpName()}, Amount = x.{s.Amount.CSharpName()} }}).ToListAsync();\n"
            + "        var t = new { Allowances = rows.Where(r => !r.Charge).Sum(r => (decimal)r.Amount), Charges = rows.Where(r => r.Charge).Sum(r => (decimal)r.Amount), Rows = rows.Count };\n"
            + $"        var net = t.Charges - t.Allowances;\n        {member}AllowanceSummary = t.Rows == 0 ? \"\" : $\"Allowances {{t.Allowances}}, charges {{t.Charges}}, net {{net}} ({{t.Rows}} rows)\";\n";
        return DesktopSummary(member, body, keyExpression);
    }

    /// <summary> Uno: the line read through the API's route of the table (<paramref name="clientMember"/>). </summary>
    public static string UnoDialogMembers(string childTable, string fkColumn, string member, ProjectSettings project, string clientMember, string keyExpression) =>
        !IsChild(childTable, fkColumn, project) ? ""
        : DesktopSummary(member,
            "        try\n"
            + "        {\n"
            + $"            var t = await {clientMember}.{CsMethod}(documentId.ToString()!);\n"
            + "    " + $"    {SummaryText(member).TrimStart()}"
            + "        }\n"
            + $"        catch (Exception) {{ {member}AllowanceSummary = \"\"; }}\n", keyExpression);
}
