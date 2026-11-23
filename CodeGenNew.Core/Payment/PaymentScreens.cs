using CodeGenNew.Core.Settings;

namespace CodeGenNew.Core.Payment;

/// <summary> One box of the payment screens: a document box, a button and an answer. The texts of the schedule box (<see cref="PaymentScreens.Schedule"/>) and the paid box (<see cref="PaymentScreens.Paid"/>) differ; the markup does not. </summary>
public sealed record PaymentBox(string Member, string Route, string DocumentLabel, string ShowCaption, string ResultLabel, string NeedsText, string NoneText, string CsMethod, string Answer, string TsText, string CsText, string Comment, string TestAnswer, string TestAnswerCs, string TestText)
{
    /// <summary> The member name with a capital: <c>Schedule</c>, <c>Paid</c>. </summary>
    public string Cap => char.ToUpperInvariant(Member[0]) + Member[1..];

    public const string DocumentHint = "Document";
}

/// <summary> The schedule box on the list page of an installment table (<c>InstallmentTables</c>): a document box and a <c>Show schedule</c> button that answer what the installments of the document come to
/// (<c>Scheduled 200 of 300, 100 left</c>, or that the schedule is complete), over the table's <c>schedule</c> route. The paid box on the list page of a ledger table (<c>LedgerTables</c>) is the same box over the <c>paid</c>
/// route (<c>Paid 200 of 300, 100 left</c>, or paid in full). The WinUI3 list reads the same sums over its own context, and its dialog runs the API's installment rule before it saves. </summary>
public static class PaymentScreens
{
    /// <summary> The schedule box of an installment table (<c>InstallmentTables</c>). </summary>
    public static readonly PaymentBox Schedule = new("schedule", "schedule", "Schedule document", "Show schedule", "Schedule", "Give the document number.", "There is no document with that number.", "ScheduleAsync",
        "{ total: number; scheduled: number; remaining: number; complete: boolean }",
        "row.complete ? `Complete: ${row.scheduled} of ${row.total}` : `Scheduled ${row.scheduled} of ${row.total}, ${row.remaining} left`",
        "row.GetProperty(\"complete\").GetBoolean() ? $\"Complete: {row.GetProperty(\"scheduled\")} of {row.GetProperty(\"total\")}\" : $\"Scheduled {row.GetProperty(\"scheduled\")} of {row.GetProperty(\"total\")}, {row.GetProperty(\"remaining\")} left\"",
        "an installment table (InstallmentTables): what the installments of a document come to",
        "{ total: 300, scheduled: 200, remaining: 100, complete: false }", "{ \\\"total\\\": 300, \\\"scheduled\\\": 200, \\\"remaining\\\": 100, \\\"complete\\\": false }", "Scheduled 200 of 300, 100 left");

    /// <summary> The paid box of a payment ledger (<c>LedgerTables</c>): what a document has been paid, over the table's <c>paid</c> route. </summary>
    public static readonly PaymentBox Paid = new("paid", "paid", "Paid document", "Show paid", "Paid", "Give the document number.", "There is no document with that number.", "PaidAsync",
        "{ total: number; captured: number; refunded: number; paid: number; remaining: number; settled: boolean }",
        "row.settled ? `Paid in full: ${row.paid} of ${row.total}` : `Paid ${row.paid} of ${row.total}, ${row.remaining} left`",
        "row.GetProperty(\"settled\").GetBoolean() ? $\"Paid in full: {row.GetProperty(\"paid\")} of {row.GetProperty(\"total\")}\" : $\"Paid {row.GetProperty(\"paid\")} of {row.GetProperty(\"total\")}, {row.GetProperty(\"remaining\")} left\"",
        "a payment ledger (LedgerTables): what a document has been paid",
        "{ total: 300, captured: 200, refunded: 0, paid: 200, remaining: 100, settled: false }", "{ \\\"total\\\": 300, \\\"captured\\\": 200, \\\"refunded\\\": 0, \\\"paid\\\": 200, \\\"remaining\\\": 100, \\\"settled\\\": false }", "Paid 200 of 300, 100 left");

    public static bool HasBox(TableModel table, ProjectSettings project) => PaymentPlan.IsInstallmentTable(table.TableName, project);

    /// <summary> Whether the list of the table has the paid box (a table of <c>LedgerTables</c>). </summary>
    public static bool HasPaidBox(TableModel table, ProjectSettings project) => LedgerPlan.IsListed(table.TableName, project);

