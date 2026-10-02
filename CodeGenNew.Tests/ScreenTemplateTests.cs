using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The screen-list templates (TSX_Screens, TS_Screens, WinUI3_Screens) write the menu and routes of each app from the table list. </summary>
[TestClass]
public class ScreenTemplateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    // E_DonateLeave: a plain table; Order: has a child table (OrderLine) so its screen is master-detail
    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = [.. tables] };

    private static async Task<string> Render(string template, DatabaseModel database, ProjectSettings? project = null)
    {
        var result = await TemplateRunner.RunAsync(Repo.Template(template), database, project ?? Project());
        Assert.IsTrue(result.Success, $"{template} failed: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    [DataRow("SalesInvoice", "Sales Invoice", "sales-invoice", "salesinvoice")]
    [DataRow("E_DonateLeave", "Donate Leave", "donate-leave", "donateleave")]
    [DataRow("SY_Role", "Role", "role", "role")]
    [DataRow("Customer", "Customer", "customer", "customer")]
    [DataRow("CustomerPO", "Customer PO", "customer-po", "customerpo")]
    public void A_screens_names_follow_one_rule(string table, string label, string route, string stem)
    {
        Assert.AreEqual(label, ScreenNames.Label(table));
        Assert.AreEqual(route, ScreenNames.Route(table));
        Assert.AreEqual(stem, ScreenNames.Stem(table));
    }

    [TestMethod]
    public async Task React_lists_a_page_per_screen_and_warns_about_a_child_without_a_screen()
    {
        string tsx = await Render("TSX_Screens_v1.tt", Database(Sample.OrderWithLines(), Sample.DonateLeave()));

        Expect.Contains(tsx, "import { OrderDetailMasterPage } from './pages/OrderDetailMasterPage';");
        Expect.Contains(tsx, "import { DonateLeavePage } from './pages/DonateLeavePage';");
        Expect.Contains(tsx, "{ path: 'donate-leave', label: 'Donate Leave', element: <DonateLeavePage /> },");
        Expect.Contains(tsx, "{ path: 'order', label: 'Order', element: <OrderDetailMasterPage /> },");
        Assert.IsLessThan(tsx.IndexOf("path: 'order'"), tsx.IndexOf("path: 'donate-leave'"), "alphabetical by default");
        Expect.Contains(tsx, "WARNING: these child tables are linked from a master-detail screen but have no screen of their own: OrderLine (a child of Order)");
    }

    [TestMethod]
    public async Task The_screens_setting_chooses_and_orders_the_menu()
    {
        string tsx = await Render("TSX_Screens_v1.tt", Database(Sample.OrderWithLines(), Sample.DonateLeave()), Project(("Screens", "Order,E_DonateLeave")));

        Assert.IsLessThan(tsx.IndexOf("path: 'donate-leave'"), tsx.IndexOf("path: 'order'"));
    }

    [TestMethod]
    public async Task A_listed_screen_that_is_not_a_table_is_an_error()
    {
        var result = await TemplateRunner.RunAsync(Repo.Template("TSX_Screens_v1.tt"), Database(Sample.DonateLeave()), Project(("Screens", "E_DonateLeave,Nothing")));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(string.Join(" | ", result.Errors), "'Nothing'");
    }

    [TestMethod]
    public async Task The_detail_master_tables_setting_chooses_the_kind_of_page()
    {
        string tsx = await Render("TSX_Screens_v1.tt", Database(Sample.OrderWithLines(), Sample.DonateLeave()), Project(("DetailMasterTables", "E_DonateLeave")));

        Expect.Contains(tsx, "element: <DonateLeaveDetailMasterPage />");
        Expect.Contains(tsx, "element: <OrderPage />");
    }

    [TestMethod]
    public async Task Angular_writes_the_screens_and_the_routes_with_the_component_names_the_screen_templates_use()
    {
        string ts = await Render("TS_Screens_v1.tt", Database(Sample.OrderWithLines(), Sample.DonateLeave()));

        Expect.Contains(ts, "import { OrderDetailMasterComponent } from './components/order-detail-master/order-detail-master.component';");
        Expect.Contains(ts, "import { DonateLeaveComponent } from './components/donateleave/donateleave.component';");
        Expect.Contains(ts, "{ path: 'donate-leave', label: 'Donate Leave', component: DonateLeaveComponent },");
        Expect.Contains(ts, "export const routes: Routes = [");
        Expect.Contains(ts, "redirectTo: screens[0].path");
    }

    [TestMethod]
    public async Task WinUI3_adds_the_menu_entries_and_the_page_switch()
    {
        string cs = await Render("WinUI3_Screens_v1.tt", Database(Sample.OrderWithLines(), Sample.DonateLeave()));

        Expect.Contains(cs, "namespace Acme.App;");
        Expect.Contains(cs, "using Acme.App.Views;");
        Expect.Contains(cs, "private const string FirstScreen = \"E_DonateLeave\";");
        Expect.Contains(cs, "NavView.MenuItems.Add(new NavigationViewItem { Content = \"Donate Leave\", Tag = \"E_DonateLeave\" });");
        Expect.Contains(cs, "\"Order\" => new OrderListPage(_context),");
    }

    [TestMethod]
    public async Task A_master_detail_screens_child_link_and_the_menu_use_the_same_route()
    {
        // The link TSX_DetailMasterPage writes for a row of the child grid, and the route TSX_Screens lists for that child's own screen.
        var order = Sample.OrderWithLines();
        var line = Sample.Table("OrderLine", [Sample.Column("OrderLineId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("OrderId", System.Data.SqlDbType.Int, ordinal: 2)]);

        var page = await TemplateRunner.RunAsync(Repo.Template("TSX_DetailMasterPage_v1.tt"), order);
        Assert.IsTrue(page.Success, string.Join(" | ", page.Errors));
        string menu = await Render("TSX_Screens_v1.tt", Database(order, line));

        string route = ScreenNames.Route("OrderLine");
        Expect.Contains(page.GeneratedText!.Replace("\r\n", "\n"), $"navigate(`/{route}?edit=");
        Expect.Contains(menu, $"path: '{route}'");
        Expect.DoesNotContain(menu, "WARNING");
    }
}
