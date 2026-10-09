using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The Angular bases (TS_EssentialCrud: CrudService, NamedCrudService, CrudScreen, PagedCrudScreen) and the short classes the generated services and screens are on them. </summary>
[TestClass]
public class CrudScreenTests
{
    /// <summary> The files the Crud essentials group writes, by file name (crud.service.ts, crud-screen.ts). </summary>
    internal static async Task<Dictionary<string, string>> Essential(params (string Key, string Value)[] values)
    {
        var project = ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));
        var result = await Repo.Cache.RunAsync(Repo.Template("TS_EssentialCrud_v1.tt"), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    private static async Task<Dictionary<string, string>> Generate(string template, TableModel table, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project ?? ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public async Task The_essentials_write_the_service_base_and_the_screen_base_in_the_projects_folders()
    {
        var files = GeneratedFiles.Split((await Repo.Cache.RunAsync(Repo.Template("TS_EssentialCrud_v1.tt"),
            ProjectSettings.FromValues([new("ProjectName", "Acme"), new("ServicesFolder", "api"), new("ComponentsFolder", "screens")]))).GeneratedText!);

        CollectionAssert.AreEquivalent(new[] { "src/app/api/crud.service.ts", "src/app/api/crud.service.spec.ts", "src/app/api/api-error.ts", "src/app/api/api-error.spec.ts", "src/app/api/notifier.ts", "src/app/screens/crud-screen.ts", "src/app/screens/crud-screen.spec.ts" },
            files.Select(f => f.RelativePath).ToArray());
        string screen = files.Single(f => f.RelativePath.EndsWith("crud-screen.ts")).Content;
        Expect.Contains(screen, "import { CrudService } from '../api/crud.service';");
        Expect.Contains(screen, "import { apiMessage } from '../api/api-error';");
        Expect.Contains(screen, "import { Notifier } from '../api/notifier';");
        string screenSpec = files.Single(f => f.RelativePath.EndsWith("crud-screen.spec.ts")).Content;
        Expect.Contains(screenSpec, "import { CrudService } from '../api/crud.service';");
        Expect.Contains(screenSpec, "import { Notifier } from '../api/notifier';");
        Expect.Contains(files.Single(f => f.RelativePath.EndsWith("crud.service.spec.ts")).Content, "import { CrudService, NameActiveCrudService, NamedCrudService } from './crud.service';");
    }

    [TestMethod]
    public async Task CrudService_has_the_calls_every_table_answers_and_NamedCrudService_encodes_the_name()
    {
        string service = (await Essential())["crud.service.ts"];

        Expect.Contains(service, "export abstract class CrudService<T, K extends number | string = number> {");
        foreach (string call in new[] { "getAll(): Observable<T[]>", "getById(id: K): Observable<T>", "create(row: T): Observable<T>", "update(id: K, row: T): Observable<T>", "delete(id: K): Observable<void>", "clone(id: K): Observable<T>", "searchPage(query: PageQuery): Observable<PagedResult<T>>" })
            Expect.Contains(service, call);
        Expect.Contains(service, "export abstract class NamedCrudService<T, K extends number | string = number> extends CrudService<T, K> {");
        Expect.Contains(service, "`${this.apiUrl}/${encodeURIComponent(name)}`");   // a space or a slash in the name cannot change the route
        // the search request: the page, the boxes that have text, and the sort only when there is one
        Expect.Contains(service, "new HttpParams().set('pageNumber', query.pageNumber).set('pageSize', query.pageSize)");
        Expect.Contains(service, "if (value) { params = params.set(name, value); }");
        Expect.Contains(service, "params = params.set('sortBy', query.sortBy).set('sortDir', query.sortDescending ? 'desc' : 'asc');");
        Expect.Contains(service, "`${this.apiUrl}/search`");
    }

    [TestMethod]
    public async Task CrudScreen_holds_the_save_delete_clone_and_way_back_every_screen_shares()
    {
        string screen = (await Essential())["crud-screen.ts"];

        Expect.Contains(screen, "export abstract class CrudScreen<T, K extends number | string = number> implements OnInit {");
        // the form is a modal dialog that opens when it is rendered
        Expect.Contains(screen, "@ViewChild('editDialog') set editDialog(");
        Expect.Contains(screen, "element.nativeElement.showModal()");
        // opened from another page's grid, and back to it
        Expect.Contains(screen, "params.get('edit')");
        Expect.Contains(screen, "this.router.navigateByUrl(this.backTo)");
        // edit works on a copy; a new row gets the empty key; the form stays open when the API refuses
        Expect.Contains(screen, "this.prepareEdit({ ...row })");
        Expect.Contains(screen, "(row as Record<string, unknown>)[this.idKey] = this.emptyKey;");
        Expect.Contains(screen, "next: () => { this.selectedRow = null; this.load(); this.goBack(); },");
        // delete and clone read the page again, and say why when the API refuses
        Expect.Contains(screen, "if (!this.notifier.confirm('Are you sure you want to delete this?'))");
        Expect.Contains(screen, "this.notifier.error(apiMessage(error, 'It cannot be deleted because it is in use!'))");
        Expect.Contains(screen, "this.service.clone(id).subscribe({");
        Expect.Contains(screen, "next: (copy) => { this.load(); this.edit(copy); },");
        Expect.Contains(screen, "this.notifier.error(apiMessage(error, this.badInputMessage))");
    }

    [TestMethod]
    public async Task A_refusal_is_shown_in_the_APIs_own_words_through_the_Notifier_and_never_by_alert_in_the_screen()
    {
        var files = await Essential();
        string screen = files["crud-screen.ts"];

        Expect.DoesNotContain(screen, "alert(");
        Expect.DoesNotContain(screen, " confirm(");
        Expect.Contains(screen, "protected readonly notifier = inject(Notifier);");
        // adding, updating, a vanished row and any other status all go through apiMessage, with the fixed words only as the fallback
        Expect.Contains(screen, "this.notifier.error(apiMessage(error, `This ${this.noun} no longer exists!`))");
        Expect.Contains(screen, "this.notifier.error(apiMessage(error, 'Problem while ' + action + '! ' + error.message))");

        string apiError = files["api-error.ts"];
        Expect.Contains(apiError, "export function apiMessage(error: unknown, fallback: string): string {");
        Expect.Contains(apiError, "problem.detail");
        Expect.Contains(apiError, "problem.errors");
        Expect.Contains(apiError, ".join(' ')");
        Expect.Contains(files["api-error.spec.ts"], "describe('apiMessage', () => {");
    }

    [TestMethod]
    public async Task The_Notifier_uses_the_browsers_dialogs_unless_the_project_asks_for_ngx_toastr()
    {
        string plain = (await Essential())["notifier.ts"];
        Expect.Contains(plain, "alert(text);");
        Expect.Contains(plain, "return confirm(text);");
        Expect.DoesNotContain(plain, "ToastrService");

        string toasts = (await Essential(("Toasts", "ngx-toastr")))["notifier.ts"];
        Expect.Contains(toasts, "import { ToastrService } from 'ngx-toastr';");
        Expect.Contains(toasts, "this.injector.get(ToastrService).error(text);");
        Expect.DoesNotContain(toasts, "alert(");
        Expect.Contains(toasts, "return confirm(text);");   // a question stays a dialog
    }

    [TestMethod]
    public async Task The_bases_ship_their_own_specs()
    {
        var files = await Essential();

        string service = files["crud.service.spec.ts"];
        Expect.Contains(service, "leaves out empty filters and writes the sort");
        Expect.Contains(service, "'api/rows/a%20b%2Fc'");
        Expect.Contains(service, "api/rows/active");

        string screen = files["crud-screen.spec.ts"];
        Expect.Contains(screen, "shows the API\\'s own words for a refusal and keeps the form open");   // the 409 problem is flushed and its detail shown
        Expect.Contains(screen, "falls back to the last page when the page asked for is past the end");
        Expect.Contains(screen, "ignores a saved sort for a column the grid cannot sort by");
        Expect.Contains(screen, "{ provide: Notifier, useValue: notifier }");
        Expect.DoesNotContain(screen, "__SERVICES__");

        var grid = await Repo.Cache.RunAsync(Repo.Template("TS_GridSort_v1.tt"), new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [] }, ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(grid.Success, string.Join(" | ", grid.Errors));
        Expect.Contains(grid.GeneratedText!, "ignores a saved column the grid cannot sort by");
    }

    private static async Task<Dictionary<string, string>> Run(string template, params (string Key, string Value)[] values)
    {
        var project = ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));
        var result = await Repo.Cache.RunAsync(Repo.Template(template), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public async Task Toasts_adds_the_package_the_provider_and_the_stylesheet_and_nothing_without_the_setting()
    {
        var plainBuild = await Run("TS_EssentialBuild_v1.tt");
        Expect.DoesNotContain(plainBuild["package.json"], "ngx-toastr");
        Expect.DoesNotContain(plainBuild["angular.json"], "toastr");
        Expect.DoesNotContain((await Run("TS_EssentialConfig_v1.tt"))["app.config.ts"], "Toastr");

        var build18 = await Run("TS_EssentialBuild_v1.tt", ("Toasts", "ngx-toastr"), ("AngularVersion", "18"));
        Expect.Contains(build18["package.json"], "\"ngx-toastr\": \"^19.1.0\"");
        Expect.Contains(build18["package.json"], "\"@angular/animations\": \"^18.0.0\"");
        Expect.Contains(build18["angular.json"], "node_modules/ngx-toastr/toastr.css");
        var config18 = (await Run("TS_EssentialConfig_v1.tt", ("Toasts", "ngx-toastr"), ("AngularVersion", "18")))["app.config.ts"];
        Expect.Contains(config18, "provideToastr(),");
        Expect.Contains(config18, "provideAnimations(),");

        var build22 = await Run("TS_EssentialBuild_v1.tt", ("Toasts", "ngx-toastr"), ("AngularVersion", "22"));
        Expect.Contains(build22["package.json"], "\"ngx-toastr\": \"^20.0.5\"");
        Expect.DoesNotContain(build22["package.json"], "@angular/animations");
        Expect.Contains(build22["package.json"], "\"overrides\"");
        var config22 = (await Run("TS_EssentialConfig_v1.tt", ("Toasts", "ngx-toastr"), ("AngularVersion", "22")))["app.config.ts"];
        Expect.Contains(config22, "provideToastr(),");
        Expect.DoesNotContain(config22, "provideAnimations");
        System.Text.Json.JsonDocument.Parse(build22["package.json"]);   // valid JSON whatever the setting
        System.Text.Json.JsonDocument.Parse(build18["package.json"]);
    }

    [TestMethod]
    public async Task PagedCrudScreen_pages_sorts_and_searches_on_the_server()
    {
        string screen = (await Essential())["crud-screen.ts"];

        Expect.Contains(screen, "export abstract class PagedCrudScreen<T, K extends number | string = number> extends CrudScreen<T, K> {");
        Expect.Contains(screen, "import { GridSort, loadSort, nextSort, saveSort } from '../grid-sort';");
        Expect.Contains(screen, "this.service.searchPage({ pageNumber: this.pageIndex + 1, pageSize: this.pageSize, filters: this.filters, sortBy: this.sort?.column, sortDescending: this.sort?.descending })");
        // a page past the end falls back to the last page there is
        Expect.Contains(screen, "if (result.items.length === 0 && result.totalCount > 0 && this.pageIndex > 0) {");
        Expect.Contains(screen, "this.pageIndex = Math.ceil(result.totalCount / this.pageSize) - 1;");
        Expect.Contains(screen, "this.pageSize = event.pageSize;");   // the page size the person picks is used
        Expect.Contains(screen, "this.loaded = true;");
        // sort from the headers, kept between visits, cleared from a right-click menu
        Expect.Contains(screen, "this.sort = loadSort(this.gridKey, this.sortableColumns) ?? this.defaultSort;");
        Expect.Contains(screen, "saveSort(this.gridKey, this.sort);");
        Expect.Contains(screen, "@HostListener('document:click')");
        Expect.Contains(screen, "@HostListener('document:keydown.escape')");
        Expect.Contains(screen, "clearSearch(): void {");
        Expect.Contains(screen, "this.filters[name] = '';");
    }

    [TestMethod]
    public async Task A_service_names_its_route_and_keeps_only_its_typed_page_call()
    {
        var holiday = (await Generate("TS_Service_v1.tt", Sample.Holiday()))["holiday.service.ts"];

        Expect.Contains(holiday, "import { NamedCrudService, PagedResult } from './crud.service';");
        Expect.Contains(holiday, "export class HolidayService extends NamedCrudService<Holiday> {");
        Expect.Contains(holiday, "super(http, 'api/holidays');");
        Expect.Contains(holiday, "export type HolidayPagedResult = PagedResult<Holiday>;");
        Expect.Contains(holiday, "getPage(pageNumber: number, pageSize: number, sY_IsoCountry_Alpha3Code?: string, name?: string, sortBy?: string, sortDescending?: boolean): Observable<HolidayPagedResult> {");
        Expect.Contains(holiday, "return this.searchPage({ pageNumber, pageSize, filters: { sY_IsoCountry_Alpha3Code, name }, sortBy, sortDescending });");
        Expect.DoesNotContain(holiday, "this.http.get");   // the calls are the base's

        var donate = (await Generate("TS_Service_v1.tt", Sample.DonateLeave()))["donateleave.service.ts"];
        Expect.Contains(donate, "extends CrudService<DonateLeave> {");   // no Name column: no find-by-name
        Expect.DoesNotContain(donate, "NamedCrudService");
    }

    [TestMethod]
    public async Task A_service_for_a_uniqueidentifier_key_says_so_in_its_base()
    {
        var service = (await Generate("TS_Service_v1.tt", Sample.AccountRef()))["accountref.service.ts"];

        Expect.Contains(service, "extends CrudService<AccountRef, string> {");
        Expect.DoesNotContain(service, "id: number");
    }

    [TestMethod]
    public async Task A_screen_is_a_short_class_on_PagedCrudScreen_that_names_its_service_key_and_search_boxes()
    {
        var files = await Generate("TS_Component_v1.tt", Sample.Holiday());
        string ts = files["holiday.component.ts"];

        Expect.Contains(ts, "import { PagedCrudScreen } from '../crud-screen';");
        Expect.Contains(ts, "export class HolidayComponent extends PagedCrudScreen<Holiday> {");
        Expect.Contains(ts, "protected readonly service = inject(HolidayService);");
        Expect.Contains(ts, "protected readonly noun = 'holiday';");
        Expect.Contains(ts, "protected readonly idKey = 'holidayId';");
        Expect.Contains(ts, "protected readonly gridKey = 'holiday';");
        Expect.Contains(ts, "override filters = { sY_IsoCountry_Alpha3Code: '', name: '' };");
        Expect.Contains(ts, "protected newRow(): Holiday {");
        Expect.Contains(ts, "protected override prepareEdit(copy: Holiday): Holiday {");
        // everything the base does is not repeated in the class
        foreach (string repeated in new[] { "submit(", "delete(", "cancel(", "onPageChange(", "sortBy(", "load()", "goBack", "@HostListener", "@ViewChild", "private showError" })
            Expect.DoesNotContain(ts, repeated);
        Assert.IsLessThan(45, ts.Split('\n').Length, "a short class");
    }

    [TestMethod]
    public async Task A_guid_key_screen_says_its_empty_key()
    {
        string ts = (await Generate("TS_Component_v1.tt", Sample.AccountRef()))["accountref.component.ts"];

        Expect.Contains(ts, "extends PagedCrudScreen<AccountRef, string> {");
        Expect.Contains(ts, "protected override readonly emptyKey: string = '00000000-0000-0000-0000-000000000000';");
    }

    [TestMethod]
    public async Task A_master_detail_screen_is_on_the_same_base_and_keeps_its_child_grids()
    {
        string ts = (await Generate("TS_DetailMasterComponent_v1.tt", Sample.TimeSheetWithEmployeeAndDetail()))
            .Single(f => f.Key.EndsWith(".component.ts")).Value;

        Expect.Contains(ts, "extends PagedCrudScreen<TimeSheet> {");
        Expect.Contains(ts, "override edit(timeSheet: TimeSheet): void {");     // the open row loads its child rows
        Expect.Contains(ts, "sortChildBy(child: string, column: string): void {");
        Expect.DoesNotContain(ts, "private load(): void");
        Expect.DoesNotContain(ts, "onPageChange(");
    }
}
