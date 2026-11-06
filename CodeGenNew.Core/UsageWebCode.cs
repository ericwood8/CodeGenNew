using static CodeGenNew.Core.TemplateText;

namespace CodeGenNew.Core;

/// <summary> The usage page of the web front ends (<c>UsageTables</c> with the sign-in and the tenants): React, Angular and Blazor read <c>GET</c> of the table's route, <c>[{ month, kind, quantity, events }]</c>, and show it as a
/// table with a filter on the kind (<c>?kind=</c>). The templates only choose the file names; the text is here. </summary>
public static class UsageWebCode
{
    private static (string, string)[] Words(UsagePage page) =>
    [
        ("§NAME", page.Name), ("§ROUTE", page.ApiRoute), ("§LABEL", page.Label), ("§HINT", UsageScreenWords.Hint), ("§NOROWS", UsageScreenWords.NoRows), ("§LOADFAILED", UsageScreenWords.LoadFailed),
        ("§MONTH", UsageScreenWords.MonthColumn), ("§KINDCOLUMN", UsageScreenWords.KindColumn), ("§QUANTITY", UsageScreenWords.QuantityColumn), ("§EVENTS", UsageScreenWords.EventsColumn),
        ("§FILTER", UsageScreenWords.KindFilter), ("§FROM", UsageScreenWords.FromFilter), ("§BEFORE", UsageScreenWords.BeforeFilter), ("§CHART", UsageScreenWords.ChartColumn), ("§OVERTIME", UsageScreenWords.OverTime), ("§SHOW", UsageScreenWords.Show), ("§OK", HttpStatus.Ok.ToString()), ("§FORBIDDEN", HttpStatus.Forbidden.ToString())
    ];

    private static (string, string)[] With(UsagePage page, params (string, string)[] more) => [.. Words(page), .. more];

    public static string React(UsagePage page, string apiFolder) => FillAll(ReactText, With(page, ("§API", apiFolder)));

    public static string ReactTest(UsagePage page) => FillAll(ReactTestText, Words(page));

    public static string AngularComponent(UsagePage page, ProjectSettings project, string folder) => FillAll(AngularText, With(page, ("§FOLDER", folder), ("§PATH", page.ApiRoute.TrimStart('/')),
        ("§IMPORTLINE", project.AngularControlFlow ? "" : "import { NgFor, NgIf } from '@angular/common';\n"), ("§STANDALONE", project.AngularStandaloneFlag ? "  standalone: true,\n" : ""),
        ("§STRATEGY", project.AngularEagerStrategy), ("§IMPORTS", project.AngularControlFlow ? "" : "NgFor, NgIf")));

    public static string AngularHtml(UsagePage page, ProjectSettings project) => project.AdaptAngularHtml(FillAll(AngularHtmlText, Words(page)));

    public static string AngularSpec(UsagePage page, string folder) => FillAll(AngularSpecText, With(page, ("§FOLDER", folder), ("§PATH", page.ApiRoute.TrimStart('/'))));

    public static string Blazor(UsagePage page, string ns, string adminRole) => FillAll(BlazorText, With(page, ("§NS", ns), ("§PAGEPATH", page.Path), ("§ADMIN", adminRole)));

