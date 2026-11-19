using CodeGenNew.Core.Naming;

namespace CodeGenNew.Core.Rate;

/// <summary> What the list screen of a rate table (<c>RateTables</c>) adds: two currency boxes, a day and a button that asks <c>GET rate</c> for the rate in force and shows it. The words are here so every front end
/// agrees. </summary>
public static class RateScreens
{
    public const string RouteName = "rate";

    /// <summary> The method a TypeScript client calls it by. </summary>
    public const string Route = "rateOn";

    public const string FromLabel = "From currency";

    public const string ToLabel = "To currency";

    public const string DayLabel = "Rate on";

    public const string FindCaption = "Find rate";

    public const string ResultLabel = "Rate found";

    public const string NoneText = "No rate for that pair on or before that day.";

    public const string NeedsText = "Give both currencies.";

    /// <summary> Whether the table's list screen gets the box. </summary>
    public static bool HasBox(TableModel table, ProjectSettings project) => RatePlan.IsListed(table.TableName, project);

    public const string InvertedText = "one divided by the rate of the opposite pair";

    /// <summary> The names of the currencies, the rate, the day and the key of a rate table's rows. <c>Inverse</c> is true with <c>RateInverse</c>: the lookup then answers a row with no key of its own (key 0) when it
    /// worked the rate out from the opposite pair, and the screen says so. </summary>
    public readonly record struct RateNames(string From, string To, string Rate, string Day, string Key, bool Inverse)
    {
        /// <summary> The suffix of the shown answer in a TypeScript template string. </summary>
        public string TsSuffix() => Inverse ? $"${{row.{Key} ? '' : ', {InvertedText}'}}" : "";

        /// <summary> The suffix of the shown answer in a C# interpolated string. </summary>
        public string CsSuffix() => Inverse ? $"{{(row.{Key} == 0 ? \", {InvertedText}\" : \"\")}}" : "";
    }

    /// <summary> The JSON names of the currencies, the rate, the day and the key of a rate table's rows, or null when the table is not a rate table. </summary>
    public static RateNames? Names(TableModel table, ProjectSettings project) =>
        RatePlan.For(table, project) is { } shape ? new RateNames(JsonNames.Camel(shape.From.Name), JsonNames.Camel(shape.To.Name), JsonNames.Camel(shape.Rate.Name), JsonNames.Camel(shape.Day.Name), JsonNames.Camel(shape.Key.Name), Inverts(shape, project)) : null;

    private static bool Inverts(RateShape shape, ProjectSettings project) => project.RateInverse && RatePlan.InvertibleRate(shape.Rate);

    /// <summary> The member of the React api module. </summary>
    public static string ReactApiLine(string rowType) =>
        $"  {Route}: (from: string, to: string, on?: string) => request<{rowType}>(`${{apiUrl}}/{RouteName}?from=${{encodeURIComponent(from)}}&to=${{encodeURIComponent(to)}}${{on ? `&on=${{encodeURIComponent(on)}}` : ''}}`),";

    /// <summary> The member of the React OData api module: the lookup is the table's REST route (OData has no such query). </summary>
    public static string ReactODataApiLine(string rowType, string restPath) =>
        $"  {Route}: (from: string, to: string, on?: string) => request<{rowType}>(`{restPath}/{RouteName}?from=${{encodeURIComponent(from)}}&to=${{encodeURIComponent(to)}}${{on ? `&on=${{encodeURIComponent(on)}}` : ''}}`),";

    public static IReadOnlyList<string> ReactHandlers(string apiVar, RateNames names) =>
    [
        "",
        "  // a rate table (RateTables): the rate in force for a pair of currencies on a day",
        "  const [rateFrom, setRateFrom] = useState('');",
        "  const [rateTo, setRateTo] = useState('');",
        "  const [rateDay, setRateDay] = useState('');",
        "  const [rateText, setRateText] = useState('');",
        "  const findRate = () => {",
        "    const from = rateFrom.trim();",
        "    const to = rateTo.trim();",
        $"    if (!from || !to) {{ setRateText('{NeedsText}'); return; }}",
        $"    {apiVar}.{Route}(from, to, rateDay || undefined)",
        $"      .then((row) => setRateText(`${{row.{names.From}}} to ${{row.{names.To}}}: ${{row.{names.Rate}}} (from ${{String(row.{names.Day}).slice(0, 10)}}{names.TsSuffix()})`))",
        $"      .catch(() => setRateText('{NoneText}'));",
        "  };",
    ];

