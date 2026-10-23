namespace CodeGenNew.Core;

/// <summary> A believable row for the generated front-end tests of a list page: the object literal a mocked list answer returns and the text of one cell the page must show.
/// Both web stacks (React and Angular) write the same row and the same two checks, so they cannot drift apart: the page shows a row the server returns, and the Edit button of that row opens the dialog. </summary>
public sealed record GridTestRow(string Literal, string Marker, string KeyText = "1")
{
    /// <summary> The same row as JSON (what the API answers), for the C# tests of the Blazor pages. </summary>
    public string Json { get; init; } = "{}";

    /// <summary> The text the marked cell shows. </summary>
    public const string MarkerText = "Sample row";

    /// <summary> The marker of a grid with no plain text column: a whole number a required integer column shows as it is (small enough for every integer type). </summary>
    public const string MarkerNumber = "42";

    private const string ShowModal = "HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };";

    /// <summary> The row, or null when the grid has no column whose text a test can look for (a required, plain text column the grid starts with, not a parent's name; failing that, a required whole-number column). </summary>
    public static GridTestRow? For(FormModel form, ChosenGridColumns chosen)
    {
        if (form.KeyColumn is null)
            return null;
        bool Plain(ChosenColumn c) => c.Initial && !c.Cut && !c.Column.IsNullable && !c.Column.IsPrimaryKey && form.ParentOf(c.Column) is null;
        var marked = chosen.Offered.FirstOrDefault(c => Plain(c) && c.Column.IsStringColumn && !c.Column.IsLongTextColumn && c.Column.PgText is null);
        bool number = marked is null;
        marked ??= chosen.Offered.FirstOrDefault(c => Plain(c) && c.Column.IsIntegerColumn && !c.Column.IsCurrencyColumn && !c.Column.IsMoneyColumn
            && !c.Column.IsBooleanColumn && !c.Column.IsDateColumn && !c.Column.IsStringColumn);
        if (marked is null)
            return null;

        string keyProp = JsonNames.Camel(form.KeyColumn.Name);
        string markedProp = JsonNames.Camel(marked.Column.Name);
        string markedValue = number ? MarkerNumber : $"'{MarkerText}'";
        var members = new List<string> { form.TextKey ? $"{keyProp}: '{ApiSampleValues.KeyText(form.KeyColumn)}'" : $"{keyProp}: 1" };
        foreach (string member in form.NewRowTypeScript.Where(m => !m.StartsWith(keyProp + ":")))
            members.Add(member.StartsWith(markedProp + ":") ? $"{markedProp}: {markedValue}" : member);
        if (!members.Any(m => m.StartsWith(markedProp + ":")))
            return null;
        var guids = form.Table.Columns.Where(c => c.IsGuidColumn).Select(c => JsonNames.Camel(c.Name)).ToHashSet();
        return new GridTestRow("{ " + string.Join(", ", members) + " }", number ? MarkerNumber : MarkerText, form.TextKey ? ApiSampleValues.KeyText(form.KeyColumn) : "1")
        {
            Json = "{ " + string.Join(", ", members.Select(m => JsonMember(m, guids))) + " }"
        };
    }

    private static string JsonMember(string member, HashSet<string> guids)
    {
        int colon = member.IndexOf(": ", StringComparison.Ordinal);
        string value = member[(colon + 2)..];
        if (guids.Contains(member[..colon]) && value == "''")
            value = "\"00000000-0000-0000-0000-000000000001\"";
        else if (value.StartsWith("new Date()", StringComparison.Ordinal))
            value = "\"2026-01-01\"";
        else if (value.StartsWith('\'') && value.EndsWith('\''))
            value = "\"" + value[1..^1].Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return $"\"{member[..colon]}\": {value}";
    }

