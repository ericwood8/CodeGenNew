namespace CodeGenNew.Core.Tax;

/// <summary> What the screens add for <c>TaxLines</c>. </summary>
public static class TaxScreens
{
    /// <summary> The lines a WinUI3 dialog runs before it saves a table of <c>TaxLines</c> (it writes through its own context, not through the API; the same rule as <c>CheckTaxAsync</c> of the API): the rate of the
    /// category is copied when the row is created or its category or day changes (a refusal when the category does not exist or is not in force) and kept otherwise, and the tax is worked out. Empty when the
    /// table is not listed. </summary>
    public static string WinUI3Check(TableModel table, ProjectSettings project, string context)
    {
        if (TableShapes.For(table, project).TaxLine is not { } s)
            return "";
        string row = table.TableName, cat = s.Category.CSharpName(), rate = s.Rate.CSharpName(), tax = s.Tax.CSharpName(), key = s.Key.CSharpName();
        string day = s.Day?.CSharpName() ?? "";
        bool dating = s.ValidFrom is not null;
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append("        ").Append(text).Append('\n');
        L("{");
        if (s.Category.IsNullable)
        {
            L($"    if (entity.{cat} is null)");
            L("    {");
            L($"        entity.{rate} = {Zero(s.Rate)};");
            L($"        entity.{tax} = {Zero(s.Tax)};");
            L("    }");
            L("    else");
        }
        L("    {");
        L($"        var taxCategory = entity.{cat};");
        if (dating)
        {
            string catType = s.ValidFrom!.CSharpBase();
            string dayValue = s.Day is not null ? "entity." + day : catType == "DateOnly" ? "DateOnly.FromDateTime(DateTime.Now)" : "DateTime.Today";
            string asOf = s.Day is null || s.Day.CSharpBase() == catType ? dayValue : catType == "DateOnly" ? $"DateOnly.FromDateTime({dayValue})" : $"{dayValue}.ToDateTime(TimeOnly.MinValue)";
            L($"        var taxAsOf = {asOf};");
        }
        L("        bool taxKeep = false;");
        L("        if (_editing is not null)");
        L("        {");
        string sameDay = s.Day is null ? "" : s.Day.CSharpBase() == "DateOnly" ? $" && taxStored.{day} == entity.{day}" : $" && taxStored.{day}.Date == entity.{day}.Date";
        L($"            var taxStored = await {context}.Set<{row}>().AsNoTracking().Where(x => x.{key} == entity.{key}).Select(x => new {{ x.{cat}, x.{rate}{(s.Day is null ? "" : ", x." + day)} }}).FirstOrDefaultAsync();");
        L($"            if (taxStored is not null && taxStored.{cat} == taxCategory{sameDay})");
        L("            {");
        L($"                entity.{rate} = taxStored.{rate};");
        L("                taxKeep = true;");
        L("            }");
        L("        }");
        L("        if (!taxKeep)");
        L("        {");
        string dates = dating ? ", x." + s.ValidFrom!.CSharpName() + (s.ValidTo is null ? "" : ", x." + s.ValidTo.CSharpName()) : "";
        L($"            var taxFound = await {context}.Set<{s.CategoryTable.TableName}>().AsNoTracking().Where(x => x.{s.CategoryKey.CSharpName()} == taxCategory).Select(x => new {{ x.{s.CategoryRate.CSharpName()}{dates} }}).FirstOrDefaultAsync();");
        L("            if (taxFound is null)");
        L("            {");
        L("                ErrorMessage = $\"" + TaxWords.Unknown("{taxCategory}") + "\";");
        L("                return false;");
        L("            }");
        if (dating)
        {
            string ended = s.ValidTo is null ? "" : $" || (taxFound.{s.ValidTo.CSharpName()} is {{ }} taxEnd && taxEnd < taxAsOf)";
            L($"            if (taxFound.{s.ValidFrom!.CSharpName()} > taxAsOf{ended})");
            L("            {");
            L("                ErrorMessage = $\"" + TaxWords.NotInForce("{taxCategory}", "{taxAsOf:yyyy-MM-dd}") + "\";");
            L("                return false;");
            L("            }");
        }
        L($"            entity.{rate} = ({s.Rate.CSharpBase()})taxFound.{s.CategoryRate.CSharpName()};");
        L("        }");
        L($"        entity.{tax} = ({s.Tax.CSharpBase()})Math.Round((decimal)entity.{s.Taxable.CSharpName()} * (decimal)entity.{rate}, {s.Tax.Scale ?? TaxWords.DefaultScale}, MidpointRounding.AwayFromZero);");
        L("    }");
        L("}");
        return o.Append('\n').ToString();
    }

