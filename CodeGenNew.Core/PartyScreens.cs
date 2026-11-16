namespace CodeGenNew.Core;

/// <summary> What the list screen of a party table (<c>PartyTables</c>) adds: a box that finds the parties holding an identifier, through <c>GET by-identifier</c>, and a button that shows every row again.
/// The identifiers themselves are the child grid of the party's master-detail screen. The words are here so every front end agrees. </summary>
public static class PartyScreens
{
    /// <summary> The API route after the table's own (<c>GET by-identifier?value=</c>). </summary>
    public const string RouteName = "by-identifier";

    /// <summary> The method a TypeScript client calls it by. </summary>
    public const string Route = "byIdentifier";

    public const string BoxLabel = "Find by identifier";

    public const string FindCaption = "Find";

    public const string AllCaption = "Show all";

    public const string FailedText = "Problem while looking for the identifier!";

    /// <summary> Whether the table's list screen gets the box. </summary>
    public static bool HasBox(TableModel table, ProjectSettings project) => PartyPlan.IsPartyTable(table.TableName, project);

    /// <summary> Whether the table's form shows the API's own sentence for a refused save: the identifier table, where a scheme and value pair another row holds is refused. </summary>
    public static bool SaysTaken(TableModel table, ProjectSettings project) => PartyPlan.IsIdentifierTable(table.TableName, project)
        || project.EditableChildren.Any(entry => PartyPlan.SavedWith(table, entry[(entry.IndexOf('.') + 1)..].Trim(), project));

    /// <summary> The function of a React page that reads the API's own words from a refused save. </summary>
    public static IReadOnlyList<string> ReactProblemDetail() =>
    [
        "// the detail of a problem-details answer (a 409 for an identifier another row holds), or nothing when the error is not one",
        "const problemDetail = (error: unknown): string | undefined => {",
        "  if (!(error instanceof ApiError)) return undefined;",
        "  try {",
        "    const body = JSON.parse(error.message) as { detail?: unknown };",
        "    return typeof body.detail === 'string' ? body.detail : undefined;",
        "  } catch {",
        "    return undefined;",
        "  }",
        "};",
        "",
    ];

    /// <summary> The line of the React api module. </summary>
    public static string ReactApiLine(string rowType) =>
        $"  {Route}: (value: string) => request<{rowType}[]>(`${{apiUrl}}/{RouteName}?value=${{encodeURIComponent(value)}}`),";

    /// <summary> The member of the React OData api module: the search is the table's REST route (OData has no such query), and answers whole rows. </summary>
    public static string ReactODataApiLine(string rowType, string restPath) =>
        $"  {Route}: (value: string) => request<{rowType}[]>(`{restPath}/{RouteName}?value=${{encodeURIComponent(value)}}`),";

    /// <summary> The method of the Angular OData service, over the table's REST route. </summary>
    public static IReadOnlyList<string> AngularODataServiceLines(string rowType) =>
    [
        "",
        "    // a party table (PartyTables): the parties that hold an identifier with this value (the table's REST route answers it)",
        $"    {Route}(value: string): Observable<{rowType}[]> {{",
        $"        return this.http.get<{rowType}[]>(`${{this.restUrl}}/{RouteName}?value=${{encodeURIComponent(value)}}`);",
        "    }",
    ];

    /// <summary> The method of the C# OData client, over the table's REST route. </summary>
    public static IReadOnlyList<string> CsODataClientLines(string rowType, string restRoute) =>
    [
        "",
        "    // A party table (PartyTables): the parties that hold an identifier with this value (the table's REST route answers it).",
        $"    public async Task<List<{rowType}>> {CsMethod}(string value) => await (await http.GetAsync($\"{restRoute}/{RouteName}?value={{Uri.EscapeDataString(value)}}\")).ReadAsync<List<{rowType}>>();",
    ];