    /// <summary> The boxes and the button above the grid of the React page. </summary>
    public static IReadOnlyList<string> ReactBox() =>
    [
        "",
        "      <div>",
        $"        <input type=\"text\" placeholder=\"{FromLabel}\" aria-label=\"{FromLabel}\" value={{rateFrom}} onChange={{(event) => setRateFrom(event.target.value)}} />",
        $"        <input type=\"text\" placeholder=\"{ToLabel}\" aria-label=\"{ToLabel}\" value={{rateTo}} onChange={{(event) => setRateTo(event.target.value)}} />",
        $"        <input type=\"date\" aria-label=\"{DayLabel}\" value={{rateDay}} onChange={{(event) => setRateDay(event.target.value)}} />",
        $"        <button type=\"button\" onClick={{findRate}}>{FindCaption}</button>",
        $"        <span role=\"status\" aria-label=\"{ResultLabel}\">{{rateText}}</span>",
        "      </div>",
    ];

    public static IReadOnlyList<string> ReactBoxTests(string apiVar, string pageName, RateNames names)
    {
        string Row(string key) => $"{{ {names.Key}: {key}, {names.From}: 'USD', {names.To}: 'EUR', {names.Rate}: 0.9, {names.Day}: '2024-03-01' }} as never";
        var lines = new List<string>
        {
            "",
            "  it('asks for the rate in force and shows it, or says there is none', async () => {",
            $"    const found = vi.spyOn({apiVar}, '{Route}').mockResolvedValueOnce({Row("7")})" + (names.Inverse ? $".mockResolvedValueOnce({Row("0")})" : "") + ".mockRejectedValueOnce(new Error('none'));",
            $"    render(<MemoryRouter><{pageName} /></MemoryRouter>);",
            $"    fireEvent.change(await screen.findByLabelText('{FromLabel}'), {{ target: {{ value: ' usd ' }} }});",
            $"    fireEvent.change(screen.getByLabelText('{ToLabel}'), {{ target: {{ value: 'EUR' }} }});",
            $"    fireEvent.change(screen.getByLabelText('{DayLabel}'), {{ target: {{ value: '2024-03-15' }} }});",
            $"    fireEvent.click(screen.getByText('{FindCaption}'));",
            "    expect(found).toHaveBeenCalledWith('usd', 'EUR', '2024-03-15');",
            "    expect(await screen.findByText(/USD to EUR: 0.9 \\(from 2024-03-01\\)$/)).toBeInTheDocument();",
        };
        if (names.Inverse)
        {
            lines.Add($"    fireEvent.click(screen.getByText('{FindCaption}'));");
            lines.Add($"    expect(await screen.findByText(/{InvertedText}/)).toBeInTheDocument();");
        }
        lines.Add($"    fireEvent.click(screen.getByText('{FindCaption}'));");
        lines.Add($"    expect(await screen.findByText('{NoneText}')).toBeInTheDocument();");
        lines.Add("  });");
        return lines;
    }

    /// <summary> The method of the Angular service. </summary>
    public static IReadOnlyList<string> AngularServiceLines(string rowType, string urlMember) =>
    [
        "",
        "    // a rate table (RateTables): the rate in force for a pair of currencies on a day",
        $"    {Route}(from: string, to: string, on?: string): Observable<{rowType}> {{",
        "        const day = on ? `&on=${encodeURIComponent(on)}` : '';",
        $"        return this.http.get<{rowType}>(`${{this.{urlMember}}}/{RouteName}?from=${{encodeURIComponent(from)}}&to=${{encodeURIComponent(to)}}${{day}}`);",
        "    }",
    ];