    private const string ReactText = """
import { useEffect, useState } from 'react';
import { request } from '../§API/client';

interface Line {
  month: string;
  kind: string;
  quantity: number;
  events: number;
}

export function §NAMEPage() {
  const [lines, setLines] = useState<Line[] | null>(null);
  const [kind, setKind] = useState('');
  const [from, setFrom] = useState('');
  const [before, setBefore] = useState('');
  const [message, setMessage] = useState('');

  const load = () => {
    setMessage('');
    const query = new URLSearchParams();
    if (kind.trim()) query.set('kind', kind.trim());
    if (from) query.set('from', from);
    if (before) query.set('to', before);
    const text = query.toString();
    request<Line[]>('§ROUTE' + (text ? '?' + text : '')).then(setLines).catch(() => setMessage('§LOADFAILED'));
  };

  useEffect(() => { load(); }, []);

  const most = Math.max(1, ...(lines ?? []).map((line) => line.quantity));
  const sums = new Map<string, number>();
  (lines ?? []).forEach((line) => sums.set(line.month, (sums.get(line.month) ?? 0) + line.quantity));
  const months = Array.from(sums.entries()).sort(([a], [b]) => a.localeCompare(b)).map(([month, sum]) => ({ month, total: Math.round(sum * 10000) / 10000 }));
  const peak = Math.max(1, ...months.map((m) => m.total));

  return (
    <div>
      <h1>§LABEL</h1>
      <p>§HINT</p>
      <form onSubmit={(event) => { event.preventDefault(); load(); }}>
        <label htmlFor="usage-kind">§FILTER</label>
        <input id="usage-kind" value={kind} onChange={(event) => setKind(event.target.value)} />
        <label htmlFor="usage-from">§FROM</label>
        <input id="usage-from" type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
        <label htmlFor="usage-before">§BEFORE</label>
        <input id="usage-before" type="date" value={before} onChange={(event) => setBefore(event.target.value)} />
        <button type="submit">§SHOW</button>
      </form>
      {message && <p role="alert">{message}</p>}
      {lines && lines.length === 0 && <p>§NOROWS</p>}
      {lines && lines.length > 0 && (
        <>
          <h2>§OVERTIME</h2>
          <ol aria-label="§OVERTIME" style={{ display: 'flex', alignItems: 'flex-end', gap: 8, listStyle: 'none', padding: 0 }}>
            {months.map((m) => (
              <li key={m.month} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                <span>{m.total}</span>
                <div role="img" aria-label={m.month + ' ' + m.total} style={{ width: 32, height: Math.round((m.total / peak) * 100), background: '#3b7dd8' }} />
                <span>{m.month}</span>
              </li>
            ))}
          </ol>
          <table aria-label="§LABEL">
            <thead>
              <tr>
                <th>§MONTH</th>
                <th>§KINDCOLUMN</th>
                <th>§QUANTITY</th>
                <th>§EVENTS</th>
                <th>§CHART</th>
              </tr>
            </thead>
            <tbody>
              {lines.map((line) => (
                <tr key={line.month + line.kind}>
                  <td>{line.month}</td>
                  <td>{line.kind}</td>
                  <td>{line.quantity}</td>
                  <td>{line.events}</td>
                  <td><meter min={0} max={most} value={line.quantity} aria-label={line.month + ' ' + line.kind} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}
""";

