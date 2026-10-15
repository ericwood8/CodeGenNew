using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The Blazor sign-in (Auth=true), the role rules on its pages and menu, and its OData clients and pages (OData=true): the groups, the shell files, the guarded pages, the OData form of a page and
/// the REST output that must not change. </summary>
[TestClass]
public class BlazorAuthODataTests
{
    private static readonly (string, string) Auth = ("Auth", "true");
    private static readonly (string, string) Api = ("ApiAuth", "true");
    private static readonly (string, string) OData = ("OData", "true");

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<string> Run(string template, object model, params (string, string)[] values)
    {
        var project = Project(values);
        var result = model switch
        {
            TableModel table => await Repo.Cache.RunAsync(Repo.Template(template), table, project),
            DatabaseModel database => await Repo.Cache.RunAsync(Repo.Template(template), database, project),
            _ => await Repo.Cache.RunAsync(Repo.Template(template), project)
        };
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static DatabaseModel Database() => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday(), Sample.NaturalKey()] };

    // ------------------------------------------------------------------ the groups

    [TestMethod]
    public void The_auth_and_odata_groups_are_offered_and_ticked_by_their_settings()
    {
        Assert.AreEqual("Auth", TemplateConfig.Load(Repo.Template("BLZ_EssentialAuth_v1.tt.config")).EssentialsGroup);
        Assert.AreEqual("OData", TemplateConfig.Load(Repo.Template("BLZ_EssentialOData_v1.tt.config")).EssentialsGroup);
        CollectionAssert.Contains(Project(Auth).ImpliedEssentialsGroups.ToArray(), "Auth");
        CollectionAssert.Contains(Project(OData).ImpliedEssentialsGroups.ToArray(), "OData");
        CollectionAssert.IsSubsetOf(new[] { "BLZ_ODataClient", "BLZ_ODataPage" }, Project(OData).ImpliedPlanTemplates.ToArray());
    }

    // ------------------------------------------------------------------ the sign-in

    [TestMethod]
    public async Task The_Auth_group_writes_the_session_the_login_page_and_the_redirect()
    {
        string auth = await Run("BLZ_EssentialAuth_v1.tt", new object(), Auth);

        foreach (string file in new[] { "Services/AuthSession.cs", "Pages/Login.razor", "Components/RedirectToStart.razor" })
            Expect.Contains(auth, "@@@FILE " + file + "@@@");
        Expect.Contains(auth, "private const string StorageKey = \"acme.auth\";");
        Expect.Contains(auth, "js.InvokeAsync<string?>(\"sessionStorage.getItem\", StorageKey)");
        Expect.Contains(auth, "http.PostAsJsonAsync(\"/api/auth/login\", new { userName, password })");
        Expect.Contains(auth, "claims.Add(new Claim(ClaimTypes.Role, role));");
        Expect.Contains(auth, "request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", token);");
        Expect.Contains(auth, "if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null)");
        Expect.Contains(auth, "401 => \"The user name or password is not right.\"");
        Expect.Contains(auth, "429 => \"Too many tries. Wait a minute and try again.\"");
        Expect.Contains(auth, "Navigation.NavigateTo(Session.IsSignedIn ? \"/\" : \"login\", replace: true);");
    }

    [TestMethod]
    public async Task The_project_files_wire_the_sign_in_only_with_Auth()
    {
        string with = await Run("BLZ_EssentialProject_v1.tt", new object(), Auth, OData);
        Expect.Contains(with, "<PackageReference Include=\"Microsoft.AspNetCore.Components.Authorization\" Version=\"10.0.*\" />");
        Expect.Contains(with, "builder.Services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();");
        Expect.Contains(with, "builder.Services.AddScoped(sp => new HttpClient(sp.GetRequiredService<AuthHandler>()) { BaseAddress = new Uri(apiBase) });");
        Expect.Contains(with, "<AuthorizeRouteView RouteData=\"routeData\" DefaultLayout=\"typeof(Layout.MainLayout)\">");
        Expect.Contains(with, "<RedirectToStart />");
        Expect.Contains(with, "@using Microsoft.AspNetCore.Authorization");
        Expect.Contains(with, "@using Acme.Blazor.Components");
        Expect.Contains(with, "<link rel=\"stylesheet\" href=\"css/odata.css\" />");

        string plain = await Run("BLZ_EssentialProject_v1.tt", new object());
        Expect.DoesNotContain(plain, "Authorization");
        Expect.DoesNotContain(plain, "AuthSession");
        Expect.DoesNotContain(plain, "odata.css");
        Expect.Contains(plain, "<RouteView RouteData=\"routeData\" DefaultLayout=\"typeof(Layout.MainLayout)\" />");
        Expect.Contains(plain, "builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase) });");
    }

    [TestMethod]
    public async Task The_layout_shows_the_user_and_Sign_out_only_with_Auth()
    {
        string with = await Run("BLZ_EssentialLayout_v1.tt", new object(), Auth);
        Expect.Contains(with, "<span class=\"who\">@Session.User!.Name</span>");
        Expect.Contains(with, "<button type=\"button\" @onclick=\"SignOutAsync\">Sign out</button>");
        Expect.Contains(with, ".login { max-width: 360px; }");

        string plain = await Run("BLZ_EssentialLayout_v1.tt", new object());
        Expect.DoesNotContain(plain, "Session");
        Expect.DoesNotContain(plain, ".login");
    }

    [TestMethod]
    public async Task The_menu_and_home_leave_out_the_screens_the_role_may_not_read()
    {
        string screens = await Run("BLZ_Screens_v1.tt", Database(), Auth, Api, ("TableReadRoles", "Holiday:Hr"));

        Expect.Contains(screens, "@if (Session.IsSignedIn)");
        Expect.Contains(screens, "@if (Session.HasRole([\"Administrator\", \"Hr\"]))");
        Expect.Contains(screens, "<NavLink href=\"country\">Country</NavLink>");
        Expect.Contains(screens, "(\"holiday\", [\"Administrator\", \"Hr\"]),");
        Expect.Contains(screens, "(\"country\", null),");
        Expect.Contains(screens, "Navigation.NavigateTo(\"login\", replace: true);");

        string plain = await Run("BLZ_Screens_v1.tt", Database());
        Expect.DoesNotContain(plain, "Session");
        Expect.Contains(plain, "Navigation.NavigateTo(\"dashboard\", replace: true);");
    }

    [TestMethod]
    public async Task A_page_asks_for_a_signed_in_person_and_hides_the_write_buttons_from_a_role_that_may_not_write()
    {
        string page = await Run("BLZ_Page_v1.tt", Sample.Holiday(), Auth, Api, ("WriteRoles", "Manager"), ("TableReadRoles", "Holiday:Hr"));

        Expect.Contains(page, "@attribute [Authorize(Roles = \"Administrator,Hr\")]");
        Expect.Contains(page, "@inject AuthSession Session");
        Expect.Contains(page, "private bool CanWrite => Session.HasRole([\"Administrator\", \"Manager\"]);");
        Expect.Contains(page, "@if (CanWrite)\n{\n    <button type=\"button\" @onclick=\"Add\">Add New Holiday</button>\n}");
        Expect.Contains(page, "<button type=\"button\" @onclick=\"() => Edit(item)\">Edit</button>");
        Expect.Contains(page, "<button type=\"button\" @onclick=\"() => DeleteAsync(item)\">Delete</button>");
        Expect.Contains(page, "if (EditId is not null && CanWrite)");

        string open = await Run("BLZ_Page_v1.tt", Sample.Holiday(), Auth, Api);
        Expect.Contains(open, "@attribute [Authorize]");
        Expect.DoesNotContain(open, "CanWrite");
        Expect.DoesNotContain(open, "AuthSession");

        string plain = await Run("BLZ_Page_v1.tt", Sample.Holiday());
        Expect.DoesNotContain(plain, "Authorize");
        Expect.DoesNotContain(plain, "CanWrite");
    }

    [TestMethod]
    public async Task The_dashboard_page_asks_for_a_signed_in_person_with_Auth()
    {
        string with = await Run("BLZ_Dashboard_v1.tt", Database(), ("Dashboard", "true"), Auth);
        Expect.Contains(with, "@attribute [Authorize]\n@inject DashboardClient Client");
        Expect.DoesNotContain(await Run("BLZ_Dashboard_v1.tt", Database(), ("Dashboard", "true")), "Authorize");
    }

    // ------------------------------------------------------------------ OData

    [TestMethod]
    public async Task The_OData_group_writes_the_query_helper_the_filter_row_the_client_base_and_the_style_sheet()
    {
        string support = await Run("BLZ_EssentialOData_v1.tt", new object(), OData);

        Expect.Contains(support, "@@@FILE Services/ODataSupport.cs@@@");
        Expect.Contains(support, "@@@FILE wwwroot/css/odata.css@@@");
        Expect.Contains(support, ".filter-row td {");
        Expect.Contains(support, "public static string ODataString(string value) => \"'\" + value.Replace(\"'\", \"''\") + \"'\";");
        Expect.Contains(support, "$\"contains(tolower({property}),{ODataString(text.ToLowerInvariant())})\"");
        Expect.Contains(support, "public class ODataClient<T, TKey>(HttpClient http, string set, ODataTable table, string restPath) where T : class");
        Expect.Contains(support, "(\"$count\", true)");
        Expect.Contains(support, "conditions.Add($\"{field.Property} lt {ODataQuery.Literal(ODataQuery.Day(entry.To).AddDays(1))}\");");
        Expect.Contains(support, "if (error.Status == 409)");
    }

    [TestMethod]
    public async Task An_OData_client_is_a_short_class_on_the_client_base()
    {
        string clients = await Run("BLZ_ODataClient_v1.tt", Database(), OData);

        Expect.Contains(clients, "@@@FILE Services/HolidayODataClient.cs@@@");
        Expect.Contains(clients, "public class HolidayODataClient(HttpClient http) : ODataClient<Holiday, int>(http, \"holidays\", Table, \"/api/holidays\")");
        Expect.Contains(clients, "@@@FILE Services/CountryODataClient.cs@@@");
        Expect.Contains(clients, "ODataClient<Country, string>(http, \"countries\"");
        Expect.Contains(clients, "private static readonly ODataTable Table = new(");
    }

    [TestMethod]
    public async Task The_clients_are_registered_and_the_menu_lists_the_OData_pages_after_the_REST_ones()
    {
        string screens = await Run("BLZ_Screens_v1.tt", Database(), OData);

        Expect.Contains(screens, "services.AddScoped<HolidayODataClient>();");
        Expect.Contains(screens, "<NavLink href=\"holiday-odata\">Holiday (OData)</NavLink>");
        Assert.IsTrue(screens.IndexOf("href=\"country\"", StringComparison.Ordinal) < screens.IndexOf("href=\"holiday-odata\"", StringComparison.Ordinal));

        string replaced = await Run("BLZ_Screens_v1.tt", Database(), OData, ("ODataScreens", "Replace"));
        Expect.DoesNotContain(replaced, "-odata");
        Expect.Contains(replaced, "services.AddScoped<HolidayODataClient>();");

        Expect.DoesNotContain(await Run("BLZ_Screens_v1.tt", Database()), "OData");
    }

    [TestMethod]
    public async Task The_OData_page_has_its_own_route_several_sorts_and_a_filter_row_in_place_of_the_search_boxes()
    {
        string page = await Run("BLZ_ODataPage_v1.tt", Sample.Holiday(), OData);

        Expect.Contains(page, "@@@FILE Pages/HolidayODataPage.razor@@@");
        Expect.Contains(page, "@page \"/holiday-odata\"");
        Expect.Contains(page, "@inject HolidayODataClient Client");
        Expect.Contains(page, "var result = await Client.QueryPageAsync(new ODataPageQuery(target, PageSize, null, sorts, applied, SelectColumns));");
        Expect.Contains(page, "<tr class=\"filter-row\">");
        Expect.Contains(page, "private static readonly ODataFilterField[] FilterFields =");
        Expect.Contains(page, "@onclick='(e) => SortAsync(");
        Expect.Contains(page, ", e.ShiftKey)'");
        Expect.Contains(page, "private async Task FilterKeyAsync(KeyboardEventArgs args)");
        Expect.Contains(page, "catch (FormatException ex) { message = ex.Message; }");
        Expect.Contains(page, "ex.ExplainOData(\"deleting\")");
        Expect.DoesNotContain(page, "class=\"search\"");
        Expect.DoesNotContain(page, "GetPageAsync");
    }

    [TestMethod]
    public async Task The_OData_page_keeps_its_sorts_and_reads_only_the_columns_the_grid_shows()
    {
        string page = await Run("BLZ_ODataPage_v1.tt", Sample.Holiday(), OData);

        Expect.Contains(page, ".sorts.holidayodatapage\";");
        Expect.Contains(page, "private static readonly string[] SortableColumns = [");
        Expect.Contains(page, "private static readonly string[] SelectColumns = [");
        Expect.Contains(page, "sorts = await SavedSorts.LoadAsync(Js, SortsKey, SortableColumns);");
        Expect.Contains(page, "await SavedSorts.SaveAsync(Js, SortsKey, sorts);");
        Expect.Contains(page, "@onclick=\"() => OpenAsync(item)\"");
        Expect.Contains(page, "Edit(await Client.GetAsync(item.");   // the slim row is read whole before the form opens it

        string rest = await Run("BLZ_Page_v1.tt", Sample.Holiday(), OData);
        Expect.DoesNotContain(rest, "SavedSorts");
        Expect.DoesNotContain(rest, "SelectColumns");
        Expect.DoesNotContain(rest, "OpenAsync");

        string support = await Run("BLZ_EssentialOData_v1.tt", new object(), OData);
        Expect.Contains(support, "public static class SavedSorts");
        Expect.Contains(support, "(\"$select\", query.Select is { Count: > 0 } ? string.Join(\",\", new[] { table.Key }.Concat(query.Select).Distinct()) : null)");
    }

    [TestMethod]
    public async Task The_OData_page_is_written_for_the_served_tables_and_the_REST_page_loses_its_route_only_when_replaced()
    {
        Assert.AreEqual("", (await Run("BLZ_ODataPage_v1.tt", Sample.Holiday())).Trim(), "OData is off: no table is served");
        Expect.Contains(await Run("BLZ_ODataPage_v1.tt", Sample.Holiday(), OData, ("ODataScreens", "Replace")), "@page \"/holiday\"");

        string rest = await Run("BLZ_Page_v1.tt", Sample.Holiday(), OData);
        Expect.Contains(rest, "@page \"/holiday\"");
        string replaced = await Run("BLZ_Page_v1.tt", Sample.Holiday(), OData, ("ODataScreens", "Replace"));
        Expect.DoesNotContain(replaced, "@page \"");
        Expect.Contains(replaced, "@inject HolidayClient Client");
    }

    [TestMethod]
    public async Task The_REST_page_is_the_same_with_OData_on_and_off()
    {
        Assert.AreEqual(await Run("BLZ_Page_v1.tt", Sample.Holiday()), await Run("BLZ_Page_v1.tt", Sample.Holiday(), OData));
    }

    [TestMethod]
    public void The_Blazor_OData_screen_tables_are_the_served_ones_listed_as_screens()
    {
        var database = Database();
        Assert.AreEqual(0, ODataPlan.BlazorScreenTables(database, Project()).Count);
        CollectionAssert.AreEquivalent(new[] { "Holiday", "Country" }, ODataPlan.BlazorScreenTables(database, Project(OData)).Select(t => t.TableName).ToArray());
        CollectionAssert.AreEqual(new[] { "Country" }, ODataPlan.BlazorScreenTables(database, Project(OData, ("ODataTables", "Country"))).Select(t => t.TableName).ToArray());
    }
}
