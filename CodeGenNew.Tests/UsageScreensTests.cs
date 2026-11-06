using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The usage page of the five front ends: which projects get it, its menu entry, and that the text of every stack is filled with nothing left over. </summary>
[TestClass]
public class UsageScreensTests
{
    private static ColumnModel C(string name, SqlDbType type, bool primaryKey = false, int ordinal = 0, int? characters = null, bool identity = false) =>
        Sample.Column(name, type, nullable: false, primaryKey: primaryKey, ordinal: ordinal, characters: characters, identity: identity);

    private static DatabaseModel Database() => new()
    {
        DatabaseName = "Shop", SchemaName = "dbo", Dialect = SqlDialect.SqlServer,
        Tables =
        [
            Sample.Table("Tenant", [C("TenantId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("Name", SqlDbType.VarChar, ordinal: 2, characters: 40)]),
            Sample.Table("UsageEvent",
                [C("UsageEventId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: 40),
                 C("Quantity", SqlDbType.Decimal, ordinal: 4), C("OccurredOn", SqlDbType.DateTime, ordinal: 5)],
                [new ForeignKeyModel { ConstraintName = "FK_u", ReferencingColumns = ["TenantId"], ReferencedSchema = "dbo", ReferencedTable = "Tenant", ReferencedColumns = ["TenantId"] }])
        ]
    };

    private static ProjectSettings Project(Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string>
        {
            ["AccessMode"] = "Ef", ["DatabaseProvider"] = "SqlServer", ["ApiAuth"] = "true", ["Auth"] = "true", ["TenantTable"] = "Tenant", ["UsageTables"] = "UsageEvent", ["UserTable"] = "Employee", ["Stacks"] = "React,Angular,Blazor,Uno,WinUI3"
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public void A_project_that_signs_in_with_tenants_gets_a_page_for_each_usage_table()
    {
        var page = UsageScreens.Pages(Database(), Project()).Single();

        Assert.AreEqual("/api/usageevents", page.ApiRoute);
        Assert.AreEqual("UsageEventSummary", page.Name);
        Assert.AreEqual("UsageEventSummaryPage", page.PageClass);
        Assert.AreEqual("usage-event", page.Path);
        Assert.IsNull(page.Rollup);
    }

    [TestMethod]
    public void Without_the_sign_in_the_api_or_the_tenants_there_is_no_page()
    {
        Assert.IsEmpty(UsageScreens.Pages(Database(), Project(new() { ["Auth"] = "false" })));
        Assert.IsEmpty(UsageScreens.Pages(Database(), Project(new() { ["ApiAuth"] = "false" })));
        Assert.IsEmpty(UsageScreens.Pages(Database(), Project(new() { ["UsageTables"] = "Missing" })));
        Assert.IsEmpty(UsageScreens.Pages(Database(), Project(new() { ["UsageTables"] = "" })));
    }

    [TestMethod]
    public void The_desktop_app_that_reads_the_database_needs_the_desktop_sign_in()
    {
        Assert.IsNotEmpty(UsageScreens.DesktopPages(Database(), Project()));
        Assert.IsEmpty(UsageScreens.DesktopPages(Database(), Project(new() { ["Stacks"] = "React" })));
    }

    [TestMethod]
    public void The_pages_are_implied_by_the_settings_and_listed_in_the_blazor_menu()
    {
        var implied = Project().ImpliedPlanTemplates.ToList();
        foreach (string name in new[] { "TSX_Usage", "TS_Usage", "BLZ_Usage", "UNO_Usage", "WinUI3_Usage" })
            CollectionAssert.Contains(implied, name);
        CollectionAssert.DoesNotContain(Project(new() { ["Auth"] = "false" }).ImpliedPlanTemplates.ToList(), "TSX_Usage");

        var entry = BlazorMenu.Entries(Database(), Project()).Single(e => e.Route == "usage-event");
        Assert.HasCount(1, entry.Roles);
    }

    [TestMethod]
    public void The_text_of_every_stack_is_filled_and_says_the_same_things()
    {
        var project = Project();
        var page = UsageScreens.Pages(Database(), project).Single();

        string react = UsageWebCode.React(page, "api");
        string angular = UsageWebCode.AngularComponent(page, project, "usage-event-summary");
        string blazor = UsageWebCode.Blazor(page, "Shop", "Administrator");
        string uno = UsageDesktopCode.UnoPage("Shop.Views", "Shop.Services", page);
        string desktop = UsageDesktopCode.DesktopPage("Shop.Views", "Shop.Repositories", "ShopContext", ["Shop.Data"], page);
        foreach (string text in new[] { react, UsageWebCode.ReactTest(page), angular, UsageWebCode.AngularHtml(page, project), UsageWebCode.AngularSpec(page, "usage-event-summary"), blazor, uno, desktop, UsageDesktopCode.Xaml("Shop.Views", page) })
            Assert.DoesNotContain("§", text, "every token is filled");
        Assert.Contains("'/api/usageevents' + (text ? '?' + text : '')", react);
        Assert.Contains("\"/api/usageevents\"", blazor);
        Assert.Contains("\"api/usageevents\"", uno);
        Assert.Contains("x.Quantity", desktop);
        Assert.Contains("CurrentTenantId", desktop);
        Assert.DoesNotContain("Set<UsageMonthly>", desktop, "without UsageRollup there is no monthly table to add");
    }

    [TestMethod]
    public void Every_stack_has_the_day_boxes_the_kind_box_and_a_bar_for_each_line()
    {
        var project = Project();
        var page = UsageScreens.Pages(Database(), project).Single();

        foreach (string web in new[] { UsageWebCode.React(page, "api"), UsageWebCode.AngularHtml(page, project), UsageWebCode.Blazor(page, "Shop", "Administrator") })
        {
            Assert.Contains("usage-from", web);
            Assert.Contains("usage-before", web);
            Assert.Contains("<meter", web);
        }
        Assert.Contains("query.set('to', before)", UsageWebCode.React(page, "api"));
        Assert.Contains("'to=' + encodeURIComponent(this.before)", UsageWebCode.AngularComponent(page, project, "usage-event-summary"));
        Assert.Contains("\"to=\" + Uri.EscapeDataString(before)", UsageWebCode.Blazor(page, "Shop", "Administrator"));
        string xaml = UsageDesktopCode.Xaml("Shop.Views", page);
        Assert.Contains("FromBox", xaml);
        Assert.Contains("BeforeBox", xaml);
        Assert.Contains("<ProgressBar", xaml);
        Assert.Contains(UsageScreenWords.BadDate, UsageDesktopCode.UnoPage("Shop.Views", "Shop.Services", page));
        string desktop = UsageDesktopCode.DesktopPage("Shop.Views", "Shop.Repositories", "ShopContext", [], page);
        Assert.Contains(UsageScreenWords.BadDate, desktop);
        Assert.Contains("x.OccurredOn >= since", desktop);
    }

    [TestMethod]
    public void Every_stack_draws_a_column_for_each_month_oldest_first()
    {
        var project = Project();
        var page = UsageScreens.Pages(Database(), project).Single();

        foreach (string web in new[] { UsageWebCode.React(page, "api"), UsageWebCode.AngularHtml(page, project), UsageWebCode.Blazor(page, "Shop", "Administrator") })
        {
            Assert.Contains(UsageScreenWords.OverTime, web);
            Assert.Contains("role=\"img\"", web);
        }
        string xaml = UsageDesktopCode.Xaml("Shop.Views", page);
        Assert.Contains("<Rectangle", xaml);
        Assert.Contains("local:UsageEventSummaryMonthColumn", xaml);
        foreach (string desktop in new[] { UsageDesktopCode.UnoPage("Shop.Views", "Shop.Services", page), UsageDesktopCode.DesktopPage("Shop.Views", "Shop.Repositories", "ShopContext", [], page) })
        {
            Assert.Contains("DrawMonths();", desktop);
            Assert.DoesNotContain("§", desktop);
        }
    }

    [TestMethod]
    public void The_first_usage_table_also_adds_the_monthly_totals_of_the_roll_up()
    {
        var database = Database();
        database.Tables.Add(Sample.Table("UsageMonthly",
            [C("UsageMonthlyId", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("TenantId", SqlDbType.Int, ordinal: 2), C("Kind", SqlDbType.VarChar, ordinal: 3, characters: 40),
             C("UsageMonth", SqlDbType.DateTime, ordinal: 4), C("Quantity", SqlDbType.Decimal, ordinal: 5)],
            [new ForeignKeyModel { ConstraintName = "FK_m", ReferencingColumns = ["TenantId"], ReferencedSchema = "dbo", ReferencedTable = "Tenant", ReferencedColumns = ["TenantId"] }]));
        var project = Project(new() { ["UsageRollup"] = "UsageMonthly:UsageMonth:Quantity" });
        var page = UsageScreens.Pages(database, project).Single();

        Assert.IsNotNull(page.Rollup);
        string desktop = UsageDesktopCode.DesktopPage("Shop.Views", "Shop.Repositories", "ShopContext", [], page);
        Assert.Contains("Set<UsageMonthly>", desktop);
        Assert.DoesNotContain("§", desktop);
    }
}
