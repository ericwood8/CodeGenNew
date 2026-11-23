using CodeGenNew.Core.Settings;

namespace CodeGenNew.Core.Payment;

/// <summary> The schedule box on the list page of an installment table (<c>InstallmentTables</c>): a document box and a <c>Show schedule</c> button that answer what the installments of the document come to
/// (<c>Scheduled 200 of 300, 100 left</c>, or that the schedule is complete), over the table's <c>schedule</c> route. The WinUI3 list reads the same sums over its own context, and its dialog runs the API's installment rule
/// before it saves. </summary>
public static class PaymentScreens
{
    public const string RouteName = "schedule";

    public const string Route = "schedule";

    public const string DocumentLabel = "Schedule document";

    public const string DocumentHint = "Document";

    public const string ShowCaption = "Show schedule";

    public const string ResultLabel = "Schedule";

    public const string NeedsText = "Give the document number.";

    public const string NoneText = "There is no document with that number.";

    public static bool HasBox(TableModel table, ProjectSettings project) => PaymentPlan.IsInstallmentTable(table.TableName, project);

    public const string CsMethod = "ScheduleAsync";

    private const string Answer = "{ total: number; scheduled: number; remaining: number; complete: boolean }";

    private const string TsText = "row.complete ? `Complete: ${row.scheduled} of ${row.total}` : `Scheduled ${row.scheduled} of ${row.total}, ${row.remaining} left`";

    public static string ReactApiLine() => $"  {Route}: (document: string) => request<{Answer}>(`${{apiUrl}}/{RouteName}?document=${{encodeURIComponent(document)}}`),";

    public static string ReactODataApiLine(string restPath) => $"  {Route}: (document: string) => request<{Answer}>(`{restPath}/{RouteName}?document=${{encodeURIComponent(document)}}`),";

    public static IReadOnlyList<string> ReactHandlers(string apiVar) =>
    [
        "",
        "  // an installment table (InstallmentTables): what the installments of a document come to",
        "  const [scheduleDocument, setScheduleDocument] = useState('');",
        "  const [scheduleText, setScheduleText] = useState('');",
        "  const showSchedule = () => {",
        "    const document = scheduleDocument.trim();",
        $"    if (!document) {{ setScheduleText('{NeedsText}'); return; }}",
        $"    {apiVar}.{Route}(document)",
        $"      .then((row) => setScheduleText({TsText}))",
        $"      .catch(() => setScheduleText('{NoneText}'));",
        "  };",
    ];

    public static IReadOnlyList<string> ReactBox() =>
    [
        "",
        "      <div>",
        $"        <input type=\"text\" placeholder=\"{DocumentHint}\" aria-label=\"{DocumentLabel}\" value={{scheduleDocument}} onChange={{(event) => setScheduleDocument(event.target.value)}} />",
        $"        <button type=\"button\" onClick={{showSchedule}}>{ShowCaption}</button>",
        $"        <span role=\"status\" aria-label=\"{ResultLabel}\">{{scheduleText}}</span>",
        "      </div>",
    ];

    public static IReadOnlyList<string> ReactBoxTests(string apiVar, string pageName) =>
    [
        "",
        "  it('asks for the schedule of a document and shows it, or says there is none', async () => {",
        $"    const found = vi.spyOn({apiVar}, '{Route}').mockResolvedValueOnce({{ total: 300, scheduled: 200, remaining: 100, complete: false }} as never).mockRejectedValueOnce(new Error('none'));",
        $"    render(<MemoryRouter><{pageName} /></MemoryRouter>);",
        $"    fireEvent.change(await screen.findByLabelText('{DocumentLabel}'), {{ target: {{ value: ' 7 ' }} }});",
        $"    fireEvent.click(screen.getByText('{ShowCaption}'));",
        "    expect(found).toHaveBeenCalledWith('7');",
        "    expect(await screen.findByText('Scheduled 200 of 300, 100 left')).toBeInTheDocument();",
        $"    fireEvent.click(screen.getByText('{ShowCaption}'));",
        $"    expect(await screen.findByText('{NoneText}')).toBeInTheDocument();",
        "  });",
    ];

    public static IReadOnlyList<string> AngularServiceLines(string urlMember) =>
    [
        "",
        "    // an installment table (InstallmentTables): what the installments of a document come to",
        $"    {Route}(document: string): Observable<{Answer}> {{",
        $"        return this.http.get<{Answer}>(`${{this.{urlMember}}}/{RouteName}?document=${{encodeURIComponent(document)}}`);",
        "    }",
    ];

    public static IReadOnlyList<string> AngularBox() =>
    [
        "",
        $"    <!-- {DocumentLabel} -->",
        "    <div class=\"form-group form-group-search dept\">",
        $"        <input type=\"text\" class=\"form-control\" placeholder=\"{DocumentHint}\" aria-label=\"{DocumentLabel}\" [(ngModel)]=\"scheduleDocument\" />",
        $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"showSchedule()\"> {ShowCaption} </button>",
        $"        <span role=\"status\" aria-label=\"{ResultLabel}\">{{{{ scheduleText }}}}</span>",
        "    </div>",
    ];

