using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary>
/// What a paged grid must do when the rows change under it (found by moving a real server to server paging, 2026-10): a delete reads the page again,
/// a page that is past the end falls back to the last page there is, the page size the person picks is used, and an empty result says so.
/// </summary>
[TestClass]
public class PagedGridTests
{
    private static async Task<string> Render(string template, TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table);
        if (!result.Success)
            Assert.Fail(template + ": " + string.Join(" | ", result.Errors).ReplaceLineEndings(" "));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static string AllFiles(string generated) => string.Join("\n", GeneratedFiles.Split(generated).Select(f => f.Content));

    [TestMethod]
    public async Task The_angular_grid_reads_the_page_again_after_a_delete_and_steps_back_from_a_page_past_the_end()
    {
        string files = AllFiles(await Render("TS_Component_v1.tt", Sample.Holiday()));

        // delete reads the page again, a page past the end falls back to the last page there is, and the page size the person picks is used: all in the base (CrudScreenTests)
        string screen = (await CrudScreenTests.Essential())["crud-screen.ts"];
        Expect.Contains(screen, "next: () => this.load(), // read the page again");
        Expect.DoesNotContain(files, ".filter((p) => p.");
        Expect.Contains(screen, "if (result.items.length === 0 && result.totalCount > 0 && this.pageIndex > 0) {");
        Expect.Contains(screen, "this.pageIndex = Math.ceil(result.totalCount / this.pageSize) - 1;");
        Expect.Contains(screen, "this.pageSize = event.pageSize;");
        Expect.Contains(files, "[pageSizeOptions]=\"[10, 20, 50, 100]\"");
        // an empty result says so, but not before the first page has arrived
        Expect.Contains(files, "<p *ngIf=\"loaded && totalCount === 0\" class=\"no-rows\">Nothing found.</p>");
        Expect.Contains(screen, "this.loaded = true;");
        // and the generated spec checks the server is asked for a page, and the step back
        Expect.Contains(files, "it('asks the server for the first page'");
        Expect.Contains(files, "it('goes back to the last page there is when the page it asked for is past the end'");
        Expect.Contains(files, "import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';");
    }

    [TestMethod]
    public void The_angular_master_detail_screen_has_the_same_behaviour()
    {
        string template = File.ReadAllText(Repo.Template("TS_DetailMasterComponent_v1.tt")).ReplaceLineEndings("\n");

        // the paging, the step back and the page size are PagedCrudScreen's, which this screen extends
        Expect.Contains(template, "\"PagedCrudScreen\"");   // a name/active parent is on NameActiveCrudScreen instead
        Expect.DoesNotContain(template, "this.pageIndex = Math.ceil(");
        Expect.Contains(template, "Nothing found.</p>");
        Expect.Contains(template, "it('asks the server for the first page'");
    }

    [TestMethod]
    [DataRow("TSX_Page_v1.tt")]
    public async Task The_react_page_reads_the_page_again_after_a_delete_and_steps_back_from_a_page_past_the_end(string template)
    {
        string tsx = await Render(template, Sample.Holiday());

        Expect.Contains(tsx, "load(page); // read the page again");
        Expect.Contains(tsx, "if (result.items.length === 0 && result.totalCount > 0 && targetPage > 1) { load(result.totalPages");
    }

    [TestMethod]
    public void The_react_master_detail_page_has_the_same_behaviour()
    {
        string template = File.ReadAllText(Repo.Template("TSX_DetailMasterPage_v1.tt")).ReplaceLineEndings("\n");

        Expect.Contains(template, "load(page); // read the page again");
        Expect.Contains(template, "result.totalCount > 0 && targetPage > 1");
    }

    [TestMethod]
    public async Task The_react_pages_offer_a_page_size_and_say_when_nothing_was_found_without_a_new_prop_on_the_shared_bar()
    {
        string tsx = await Render("TSX_Page_v1.tt", Sample.Holiday());

        Expect.Contains(tsx, "const pageSizeChoices = [10, 20, 50, 100];");
        Expect.Contains(tsx, "const [pageSize, setPageSize] = useState(initialPageSize);");
        Expect.Contains(tsx, "<select value={pageSize} onChange={(event) => { const size = Number(event.target.value); setPageSize(size); load(1, filters, sort, size); }}>");
        Expect.Contains(tsx, "{loaded && holidays.length === 0 && <p className=\"no-rows\">Nothing found.</p>}");
        Expect.Contains(tsx, "setLoaded(true);");
        // PaginationBar.tsx is written once with the essentials: the page must not pass it anything the older file does not know
        Expect.DoesNotContain(tsx, "onPageSize=");
    }

