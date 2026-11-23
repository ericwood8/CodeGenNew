namespace CodeGenNew.Core.Catalog;

/// <summary> What the list screen of a product table (<c>FacetTables</c>) adds: a "narrow by" box with a keyword, the facets (each attribute with its values and the number of products, a check box for each value) and the page of products that match,
/// read from <c>GET facets</c> and <c>GET narrow</c>. The words are here so every front end agrees. </summary>
public static class CatalogScreens
{
    public const string KeywordHint = "Keyword";
    public const string KeywordLabel = "Catalog keyword";
    public const string FindCaption = "Find products";
    public const string ClearCaption = "Clear filters";
    public const string ApplyCaption = "Apply ranges";
    public const string PreviousCaption = "Previous products";
    public const string NextCaption = "Next products";
    public const string BoxLabel = "Narrow by";
    public const string ResultLabel = "Catalog result";
    public const string ListLabel = "Matching products";
    public const string FailedText = "The catalog could not be read.";

    private const string Answer = "{ total: number; facets: { name: string; values: { value: string; count: number; selected: boolean }[]; min: number | null; max: number | null; selectedMin: number | null; selectedMax: number | null }[] }";
    private const string Page = "{ total: number; page: number; pageSize: number; items: { id: number | string; title: string | null; code: string | null; detail: string | null }[] }";

    public const string TestFacets = "{ total: 3, facets: [{ name: 'Material', values: [{ value: 'Copper', count: 3, selected: false }, { value: 'Steel', count: 1, selected: false }], min: null, max: null, selectedMin: null, selectedMax: null }, { name: 'Voltage', values: [{ value: '600 V', count: 3, selected: false }], min: 120, max: 600, selectedMin: null, selectedMax: null }] }";
    public const string TestPage = "{ total: 3, page: 1, pageSize: 25, items: [{ id: 1, title: 'Building Wire', code: 'NM-B-14/2', detail: null }] }";
    public const string TestCountText = "3 products";
    public const string TestRowText = "NM-B-14/2 - Building Wire";

    public static bool HasBox(TableModel table, ProjectSettings project) => CatalogPlan.IsProduct(table, project);

    /// <summary> The members of the React api module. </summary>
    public static IReadOnlyList<string> ReactApiLines(string restPath = "${apiUrl}") =>
    [
        $"  catalogFacets: (query: string, filters: string[], ranges: string[]) => request<{Answer}>(`{restPath}/{CatalogPlan.FacetsRoute}?${{new URLSearchParams([...(query ? [['q', query]] : []), ...filters.map((f) => ['filter', f]), ...ranges.map((r) => ['range', r])])}}`),",
        $"  catalogNarrow: (query: string, filters: string[], page: number, ranges: string[]) => request<{Page}>(`{restPath}/{CatalogPlan.NarrowRoute}?${{new URLSearchParams([...(query ? [['q', query]] : []), ...filters.map((f) => ['filter', f]), ...ranges.map((r) => ['range', r]), ['page', String(page)]])}}`),",
    ];