    public static IReadOnlyList<string> AngularHandlers() =>
    [
        "",
        "  // an installment table (InstallmentTables): what the installments of a document come to",
        "  scheduleDocument = '';",
        "  scheduleText = '';",
        "",
        "  showSchedule(): void {",
        "    const document = this.scheduleDocument.trim();",
        $"    if (!document) {{ this.scheduleText = '{NeedsText}'; return; }}",
        $"    this.service.{Route}(document).subscribe({{",
        $"      next: (row) => {{ this.scheduleText = {TsText}; }},",
        $"      error: () => {{ this.scheduleText = '{NoneText}'; }}",
        "    });",
        "  }",
    ];

    public static string AngularBoxTest() =>
        "  it('asks for the schedule of a document and shows it', () => {\n"
        + "    const http = TestBed.inject(HttpTestingController);\n"
        + "    component.scheduleDocument = ' 7 ';\n"
        + "    component.showSchedule();\n"
        + $"    const request = http.expectOne((r) => r.urlWithParams.endsWith('/{RouteName}?document=7'));\n"
        + "    request.flush({ total: 300, scheduled: 200, remaining: 100, complete: false });\n"
        + "    expect(component.scheduleText).toContain('Scheduled 200 of 300, 100 left');\n"
        + "  });\n";

    public static IReadOnlyList<string> CsClientLines(string route) =>
    [
        "",
        "    // An installment table (InstallmentTables): what the installments of a document come to.",
        $"    public async Task<System.Text.Json.JsonElement> {CsMethod}(string document) => await (await http.GetAsync($\"{route}/{RouteName}?document={{Uri.EscapeDataString(document)}}\")).ReadAsync<System.Text.Json.JsonElement>();",
    ];

    private const string CsText = "row.GetProperty(\"complete\").GetBoolean() ? $\"Complete: {row.GetProperty(\"scheduled\")} of {row.GetProperty(\"total\")}\" : $\"Scheduled {row.GetProperty(\"scheduled\")} of {row.GetProperty(\"total\")}, {row.GetProperty(\"remaining\")} left\"";

    public static IReadOnlyList<string> BlazorBox() =>
    [
        "",
        "<div class=\"search\">",
        $"    <input type=\"text\" placeholder=\"{DocumentHint}\" aria-label=\"{DocumentLabel}\" @bind=\"scheduleDocument\" @bind:event=\"oninput\" />",
        $"    <button type=\"button\" @onclick=\"ShowScheduleAsync\">{ShowCaption}</button>",
        $"    <span role=\"status\" aria-label=\"{ResultLabel}\">@scheduleText</span>",
        "</div>",
    ];

    public static IReadOnlyList<string> BlazorHandlers(string client) =>
    [
        "",
        "    // An installment table (InstallmentTables): what the installments of a document come to.",
        "    private string? scheduleDocument;",
        "    private string? scheduleText;",
        "",
        "    private async Task ShowScheduleAsync()",
        "    {",
        "        string document = (scheduleDocument ?? \"\").Trim();",
        $"        if (document.Length == 0) {{ scheduleText = \"{NeedsText}\"; return; }}",
        "        try",
        "        {",
        $"            var row = await {client}.{CsMethod}(document);",
        $"            scheduleText = {CsText};",
        "        }",
        $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ scheduleText = \"{NoneText}\"; }}",
        "    }",
    ];

    public static string BlazorBoxTest() =>
        "\n"
        + "    [TestMethod]\n"
        + "    public void Asks_for_the_schedule_of_a_document_and_shows_it()\n"
        + "    {\n"
        + $"        Api.Answer(\"/{RouteName}\", \"{{ \\\"total\\\": 300, \\\"scheduled\\\": 200, \\\"remaining\\\": 100, \\\"complete\\\": false }}\");\n"
        + "        var page = Open();\n"
        + $"        page.Find(\"[aria-label=\\\"{DocumentLabel}\\\"]\").Input(\" 7 \");\n"
        + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{ShowCaption}\").Click();\n"
        + "        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"Scheduled 200 of 300, 100 left\"));\n"
        + $"        Assert.IsTrue(Api.Requests.Any(r => r.Path.EndsWith(\"/{RouteName}?document=7\")));\n"
        + "    }\n"
        + "\n";

    /// <summary> The box, the button and the answer in the top bar of a WinUI3 or Uno list (sixteen spaces in). </summary>
    public static string DesktopBox() =>
        $"                <TextBox Width=\"120\" PlaceholderText=\"{DocumentHint}\" AutomationProperties.Name=\"{DocumentLabel}\" Text=\"{{x:Bind ViewModel.ScheduleDocument, Mode=TwoWay}}\" />\n"
        + $"                <Button Content=\"{ShowCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnShowScheduleClick\" />\n"
        + $"                <TextBlock VerticalAlignment=\"Center\" Text=\"{{x:Bind ViewModel.ScheduleText, Mode=OneWay}}\" />\n";