    /// <summary> The Angular spec lines (each starts with a blank line): the first page request is answered with the row; the page shows it; its Edit button opens the dialog.
    /// <paramref name="firstPage"/> is the request matcher of the first page and <paramref name="firstReply"/> its answer holding <see cref="Literal"/>. An OData page reads the row again by its key when Edit
    /// is pressed (a slim grid has not read every property), so <paramref name="rowRead"/> is the matcher of that request (null: the page makes none). A screen with write roles is signed in by the spec's beforeEach. </summary>
    public IReadOnlyList<string> AngularSpecs(string title, string firstPage, string firstReply, string? rowRead = null)
    {
        var lines = new List<string>
        {
            "",
            "  it('shows a row the server returns', () => {",
            "    const http = TestBed.inject(HttpTestingController);",
            $"    http.expectOne({firstPage}).flush({firstReply});",
            "    fixture.detectChanges();",
            $"    expect(fixture.nativeElement.textContent).toContain('{Marker}');",
            "  });"
        };
        lines.AddRange(
        [
            "",
            $"  it('opens the {title} dialog from the Edit button of a row', () => {{",
            "    // jsdom has no showModal, so the test gives the dialog one that just opens it",
            "    " + ShowModal,
            "    const http = TestBed.inject(HttpTestingController);",
            $"    http.expectOne({firstPage}).flush({firstReply});",
            "    fixture.detectChanges();",
            "    const edit = (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]).find((b) => b.textContent?.trim() === 'Edit');",
            "    expect(edit).toBeTruthy();",
            "    edit!.click();"
        ]);
        if (rowRead is not null)
            lines.Add($"    http.match({rowRead}).forEach((request) => request.flush({Literal}));");
        lines.AddRange(
        [
            "    fixture.detectChanges();",
            $"    expect(fixture.nativeElement.textContent).toContain('Edit {title}');",
            "  });",
            "",
            $"  it('fills the {title} dialog with the row and sends the row to the service on Submit', async () => {{",
            "    " + ShowModal,
            "    const http = TestBed.inject(HttpTestingController);",
            $"    http.expectOne({firstPage}).flush({firstReply});",
            "    fixture.detectChanges();",
            "    const edit = (Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[]).find((b) => b.textContent?.trim() === 'Edit');",
            "    edit!.click();"
        ]);
        if (rowRead is not null)
            lines.Add($"    http.match({rowRead}).forEach((request) => request.flush({Literal}));");
        lines.AddRange(
        [
            "    fixture.detectChanges();",
            "    await fixture.whenStable();",
            "    fixture.detectChanges();",
            "    const fields = Array.from(fixture.nativeElement.querySelectorAll('dialog input')) as HTMLInputElement[];",
            $"    expect(fields.some((f) => f.value === '{Marker}')).toBe(true);",
            "    (fixture.nativeElement.querySelector('dialog button[type=\"submit\"]') as HTMLButtonElement).click();",
            "    const save = http.expectOne((r) => r.method === 'PUT');",
            $"    expect(JSON.stringify(save.request.body)).toContain('{Marker}');",
            "    save.flush(null);",
            "  });"
        ]);
        return lines;
    }

    /// <summary> The describe block to append after a screen's own spec: another page's grid opens this screen with <c>?edit=&lt;key&gt;</c>, the screen reads that row by its key and shows it in the dialog. </summary>
    public string AngularOpenedFromAnotherPage(string title, string screenClass) => $$"""

describe('{{screenClass}} opened from another page', () => {
  it('opens the row named by ?edit= in the dialog', async () => {
    {{ShowModal}}
    const { ActivatedRoute, convertToParamMap, provideRouter } = await import('@angular/router');
    await TestBed.configureTestingModule({
      imports: [{{screenClass}}],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ edit: '{{KeyText}}' }) } } }]
    }).compileComponents();
    const fixture = TestBed.createComponent({{screenClass}});
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.match((r) => r.method === 'GET' && r.url.endsWith('/{{KeyText}}')).forEach((request) => request.flush({{Literal}}));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Edit {{title}}');
  });
});

""";

    /// <summary> The React test lines (each test starts with a blank line): the mocked list answers with the row; the page shows it; its Edit button opens the dialog.
    /// <paramref name="writeRole"/> is a role that may write, for a page with sign-in, else null. An OData page reads the row again by its key when Edit is pressed (<paramref name="readsRowById"/>), so the test answers that too. </summary>
    public IReadOnlyList<string> ReactTests(string apiVar, string pageName, string title, string listCall, int pageSize, string? writeRole, bool readsRowById)
    {
        var lines = new List<string>();
        foreach (bool edit in new[] { false, true })
        {
            lines.Add("");
            lines.Add(edit ? $"  it('opens the {title} dialog from the Edit button of a row', async () => {{" : "  it('shows a row the server returns', async () => {");
            if (edit)
                lines.Add("    " + ShowModal);
            if (writeRole is not null)
                lines.Add($"    setSession({{ token: 'abc', user: {{ name: 'Pat', role: '{writeRole}' }} }});");
            lines.Add($"    vi.spyOn({apiVar}, '{listCall}').mockResolvedValue({{ items: [{Literal}], page: 1, pageSize: {pageSize}, totalCount: 1, totalPages: 1 }});");
            if (edit && readsRowById)
                lines.Add($"    vi.spyOn({apiVar}, 'getById').mockResolvedValue({Literal});");
            lines.Add($"    render(<MemoryRouter><{pageName} /></MemoryRouter>);");
            if (edit)
            {
                lines.Add("    fireEvent.click(await screen.findByText('Edit'));");
                lines.Add($"    expect(await screen.findByText('Edit {title}')).toBeInTheDocument();");
            }
            else
                lines.Add($"    expect(await screen.findByText('{Marker}')).toBeInTheDocument();");
            lines.Add("  });");
        }
        return lines;
    }
}