    /// <summary> The state and the handlers of the React box. <paramref name="setList"/> is the setter of the grid's rows (a page over the REST routes): the matching products are then also the rows of the grid, read by key; null leaves the grid alone (the OData page). </summary>
    public static IReadOnlyList<string> ReactHandlers(string apiVar, string? setList = null) =>
    [
        "",
        "  // a product table with attributes (FacetTables): a keyword, the facets (each attribute with its values and counts, and a range for a numeric one) and the page of products that match",
        "  const [facetQuery, setFacetQuery] = useState('');",
        "  const [facetFilters, setFacetFilters] = useState<string[]>([]);",
        "  const [facetRanges, setFacetRanges] = useState<Record<string, { min: string; max: string }>>({});",
        $"  const [facetData, setFacetData] = useState<{Answer} | null>(null);",
        $"  const [facetRows, setFacetRows] = useState<{Page} | null>(null);",
        "  const [facetText, setFacetText] = useState('');",
        "  const rangeParams = () => Object.entries(facetRanges).filter(([, r]) => r.min || r.max).map(([name, r]) => `${name}:${r.min}:${r.max}`);",
        "  const narrowBy = (query: string, filters: string[], pageNumber: number) => {",
        "    const ranges = rangeParams();",
        $"    Promise.all([{apiVar}.catalogFacets(query, filters, ranges), {apiVar}.catalogNarrow(query, filters, pageNumber, ranges)])",
        "      .then(([facets, rows]) => {",
        "        setFacetData(facets);",
        "        setFacetRows(rows);",
        "        setFacetText('');",
        .. setList is null ? Array.Empty<string>() :
        [
            $"        Promise.all(rows.items.map((item) => {apiVar}.getById(item.id as never)))",
            $"          .then((found) => {{ {setList}(found); setPage(1); setTotalPages(1); }})",
            $"          .catch(() => setFacetText('{FailedText}'));",
        ],
        "      })",
        $"      .catch(() => setFacetText('{FailedText}'));",
        "  };",
        "  const findProducts = () => narrowBy(facetQuery.trim(), facetFilters, 1);",
        "  const toggleFacet = (name: string, value: string) => {",
        "    const chosen = `${name}:${value}`;",
        "    const next = facetFilters.includes(chosen) ? facetFilters.filter((f) => f !== chosen) : [...facetFilters, chosen];",
        "    setFacetFilters(next);",
        "    narrowBy(facetQuery.trim(), next, 1);",
        "  };",
        "  const changeRange = (name: string, edge: 'min' | 'max', value: string) => setFacetRanges({ ...facetRanges, [name]: { ...(facetRanges[name] ?? { min: '', max: '' }), [edge]: value } });",
        $"  const clearFacets = () => {{ setFacetFilters([]); setFacetRanges({{}}); setFacetQuery(''); setFacetData(null); setFacetRows(null); setFacetText(''); {(setList is null ? "" : "load(); ")}}};",
    ];

    public static IReadOnlyList<string> ReactBox() =>
    [
        "",
        $"      <section aria-label=\"{BoxLabel}\">",
        $"        <input type=\"text\" placeholder=\"{KeywordHint}\" aria-label=\"{KeywordLabel}\" value={{facetQuery}} onChange={{(event) => setFacetQuery(event.target.value)}} />",
        $"        <button type=\"button\" onClick={{findProducts}}>{FindCaption}</button>",
        $"        <button type=\"button\" onClick={{findProducts}}>{ApplyCaption}</button>",
        $"        <button type=\"button\" onClick={{clearFacets}}>{ClearCaption}</button>",
        "        {facetText && <span role=\"alert\">{facetText}</span>}",
        "        {facetData && (",
        "          <div>",
        $"            <p role=\"status\" aria-label=\"{ResultLabel}\">{{facetData.total}} products</p>",
        "            {facetData.facets.map((group) => (",
        "              <fieldset key={group.name}>",
        "                <legend>{group.name}</legend>",
        "                {group.values.map((v) => (",
        "                  <label key={v.value}>",
        "                    <input type=\"checkbox\" aria-label={`${group.name}: ${v.value}`} checked={v.selected} onChange={() => toggleFacet(group.name, v.value)} />",
        "                    {' '}{v.value} ({v.count})",
        "                  </label>",
        "                ))}",
        "                {group.min !== null && group.max !== null && (",
        "                  <span>",
        "                    <input type=\"number\" aria-label={`${group.name} from`} placeholder={String(group.min)} value={facetRanges[group.name]?.min ?? ''} onChange={(event) => changeRange(group.name, 'min', event.target.value)} />",
        "                    <input type=\"number\" aria-label={`${group.name} to`} placeholder={String(group.max)} value={facetRanges[group.name]?.max ?? ''} onChange={(event) => changeRange(group.name, 'max', event.target.value)} />",
        "                  </span>",
        "                )}",
        "              </fieldset>",
        "            ))}",
        "          </div>",
        "        )}",
        "        {facetRows && (",
        "          <div>",
        $"            <ul aria-label=\"{ListLabel}\">",
        "              {facetRows.items.map((item) => (",
        "                <li key={item.id}>{[item.code, item.title].filter(Boolean).join(' - ')}</li>",
        "              ))}",
        "            </ul>",
        $"            <button type=\"button\" disabled={{facetRows.page <= 1}} onClick={{() => narrowBy(facetQuery.trim(), facetFilters, facetRows.page - 1)}}>{PreviousCaption}</button>",
        $"            <button type=\"button\" disabled={{facetRows.page * facetRows.pageSize >= facetRows.total}} onClick={{() => narrowBy(facetQuery.trim(), facetFilters, facetRows.page + 1)}}>{NextCaption}</button>",
        "          </div>",
        "        )}",
        "      </section>",
    ];