    private const string ReactTestText = """
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { §NAMEPage } from '../§NAMEPage';

const lines = [
  { month: '2026-11', kind: 'api_calls', quantity: 12.5, events: 3 },
  { month: '2026-10', kind: 'emails', quantity: 40, events: 0 },
];

const answer = (body: unknown, status = §OK) => () => Promise.resolve(new Response(JSON.stringify(body), { status }));

describe('§NAMEPage', () => {
  afterEach(() => vi.restoreAllMocks());

  it('lists the months and kinds with their units and events', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(answer(lines));
    render(<§NAMEPage />);
    expect(await screen.findByRole('table')).toBeInTheDocument();
    expect(screen.getAllByText('2026-11')).toHaveLength(2);
    expect(screen.getByText('api_calls')).toBeInTheDocument();
    expect(screen.getAllByText('12.5')).toHaveLength(2);
    expect(screen.getByText('emails')).toBeInTheDocument();
  });

  it('asks for one kind when the filter is used', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockImplementation(answer(lines));
    render(<§NAMEPage />);
    await screen.findByRole('table');
    fireEvent.change(screen.getByLabelText('§FILTER'), { target: { value: 'emails' } });
    fireEvent.click(screen.getByRole('button', { name: '§SHOW' }));
    await vi.waitFor(() => expect(spy.mock.calls.some(([url]) => String(url).includes('§ROUTE?kind=emails'))).toBe(true));
  });

  it('asks for the days between From and Before and draws a bar for each line', async () => {
    const spy = vi.spyOn(globalThis, 'fetch').mockImplementation(answer(lines));
    render(<§NAMEPage />);
    await screen.findByRole('table');
    expect(screen.getAllByRole('meter')).toHaveLength(2);
    expect(screen.getAllByRole('img').map((bar) => bar.getAttribute('aria-label'))).toEqual(['2026-10 40', '2026-11 12.5']);
    fireEvent.change(screen.getByLabelText('§FROM'), { target: { value: '2026-10-01' } });
    fireEvent.change(screen.getByLabelText('§BEFORE'), { target: { value: '2026-11-01' } });
    fireEvent.click(screen.getByRole('button', { name: '§SHOW' }));
    await vi.waitFor(() => expect(spy.mock.calls.some(([url]) => String(url).includes('§ROUTE?from=2026-10-01&to=2026-11-01'))).toBe(true));
  });

  it('says so when nothing was recorded', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(answer([]));
    render(<§NAMEPage />);
    expect(await screen.findByText('§NOROWS')).toBeInTheDocument();
  });

  it('explains when the usage cannot be read', async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(answer({}, §FORBIDDEN));
    render(<§NAMEPage />);
    expect(await screen.findByRole('alert')).toHaveTextContent('§LOADFAILED');
  });
});
""";

    private const string AngularText = """
import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
§IMPORTLINE
interface Line {
  month: string;
  kind: string;
  quantity: number;
  events: number;
}

@Component({
  selector: 'app-§FOLDER',
§STANDALONE  changeDetection: ChangeDetectionStrategy.§STRATEGY, // plain properties change inside subscribe callbacks
  imports: [§IMPORTS],
  templateUrl: './§FOLDER.component.html',
  styleUrl: './§FOLDER.component.css'
})
export class §NAMEComponent implements OnInit {
  private readonly http = inject(HttpClient);

  lines: Line[] | null = null;
  kind = '';
  from = '';
  before = '';
  most = 1;
  months: { month: string; total: number; height: number }[] = [];
  message = '';

  load(): void {
    this.message = '';
    const query = [
      this.kind.trim() ? 'kind=' + encodeURIComponent(this.kind.trim()) : '',
      this.from ? 'from=' + encodeURIComponent(this.from) : '',
      this.before ? 'to=' + encodeURIComponent(this.before) : ''
    ].filter((part) => part).join('&');
    this.http.get<Line[]>('§PATH' + (query ? '?' + query : '')).subscribe({
      next: (lines) => { this.lines = lines; this.most = Math.max(1, ...lines.map((line) => line.quantity)); this.draw(lines); },
      error: () => { this.message = '§LOADFAILED'; }
    });
  }

  // One column for each month, oldest first, as tall as the units of all the kinds shown.
  private draw(lines: Line[]): void {
    const sums = new Map<string, number>();
    lines.forEach((line) => sums.set(line.month, (sums.get(line.month) ?? 0) + line.quantity));
    const peak = Math.max(1, ...Array.from(sums.values()));
    this.months = Array.from(sums.entries()).sort(([a], [b]) => a.localeCompare(b))
      .map(([month, sum]) => ({ month, total: Math.round(sum * 10000) / 10000, height: Math.round((sum / peak) * 100) }));
  }

  ngOnInit(): void {
    this.load();
  }
}
""";