    /// <summary> The boxes and the button above the grid of the Angular screen. </summary>
    public static IReadOnlyList<string> AngularBox() =>
    [
        "",
        $"    <!-- {DayLabel} -->",
        "    <div class=\"form-group form-group-search dept\">",
        $"        <input type=\"text\" class=\"form-control\" placeholder=\"{FromLabel}\" aria-label=\"{FromLabel}\" [(ngModel)]=\"rateFrom\" />",
        $"        <input type=\"text\" class=\"form-control\" placeholder=\"{ToLabel}\" aria-label=\"{ToLabel}\" [(ngModel)]=\"rateTo\" />",
        $"        <input type=\"date\" class=\"form-control\" aria-label=\"{DayLabel}\" [(ngModel)]=\"rateDay\" />",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"findRate()\"> {FindCaption} </button>",
        $"        <span role=\"status\" aria-label=\"{ResultLabel}\">{{{{ rateText }}}}</span>",
        "    </div>",
    ];

    public static IReadOnlyList<string> AngularHandlers(RateNames names) =>
    [
        "",
        "  // a rate table (RateTables): the rate in force for a pair of currencies on a day",
        "  rateFrom = '';",
        "  rateTo = '';",
        "  rateDay = '';",
        "  rateText = '';",
        "",
        "  findRate(): void {",
        "    const from = this.rateFrom.trim();",
        "    const to = this.rateTo.trim();",
        $"    if (!from || !to) {{ this.rateText = '{NeedsText}'; return; }}",
        $"    this.service.{Route}(from, to, this.rateDay || undefined).subscribe({{",
        $"      next: (row) => {{ this.rateText = `${{row.{names.From}}} to ${{row.{names.To}}}: ${{row.{names.Rate}}} (from ${{String(row.{names.Day}).slice(0, 10)}}{names.TsSuffix()})`; }},",
        $"      error: () => {{ this.rateText = '{NoneText}'; }}",
        "    });",
        "  }",
    ];

    /// <summary> The Jasmine test of the box: the currencies are asked for trimmed, the day goes along and the answer is shown. </summary>
    public static string AngularBoxTest(RateNames names) =>
        "  it('asks for the rate in force and shows it', () => {\n"
        + "    const http = TestBed.inject(HttpTestingController);\n"
        + "    component.rateFrom = ' usd ';\n"
        + "    component.rateTo = 'EUR';\n"
        + "    component.rateDay = '2024-03-15';\n"
        + "    component.findRate();\n"
        + $"    const request = http.expectOne((r) => r.urlWithParams.endsWith('/{RouteName}?from=usd&to=EUR&on=2024-03-15'));\n"
        + $"    request.flush({{ {names.Key}: 7, {names.From}: 'USD', {names.To}: 'EUR', {names.Rate}: 0.9, {names.Day}: '2024-03-01' }});\n"
        + "    expect(component.rateText).toContain('USD to EUR: 0.9 (from 2024-03-01)');\n"
        + (names.Inverse
            ? "    component.findRate();\n"
              + $"    http.expectOne((r) => r.urlWithParams.endsWith('/{RouteName}?from=usd&to=EUR&on=2024-03-15')).flush({{ {names.Key}: 0, {names.From}: 'USD', {names.To}: 'EUR', {names.Rate}: 0.5, {names.Day}: '2024-03-10' }});\n"
              + $"    expect(component.rateText).toContain('{InvertedText}');\n"
            : "")
        + "  });\n";

    /// <summary> The method a C# client and a Blazor page call it by. </summary>
    public const string CsMethod = "RateOnAsync";