    /// <summary> The test of the box; <paramref name="routed"/> wraps the page in a router (the master-detail page links to its detail pages). </summary>
    /// <param name="gridKey"> The JSON name of the key when the grid takes the filters: the test then answers the row of a product by key. </param>
    public static IReadOnlyList<string> ReactBoxTests(string apiVar, string pageName, bool routed = false, string? gridKey = null) =>
    [
        "",
        "  it('narrows the catalog by facets', async () => {",
        $"    const facets = vi.spyOn({apiVar}, 'catalogFacets').mockResolvedValue({TestFacets} as never);",
        $"    const narrow = vi.spyOn({apiVar}, 'catalogNarrow').mockResolvedValue({TestPage} as never);",
        .. gridKey is null ? Array.Empty<string>() : [$"    const row = vi.spyOn({apiVar}, 'getById').mockResolvedValue({{ {gridKey}: 1 }} as never);"],
        routed ? $"    render(<MemoryRouter><{pageName} /></MemoryRouter>);" : $"    render(<{pageName} />);",
        $"    fireEvent.change(await screen.findByLabelText('{KeywordLabel}'), {{ target: {{ value: ' romex ' }} }});",
        $"    fireEvent.click(screen.getByText('{FindCaption}'));",
        "    expect(facets).toHaveBeenCalledWith('romex', [], []);",
        $"    expect(await screen.findByText('{TestCountText}')).toBeInTheDocument();",
        $"    expect(await screen.findByText('{TestRowText}')).toBeInTheDocument();",
        "    fireEvent.click(await screen.findByLabelText('Material: Copper'));",
        "    expect(narrow).toHaveBeenLastCalledWith('romex', ['Material:Copper'], 1, []);",
        "    fireEvent.change(await screen.findByLabelText('Voltage from'), { target: { value: '100' } });",
        "    fireEvent.change(screen.getByLabelText('Voltage to'), { target: { value: '300' } });",
        $"    fireEvent.click(screen.getByText('{ApplyCaption}'));",
        "    expect(narrow).toHaveBeenLastCalledWith('romex', ['Material:Copper'], 1, ['Voltage:100:300']);",
        .. gridKey is null ? Array.Empty<string>() : [$"    await vi.waitFor(() => expect(row).toHaveBeenCalledWith(1));"],
        "  });",
    ];

    public const string NarrowByCaption = "Narrow by";
    public const string ProductsCaption = "products";
    private const string FacetsJson = "{ \\\"total\\\": 3, \\\"facets\\\": [{ \\\"name\\\": \\\"Material\\\", \\\"values\\\": [{ \\\"value\\\": \\\"Copper\\\", \\\"count\\\": 3, \\\"selected\\\": false }, { \\\"value\\\": \\\"Steel\\\", \\\"count\\\": 1, \\\"selected\\\": false }] }] }";
    private const string PageJson = "{ \\\"total\\\": 3, \\\"page\\\": 1, \\\"pageSize\\\": 25, \\\"items\\\": [{ \\\"id\\\": 1, \\\"title\\\": \\\"Building Wire\\\", \\\"code\\\": \\\"NM-B-14/2\\\", \\\"detail\\\": null }] }";