    /// <summary> The state and the two handlers of the React page: the rows found replace the list as one page, and an empty box or Show all reads the list again. </summary>
    public static IReadOnlyList<string> ReactHandlers(string apiVar, string setList, bool paged) =>
    [
        "",
        "  // a party table (PartyTables): the parties that hold an identifier, whatever its scheme",
        "  const [identifierText, setIdentifierText] = useState('');",
        "  const findByIdentifier = () => {",
        "    const value = identifierText.trim();",
        "    if (!value) { load(); return; }",
        $"    {apiVar}.{Route}(value).then((rows) => {{",
        $"      {setList}(rows);",
        .. paged ? new[] { "      setPage(1);", "      setTotalPages(1);" } : [],
        $"    }}).catch(() => setError('{FailedText}'));",
        "  };",
        "  const showAllParties = () => {",
        "    setIdentifierText('');",
        "    load();",
        "  };",
    ];

    /// <summary> The method a C# client and a Blazor page call it by. </summary>
    public const string CsMethod = "ByIdentifierAsync";

    /// <summary> The method of the Angular service. </summary>
    public static IReadOnlyList<string> AngularServiceLines(string rowType) =>
    [
        "",
        "    // a party table (PartyTables): the parties that hold an identifier with this value",
        $"    {Route}(value: string): Observable<{rowType}[]> {{",
        $"        return this.http.get<{rowType}[]>(`${{this.apiUrl}}/{RouteName}?value=${{encodeURIComponent(value)}}`);",
        "    }",
    ];

    /// <summary> The box and its two buttons above the grid of the Angular screen. </summary>
    public static IReadOnlyList<string> AngularBox() =>
    [
        "",
        $"    <!-- {BoxLabel} -->",
        "    <div class=\"form-group form-group-search dept\">",
        $"        <input type=\"text\" class=\"form-control\" placeholder=\"{BoxLabel}\" aria-label=\"{BoxLabel}\" [(ngModel)]=\"identifierText\" (keyup.enter)=\"findByIdentifier()\" />",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"findByIdentifier()\"> {FindCaption} </button>",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"showAllParties()\"> {AllCaption} </button>",
        "    </div>",
    ];

    /// <summary> The field and the two methods of the Angular screen: the rows found replace the list as one page. </summary>
    public static IReadOnlyList<string> AngularHandlers(bool paged) =>
    [
        "",
        "  // a party table (PartyTables): the parties that hold an identifier, whatever its scheme",
        "  identifierText = '';",
        "",
        "  findByIdentifier(): void {",
        "    const value = this.identifierText.trim();",
        "    if (!value) { this.load(); return; }",
        $"    this.service.{Route}(value).subscribe({{",
        "      next: (rows) => { this.rows = rows; " + (paged ? "this.pageIndex = 0; this.totalCount = rows.length; " : "") + "},",
        "      error: (error) => this.showError('looking for the identifier', error)",
        "    });",
        "  }",
        "",
        "  showAllParties(): void {",
        "    this.identifierText = '';",
        "    this.load();",
        "  }",
    ];

    /// <summary> The method of the C# client. </summary>
    public static IReadOnlyList<string> CsClientLines(string rowType) =>
    [
        "",
        "    // A party table (PartyTables): the parties that hold an identifier with this value.",
        $"    public async Task<List<{rowType}>> {CsMethod}(string value) => await (await http.GetAsync($\"{{Url}}/{RouteName}?value={{Uri.EscapeDataString(value)}}\")).ReadAsync<List<{rowType}>>();",
    ];

    /// <summary> The box and its two buttons above the grid of the Blazor page. </summary>
    public static IReadOnlyList<string> BlazorBox() =>
    [
        "",
        "<div class=\"search\">",
        $"    <input type=\"text\" placeholder=\"{BoxLabel}\" aria-label=\"{BoxLabel}\" @bind=\"identifierText\" @bind:event=\"oninput\" />",
        $"    <button type=\"button\" @onclick=\"FindByIdentifierAsync\">{FindCaption}</button>",
        $"    <button type=\"button\" @onclick=\"ShowAllPartiesAsync\">{AllCaption}</button>",
        "</div>",
    ];

