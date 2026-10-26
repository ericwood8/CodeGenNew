using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The many-to-many editor reached from the menu on every stack: one rule in Core (<see cref="JunctionPlan"/>) names the route, the label and the roles, the screens templates list the entry, and each
/// stack's junction template writes the page that picks the anchor row. </summary>
[TestClass]
public class JunctionPlanTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static DatabaseModel Database() => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.JunctionWithSurrogateKey()] };

    private static async Task<string> RunDatabase(string template, params (string, string)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Database(), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    private static async Task<List<(string RelativePath, string Content)>> RunTable(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Sample.JunctionWithSurrogateKey(), Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).Select(f => (f.RelativePath, f.Content)).ToList();
    }

    [TestMethod]
    public void The_route_the_label_and_the_heading_are_named_once()
    {
        var table = Sample.JunctionWithSurrogateKey();

        Assert.AreEqual("name-base-group-xref-links", JunctionPlan.Route(table.TableName));
        Assert.AreEqual("Name Base Group Xref Links", JunctionPlan.Label(table.TableName));
        Assert.AreEqual("Groups by Name Base", JunctionPlan.Heading(table));
        Assert.AreEqual(JunctionPlan.Route(table.TableName), BlazorNames.JunctionRoute(table.TableName));
        CollectionAssert.AreEqual(new[] { table.TableName }, JunctionPlan.Tables(Database(), Project()).Select(t => t.TableName).ToArray());
    }

    [TestMethod]
    public void The_shared_menu_ends_with_the_junction_pages_and_asks_the_roles_that_may_read_them()
    {
        var database = Database();
        var junctions = JunctionPlan.Tables(database, Project());

        var entries = ScreenMenu.For(Project(("Dashboard", "false")), [Sample.Holiday()], [], junctions);

        CollectionAssert.AreEqual(new[] { ScreenMenuKind.Screen, ScreenMenuKind.Junction }, entries.Select(e => e.Kind).ToArray());
        Assert.AreEqual(JunctionPlan.Label(junctions[0].TableName), entries[1].Label);
        Assert.IsEmpty(entries[1].Roles);
        Assert.AreEqual(JunctionPlan.Route(junctions[0].TableName), BlazorMenu.Entries(database, Project(("Dashboard", "false"))).Last().Route);
    }

    [TestMethod]
    public async Task Every_stacks_screens_list_the_same_route_and_label()
    {
        string route = JunctionPlan.Route("NameBaseGroupXref");
        string label = JunctionPlan.Label("NameBaseGroupXref");

        string react = await RunDatabase("TSX_Screens_v1.tt");
        string angular = await RunDatabase("TS_Screens_v1.tt");
        string blazor = string.Join("\n", BlazorMenu.Entries(Database(), Project()).Select(e => e.Route + "|" + e.Label));
        string winui3 = await RunDatabase("WinUI3_Screens_v1.tt");
        string uno = await RunDatabase("UNO_Screens_v1.tt");

        Expect.Contains(react, $"path: '{route}', label: '{label}', element: <NameBaseGroupXrefLinksPage />");
        Expect.Contains(react, "import { NameBaseGroupXrefLinksPage } from './pages/NameBaseGroupXrefLinksPage';");
        Expect.Contains(angular, $"path: '{route}', label: '{label}', component: NameBaseGroupXrefLinksComponent");
        Expect.Contains(blazor, $"{route}|{label}");
        Expect.Contains(winui3, $"Content = \"{label}\", Tag = \"NameBaseGroupXrefLinks\"");
        Expect.Contains(winui3, "\"NameBaseGroupXrefLinks\" => new NameBaseGroupXrefLinksPage(_context),");
        Expect.Contains(uno, $"Content = \"{label}\", Tag = \"NameBaseGroupXrefLinks\"");
        Expect.Contains(uno, "\"NameBaseGroupXrefLinks\" => new NameBaseGroupXrefLinksPage(_api),");
    }

    [TestMethod]
    public async Task The_roles_that_may_read_a_junction_table_gate_its_entry_on_the_web_stacks()
    {
        var roles = new[] { ("Auth", "true"), ("ApiAuth", "true"), ("ReadRoles", "Hr") };

        string react = await RunDatabase("TSX_Screens_v1.tt", roles);
        string angular = await RunDatabase("TS_Screens_v1.tt", roles);

        Expect.Contains(react, "element: <NameBaseGroupXrefLinksPage />, roles: ['Administrator', 'Hr'] }");
        Expect.Contains(angular, "component: NameBaseGroupXrefLinksComponent, roles: ['Administrator', 'Hr'] }");
    }

    [TestMethod]
    public async Task The_React_page_picks_the_anchor_from_its_lookup_and_shows_the_shuttle_for_it()
    {
        var files = await RunTable("TSX_JunctionPage_v1.tt");
        string page = files.Single(f => f.RelativePath == "pages/NameBaseGroupXrefLinksPage.tsx").Content;

        Expect.Contains(page, "request<Anchor[]>('/api/namebases/lookup')");
        Expect.Contains(page, "<h1>Groups by Name Base</h1>");
        Expect.Contains(page, "<NameBaseGroupXrefJunction key={anchorId} anchorId={Number(anchorId)} />");
    }

    [TestMethod]
    public async Task The_Angular_screen_reads_the_lists_again_when_another_anchor_is_picked()
    {
        var files = (await RunTable("TS_JunctionComponent_v1.tt")).ToDictionary(f => Path.GetFileName(f.RelativePath));

        string links = files["namebasegroupxref-links.component.ts"].Content;
        Expect.Contains(links, "this.http.get<{ id: number; name: string }[]>('api/namebases/lookup')");
        Expect.Contains(links, "this.anchorId = index > 0 ? this.anchors[index - 1].id : null;");
        Expect.Contains(files["namebasegroupxref-links.component.html"].Content, "*ngIf=\"anchorId !== null\"");
        string shuttle = files["namebasegroupxref-junction.component.ts"].Content;
        Expect.Contains(shuttle, "implements OnInit, OnChanges");
        Expect.Contains(shuttle, "if (changes['anchorId'] && !changes['anchorId'].firstChange) {");
    }

    [TestMethod]
    public async Task The_desktop_pages_open_the_editor_on_the_row_picked()
    {
        var winui3 = (await RunTable("WinUI3_JunctionEditor_v1.tt")).ToDictionary(f => Path.GetFileName(f.RelativePath));
        var uno = (await RunTable("UNO_JunctionEditor_v1.tt")).ToDictionary(f => Path.GetFileName(f.RelativePath));

        Expect.Contains(winui3["NameBaseGroupXrefLinksPage.xaml.cs"].Content, "var dialog = new NameBaseGroupXrefJunctionEditor(viewModel) { XamlRoot = XamlRoot };");
        Expect.Contains(winui3["NameBaseGroupXrefLinksPage.xaml.cs"].Content, "_context.Set<NameBase>().AsNoTracking()");
        Expect.Contains(uno["NameBaseGroupXrefLinksPage.xaml.cs"].Content, "var dialog = new NameBaseGroupXrefJunctionEditor(_api, anchor.Id) { XamlRoot = XamlRoot };");
        Expect.Contains(uno["NameBaseGroupXrefLinksPage.xaml.cs"].Content, "_api.NameBase.GetLookupAsync()");
        // the same page: the XAML differs only in nothing
        Assert.AreEqual(winui3["NameBaseGroupXrefLinksPage.xaml"].Content.Replace("Acme.App.Views", "X"), uno["NameBaseGroupXrefLinksPage.xaml"].Content.Replace("Acme.App.Views", "X"));
    }

    [TestMethod]
    public async Task A_junction_table_whose_anchor_has_no_lookup_gets_the_editor_and_no_page()
    {
        var project = Project(("NoApiTables", "NameBase"));
        var table = Sample.JunctionWithSurrogateKey();

        Assert.IsFalse(JunctionPlan.HasPage(table, project));
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_JunctionEditor_v1.tt"), table, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Assert.DoesNotContain("LinksPage", result.GeneratedText!);
    }
}