    public static IReadOnlyList<string> AngularServiceLines(string urlMember) =>
    [
        "",
        "    // a product table with attributes (FacetTables): the facets and the page of products that match",
        $"    catalogFacets(query: string, filters: string[]): Observable<{Answer}> {{",
        $"        return this.http.get<{Answer}>(`${{this.{urlMember}}}/{CatalogPlan.FacetsRoute}`, {{ params: {{ ...(query ? {{ q: query }} : {{}}), filter: filters }} }});",
        "    }",
        "",
        $"    catalogNarrow(query: string, filters: string[], page: number): Observable<{Page}> {{",
        $"        return this.http.get<{Page}>(`${{this.{urlMember}}}/{CatalogPlan.NarrowRoute}`, {{ params: {{ ...(query ? {{ q: query }} : {{}}), filter: filters, page: String(page) }} }});",
        "    }",
    ];

    public static IReadOnlyList<string> AngularBox() =>
    [
        "",
        $"    <!-- {BoxLabel} -->",
        $"    <section aria-label=\"{BoxLabel}\">",
        "        <div class=\"form-group form-group-search dept\">",
        $"            <input type=\"text\" class=\"form-control\" placeholder=\"{KeywordHint}\" aria-label=\"{KeywordLabel}\" [(ngModel)]=\"facetQuery\" />",
        $"            <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"findProducts()\"> {FindCaption} </button>",
        $"            <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"clearFacets()\"> {ClearCaption} </button>",
        "            <span role=\"alert\" *ngIf=\"facetText\">{{ facetText }}</span>",
        "        </div>",
        "        <div *ngIf=\"facetData\">",
        $"            <p role=\"status\" aria-label=\"{ResultLabel}\">{{{{ facetData.total }}}} {ProductsCaption}</p>",
        "            <fieldset *ngFor=\"let group of facetData.facets\">",
        "                <legend>{{ group.name }}</legend>",
        "                <label *ngFor=\"let v of group.values\">",
        "                    <input type=\"checkbox\" [attr.aria-label]=\"group.name + ': ' + v.value\" [checked]=\"v.selected\" (change)=\"toggleFacet(group.name, v.value)\" />",
        "                    {{ v.value }} ({{ v.count }})",
        "                </label>",
        "            </fieldset>",
        "        </div>",
        "        <div *ngIf=\"facetRows\">",
        $"            <ul aria-label=\"{ListLabel}\">",
        "                <li *ngFor=\"let item of facetRows.items\">{{ productLabel(item) }}</li>",
        "            </ul>",
        $"            <button class=\"btn btn-outline-secondary\" type=\"button\" [disabled]=\"facetRows.page <= 1\" (click)=\"previousProducts()\"> {PreviousCaption} </button>",
        $"            <button class=\"btn btn-outline-secondary\" type=\"button\" [disabled]=\"facetRows.page * facetRows.pageSize >= facetRows.total\" (click)=\"nextProducts()\"> {NextCaption} </button>",
        "        </div>",
        "    </section>",
    ];

    public static IReadOnlyList<string> AngularHandlers() =>
    [
        "",
        "  // a product table with attributes (FacetTables): a keyword, the facets (each attribute with its values and counts) and the page of products that match",
        "  facetQuery = '';",
        "  facetFilters: string[] = [];",
        $"  facetData: {Answer} | null = null;",
        $"  facetRows: {Page} | null = null;",
        "  facetText = '';",
        "",
        "  narrowBy(pageNumber: number): void {",
        "    const query = this.facetQuery.trim();",
        "    this.service.catalogFacets(query, this.facetFilters).subscribe({",
        "      next: (facets) => { this.facetData = facets; this.facetText = ''; },",
        $"      error: () => {{ this.facetText = '{FailedText}'; }}",
        "    });",
        "    this.service.catalogNarrow(query, this.facetFilters, pageNumber).subscribe({",
        "      next: (rows) => { this.facetRows = rows; },",
        $"      error: () => {{ this.facetText = '{FailedText}'; }}",
        "    });",
        "  }",
        "",
        "  findProducts(): void {",
        "    this.narrowBy(1);",
        "  }",
        "",
        "  toggleFacet(name: string, value: string): void {",
        "    const chosen = `${name}:${value}`;",
        "    this.facetFilters = this.facetFilters.includes(chosen) ? this.facetFilters.filter((f) => f !== chosen) : [...this.facetFilters, chosen];",
        "    this.narrowBy(1);",
        "  }",
        "",
        "  clearFacets(): void {",
        "    this.facetFilters = [];",
        "    this.facetQuery = '';",
        "    this.facetData = null;",
        "    this.facetRows = null;",
        "    this.facetText = '';",
        "  }",
        "",
        "  previousProducts(): void {",
        "    if (this.facetRows) { this.narrowBy(this.facetRows.page - 1); }",
        "  }",
        "",
        "  nextProducts(): void {",
        "    if (this.facetRows) { this.narrowBy(this.facetRows.page + 1); }",
        "  }",
        "",
        "  productLabel(item: { code: string | null; title: string | null }): string {",
        "    return [item.code, item.title].filter(Boolean).join(' - ');",
        "  }",
    ];