    public const string RouteName = "tax-subtotals";
    public const string Route = "taxSubtotals";
    public const string CsMethod = "TaxSubtotalsAsync";
    public const string DocumentLabel = "Tax document";
    public const string ShowCaption = "Show tax";
    public const string StatusLabel = "Tax status";
    public const string TableLabel = "Tax by category";
    public const string NeedsText = "Give the document.";
    public const string NoneText = "No lines for that document.";
    public const string FailText = "Could not read the tax.";

    /// <summary> Whether the table's list screen gets the subtotals box: the entry names the document column. </summary>
    public static bool HasBox(TableModel table, ProjectSettings project) =>
        TaxLinePlan.Entries(project).Any(e => e.Table.EqualsIgnoreCase(table.TableName) && e.Document is not null);

    private const string TsRow = "{ category: number | null; label: string | null; rate: number; taxable: number; tax: number; lines: number }";

    public static string ReactApiLine() =>
        $"  {Route}: (document: string) => request<Array<{TsRow}>>(`${{apiUrl}}/{RouteName}?document=${{encodeURIComponent(document)}}`),";

    public static string ReactODataApiLine(string restPath) =>
        $"  {Route}: (document: string) => request<Array<{TsRow}>>(`{restPath}/{RouteName}?document=${{encodeURIComponent(document)}}`),";

    public static IReadOnlyList<string> ReactHandlers(string apiVar) =>
    [
        "",
        "  // a table of TaxLines: the tax of one document by category and rate",
        "  const [taxDocument, setTaxDocument] = useState('');",
        $"  const [taxRows, setTaxRows] = useState<Array<{TsRow}>>([]);",
        "  const [taxText, setTaxText] = useState('');",
        "  const showTax = () => {",
        "    const document = taxDocument.trim();",
        $"    if (!document) {{ setTaxRows([]); setTaxText('{NeedsText}'); return; }}",
        $"    {apiVar}.{Route}(document)",
        $"      .then((rows) => {{ setTaxRows(rows); setTaxText(rows.length === 0 ? '{NoneText}' : ''); }})",
        $"      .catch(() => {{ setTaxRows([]); setTaxText('{FailText}'); }});",
        "  };",
    ];

    public static IReadOnlyList<string> ReactBox() =>
    [
        "",
        "      <div>",
        $"        <input type=\"text\" placeholder=\"Document\" aria-label=\"{DocumentLabel}\" value={{taxDocument}} onChange={{(event) => setTaxDocument(event.target.value)}} />",
        $"        <button type=\"button\" onClick={{showTax}}>{ShowCaption}</button>",
        $"        <span role=\"status\" aria-label=\"{StatusLabel}\">{{taxText}}</span>",
        "        {taxRows.length > 0 && (",
        $"          <table aria-label=\"{TableLabel}\">",
        "            <thead><tr><th>Category</th><th>Rate</th><th>Taxable</th><th>Tax</th><th>Lines</th></tr></thead>",
        "            <tbody>",
        "              {taxRows.map((row, index) => (",
        "                <tr key={index}><td>{row.label ?? row.category ?? ''}</td><td>{+(row.rate * 100).toFixed(2)}%</td><td>{row.taxable}</td><td>{row.tax}</td><td>{row.lines}</td></tr>",
        "              ))}",
        "            </tbody>",
        "          </table>",
        "        )}",
        "      </div>",
    ];

    public static IReadOnlyList<string> ReactBoxTests(string apiVar, string pageName) =>
    [
        "",
        "  it('shows the tax of a document by category, or says there is none', async () => {",
        $"    const found = vi.spyOn({apiVar}, '{Route}').mockResolvedValueOnce([{{ category: 1, label: 'VAT20', rate: 20, taxable: 100, tax: 20, lines: 2 }}]).mockResolvedValueOnce([]);",
        $"    render(<MemoryRouter><{pageName} /></MemoryRouter>);",
        $"    fireEvent.change(await screen.findByLabelText('{DocumentLabel}'), {{ target: {{ value: ' 7 ' }} }});",
        $"    fireEvent.click(screen.getByText('{ShowCaption}'));",
        "    expect(found).toHaveBeenCalledWith('7');",
        "    expect(await screen.findByText('VAT20')).toBeInTheDocument();",
        $"    fireEvent.click(screen.getByText('{ShowCaption}'));",
        $"    expect(await screen.findByText('{NoneText}')).toBeInTheDocument();",
        "  });",
    ];

    public static IReadOnlyList<string> AngularServiceLines(string urlMember) =>
    [
        "",
        "    // a table of TaxLines: the tax of one document by category and rate",
        $"    {Route}(document: string): Observable<Array<{TsRow}>> {{",
        $"        return this.http.get<Array<{TsRow}>>(`${{this.{urlMember}}}/{RouteName}?document=${{encodeURIComponent(document)}}`);",
        "    }",
    ];

