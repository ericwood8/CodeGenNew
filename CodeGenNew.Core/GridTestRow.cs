namespace CodeGenNew.Core;

/// <summary> A believable row for the generated front-end tests of a list page: the object literal a mocked list answer returns and the text of one cell the page must show.
/// Both web stacks (React and Angular) write the same row and the same two checks, so they cannot drift apart: the page shows a row the server returns, and the Edit button of that row opens the dialog. </summary>
public sealed record GridTestRow(string Literal, string Marker)
{
    /// <summary> The text the marked cell shows. </summary>
    public const string MarkerText = "Sample row";

    private const string ShowModal = "HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };";

    /// <summary> The row, or null when the grid has no column whose text a test can look for (a required, plain text column the grid starts with, not a parent's name). </summary>
    public static GridTestRow? For(FormModel form, ChosenGridColumns chosen)
    {
        if (form.KeyColumn is null)
            return null;
        var marked = chosen.Offered.FirstOrDefault(c => c.Initial && !c.Cut && c.Column.IsStringColumn && !c.Column.IsLongTextColumn && !c.Column.IsNullable
            && !c.Column.IsPrimaryKey && form.ParentOf(c.Column) is null);
        if (marked is null)
            return null;

        string keyProp = JsonNames.Camel(form.KeyColumn.Name);
        string markedProp = JsonNames.Camel(marked.Column.Name);
        var members = new List<string> { form.TextKey ? $"{keyProp}: '{ApiSampleValues.KeyText(form.KeyColumn)}'" : $"{keyProp}: 1" };
        foreach (string member in form.NewRowTypeScript.Where(m => !m.StartsWith(keyProp + ":")))
            members.Add(member.StartsWith(markedProp + ":") ? $"{markedProp}: '{MarkerText}'" : member);
        if (!members.Any(m => m.StartsWith(markedProp + ":")))
            return null;
        return new GridTestRow("{ " + string.Join(", ", members) + " }", MarkerText);
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