    public static string AngularBoxTest() =>
        "  it('narrows the catalog by facets', () => {\n"
        + "    const http = TestBed.inject(HttpTestingController);\n"
        + "    component.facetQuery = ' romex ';\n"
        + "    component.findProducts();\n"
        + $"    http.expectOne((r) => r.url.endsWith('/{CatalogPlan.FacetsRoute}') && r.params.get('q') === 'romex').flush({TestFacets});\n"
        + $"    http.expectOne((r) => r.url.endsWith('/{CatalogPlan.NarrowRoute}') && r.params.get('page') === '1').flush({TestPage});\n"
        + "    expect(component.facetData?.total).toBe(3);\n"
        + $"    expect(component.productLabel(component.facetRows!.items[0])).toBe('{TestRowText}');\n"
        + "    component.toggleFacet('Material', 'Copper');\n"
        + $"    http.expectOne((r) => r.url.endsWith('/{CatalogPlan.FacetsRoute}') && r.params.getAll('filter')?.[0] === 'Material:Copper').flush({TestFacets});\n"
        + $"    http.expectOne((r) => r.url.endsWith('/{CatalogPlan.NarrowRoute}') && r.params.getAll('filter')?.[0] === 'Material:Copper').flush({TestPage});\n"
        + "    expect(component.facetFilters).toEqual(['Material:Copper']);\n"
        + "  });\n";

    public static IReadOnlyList<string> CsClientLines(string route) =>
    [
        "",
        "    // A product table with attributes (FacetTables): the facets and the page of products that match.",
        "    private static string CatalogQuery(string query, IEnumerable<string> filters, int? page = null)",
        "    {",
        "        var parts = new List<string>();",
        "        if (!string.IsNullOrEmpty(query))",
        "            parts.Add(\"q=\" + Uri.EscapeDataString(query));",
        "        parts.AddRange(filters.Select(f => \"filter=\" + Uri.EscapeDataString(f)));",
        "        if (page is not null)",
        "            parts.Add(\"page=\" + page);",
        "        return parts.Count == 0 ? \"\" : \"?\" + string.Join(\"&\", parts);",
        "    }",
        "",
        $"    public async Task<System.Text.Json.JsonElement> CatalogFacetsAsync(string query, IEnumerable<string> filters) => await (await http.GetAsync($\"{route}/{CatalogPlan.FacetsRoute}{{CatalogQuery(query, filters)}}\")).ReadAsync<System.Text.Json.JsonElement>();",
        "",
        $"    public async Task<System.Text.Json.JsonElement> CatalogNarrowAsync(string query, IEnumerable<string> filters, int page) => await (await http.GetAsync($\"{route}/{CatalogPlan.NarrowRoute}{{CatalogQuery(query, filters, page)}}\")).ReadAsync<System.Text.Json.JsonElement>();",
    ];