    private const string AngularHtmlText = """
<div class="usage">
  <h1>§LABEL</h1>
  <p>§HINT</p>
  <form (submit)="$event.preventDefault(); load()">
    <label for="usage-kind">§FILTER</label>
    <input id="usage-kind" [value]="kind" (input)="kind = $any($event.target).value" />
    <label for="usage-from">§FROM</label>
    <input id="usage-from" type="date" [value]="from" (input)="from = $any($event.target).value" />
    <label for="usage-before">§BEFORE</label>
    <input id="usage-before" type="date" [value]="before" (input)="before = $any($event.target).value" />
    <button type="submit">§SHOW</button>
  </form>
  <p role="alert" *ngIf="message">{{ message }}</p>
  <p *ngIf="lines && lines.length === 0">§NOROWS</p>
  <h2 *ngIf="lines && lines.length > 0">§OVERTIME</h2>
  <ol class="months" *ngIf="lines && lines.length > 0" aria-label="§OVERTIME">
    <li *ngFor="let m of months">
      <span>{{ m.total }}</span>
      <div class="bar" role="img" [style.height.px]="m.height" [attr.aria-label]="m.month + ' ' + m.total"></div>
      <span>{{ m.month }}</span>
    </li>
  </ol>
  <table *ngIf="lines && lines.length > 0" aria-label="§LABEL">
    <thead>
      <tr>
        <th>§MONTH</th>
        <th>§KINDCOLUMN</th>
        <th>§QUANTITY</th>
        <th>§EVENTS</th>
        <th>§CHART</th>
      </tr>
    </thead>
    <tbody>
      <tr *ngFor="let line of lines">
        <td>{{ line.month }}</td>
        <td>{{ line.kind }}</td>
        <td>{{ line.quantity }}</td>
        <td>{{ line.events }}</td>
        <td><meter min="0" [max]="most" [value]="line.quantity" [attr.aria-label]="line.month + ' ' + line.kind"></meter></td>
      </tr>
    </tbody>
  </table>
</div>
""";

    public const string AngularCss = """
.usage table { border-collapse: collapse; }
.usage th, .usage td { padding: 4px 12px; text-align: left; }
.usage .months { display: flex; align-items: flex-end; gap: 8px; list-style: none; padding: 0; }
.usage .months li { display: flex; flex-direction: column; align-items: center; }
.usage .bar { width: 32px; background: #3b7dd8; }
""";

    private const string AngularSpecText = """
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { §NAMEComponent } from './§FOLDER.component';

describe('§NAMEComponent', () => {
  let component: §NAMEComponent;
  let fixture: ComponentFixture<§NAMEComponent>;
  let http: HttpTestingController;

  const lines = [
    { month: '2026-11', kind: 'api_calls', quantity: 12.5, events: 3 },
    { month: '2026-10', kind: 'emails', quantity: 40, events: 0 },
  ];

  const create = () => {
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(§NAMEComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [§NAMEComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  afterEach(() => http.verify());

  it('lists the months and kinds with their units and events', () => {
    create();
    http.expectOne('§PATH').flush(lines);
    fixture.detectChanges();
    expect(text()).toContain('2026-11');
    expect(text()).toContain('api_calls');
    expect(text()).toContain('12.5');
    expect(text()).toContain('emails');
  });

  it('asks for one kind when the filter is used', () => {
    create();
    http.expectOne('§PATH').flush(lines);
    component.kind = 'emails';
    component.load();
    http.expectOne('§PATH?kind=emails').flush([lines[1]]);
    fixture.detectChanges();
    expect(component.lines?.length).toBe(1);
  });

  it('asks for the days between From and Before and draws a bar for each line', () => {
    create();
    http.expectOne('§PATH').flush(lines);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('meter').length).toBe(2);
    expect(Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.bar')).map((bar) => bar.getAttribute('aria-label'))).toEqual(['2026-10 40', '2026-11 12.5']);
    component.from = '2026-10-01';
    component.before = '2026-11-01';
    component.load();
    http.expectOne('§PATH?from=2026-10-01&to=2026-11-01').flush(lines);
  });

  it('says so when nothing was recorded', () => {
    create();
    http.expectOne('§PATH').flush([]);
    fixture.detectChanges();
    expect(text()).toContain('§NOROWS');
  });

  it('explains when the usage cannot be read', () => {
    create();
    http.expectOne('§PATH').flush(null, { status: §FORBIDDEN, statusText: 'Forbidden' });
    expect(component.message).toBe('§LOADFAILED');
  });
});
""";