    /// <summary> The method of the C# client; <paramref name="route"/> is the REST route of the table (the OData client asks it too). </summary>
    public static IReadOnlyList<string> CsClientLines(string rowType, string route) =>
    [
        "",
        "    // A rate table (RateTables): the rate in force for a pair of currencies on a day.",
        $"    public async Task<{rowType}> {CsMethod}(string from, string to, string? on = null) => await (await http.GetAsync($\"{route}/{RouteName}?from={{Uri.EscapeDataString(from)}}&to={{Uri.EscapeDataString(to)}}{{(string.IsNullOrEmpty(on) ? \"\" : \"&on=\" + Uri.EscapeDataString(on))}}\")).ReadAsync<{rowType}>();",
    ];

    /// <summary> The boxes and the button above the grid of the Blazor page. </summary>
    public static IReadOnlyList<string> BlazorBox() =>
    [
        "",
        "<div class=\"search\">",
        $"    <input type=\"text\" placeholder=\"{FromLabel}\" aria-label=\"{FromLabel}\" @bind=\"rateFrom\" @bind:event=\"oninput\" />",
        $"    <input type=\"text\" placeholder=\"{ToLabel}\" aria-label=\"{ToLabel}\" @bind=\"rateTo\" @bind:event=\"oninput\" />",
        $"    <input type=\"text\" placeholder=\"yyyy-mm-dd\" aria-label=\"{DayLabel}\" value=\"@rateDay\" @oninput=\"e => rateDay = e.Value?.ToString()\" />",
        $"    <button type=\"button\" @onclick=\"FindRateAsync\">{FindCaption}</button>",
        $"    <span role=\"status\" aria-label=\"{ResultLabel}\">@rateText</span>",
        "</div>",
    ];

    /// <summary> The C# names of the currencies, the rate and the day, or null when the table is not a rate table. </summary>
    public static RateNames? CsNames(TableModel table, ProjectSettings project) =>
        RatePlan.For(table, project) is { } shape ? new RateNames(shape.From.CSharpName(), shape.To.CSharpName(), shape.Rate.CSharpName(), shape.Day.CSharpName(), shape.Key.CSharpName(), Inverts(shape, project)) : null;

    /// <summary> The fields and the method of the Blazor page. </summary>
    public static IReadOnlyList<string> BlazorHandlers(string client, RateNames names) =>
    [
        "",
        "    // A rate table (RateTables): the rate in force for a pair of currencies on a day.",
        "    private string? rateFrom;",
        "    private string? rateTo;",
        "    private string? rateDay;",
        "    private string? rateText;",
        "",
        "    private async Task FindRateAsync()",
        "    {",
        "        string from = (rateFrom ?? \"\").Trim(), to = (rateTo ?? \"\").Trim();",
        $"        if (from.Length == 0 || to.Length == 0) {{ rateText = \"{NeedsText}\"; return; }}",
        "        try",
        "        {",
        $"            var row = await {client}.{CsMethod}(from, to, rateDay);",
        $"            rateText = $\"{{row.{names.From}}} to {{row.{names.To}}}: {{row.{names.Rate}}} (from {{row.{names.Day}:yyyy-MM-dd}}{names.CsSuffix()})\";",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ rateText = \"{NoneText}\"; }}",
        "    }",
    ];

    public const string DayNeedsText = "Give the day as yyyy-mm-dd.";

    /// <summary> The boxes, the button and the answer in the top bar of a WinUI3 or Uno list (sixteen spaces in). </summary>
    public static string DesktopBox() =>
        $"                <TextBox Width=\"90\" PlaceholderText=\"{FromLabel}\" AutomationProperties.Name=\"{FromLabel}\" Text=\"{{x:Bind ViewModel.RateFrom, Mode=TwoWay}}\" />\n"
        + $"                <TextBox Width=\"90\" PlaceholderText=\"{ToLabel}\" AutomationProperties.Name=\"{ToLabel}\" Text=\"{{x:Bind ViewModel.RateTo, Mode=TwoWay}}\" />\n"
        + $"                <TextBox Width=\"110\" PlaceholderText=\"yyyy-mm-dd\" AutomationProperties.Name=\"{DayLabel}\" Text=\"{{x:Bind ViewModel.RateDay, Mode=TwoWay}}\" />\n"
        + $"                <Button Content=\"{FindCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnFindRateClick\" />\n"
        + $"                <TextBlock VerticalAlignment=\"Center\" Text=\"{{x:Bind ViewModel.RateText, Mode=OneWay}}\" />\n";