    private const string ChoicesCs = "{0}.GetProperty(\"facets\").EnumerateArray().SelectMany(g => g.GetProperty(\"values\").EnumerateArray().Select(v => new CatalogChoice(g.GetProperty(\"name\").GetString()!, v.GetProperty(\"value\").GetString()!, v.GetProperty(\"count\").GetInt32(), v.GetProperty(\"selected\").GetBoolean()))).ToList()";
    private const string LabelsCs = "{0}.GetProperty(\"items\").EnumerateArray().Select(i => string.Join(\" - \", new[] { i.GetProperty(\"code\"), i.GetProperty(\"title\") }.Where(p => p.ValueKind == System.Text.Json.JsonValueKind.String).Select(p => p.GetString()!))).ToList()";

    public static IReadOnlyList<string> BlazorBox() =>
    [
        "",
        $"<section aria-label=\"{BoxLabel}\">",
        $"    <input type=\"text\" placeholder=\"{KeywordHint}\" aria-label=\"{KeywordLabel}\" @bind=\"FacetQuery\" @bind:event=\"oninput\" />",
        $"    <button type=\"button\" @onclick=\"FindProductsAsync\">{FindCaption}</button>",
        $"    <button type=\"button\" @onclick=\"ClearFacets\">{ClearCaption}</button>",
        "    @if (FacetText is not null)",
        "    {",
        "        <span role=\"alert\">@FacetText</span>",
        "    }",
        "    @if (FacetShown)",
        "    {",
        $"        <p role=\"status\" aria-label=\"{ResultLabel}\">@FacetTotal {ProductsCaption}</p>",
        "        @foreach (var group in FacetChoices.GroupBy(c => c.Name))",
        "        {",
        "            <fieldset>",
        "                <legend>@group.Key</legend>",
        "                @foreach (var choice in group)",
        "                {",
        "                    <label><input type=\"checkbox\" aria-label=\"@($\"{choice.Name}: {choice.Value}\")\" checked=\"@choice.Selected\" @onchange=\"() => ToggleFacetAsync(choice.Name, choice.Value)\" /> @(choice.Value) (@(choice.Count))</label>",
        "                }",
        "            </fieldset>",
        "        }",
        $"        <ul aria-label=\"{ListLabel}\">",
        "            @foreach (var label in FacetLabels)",
        "            {",
        "                <li>@label</li>",
        "            }",
        "        </ul>",
        $"        <button type=\"button\" disabled=\"@(FacetPage <= 1)\" @onclick=\"PreviousProductsAsync\">{PreviousCaption}</button>",
        $"        <button type=\"button\" disabled=\"@(FacetPage * FacetPageSize >= FacetTotal)\" @onclick=\"NextProductsAsync\">{NextCaption}</button>",
        "    }",
        "</section>",
    ];

    public static IReadOnlyList<string> BlazorHandlers(string client) =>
    [
        "",
        "    // A product table with attributes (FacetTables): a keyword, the facets (each attribute with its values and counts) and the page of products that match.",
        "    private sealed record CatalogChoice(string Name, string Value, int Count, bool Selected);",
        "",
        "    private string? FacetQuery;",
        "    private readonly List<string> FacetFilters = [];",
        "    private List<CatalogChoice> FacetChoices = [];",
        "    private List<string> FacetLabels = [];",
        "    private bool FacetShown;",
        "    private int FacetTotal;",
        "    private int FacetPage = 1;",
        "    private int FacetPageSize = 1;",
        "    private string? FacetText;",
        "",
        "    private async Task NarrowByAsync(int pageNumber)",
        "    {",
        "        try",
        "        {",
        "            string query = (FacetQuery ?? \"\").Trim();",
        $"            var facets = await {client}.CatalogFacetsAsync(query, FacetFilters);",
        $"            var rows = await {client}.CatalogNarrowAsync(query, FacetFilters, pageNumber);",
        "            FacetChoices = " + ChoicesCs.Replace("{0}", "facets") + ";",
        "            FacetLabels = " + LabelsCs.Replace("{0}", "rows") + ";",
        "            FacetTotal = rows.GetProperty(\"total\").GetInt32();",
        "            FacetPage = rows.GetProperty(\"page\").GetInt32();",
        "            FacetPageSize = rows.GetProperty(\"pageSize\").GetInt32();",
        "            FacetShown = true;",
        "            FacetText = null;",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ FacetText = \"{FailedText}\"; }}",
        "    }",
        "",
        "    private Task FindProductsAsync() => NarrowByAsync(1);",
        "",
        "    private Task PreviousProductsAsync() => NarrowByAsync(FacetPage - 1);",
        "",
        "    private Task NextProductsAsync() => NarrowByAsync(FacetPage + 1);",
        "",
        "    private async Task ToggleFacetAsync(string name, string value)",
        "    {",
        "        string chosen = name + \":\" + value;",
        "        if (!FacetFilters.Remove(chosen))",
        "            FacetFilters.Add(chosen);",
        "        await NarrowByAsync(1);",
        "    }",
        "",
        "    private void ClearFacets()",
        "    {",
        "        FacetFilters.Clear();",
        "        FacetQuery = null;",
        "        FacetChoices = [];",
        "        FacetLabels = [];",
        "        FacetShown = false;",
        "        FacetText = null;",
        "    }",
    ];