    private const string BlazorText = """
@page "/§PAGEPATH"
@using System.Net.Http.Json
@using §NS.Services
@attribute [Authorize(Roles = "§ADMIN")]
@inject HttpClient Http

<PageTitle>§LABEL</PageTitle>

<h1>§LABEL</h1>
<p>§HINT</p>
<form @onsubmit="LoadAsync">
    <label for="usage-kind">§FILTER</label>
    <input id="usage-kind" @bind="kind" @bind:event="oninput" />
    <label for="usage-from">§FROM</label>
    <input id="usage-from" type="date" value="@from" @oninput="e => from = e.Value?.ToString() ?? string.Empty" />
    <label for="usage-before">§BEFORE</label>
    <input id="usage-before" type="date" value="@before" @oninput="e => before = e.Value?.ToString() ?? string.Empty" />
    <button type="submit">§SHOW</button>
</form>
@if (message is not null)
{
    <p role="alert">@message</p>
}
@if (lines is not null && lines.Count == 0)
{
    <p>§NOROWS</p>
}
@if (lines is not null && lines.Count > 0)
{
    <h2>§OVERTIME</h2>
    <ol aria-label="§OVERTIME" style="display:flex;align-items:flex-end;gap:8px;list-style:none;padding:0">
        @foreach (var m in Months)
        {
            <li style="display:flex;flex-direction:column;align-items:center">
                <span>@m.Total.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)</span>
                <div class="usage-bar" role="img" aria-label="@(m.Month + " " + m.Total.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))" style="width:32px;background:#3b7dd8;height:@(Math.Round(m.Total / Peak * 100).ToString(System.Globalization.CultureInfo.InvariantCulture))px"></div>
                <span>@m.Month</span>
            </li>
        }
    </ol>
    <table aria-label="§LABEL">
        <thead>
            <tr>
                <th>§MONTH</th>
                <th>§KINDCOLUMN</th>
                <th>§QUANTITY</th>
                <th>§EVENTS</th>
                <th>§CHART</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var line in lines)
            {
                <tr>
                    <td>@line.Month</td>
                    <td>@line.Kind</td>
                    <td>@line.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)</td>
                    <td>@line.Events</td>
                    <td><meter min="0" max="@Most" value="@line.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)" aria-label="@(line.Month + " " + line.Kind)"></meter></td>
                </tr>
            }
        </tbody>
    </table>
}

@code {
    private sealed record Line(string Month, string Kind, decimal Quantity, int Events);

    private List<Line>? lines;
    private string kind = "";
    private string from = "";
    private string before = "";
    private string? message;

    private string Most => Math.Max(1m, lines?.Max(l => l.Quantity) ?? 1m).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // One column for each month, oldest first, as tall as the units of all the kinds shown.
    private IEnumerable<(string Month, decimal Total)> Months => lines?.GroupBy(l => l.Month).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, g.Sum(l => l.Quantity))) ?? [];

    private decimal Peak => Math.Max(1m, Months.Select(m => m.Total).DefaultIfEmpty(0).Max());

    private async Task LoadAsync()
    {
        message = null;
        var query = new List<string>();
        if (kind.Trim().Length > 0)
            query.Add("kind=" + Uri.EscapeDataString(kind.Trim()));
        if (from.Length > 0)
            query.Add("from=" + Uri.EscapeDataString(from));
        if (before.Length > 0)
            query.Add("to=" + Uri.EscapeDataString(before));
        try
        {
            lines = await Http.GetFromJsonAsync<List<Line>>("§ROUTE" + (query.Count == 0 ? "" : "?" + string.Join("&", query)));
        }
        catch (HttpRequestException)
        {
            message = "§LOADFAILED";
        }
    }

    protected override async Task OnInitializedAsync() => await LoadAsync();
}
""";
}