    /// <summary> The field and the two methods of the Blazor page: the rows found replace the list as one page. </summary>
    public static IReadOnlyList<string> BlazorHandlers(string client, bool paged) =>
    [
        "",
        "    // A party table (PartyTables): the parties that hold an identifier, whatever its scheme.",
        "    private string? identifierText;",
        "",
        "    private async Task FindByIdentifierAsync()",
        "    {",
        "        if (string.IsNullOrWhiteSpace(identifierText)) { await LoadAsync(); return; }",
        "        try",
        "        {",
        $"            items = await {client}.{CsMethod}(identifierText.Trim());",
        .. paged ? new[] { "            pageNumber = 1;", "            totalPages = 1;" } : [],
        "            message = null;",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ message = \"{FailedText}\"; }}",
        "    }",
        "",
        "    private async Task ShowAllPartiesAsync()",
        "    {",
        "        identifierText = null;",
        "        await LoadAsync();",
        "    }",
    ];

    /// <summary> The view-model property of a desktop list that says the grid lists the holders of an identifier instead of the table's pages. </summary>
    public const string DesktopView = "FindingIdentifier";

    /// <summary> The method of a desktop list's view model that reads the parties found. </summary>
    public const string DesktopRows = "PartiesFoundAsync";

    /// <summary> The box and its two buttons in the top bar of a WinUI3 or Uno list (sixteen spaces in). </summary>
    public static string DesktopBox() =>
        $"                <TextBox Width=\"180\" PlaceholderText=\"{BoxLabel}\" AutomationProperties.Name=\"{BoxLabel}\" Text=\"{{x:Bind ViewModel.IdentifierText, Mode=TwoWay}}\" />\n"
        + $"                <Button Content=\"{FindCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnFindByIdentifierClick\" />\n"
        + $"                <Button Content=\"{AllCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnShowAllPartiesClick\" />\n";

    /// <summary> The two click handlers of the desktop page. </summary>
    public static string DesktopHandlers() =>
        "    private async void OnFindByIdentifierClick(object sender, RoutedEventArgs e) => await ViewModel.FindByIdentifierAsync();\n\n"
        + "    private async void OnShowAllPartiesClick(object sender, RoutedEventArgs e) => await ViewModel.ShowAllPartiesAsync();\n\n";

    /// <summary> The members of the desktop list's view model: the box's text, the identifier being looked for, and the two methods. The parties found are one list, not the pages of the search. </summary>
    public static string DesktopMembers() =>
        "    /// <summary> The text of the Find by identifier box (PartyTables). </summary>\n"
        + "    [ObservableProperty]\n"
        + "    private string? _identifierText;\n\n"
        + "    private string? _identifierSought;\n\n"
        + "    /// <summary> The grid lists the parties that hold the identifier that was looked for instead of the table's rows. </summary>\n"
        + $"    public bool {DesktopView} => _identifierSought is not null;\n\n"
        + "    public Task FindByIdentifierAsync()\n"
        + "    {\n"
        + "        _identifierSought = string.IsNullOrWhiteSpace(IdentifierText) ? null : IdentifierText.Trim();\n"
        + "        PageNumber = 1;\n"
        + "        return LoadAsync();\n"
        + "    }\n\n"
        + "    public Task ShowAllPartiesAsync()\n"
        + "    {\n"
        + "        IdentifierText = null;\n"
        + "        _identifierSought = null;\n"
        + "        PageNumber = 1;\n"
        + "        return LoadAsync();\n"
        + "    }\n\n";

    /// <summary> The method of a Uno list that asks the API for the parties found. </summary>
    public static string UnoRows(string rowType) =>
        $"    private async Task<List<{rowType}>> {DesktopRows}() => await Client.{CsMethod}(_identifierSought!);\n\n";

    /// <summary> The method of a WinUI3 list, which reads the database itself: the same query as the API's route, whatever the scheme. </summary>
    public static string WinUI3Rows(TableModel table, ProjectSettings project, string context)
    {
        if (PartyPlan.PartyOf(table, project) is not { } shape)
            return "";
        string key = shape.PartyKey.CSharpName();
        return $"    private async Task<List<{table.TableName}>> {DesktopRows}()\n"
            + "    {\n"
            + "        string wanted = _identifierSought!.ToLower();\n"
            + $"        var held = {context}.Set<{shape.Identifiers.TableName}>().AsNoTracking().Where(i => i.{shape.Value.CSharpName()}.ToLower() == wanted);\n"
            + $"        return await {context}.Set<{table.TableName}>().AsNoTracking().Where(p => held.Any(i => i.{shape.Link.CSharpName()} == p.{key})).OrderBy(p => p.{key}).Take({PartyWords.FindLimit}).ToListAsync();\n"
            + "    }\n\n";
    }