    public static IReadOnlyList<string> AngularBox() =>
    [
        "",
        "    <!-- the tax of one document by category and rate -->",
        "    <div class=\"form-group form-group-search dept\">",
        $"        <input type=\"text\" class=\"form-control\" placeholder=\"Document\" aria-label=\"{DocumentLabel}\" [(ngModel)]=\"taxDocument\" />",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"showTax()\"> {ShowCaption} </button>",
        $"        <span role=\"status\" aria-label=\"{StatusLabel}\">{{{{ taxText }}}}</span>",
        "    </div>",
        $"    <table class=\"table\" *ngIf=\"taxRows.length > 0\" aria-label=\"{TableLabel}\">",
        "        <thead><tr><th>Category</th><th>Rate</th><th>Taxable</th><th>Tax</th><th>Lines</th></tr></thead>",
        "        <tbody>",
        "            <tr *ngFor=\"let row of taxRows\"><td>{{ row.label ?? row.category }}</td><td>{{ (row.rate * 100).toFixed(2) }}%</td><td>{{ row.taxable }}</td><td>{{ row.tax }}</td><td>{{ row.lines }}</td></tr>",
        "        </tbody>",
        "    </table>",
    ];

    public static IReadOnlyList<string> AngularHandlers() =>
    [
        "",
        "  // a table of TaxLines: the tax of one document by category and rate",
        "  taxDocument = '';",
        $"  taxRows: Array<{TsRow}> = [];",
        "  taxText = '';",
        "",
        "  showTax(): void {",
        "    const document = this.taxDocument.trim();",
        $"    if (!document) {{ this.taxRows = []; this.taxText = '{NeedsText}'; return; }}",
        $"    this.service.{Route}(document).subscribe({{",
        $"      next: (rows) => {{ this.taxRows = rows; this.taxText = rows.length === 0 ? '{NoneText}' : ''; }},",
        $"      error: () => {{ this.taxRows = []; this.taxText = '{FailText}'; }}",
        "    });",
        "  }",
    ];

    public static string AngularBoxTest() =>
        "  it('shows the tax of a document by category', () => {\n"
        + "    const http = TestBed.inject(HttpTestingController);\n"
        + "    component.taxDocument = ' 7 ';\n"
        + "    component.showTax();\n"
        + $"    http.expectOne((r) => r.urlWithParams.endsWith('/{RouteName}?document=7')).flush([{{ category: 1, label: 'VAT20', rate: 20, taxable: 100, tax: 20, lines: 2 }}]);\n"
        + "    expect(component.taxRows.length).toBe(1);\n"
        + "    expect(component.taxRows[0].label).toBe('VAT20');\n"
        + "    expect(component.taxText).toBe('');\n"
        + "  });\n";

    public static IReadOnlyList<string> CsClientLines() =>
    [
        "",
        "    // A table of TaxLines: the tax of one document by category and rate.",
        "    public sealed record TaxSubtotal(long? Category, string? Label, decimal Rate, decimal Taxable, decimal Tax, int Lines);",
        "",
    ];

    public static IReadOnlyList<string> CsClientMethod(string route) =>
    [
        $"    public async Task<List<TaxSubtotal>> {CsMethod}(string document) => await (await http.GetAsync($\"{route}/{RouteName}?document={{Uri.EscapeDataString(document)}}\")).ReadAsync<List<TaxSubtotal>>();",
    ];

    public static IReadOnlyList<string> BlazorBox() =>
    [
        "",
        "<div class=\"search\">",
        $"    <input type=\"text\" placeholder=\"Document\" aria-label=\"{DocumentLabel}\" @bind=\"taxDocument\" @bind:event=\"oninput\" />",
        $"    <button type=\"button\" @onclick=\"ShowTaxAsync\">{ShowCaption}</button>",
        $"    <span role=\"status\" aria-label=\"{StatusLabel}\">@taxText</span>",
        "</div>",
        "@if (taxRows.Count > 0)",
        "{",
        $"    <table aria-label=\"{TableLabel}\">",
        "        <thead><tr><th>Category</th><th>Rate</th><th>Taxable</th><th>Tax</th><th>Lines</th></tr></thead>",
        "        <tbody>",
        "            @foreach (var row in taxRows)",
        "            {",
        "                <tr><td>@row.Label</td><td>@((row.Rate * 100).ToString(\"0.##\"))%</td><td>@row.Taxable</td><td>@row.Tax</td><td>@row.Lines</td></tr>",
        "            }",
        "        </tbody>",
        "    </table>",
        "}",
    ];