    /// <summary> The click handler of the desktop page. </summary>
    public static string DesktopHandlers() =>
        "    private async void OnFindRateClick(object sender, RoutedEventArgs e) => await ViewModel.FindRateAsync();\n";

    /// <summary> The members of the desktop list's view model; <paramref name="read"/> is the body of the lookup after the currencies and the day are checked. </summary>
    public static string DesktopMembers(string read) =>
        "    /// <summary> The boxes and the answer of the rate lookup (RateTables). </summary>\n"
        + "    [ObservableProperty]\n    private string? _rateFrom;\n\n"
        + "    [ObservableProperty]\n    private string? _rateTo;\n\n"
        + "    [ObservableProperty]\n    private string? _rateDay;\n\n"
        + "    [ObservableProperty]\n    private string? _rateText;\n\n"
        + "    public async Task FindRateAsync()\n"
        + "    {\n"
        + "        string from = (RateFrom ?? \"\").Trim(), to = (RateTo ?? \"\").Trim();\n"
        + $"        if (from.Length == 0 || to.Length == 0) {{ RateText = \"{NeedsText}\"; return; }}\n"
        + "        DateTime day = DateTime.Today;\n"
        + $"        if (!string.IsNullOrWhiteSpace(RateDay) && !DateTime.TryParse(RateDay.Trim(), System.Globalization.CultureInfo.InvariantCulture, out day)) {{ RateText = \"{DayNeedsText}\"; return; }}\n"
        + read
        + "    }\n\n";

    /// <summary> The Uno lookup: the API's route. </summary>
    public static string UnoRead(RateNames names) =>
        "        try\n"
        + "        {\n"
        + $"            var row = await Client.{CsMethod}(from, to, day.ToString(\"yyyy-MM-dd\"));\n"
        + $"            RateText = $\"{{row.{names.From}}} to {{row.{names.To}}}: {{row.{names.Rate}}} (from {{row.{names.Day}:yyyy-MM-dd}}{names.CsSuffix()})\";\n"
        + "        }\n"
        + $"        catch (Exception) {{ RateText = \"{NoneText}\"; }}\n";

    /// <summary> The WinUI3 lookup: the same query as the API's route, over the dialog's own context. </summary>
    public static string WinUI3Read(TableModel table, ProjectSettings project, string context)
    {
        if (RatePlan.For(table, project) is not { } shape)
            return "";
        string row = table.TableName, f = shape.From.CSharpName(), t = shape.To.CSharpName(), d = shape.Day.CSharpName(), k = shape.Key.CSharpName();
        bool dateOnly = shape.Day.CSharpBase() == "DateOnly";
        string test = dateOnly ? $"x.{d} <= DateOnly.FromDateTime(day)" : $"x.{d} < end";
        string r = shape.Rate.CSharpName();
        string inverse = Inverts(shape, project)
            ? $"        bool inverted = false;\n"
              + $"        var other = await {context}.Set<{row}>().AsNoTracking().Where(x => x.{f}.ToUpper() == to && x.{t}.ToUpper() == from && {test}).OrderByDescending(x => x.{d}).ThenByDescending(x => x.{k}).FirstOrDefaultAsync();\n"
              + $"        if (other is not null && (row is null || other.{d} > row.{d}))\n"
              + "        {\n"
              + $"            (other.{f}, other.{t}) = (other.{t}, other.{f});\n"
              + $"            other.{r} = {RatePlan.One(shape.Rate)} / other.{r};\n"
              + "            row = other;\n"
              + "            inverted = true;\n"
              + "        }\n"
            : "";
        string suffix = Inverts(shape, project) ? $"{{(inverted ? \", {InvertedText}\" : \"\")}}" : "";
        return "        from = from.ToUpperInvariant();\n"
            + "        to = to.ToUpperInvariant();\n"
            + (dateOnly ? "" : "        var end = day.Date.AddDays(1);\n")
            + $"        var row = await {context}.Set<{row}>().AsNoTracking().Where(x => x.{f}.ToUpper() == from && x.{t}.ToUpper() == to && {test}).OrderByDescending(x => x.{d}).ThenByDescending(x => x.{k}).FirstOrDefaultAsync();\n"
            + inverse
            + $"        RateText = row is null ? \"{NoneText}\" : $\"{{row.{f}}} to {{row.{t}}}: {{row.{r}}} (from {{row.{d}:yyyy-MM-dd}}{suffix})\";\n";
    }