    public static string ReactApiLine(PaymentBox? box = null) { var b = box ?? Schedule; return $"  {b.Route}: (document: string) => request<{b.Answer}>(`${{apiUrl}}/{b.Route}?document=${{encodeURIComponent(document)}}`),"; }

    public static string ReactODataApiLine(string restPath, PaymentBox? box = null) { var b = box ?? Schedule; return $"  {b.Route}: (document: string) => request<{b.Answer}>(`{restPath}/{b.Route}?document=${{encodeURIComponent(document)}}`),"; }

    public static IReadOnlyList<string> ReactHandlers(string apiVar, PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            $"  // {b.Comment}",
            $"  const [{b.Member}Document, set{b.Cap}Document] = useState('');",
            $"  const [{b.Member}Text, set{b.Cap}Text] = useState('');",
            $"  const show{b.Cap} = () => {{",
            $"    const document = {b.Member}Document.trim();",
            $"    if (!document) {{ set{b.Cap}Text('{b.NeedsText}'); return; }}",
            $"    {apiVar}.{b.Route}(document)",
            $"      .then((row) => set{b.Cap}Text({b.TsText}))",
            $"      .catch(() => set{b.Cap}Text('{b.NoneText}'));",
            "  };",
        ];
    }

    public static IReadOnlyList<string> ReactBox(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            "      <div>",
            $"        <input type=\"text\" placeholder=\"{PaymentBox.DocumentHint}\" aria-label=\"{b.DocumentLabel}\" value={{{b.Member}Document}} onChange={{(event) => set{b.Cap}Document(event.target.value)}} />",
            $"        <button type=\"button\" onClick={{show{b.Cap}}}>{b.ShowCaption}</button>",
            $"        <span role=\"status\" aria-label=\"{b.ResultLabel}\">{{{b.Member}Text}}</span>",
            "      </div>",
        ];
    }

    public static IReadOnlyList<string> ReactBoxTests(string apiVar, string pageName, PaymentBox? box = null, bool router = true)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            $"  it('asks for the {b.Member} of a document and shows it, or says there is none', async () => {{",
            $"    const found = vi.spyOn({apiVar}, '{b.Route}').mockResolvedValueOnce({b.TestAnswer} as never).mockRejectedValueOnce(new Error('none'));",
            router ? $"    render(<MemoryRouter><{pageName} /></MemoryRouter>);" : $"    render(<{pageName} />);",
            $"    fireEvent.change(await screen.findByLabelText('{b.DocumentLabel}'), {{ target: {{ value: ' 7 ' }} }});",
            $"    fireEvent.click(screen.getByText('{b.ShowCaption}'));",
            "    expect(found).toHaveBeenCalledWith('7');",
            $"    expect(await screen.findByText('{b.TestText}')).toBeInTheDocument();",
            $"    fireEvent.click(screen.getByText('{b.ShowCaption}'));",
            $"    expect(await screen.findByText('{b.NoneText}')).toBeInTheDocument();",
            "  });",
        ];
    }

    public static IReadOnlyList<string> AngularServiceLines(string urlMember, PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            $"    // {b.Comment}",
            $"    {b.Route}(document: string): Observable<{b.Answer}> {{",
            $"        return this.http.get<{b.Answer}>(`${{this.{urlMember}}}/{b.Route}?document=${{encodeURIComponent(document)}}`);",
            "    }",
        ];
    }

    public static IReadOnlyList<string> AngularBox(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            $"    <!-- {b.DocumentLabel} -->",
            "    <div class=\"form-group form-group-search dept\">",
            $"        <input type=\"text\" class=\"form-control\" placeholder=\"{PaymentBox.DocumentHint}\" aria-label=\"{b.DocumentLabel}\" [(ngModel)]=\"{b.Member}Document\" />",
            $"        <button class=\"btn btn-outline-secondary\" type=\"button\" (click)=\"show{b.Cap}()\"> {b.ShowCaption} </button>",
            $"        <span role=\"status\" aria-label=\"{b.ResultLabel}\">{{{{ {b.Member}Text }}}}</span>",
            "    </div>",
        ];
    }

    public static IReadOnlyList<string> AngularHandlers(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            $"  // {b.Comment}",
            $"  {b.Member}Document = '';",
            $"  {b.Member}Text = '';",
            "",
            $"  show{b.Cap}(): void {{",
            $"    const document = this.{b.Member}Document.trim();",
            $"    if (!document) {{ this.{b.Member}Text = '{b.NeedsText}'; return; }}",
            $"    this.service.{b.Route}(document).subscribe({{",
            $"      next: (row) => {{ this.{b.Member}Text = {b.TsText}; }},",
            $"      error: () => {{ this.{b.Member}Text = '{b.NoneText}'; }}",
            "    });",
            "  }",
        ];
    }

    public static string AngularBoxTest(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return $"  it('asks for the {b.Member} of a document and shows it', () => {{\n"
            + "    const http = TestBed.inject(HttpTestingController);\n"
            + $"    component.{b.Member}Document = ' 7 ';\n"
            + $"    component.show{b.Cap}();\n"
            + $"    const request = http.expectOne((r) => r.urlWithParams.endsWith('/{b.Route}?document=7'));\n"
            + $"    request.flush({b.TestAnswer});\n"
            + $"    expect(component.{b.Member}Text).toContain('{b.TestText}');\n"
            + "  });\n";
    }

    public static IReadOnlyList<string> CsClientLines(string route, PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            $"    // {char.ToUpperInvariant(b.Comment[0])}{b.Comment[1..]}.",
            $"    public async Task<System.Text.Json.JsonElement> {b.CsMethod}(string document) => await (await http.GetAsync($\"{route}/{b.Route}?document={{Uri.EscapeDataString(document)}}\")).ReadAsync<System.Text.Json.JsonElement>();",
        ];
    }

    public static IReadOnlyList<string> BlazorBox(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            "<div class=\"search\">",
            $"    <input type=\"text\" placeholder=\"{PaymentBox.DocumentHint}\" aria-label=\"{b.DocumentLabel}\" @bind=\"{b.Member}Document\" @bind:event=\"oninput\" />",
            $"    <button type=\"button\" @onclick=\"Show{b.Cap}Async\">{b.ShowCaption}</button>",
            $"    <span role=\"status\" aria-label=\"{b.ResultLabel}\">@{b.Member}Text</span>",
            "</div>",
        ];
    }

    public static IReadOnlyList<string> BlazorHandlers(string client, PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return
        [
            "",
            $"    // {char.ToUpperInvariant(b.Comment[0])}{b.Comment[1..]}.",
            $"    private string? {b.Member}Document;",
            $"    private string? {b.Member}Text;",
            "",
            $"    private async Task Show{b.Cap}Async()",
            "    {",
            $"        string document = ({b.Member}Document ?? \"\").Trim();",
            $"        if (document.Length == 0) {{ {b.Member}Text = \"{b.NeedsText}\"; return; }}",
            "        try",
            "        {",
            $"            var row = await {client}.{b.CsMethod}(document);",
            $"            {b.Member}Text = {b.CsText};",
            "        }",
            $"        catch (Exception ex) when (ex is ApiException or HttpRequestException) {{ {b.Member}Text = \"{b.NoneText}\"; }}",
            "    }",
        ];
    }

    public static string BlazorBoxTest(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return "\n"
            + "    [TestMethod]\n"
            + $"    public void Asks_for_the_{b.Member}_of_a_document_and_shows_it()\n"
            + "    {\n"
            + $"        Api.Answer(\"/{b.Route}\", \"{b.TestAnswerCs}\");\n"
            + "        var page = Open();\n"
            + $"        page.Find(\"[aria-label=\\\"{b.DocumentLabel}\\\"]\").Input(\" 7 \");\n"
            + $"        page.FindAll(\"button\").First(b => b.TextContent.Trim() == \"{b.ShowCaption}\").Click();\n"
            + $"        page.WaitForAssertion(() => StringAssert.Contains(page.Markup, \"{b.TestText}\"));\n"
            + $"        Assert.IsTrue(Api.Requests.Any(r => r.Path.EndsWith(\"/{b.Route}?document=7\")));\n"
            + "    }\n"
            + "\n";
    }

    /// <summary> The box, the button and the answer in the top bar of a WinUI3 or Uno list (sixteen spaces in). </summary>
    public static string DesktopBox(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return $"                <TextBox Width=\"120\" PlaceholderText=\"{PaymentBox.DocumentHint}\" AutomationProperties.Name=\"{b.DocumentLabel}\" Text=\"{{x:Bind ViewModel.{b.Cap}Document, Mode=TwoWay}}\" />\n"
            + $"                <Button Content=\"{b.ShowCaption}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Click=\"OnShow{b.Cap}Click\" />\n"
            + $"                <TextBlock VerticalAlignment=\"Center\" Text=\"{{x:Bind ViewModel.{b.Cap}Text, Mode=OneWay}}\" />\n";
    }

    public static string DesktopHandlers(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return $"    private async void OnShow{b.Cap}Click(object sender, RoutedEventArgs e) => await ViewModel.Show{b.Cap}Async();\n";
    }

    /// <summary> The members of the desktop list's view model; <paramref name="read"/> is the body that reads the answer once the document is a number. </summary>
    public static string DesktopMembers(string read, string keyType, PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        string setting = b == Paid ? "LedgerTables" : "InstallmentTables";
        return $"    /// <summary> The box and the answer of the {b.Member} ({setting}). </summary>\n"
            + $"    [ObservableProperty]\n    private string? _{b.Member}Document;\n\n"
            + $"    [ObservableProperty]\n    private string? _{b.Member}Text;\n\n"
            + $"    public async Task Show{b.Cap}Async()\n"
            + "    {\n"
            + $"        if (string.IsNullOrWhiteSpace({b.Cap}Document)) {{ {b.Cap}Text = \"{b.NeedsText}\"; return; }}\n"
            + $"        if (!{keyType}.TryParse({b.Cap}Document.Trim(), out {keyType} document)) {{ {b.Cap}Text = \"{b.NeedsText}\"; return; }}\n"
            + read
            + "    }\n\n";
    }

    public static string UnoRead(PaymentBox? box = null)
    {
        var b = box ?? Schedule;
        return "        try\n"
            + "        {\n"
            + "            var row = await Client." + b.CsMethod + "(document.ToString());\n"
            + $"            {b.Cap}Text = {b.CsText};\n"
            + "        }\n"
            + $"        catch (Exception) {{ {b.Cap}Text = \"{b.NoneText}\"; }}\n";
    }

    /// <summary> The WinUI3 read: the same sums as the API's route, over the dialog's own context. </summary>
    public static string WinUI3Read(TableModel table, ProjectSettings project, string context)
    {
        if (PaymentPlan.InstallmentFor(table, project) is not { } s)
            return "";
        string documents = s.DocumentTable.TableName, rows = table.TableName;
        return $"        var held = await {context}.Set<{documents}>().AsNoTracking().Where(d => d.{s.DocumentKey.CSharpName()} == document).Select(d => new {{ d.{s.Total.CSharpName()} }}).FirstOrDefaultAsync();\n"
            + $"        if (held is null) {{ ScheduleText = \"{Schedule.NoneText}\"; return; }}\n"
            + $"        var amounts = await {context}.Set<{rows}>().AsNoTracking().Where(x => x.{s.Document.CSharpName()} == document).Select(x => x.{s.Amount.CSharpName()}).ToListAsync();\n"
            + $"        decimal total = (decimal)held.{s.Total.CSharpName()}, scheduled = amounts.Sum(x => (decimal)x);\n"
            + "        ScheduleText = scheduled == total ? $\"Complete: {scheduled} of {total}\" : $\"Scheduled {scheduled} of {total}, {total - scheduled} left\";\n";
    }

    /// <summary> The WinUI3 read of the paid box: the same sums as the API's <c>paid</c> route, over the list's own context. </summary>
    public static string WinUI3PaidRead(TableModel table, ProjectSettings project, string context)
    {
        if (LedgerPlan.For(table, project) is not { } s)
            return "";
        string documents = s.DocumentTable.TableName, rows = table.TableName;
        string kind = s.Kind.CSharpName(), amount = s.Amount.CSharpName();
        return $"        var held = await {context}.Set<{documents}>().AsNoTracking().Where(d => d.{s.DocumentKey.CSharpName()} == document).Select(d => new {{ d.{s.Total.CSharpName()} }}).FirstOrDefaultAsync();\n"
            + $"        if (held is null) {{ PaidText = \"{Paid.NoneText}\"; return; }}\n"
            + $"        var entries = await {context}.Set<{rows}>().AsNoTracking().Where(x => x.{s.Document.CSharpName()} == document && x.{s.Status.CSharpName()} == \"{LedgerPlan.Succeeded}\").Select(x => new {{ x.{kind}, x.{amount} }}).ToListAsync();\n"
            + $"        decimal total = (decimal)held.{s.Total.CSharpName()}, captured = entries.Where(x => x.{kind} == \"{LedgerPlan.Capture}\").Sum(x => (decimal)x.{amount}), refunded = -entries.Where(x => x.{kind} == \"{LedgerPlan.Refund}\").Sum(x => (decimal)x.{amount});\n"
            + "        decimal paid = captured - refunded;\n"
            + "        PaidText = paid >= total ? $\"Paid in full: {paid} of {total}\" : $\"Paid {paid} of {total}, {total - paid} left\";\n";
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