    /// <summary> The lines a WinUI3 dialog of the identifier table runs before it saves (it writes through its own context, not through the API): the scheme and the value are trimmed, and a pair another row holds is the
    /// dialog's error. Empty for any other table. </summary>
    public static string CheckLines(ProjectSettings project, TableModel table, string context)
    {
        if (PartyPlan.IdentifiersOf(table, project) is not { } shape)
            return "";
        string scheme = shape.Scheme.CSharpName(), value = shape.Value.CSharpName(), key = shape.IdentifierKey.CSharpName();
        return $"        entity.{scheme} = (entity.{scheme} ?? \"\").Trim();\n"
            + $"        entity.{value} = (entity.{value} ?? \"\").Trim();\n"
            + $"        string heldScheme = entity.{scheme}.ToLower(), heldValue = entity.{value}.ToLower();\n"
            + $"        if (await {context}.Set<{table.TableName}>().AsNoTracking().AnyAsync(x => x.{key} != entity.{key} && x.{scheme}.ToLower() == heldScheme && x.{value}.ToLower() == heldValue))\n"
            + "        {\n"
            + $"            ErrorMessage = \"{PartyWords.IdentifierTaken}\";\n"
            + "            if (_editing is not null)\n"
            + $"                await {context}.Entry(entity).ReloadAsync();   // the refused values are not kept in the tracked row\n"
            + "            return false;\n"
            + "        }\n\n";
    }

    /// <summary> The lines a WinUI3 dialog of the party table runs before it saves the identifiers edited as rows inside it (<c>EditableChildren</c>): <paramref name="rows"/> is the collection of the lines, which are
    /// the party's identifiers as they will be. Both columns are trimmed; a pair twice in the lines, or held by a row of another party, is the dialog's error. Empty when <paramref name="child"/> is not the party's identifier table. </summary>
    public static string EditRowsCheckLines(ProjectSettings project, TableModel party, string child, string rows, string context)
    {
        if (PartyPlan.SavedWithShape(party, child, project) is not { } shape)
            return "";
        string scheme = shape.Scheme.CSharpName(), value = shape.Value.CSharpName(), link = shape.Link.CSharpName(), key = shape.PartyKey.CSharpName();
        return $"        var heldPairs = new HashSet<(string, string)>();\n"
            + $"        foreach (var line in {rows})\n"
            + "        {\n"
            + $"            line.{scheme} = line.{scheme}.Trim();\n"
            + $"            line.{value} = line.{value}.Trim();\n"
            + $"            string lineScheme = line.{scheme}.ToLower(), lineValue = line.{value}.ToLower();\n"
            + $"            if (!heldPairs.Add((lineScheme, lineValue)) || await {context}.Set<{shape.Identifiers.TableName}>().AsNoTracking().AnyAsync(x => x.{link} != entity.{key} && x.{scheme}.ToLower() == lineScheme && x.{value}.ToLower() == lineValue))\n"
            + "            {\n"
            + $"                ErrorMessage = \"{PartyWords.IdentifierTaken}\";\n"
            + "                return false;\n"
            + "            }\n"
            + "        }\n\n";
    }

    /// <summary> The box and its two buttons above the grid of the React page. </summary>
    public static IReadOnlyList<string> ReactBox() =>
    [
        "",
        "      <div>",
        $"        <input type=\"text\" placeholder=\"{BoxLabel}\" aria-label=\"{BoxLabel}\" value={{identifierText}} onChange={{(event) => setIdentifierText(event.target.value)}} onKeyUp={{(event) => {{ if (event.key === 'Enter') findByIdentifier(); }}}} />",
        $"        <button type=\"button\" onClick={{findByIdentifier}}>{FindCaption}</button>",
        $"        <button type=\"button\" onClick={{showAllParties}}>{AllCaption}</button>",
        "      </div>",
    ];
}