    /// <summary> The lines a WinUI3 dialog runs before it saves a table of <c>RateDocuments</c> (it writes through its own context, not through the API; the same rule as <c>ConvertCurrencyAsync</c> of the API): the
    /// currency is trimmed and upper case, the rate is the one in force on the day (1 for the base currency, a refusal when there is none) and kept while the currency and day stay, and the converted amount is worked
    /// out. Empty when the table is not listed. </summary>
    public static string WinUI3DocumentCheck(TableModel table, ProjectSettings project, string context)
    {
        if (RateDocumentPlan.For(table, project) is not { } d)
            return "";
        var rates = d.Rates;
        string row = table.TableName, cur = d.Currency.CSharpName(), rate = d.Rate.CSharpName(), key = d.Key.CSharpName();
        string rateType = rates.Day.CSharpBase();
        bool rateDateTime = rateType != "DateOnly";
        string documentDay = d.Day?.CSharpBase() ?? "";
        string day = d.Day?.CSharpName() ?? "";
        string toRateDay = d.Day is null ? (rateDateTime ? "DateTime.Today" : "DateOnly.FromDateTime(DateTime.Now)")
            : documentDay == rateType ? "rdDay" : rateDateTime ? "rdDay.ToDateTime(TimeOnly.MinValue)" : "DateOnly.FromDateTime(rdDay)";
        string today = documentDay == "DateOnly" ? "DateOnly.FromDateTime(DateTime.Now)" : "DateTime.Today";
        string rf = rates.From.CSharpName(), rt = rates.To.CSharpName(), rd = rates.Day.CSharpName(), rr = rates.Rate.CSharpName(), rk = rates.Key.CSharpName();
        string onOrBefore = rateDateTime ? $"x.{rd} < rdEnd" : $"x.{rd} <= rdRateDay";
        string baseCurrency = d.BaseCurrency;
        string one = d.Rate.CSharpBase() switch { "decimal" => "1m", "double" => "1.0", "float" => "1f", _ => "1" };
        string sameDay = d.Day is null ? "" : documentDay == "DateOnly" ? $" && rdStored.{day} == entity.{day}" : $" && rdStored.{day}.Date == entity.{day}.Date";
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append("        ").Append(text).Append('\n');
        L("{");
        L($"    entity.{cur} = (entity.{cur} ?? \"\").Trim().ToUpperInvariant();");
        L($"    if (entity.{cur}.Length == 0)");
        L("    {");
        L($"        ErrorMessage = \"{RateWords.DocumentNeedsCurrency}\";");
        L("        return false;");
        L("    }");
        L($"    var rdDay = {(d.Day is null ? today : "entity." + day)};");
        L("    bool rdKeep = false;");
        L("    if (_editing is not null)");
        L("    {");
        L($"        var rdStored = await {context}.Set<{row}>().AsNoTracking().Where(x => x.{key} == entity.{key}).Select(x => new {{ x.{cur}, x.{rate}{(d.Day is null ? "" : ", x." + day)} }}).FirstOrDefaultAsync();");
        L($"        if (rdStored is not null && (rdStored.{cur} ?? \"\").Trim().ToUpperInvariant() == entity.{cur}{sameDay})");
        L("        {");
        L($"            entity.{rate} = rdStored.{rate};");
        L("            rdKeep = true;");
        L("        }");
        L("    }");
        L("    if (!rdKeep)");
        L("    {");
        L($"        if (entity.{cur} == \"{baseCurrency}\")");
        L($"            entity.{rate} = {one};");
        L("        else");
        L("        {");
        L($"            string rdFrom = entity.{cur}, rdTo = \"{baseCurrency}\";");
        L($"            var rdRateDay = {toRateDay};");
        if (rateDateTime)
            L("            var rdEnd = rdRateDay.Date.AddDays(1);");
        L($"            var rdFound = await {context}.Set<{rates.Table.TableName}>().AsNoTracking().Where(x => x.{rf}.ToUpper() == rdFrom && x.{rt}.ToUpper() == rdTo && {onOrBefore}).OrderByDescending(x => x.{rd}).ThenByDescending(x => x.{rk}).FirstOrDefaultAsync();");
        if (Inverts(rates, project))
        {
            L($"            var rdOther = await {context}.Set<{rates.Table.TableName}>().AsNoTracking().Where(x => x.{rf}.ToUpper() == rdTo && x.{rt}.ToUpper() == rdFrom && {onOrBefore}).OrderByDescending(x => x.{rd}).ThenByDescending(x => x.{rk}).FirstOrDefaultAsync();");
            L($"            if (rdOther is not null && (rdFound is null || rdOther.{rd} > rdFound.{rd}))");
            L("            {");
            L($"                rdOther.{rr} = {RatePlan.One(rates.Rate)} / rdOther.{rr};");
            L("                rdFound = rdOther;");
            L("            }");
        }
        L("            if (rdFound is null)");
        L("            {");
        L($"                ErrorMessage = $\"There is no exchange rate from {{entity.{cur}}} to {baseCurrency} on or before {{rdDay:yyyy-MM-dd}}.\";");
        L("                return false;");
        L("            }");
        L($"            entity.{rate} = ({d.Rate.CSharpBase()})rdFound.{rr};");
        L("        }");
        L("    }");
        L($"    entity.{d.BaseAmount.CSharpName()} = ({d.BaseAmount.CSharpBase()})Math.Round((decimal)entity.{d.Amount.CSharpName()} * (decimal)entity.{rate}, {d.BaseAmount.Scale ?? RateDocumentPlan.DefaultScale}, MidpointRounding.AwayFromZero);");
        L("}");
        return o.Append('\n').ToString();
    }