    public static IReadOnlyList<string> BlazorHandlers(string client) =>
    [
        "",
        "    // A table of TaxLines: the tax of one document by category and rate.",
        "    private string? taxDocument;",
        "    private string? taxText;",
        "    private List<(string Label, decimal Rate, decimal Taxable, decimal Tax, int Lines)> taxRows = [];",
        "",
        "    private async Task ShowTaxAsync()",
        "    {",
        "        string document = (taxDocument ?? \"\").Trim();",
        $"        if (document.Length == 0) {{ taxRows = []; taxText = \"{NeedsText}\"; return; }}",
        "        try",
        "        {",
        $"            var rows = await {client}.{CsMethod}(document);",
        "            taxRows = rows.Select(r => (r.Label ?? r.Category?.ToString() ?? \"\", r.Rate, r.Taxable, r.Tax, r.Lines)).ToList();",
        $"            taxText = taxRows.Count == 0 ? \"{NoneText}\" : \"\";",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ taxRows = []; taxText = \"{FailText}\"; }}",
        "    }",
    ];

    public static string BlazorBoxTest() =>
        "\n"
        + "    [TestMethod]\n"
        + "    public void Shows_the_tax_of_a_document_by_category()\n"
        + "    {\n"
        + $"        Api.Answer(\"/{RouteName}\", \"[{{ \\\"category\\\": 1, \\\"label\\\": \\\"VAT20\\\", \\\"rate\\\": 20, \\\"taxable\\\": 100, \\\"tax\\\": 20, \\\"lines\\\": 2 }}]\");\n"
        + "        var page = Open();\n"
        + $"        page.Find(\"[aria-label=\\\"{DocumentLabel}\\\"]\").Input(\" 7 \");\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{ShowCaption}\").Click();\n"
        + "        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"VAT20\"));\n"
        + $"        Assert.IsTrue(Api.Requests.Any(r => r.Path.EndsWith(\"/{RouteName}?document=7\")));\n"
        + "    }\n"
        + "\n";

    /// <summary> Whether the child grid of a document's master-detail page (<paramref name="childTable"/>, its foreign key column <paramref name="fkColumn"/>) is a line table of <c>TaxLines</c> whose document column is that key:
    /// the page then shows a line of text with the tax by category and rate under the grid. </summary>
    public static bool IsChild(string childTable, string fkColumn, ProjectSettings project) =>
        TaxDesktopScreens.ChildShape(childTable, fkColumn, project) is not null;

    private const string TsText = "rows.length === 0 ? '' : rows.map((g) => `${g.label ?? g.category ?? ''}: ${+(g.rate * 100).toFixed(2)}% on ${g.taxable} = ${g.tax}`).join('; ')";

    public static string ReactChildState(string variable, string name) => $"  const [{variable}Tax, set{name}Tax] = useState('');";

    public static IReadOnlyList<string> ReactChildLoad(string name, string apiPrefix, string route) =>
    [
        $"    set{name}Tax('');",
        $"    request<Array<{TsRow}>>(`{apiPrefix}/{route}/{RouteName}?document=${{encodeURIComponent(String(parentId))}}`).then((rows) => set{name}Tax({TsText})).catch(() => {{}});",
    ];

    public static string ReactChildText(string variable) =>
        $"              {{{variable}Tax && <p role=\"status\" aria-label=\"{TableLabel}\">{{{variable}Tax}}</p>}}";

    public static string AngularChildState(string variable) => $"  {variable}Tax = '';";

    public static IReadOnlyList<string> AngularChildLoad(string variable, string apiPrefix, string route) =>
    [
        $"    this.{variable}Tax = '';",
        $"    this.http.get<Array<{TsRow}>>(`{apiPrefix}/{route}/{RouteName}?document=${{encodeURIComponent(String(parentId))}}`).subscribe({{ next: (rows) => {{ this.{variable}Tax = {TsText}; }}, error: () => {{ this.{variable}Tax = ''; }} }});",
    ];

    public static string AngularChildText(string variable) =>
        $"            <p *ngIf=\"{variable}Tax\" role=\"status\" aria-label=\"{TableLabel}\">{{{{ {variable}Tax }}}}</p>";

    public static string BlazorChildField(string variable) => $"    private string? {variable}TaxText;";

    public static string BlazorChildLoad(string variable) =>
        $"        try {{ var taxGroups = await {variable}Client.{CsMethod}(Convert.ToString(rowKey, System.Globalization.CultureInfo.InvariantCulture)!); {variable}TaxText = string.Join(\"; \", taxGroups.Select(g => $\"{{g.Label ?? g.Category?.ToString() ?? \"\"}}: {{g.Rate * 100:0.##}}% on {{g.Taxable}} = {{g.Tax}}\")); }} catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ {variable}TaxText = null; }}";

    public static string BlazorChildText(string variable) =>
        $"            @if (!string.IsNullOrEmpty({variable}TaxText))\n            {{\n                <p role=\"status\" aria-label=\"{TableLabel}\">@{variable}TaxText</p>\n            }}";

    private static string Zero(ColumnModel column) => column.CSharpBase() switch { "decimal" => "0m", "float" => "0f", _ => "0.0" };
}