    [TestMethod]
    public async Task The_blazor_page_offers_a_page_size_and_says_when_nothing_was_found()
    {
        string razor = await Render("BLZ_Page_v1.tt", Sample.Holiday());

        Expect.Contains(razor, "private int PageSize = 20;");
        Expect.Contains(razor, "private static readonly int[] PageSizeChoices = [10, 20, 50, 100];");
        Expect.Contains(razor, "<select @onchange=\"ChangePageSizeAsync\">");
        Expect.Contains(razor, "private async Task ChangePageSizeAsync(ChangeEventArgs args)");
        Expect.Contains(razor, "@if (loaded && items.Count == 0)");
        Expect.Contains(razor, "<p class=\"no-rows\">Nothing found.</p>");
    }

    [TestMethod]
    public async Task The_winui3_list_steps_back_from_a_page_past_the_end_and_offers_a_page_size()
    {
        string files = await Render("WinUI3_MasterScreen_v1.tt", Sample.Holiday());

        // the view model
        Expect.Contains(files, "if (rows.Count == 0 && totalCount > 0 && PageNumber > TotalPages)");
        Expect.Contains(files, "PageNumber = TotalPages;");
        Expect.Contains(files, "public async Task ChangePageSizeAsync(int size)");
        Expect.Contains(files, "public string PageLabel => !HasLoaded ? \"\" : TotalRows == 0 ? \"Nothing found.\" : $\"Page {PageNumber} of {TotalPages} ({TotalRows} rows)\";");
        // the bar and the page that uses it
        Expect.Contains(files, "<ComboBox x:Name=\"PageSizeBox\" Width=\"80\" AutomationProperties.Name=\"Rows per page\" SelectionChanged=\"OnPageSizeSelected\" />");
        Expect.Contains(files, "public event EventHandler<int>? PageSizeChanged;");
        Expect.Contains(files, "PageSizeChanged=\"OnPageSizeChanged\"");
        Expect.Contains(files, "private async void OnPageSizeChanged(object? sender, int size) => await ViewModel.ChangePageSizeAsync(size);");
    }

    [TestMethod]
    public void The_two_winui3_templates_that_write_the_pagination_bar_write_the_same_one()
    {
        static string Bar(string file)
        {
            string text = File.ReadAllText(Repo.Template(file)).ReplaceLineEndings("\n");
            int start = text.IndexOf("/PaginationBar.xaml@@@", StringComparison.Ordinal);
            const string last = "OnNextClick(object sender, RoutedEventArgs e) => NextClicked?.Invoke(this, e);\n}\n";
            return text[start..(text.IndexOf(last, start, StringComparison.Ordinal) + last.Length)];
        }

        Assert.AreEqual(Bar("WinUI3_MasterScreen_v1.tt"), Bar("WinUI3_DetailMasterScreen_v1.tt"));
    }

    [TestMethod]
    public async Task The_generated_base_api_needs_no_helper_of_ours_and_leaves_the_table_prefix_off_the_route()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialApiBase_v1.tt"), ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!.ReplaceLineEndings("\n");

        // EndsWithIgnoreCase is a helper of CodeGenNew itself: nothing writes it into the project, so the file did not compile on its own
        Expect.DoesNotContain(cs, "EndsWithIgnoreCase");
        Expect.Contains(cs, "word.EndsWith(\"ss\", StringComparison.OrdinalIgnoreCase)");
        // E_TimeSheet is /api/timesheets, as the TypeScript templates call it
        Expect.Contains(cs, "plural = Pluralize(WithoutPrefix(singular));");
        Expect.Contains(cs, "name.StartsWith(\"E_\", StringComparison.Ordinal)");
        Expect.Contains(cs, "name.StartsWith(\"SY_\", StringComparison.Ordinal)");
    }

    [TestMethod]
    public async Task The_blazor_page_steps_back_from_a_page_past_the_end()
    {
        string razor = await Render("BLZ_Page_v1.tt", Sample.Holiday());

        Expect.Contains(razor, "if (result.Items.Count == 0 && result.TotalCount > 0 && target > 1)");
        Expect.Contains(razor, "await LoadAsync(Math.Max(result.TotalPages, 1));");
    }
}