    public static string BlazorBoxTest(RateNames json) =>
        BlazorBoxMethod(json, "Asks_for_the_rate_in_force_and_shows_it", 7, "USD to EUR: 0.9 (from 2024-03-01)")
        + (json.Inverse ? BlazorBoxMethod(json, "Says_when_the_rate_is_worked_out_from_the_opposite_pair", 0, InvertedText) : "");

    private static string BlazorBoxMethod(RateNames json, string name, int key, string expected) =>
        "\n"
        + "    [TestMethod]\n"
        + $"    public void {name}()\n"
        + "    {\n"
        + $"        Api.Answer(\"/{RouteName}\", \"{{ \\\"{json.Key}\\\": {key}, \\\"{json.From}\\\": \\\"USD\\\", \\\"{json.To}\\\": \\\"EUR\\\", \\\"{json.Rate}\\\": 0.9, \\\"{json.Day}\\\": \\\"2024-03-01T00:00:00\\\" }}\");\n"
        + "        var page = Open();\n"
        + $"        page.Find(\"[aria-label=\\\"{FromLabel}\\\"]\").Input(\" usd \");\n"
        + $"        page.Find(\"[aria-label=\\\"{ToLabel}\\\"]\").Input(\"EUR\");\n"
        + $"        page.Find(\"[aria-label=\\\"{DayLabel}\\\"]\").Input(\"2024-03-15\");\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{FindCaption}\").Click();\n"
        + $"        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"{expected}\"));\n"
        + $"        Assert.IsTrue(Api.Requests.Any(r => r.Path.EndsWith(\"/{RouteName}?from=usd&to=EUR&on=2024-03-15\")));\n"
        + "    }\n"
        + "\n";
}