    public static string BlazorBoxTest() =>
        "\n"
        + "    [TestMethod]\n"
        + "    public void Narrows_the_catalog_by_facets()\n"
        + "    {\n"
        + $"        Api.Answer(\"/{CatalogPlan.FacetsRoute}\", \"{FacetsJson}\");\n"
        + $"        Api.Answer(\"/{CatalogPlan.NarrowRoute}\", \"{PageJson}\");\n"
        + "        var page = Open();\n"
        + $"        page.Find(\"[aria-label=\\\"{KeywordLabel}\\\"]\").Input(\" romex \");\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{FindCaption}\").Click();\n"
        + $"        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"{TestCountText}\"));\n"
        + $"        StringAssert.Contains(page.Markup, \"{TestRowText}\");\n"
        + $"        Assert.IsTrue(Api.Requests.Any(r => r.Path.Contains(\"/{CatalogPlan.FacetsRoute}?q=romex\")));\n"
        + "        page.Find(\"[aria-label=\\\"Material: Copper\\\"]\").Change(true);\n"
        + "        page.WaitForAssertion(() => Assert.IsTrue(Api.Requests.Any(r => r.Path.Contains(\"filter=Material%3ACopper\"))));\n"
        + "    }\n"
        + "\n";

    /// <summary> The keyword box and the buttons in the top bar of a WinUI3 or Uno list (sixteen spaces in); the facets open as a menu of the "Narrow by" button. </summary>
    public static string DesktopBox() =>
        $"                <TextBox Width=\"140\" PlaceholderText=\"{KeywordHint}\" AutomationProperties.Name=\"{KeywordLabel}\" Text=\"{{x:Bind ViewModel.CatalogQuery, Mode=TwoWay}}\" />\n"
        + $"                <Button Content=\"{FindCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnFindProductsClick\" />\n"
        + $"                <Button Content=\"{NarrowByCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnNarrowByClick\" />\n"
        + $"                <Button Content=\"{ClearCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnClearFacetsClick\" />\n"
        + $"                <TextBlock VerticalAlignment=\"Center\" Text=\"{{x:Bind ViewModel.CatalogText, Mode=OneWay}}\" />\n";

    public static string DesktopHandlers() =>
        "    private async void OnFindProductsClick(object sender, RoutedEventArgs e) => await ViewModel.FindProductsAsync();\n"
        + "    private void OnClearFacetsClick(object sender, RoutedEventArgs e) => ViewModel.ClearFacets();\n"
        + "    private void OnNarrowByClick(object sender, RoutedEventArgs e)\n"
        + "    {\n"
        + "        var menu = new MenuFlyout();\n"
        + "        string? last = null;\n"
        + "        foreach (var choice in ViewModel.CatalogChoices)\n"
        + "        {\n"
        + "            if (last is not null && last != choice.Name)\n"
        + "                menu.Items.Add(new MenuFlyoutSeparator());\n"
        + "            last = choice.Name;\n"
        + "            var item = new ToggleMenuFlyoutItem { Text = choice.Text, IsChecked = choice.Selected };\n"
        + "            string name = choice.Name, value = choice.Value;\n"
        + "            item.Click += async (_, _) => await ViewModel.ToggleFacetAsync(name, value);\n"
        + "            menu.Items.Add(item);\n"
        + "        }\n"
        + $"        if (menu.Items.Count == 0)\n            menu.Items.Add(new MenuFlyoutItem {{ Text = \"{FindFirstText}\", IsEnabled = false }});\n"
        + "        menu.ShowAt((FrameworkElement)sender);\n"
        + "    }\n";