    public static string DesktopHandlers() =>
        "    private async void OnShowScheduleClick(object sender, RoutedEventArgs e) => await ViewModel.ShowScheduleAsync();\n";

    /// <summary> The members of the desktop list's view model; <paramref name="read"/> is the body that reads the answer once the document is a number. </summary>
    public static string DesktopMembers(string read, string keyType) =>
        "    /// <summary> The box and the answer of the schedule (InstallmentTables). </summary>\n"
        + "    [ObservableProperty]\n    private string? _scheduleDocument;\n\n"
        + "    [ObservableProperty]\n    private string? _scheduleText;\n\n"
        + "    public async Task ShowScheduleAsync()\n"
        + "    {\n"
        + $"        if (string.IsNullOrWhiteSpace(ScheduleDocument)) {{ ScheduleText = \"{NeedsText}\"; return; }}\n"
        + $"        if (!{keyType}.TryParse(ScheduleDocument.Trim(), out {keyType} document)) {{ ScheduleText = \"{NeedsText}\"; return; }}\n"
        + read
        + "    }\n\n";

    public static string UnoRead() =>
        "        try\n"
        + "        {\n"
        + "            var row = await Client." + CsMethod + "(document.ToString());\n"
        + $"            ScheduleText = {CsText};\n"
        + "        }\n"
        + $"        catch (Exception) {{ ScheduleText = \"{NoneText}\"; }}\n";

    /// <summary> The WinUI3 read: the same sums as the API's route, over the dialog's own context. </summary>
    public static string WinUI3Read(TableModel table, ProjectSettings project, string context)
    {
        if (PaymentPlan.InstallmentFor(table, project) is not { } s)
            return "";
        string documents = s.DocumentTable.TableName, rows = table.TableName;
        return $"        var held = await {context}.Set<{documents}>().AsNoTracking().Where(d => d.{s.DocumentKey.CSharpName()} == document).Select(d => new {{ d.{s.Total.CSharpName()} }}).FirstOrDefaultAsync();\n"
            + $"        if (held is null) {{ ScheduleText = \"{NoneText}\"; return; }}\n"
            + $"        var amounts = await {context}.Set<{rows}>().AsNoTracking().Where(x => x.{s.Document.CSharpName()} == document).Select(x => x.{s.Amount.CSharpName()}).ToListAsync();\n"
            + $"        decimal total = (decimal)held.{s.Total.CSharpName()}, scheduled = amounts.Sum(x => (decimal)x);\n"
            + "        ScheduleText = scheduled == total ? $\"Complete: {scheduled} of {total}\" : $\"Scheduled {scheduled} of {total}, {total - scheduled} left\";\n";
    }

    /// <summary> The lines a WinUI3 dialog runs before it saves a row of an installment table (it writes through its own context; the same rule as <c>CheckInstallmentAsync</c> of the API). Empty when the table is not listed. </summary>
    public static string WinUI3Check(TableModel table, ProjectSettings project, string context)
    {
        if (PaymentPlan.InstallmentFor(table, project) is not { } s)
            return "";
        string row = table.TableName, doc = s.Document.CSharpName(), seq = s.Sequence.CSharpName(), due = s.Due.CSharpName(), amount = s.Amount.CSharpName(), key = s.Key.CSharpName();
        var o = new System.Text.StringBuilder();
        void L(string text) => o.Append("        ").Append(text).Append('\n');
        void Refuse(string condition, string message)
        {
            L($"    if ({condition})");
            L("    {");
            L($"        ErrorMessage = \"{message}\";");
            L("        return false;");
            L("    }");
        }
        L("{");
        Refuse($"(decimal)entity.{seq} <= 0", PaymentWords.SequenceNotPositive);
        Refuse($"(decimal)entity.{amount} <= 0", PaymentWords.AmountNotPositive);
        L($"    var pmOthers = await {context}.Set<{row}>().AsNoTracking().Where(x => x.{key} != entity.{key} && x.{doc} == entity.{doc}).Select(x => new {{ x.{seq}, x.{due}, x.{amount} }}).ToListAsync();");
        Refuse($"pmOthers.Any(x => x.{seq} == entity.{seq})", PaymentWords.SequenceTaken);
        Refuse($"pmOthers.Any(x => x.{seq} < entity.{seq} && x.{due} > entity.{due} || x.{seq} > entity.{seq} && x.{due} < entity.{due})", PaymentWords.OutOfOrder);
        L($"    var pmTotal = await {context}.Set<{s.DocumentTable.TableName}>().AsNoTracking().Where(d => d.{s.DocumentKey.CSharpName()} == entity.{doc}).Select(d => new {{ d.{s.Total.CSharpName()} }}).FirstOrDefaultAsync();");
        Refuse($"pmTotal is not null && pmOthers.Sum(x => (decimal)x.{amount}) + (decimal)entity.{amount} > (decimal)pmTotal.{s.Total.CSharpName()}", PaymentWords.OverTotal);
        L("}");
        return o.Append('\n').ToString();
    }
}
