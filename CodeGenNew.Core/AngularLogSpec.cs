using System.Text;

namespace CodeGenNew.Core;

/// <summary> The spec of the Angular screen of a log table (LogTables): it only lists rows, so it checks the first page, the date range the search sends, Clear, and that no button changes a row. </summary>
public static class AngularLogSpec
{
    public static string Text(string cls, string screenStem, IReadOnlyList<SearchField> dates)
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
        L("});");
        return o.ToString();
    }
}