    public const string FindFirstText = "Find products first";

    private static string DesktopState() => $$"""
    /// <summary> One value of an attribute in the "Narrow by" menu (FacetTables). </summary>
    public sealed record CatalogChoice(string Name, string Value, int Count, bool Selected)
    {
        public string Text => $"{Name}: {Value} ({Count})";
    }

    [ObservableProperty]
    private string? _catalogQuery;

    [ObservableProperty]
    private string? _catalogText;

    public List<string> CatalogFilters { get; } = [];

    public List<CatalogChoice> CatalogChoices { get; private set; } = [];

    public async Task ToggleFacetAsync(string name, string value)
    {
        string chosen = name + ":" + value;
        if (!CatalogFilters.Remove(chosen))
            CatalogFilters.Add(chosen);
        await FindProductsAsync();
    }

    public void ClearFacets()
    {
        CatalogFilters.Clear();
        CatalogChoices = [];
        CatalogQuery = null;
        CatalogText = null;
    }


""";

    private const string SummaryCs = "$\"{total} " + ProductsCaption + "\" + (labels.Count == 0 ? \"\" : \": \" + string.Join(\"; \", labels.Take(5)) + (total > 5 ? \"; ...\" : \"\"))";

    /// <summary> The members of the Uno list's view model: the keyword, the facets and the answer, read through the API client. </summary>
    public static string UnoMembers() => DesktopState() + $$"""
    public async Task FindProductsAsync()
    {
        try
        {
            string query = (CatalogQuery ?? "").Trim();
            var facets = await Client.CatalogFacetsAsync(query, CatalogFilters);
            var rows = await Client.CatalogNarrowAsync(query, CatalogFilters, 1);
            CatalogChoices = {{ChoicesCs.Replace("{0}", "facets")}};
            var labels = {{LabelsCs.Replace("{0}", "rows")}};
            int total = rows.GetProperty("total").GetInt32();
            CatalogText = {{SummaryCs}};
        }
        catch (Exception) { CatalogText = "{{FailedText}}"; }
    }


""";

    /// <summary> The members of the WinUI3 list's view model: the same reads as the API (<see cref="CatalogWriter.Engine"/>) through the list's own context. </summary>
    public static string WinUI3Members(CatalogShape s, string context, string contextType) => DesktopState() + CatalogWriter.Engine(s, contextType) + $$"""
    private List<(string Name, string[] Values)> ChosenFacets() =>
        CatalogFilters.Select(f => f.Split(':', 2)).GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase).Select(g => (g.Key, g.Select(p => p[1]).ToArray())).ToList();

    public async Task FindProductsAsync()
    {
        try
        {
            string query = (CatalogQuery ?? "").Trim();
            var filters = ChosenFacets();
            var (_, groups) = await FacetsCoreAsync({{context}}, query, filters, [], {{CatalogPlan.MaxFacets}}, {{CatalogPlan.MaxValues}});
            var (total, items) = await NarrowCoreAsync({{context}}, query, filters, [], 1, {{CatalogPlan.DefaultPageSize}});
            CatalogChoices = groups.SelectMany(g => g.Values.Select(v => new CatalogChoice(g.Name, v.Value, v.Count, v.Selected))).ToList();
            var labels = items.Select(i => string.Join(" - ", new[] { i.Code, i.Title }.Where(p => !string.IsNullOrEmpty(p)))).ToList();
            CatalogText = {{SummaryCs}};
        }
        catch (Exception) { CatalogText = "{{FailedText}}"; }
    }


""";
}
