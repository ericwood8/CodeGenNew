using CodeGenNew.Core;
using CodeGenNew.Generation;

namespace CodeGenNew.Tests;

/// <summary> The strip of cards above a table's page on Blazor and Uno: the same plan and the same <c>GET dashboard?table=</c> as React, Angular and WinUI3. </summary>
[TestClass]
public class DashboardStripTests
{
    private static async Task<Dictionary<string, string>> Generate(bool strip, string stacks)
    {
        string path = DashboardTests.CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_strip_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var (provider, _) = await DashboardTests.ReadAsync(path);
            var settings = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api," + stacks, ["OutputApi"] = "Api", ["OutputBlazor"] = "blz", ["OutputUno"] = "uno",
                ["Screens"] = "Customer,Invoice,InvoiceLine,Promotion", ["Dashboard"] = "true", ["DashboardStrip"] = strip ? "true" : "false", ["ProjectDocs"] = "false"
            };
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(settings), Stacks = ["Api", .. stacks.Split(',')], OutputDirectory = output, DatabaseName = path, Schema = "main"
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            DashboardTests.Delete(path);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task Blazor_shows_the_strip_above_the_heading_and_asks_only_for_the_tables_that_have_cards()
    {
        var files = await Generate(strip: true, "Blazor");

        string strip = files["blz/Components/DashboardStrip.razor"];
        Expect.Contains(strip, "StripTables = new(StringComparer.OrdinalIgnoreCase) { ");
        Expect.Contains(strip, "\"Invoice\"");
        Expect.Contains(strip, "(await Client.GetAsync(Table)).Widgets");
        Expect.Contains(files["blz/Services/DashboardClient.cs"], "\"/api/dashboard?table=\" + Uri.EscapeDataString(table)");
        string page = files["blz/Pages/InvoicePage.razor"];
        Assert.IsTrue(page.IndexOf("<DashboardStrip Table=\"Invoice\" />", StringComparison.Ordinal) is var at and > 0 && at < page.IndexOf("<h1>", StringComparison.Ordinal), page);
        Expect.Contains(page, "@using Shop.Blazor.Components");
    }

    [TestMethod]
    public async Task Uno_puts_the_strip_above_the_list_page_of_a_table_with_cards_and_reads_it_through_the_dashboard_client()
    {
        var files = await Generate(strip: true, "Uno");

        string strip = files["uno/Views/DashboardStrip.cs"];
        Expect.Contains(strip, "public DashboardStrip(DashboardClient client, string table)");
        Expect.Contains(strip, "var result = await _client.GetAsync(_table);");
        string screens = files["uno/MainPage.Screens.cs"];
        Expect.Contains(screens, "\"Invoice\" => WithStrip(\"Invoice\", new InvoiceListPage(_api)),");
        Expect.Contains(screens, "host.Children.Add(new DashboardStrip(_api.Dashboard, table));");
        Expect.Contains(files["uno/Services/DashboardClient.cs"], "GetAsync(string? table = null)");
    }

    [TestMethod]
    public async Task Without_the_setting_neither_stack_writes_a_strip()
    {
        var files = await Generate(strip: false, "Blazor,Uno");

        Assert.IsFalse(files.Keys.Any(f => f.EndsWith("DashboardStrip.razor") || f.EndsWith("DashboardStrip.cs")));
        Expect.DoesNotContain(files["blz/Pages/InvoicePage.razor"], "DashboardStrip");
        Expect.DoesNotContain(files["uno/MainPage.Screens.cs"], "WithStrip");
    }

    [TestMethod]
    public void The_desktop_strip_differs_between_WinUI3_and_Uno_only_in_where_the_cards_are_read_from()
    {
        string winui3 = DesktopDashboardCode.StripFile("Acme.Views", "using Acme.Data;\n", "", "", "string table", "", "            using var context = new C();\n            var result = await DashboardData.LoadAsync(context, _table);\n");
        string uno = DesktopDashboardCode.StripFile("Acme.Views", "using Acme.Services;\n", "", "    private readonly DashboardClient _client;\n", "DashboardClient client, string table", "        _client = client;\n", "            var result = await _client.GetAsync(_table);\n");

        Expect.Contains(winui3, "foreach (var widget in result.Widgets)");
        Expect.Contains(uno, "foreach (var widget in result.Widgets)");
        Assert.AreEqual(winui3.Split("foreach").Last(), uno.Split("foreach").Last());
    }

    [TestMethod]
    public async Task React_writes_a_Vitest_test_beside_the_dashboard_page_its_widgets_and_the_strip()
    {
        var files = await Generate(strip: true, "React");

        Expect.Contains(files["frontend/src/components/__tests__/DashboardWidgetView.test.tsx"], "it('gives a trend a labelled line and the numbers as text'");
        Expect.Contains(files["frontend/src/components/__tests__/DashboardWidgetView.test.tsx"], "import type { DashboardWidgetData } from '../../api/dashboardApi';");
        Expect.Contains(files["frontend/src/pages/__tests__/DashboardPage.test.tsx"], "Failed to load the dashboard.");
        Expect.Contains(files["frontend/src/components/__tests__/DashboardStrip.test.tsx"], "expect(get).toHaveBeenCalledWith('Customer');");

        var noStrip = await Generate(strip: false, "React");
        Assert.IsTrue(noStrip.ContainsKey("frontend/src/pages/__tests__/DashboardPage.test.tsx"));
        Assert.IsFalse(noStrip.ContainsKey("frontend/src/components/__tests__/DashboardStrip.test.tsx"), "no strip, no strip test");
    }
}
