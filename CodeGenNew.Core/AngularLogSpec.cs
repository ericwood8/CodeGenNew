using System.Text;

namespace CodeGenNew.Core;

/// <summary> The spec of the Angular screen of a log table (LogTables): it only lists rows, so it checks the first page, the date range the search sends, Clear, and that no button changes a row. </summary>
public static class AngularLogSpec
{
    public static string Text(string cls, string screenStem, IReadOnlyList<SearchField> dates, QueueTable? queue = null, ProjectSettings? project = null)
    {
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        string from = dates[0].ParamName, to = dates[1].ParamName;
        L("import { ComponentFixture, TestBed } from '@angular/core/testing';");
        L("import { provideHttpClient } from '@angular/common/http';");
        L("import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';");
        L("import { provideRouter } from '@angular/router';");
        L("import { provideNoopAnimations } from '@angular/platform-browser/animations';");
        L();
        L($"import {{ {cls} }} from './{screenStem}.component';");
        L();
        L($"describe('{cls}', () => {{");
        L($"  let component: {cls};");
        L($"  let fixture: ComponentFixture<{cls}>;");
        L("  let http: HttpTestingController;");
        L("  const empty = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };");
        L();
        L("  beforeEach(async () => {");
        L("    await TestBed.configureTestingModule({");
        L($"      imports: [{cls}],");
        L("      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideRouter([])]");
        L("    })");
        L("    .compileComponents();");
        L();
        L($"    fixture = TestBed.createComponent({cls});");
        L("    component = fixture.componentInstance;");
        L("    http = TestBed.inject(HttpTestingController);");
        L("    fixture.detectChanges();");
        L("  });");
        L();
        L("  it('asks for the first page when it opens', () => {");
        L("    const request = http.expectOne((r) => r.url.endsWith('/search'));");
        L("    expect(request.request.params.get('pageNumber')).toBe('1');");
        L($"    expect(request.request.params.has('{from}')).toBe(false);");
        L("    request.flush(empty);");
        L("    expect(component).toBeTruthy();");
        L("  });");
        L();
        L("  it('sends the first and the last day when the person searches', () => {");
        L("    http.expectOne((r) => r.url.endsWith('/search')).flush(empty);");
        L($"    component.filters.{from} = '2026-10-01';");
        L($"    component.filters.{to} = '2026-10-31';");
        L("    component.search();");
        L($"    const request = http.expectOne((r) => r.url.endsWith('/search') && r.params.get('{from}') === '2026-10-01' && r.params.get('{to}') === '2026-10-31');");
        L("    request.flush(empty);");
        L("  });");
        L();
        L("  it('has the date boxes and no button that adds, changes or deletes a row', () => {");
        L("    http.expectOne((r) => r.url.endsWith('/search')).flush(empty);");
        L("    fixture.detectChanges();");
        L("    const page = fixture.nativeElement as HTMLElement;");
        L($"    expect(page.querySelector('input[aria-label=\"{dates[0].Label}\"]')).not.toBeNull();");
        L($"    expect(page.querySelector('input[aria-label=\"{dates[1].Label}\"]')).not.toBeNull();");
        L("    const labels = Array.from(page.querySelectorAll('button')).map((button) => (button.textContent ?? '').trim());");
        L("    expect(labels).not.toContain('Add New');");
        L("    expect(labels).not.toContain('Edit');");
        L("    expect(labels).not.toContain('Delete');");
        L("    expect(page.querySelector('dialog')).toBeNull();");
        L("  });");
        if (queue is not null)
            QueueSpecs(L, queue, project!);
        L("});");
        return o.ToString();
    }

    private static void QueueSpecs(Action<string> L, QueueTable queue, ProjectSettings project)
    {
        string key = JsonNames.Camel(queue.Key.Name), status = JsonNames.Camel(queue.Status.Name);
        string row = "{ " + string.Join(", ", queue.Table.Columns.Select(c => c == queue.Key ? $"{key}: 7" : c == queue.Status ? $"{status}: '{QueueStatus.Failed}'" : $"{JsonNames.Camel(c.Name)}: {ApiSampleValues.Value(c, project, queue.Table.TableName)}")) + " }";
        string stats = "{ pending: 2, claimed: 1, done: 5, failed: 1, oldestPendingId: 4, oldestPendingOn: '2026-10-26T08:12:00' }";
        L("");
        L("  it('shows the counts by status and the oldest pending row', () => {");
        L("    http.expectOne((r) => r.url.endsWith('/search')).flush(empty);");
        L($"    http.expectOne((r) => r.url.endsWith('/stats')).flush({stats});");
        L("    fixture.detectChanges();");
        L("    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';");
        L("    expect(text).toContain('Pending: 2, claimed: 1, done: 5, failed: 1');");
        L("    expect(text).toContain('Oldest pending: #4');");
        L("  });");
        L("");
        L("  it('puts a failed row back to pending with Retry', () => {");
        L($"    const failed = {row};");
        L("    http.expectOne((r) => r.url.endsWith('/search')).flush({ items: [failed], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });");
        L($"    http.expectOne((r) => r.url.endsWith('/stats')).flush({stats});");
        L("    fixture.detectChanges();");
        L("    const retry = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((button) => (button.textContent ?? '').trim() === 'Retry');");
        L("    expect(retry).toBeDefined();");
        L("    retry!.click();");
        L("    const request = http.expectOne((r) => r.url.endsWith('/7/retry'));");
        L("    expect(request.request.method).toBe('POST');");
        L("    request.flush(failed);");
        L("    http.expectOne((r) => r.url.endsWith('/search')).flush(empty);");
        L($"    http.expectOne((r) => r.url.endsWith('/stats')).flush({stats});");
        L("  });");
    }
}
