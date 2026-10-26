using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> What the Uno stack adds to its plain screens: the master-detail dialog, the sign-in with its role rules, the guarded columns, the dashboard, the junction editor and the OData pages.
/// Each is checked on the text of the generated app (a SQLite file); the app was built and driven by hand (Docs/TemplateNotes/UNO_Screens_v1.md), not in this suite. </summary>
[TestClass]
public class UnoFeatureTests
{
    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_unof_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Region (RegionId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
            "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY, AccountNumber VARCHAR(20) NOT NULL, Name VARCHAR(50) NOT NULL, RegionId INTEGER NOT NULL REFERENCES Region (RegionId), CreditLimit DECIMAL(10,2) NULL)",
            "CREATE TABLE Person (PersonId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
            "CREATE TABLE Tag (TagId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
            "CREATE TABLE PersonTag (PersonId INTEGER NOT NULL REFERENCES Person (PersonId), TagId INTEGER NOT NULL REFERENCES Tag (TagId), PRIMARY KEY (PersonId, TagId))"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    /// <summary> Generates the Uno app of the test database with the extra settings and hands the files to <paramref name="check"/>. </summary>
    private static async Task GenerateAsync(Dictionary<string, string> extra, Func<Func<string, string>, List<string>, Task> check)
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_unof_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Uno",
                ["Screens"] = "Region,Customer,Person,Tag", ["Dashboard"] = "false"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = ["Uno"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            await check(relative => File.ReadAllText(Path.Combine(output, relative)).Replace("\r\n", "\n"), files);
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task A_table_with_child_tables_gets_the_master_detail_dialog_with_a_grid_per_child_read_through_the_childs_client()
    {
        await GenerateAsync([], (read, files) =>
        {
            CollectionAssert.Contains(files, "Shop.Uno/Views/RegionDetailMasterDialog.xaml");
            Assert.IsFalse(files.Contains("Shop.Uno/Views/RegionDetailDialog.xaml"), "a master gets the master-detail dialog only");
            CollectionAssert.Contains(files, "Shop.Uno/Views/PersonDetailMasterDialog.xaml");
            string model = read("Shop.Uno/ViewModels/RegionDetailMasterViewModel.cs");
            Expect.Contains(model, "public RegionDetailMasterViewModel(ApiClients api, Region? editing)");
            Expect.Contains(model, "(await _api.Customer.GetAllAsync())");
            Expect.Contains(model, ".Where(c => c.RegionId == _editing!.RegionId)");
            Expect.Contains(model, "typeof(Customer).GetProperties()");
            string dialog = read("Shop.Uno/Views/RegionDetailMasterDialog.xaml.cs");
            Expect.Contains(dialog, "await _api.Customer.DeleteAsync(entity.CustomerId);");
            Expect.Contains(dialog, "new CustomerDetailDialog(_api, entity)");   // the child has no children of its own: the plain dialog
            Expect.Contains(read("Shop.Uno/Views/RegionListPage.xaml.cs"), "new RegionDetailMasterDialog(_api)");
            Expect.DoesNotContain(model, "DbContext");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Auth_shows_the_sign_in_first_and_leaves_out_what_the_role_may_not_use()
    {
        await GenerateAsync(new() { ["Auth"] = "true", ["ApiAuth"] = "true", ["WriteRoles"] = "Administrator", ["TableReadRoles"] = "Tag:Administrator" }, (read, files) =>
        {
            foreach (string expected in new[] { "Shop.Uno/Services/AuthSession.cs", "Shop.Uno/Views/LoginPage.xaml", "Shop.Uno/Views/LoginPage.xaml.cs" })
                CollectionAssert.Contains(files, expected);

            string session = read("Shop.Uno/Services/AuthSession.cs");
            Expect.Contains(session, "public class AuthHandler : DelegatingHandler");
            Expect.Contains(session, "\"/api/auth/login\"");
            Expect.Contains(session, "session.Logout();");
            Expect.Contains(read("Shop.Uno/Views/LoginPage.xaml.cs"), "The user name or password is not right.");

            string main = read("Shop.Uno/MainPage.xaml.cs");
            Expect.Contains(main, "new HttpClient(new AuthHandler(_session))");
            Expect.Contains(main, "LoginHost.Content = new LoginPage(_http, _session);");
            Expect.Contains(read("Shop.Uno/MainPage.xaml"), "Sign out");

            string screens = read("Shop.Uno/MainPage.Screens.cs");
            Expect.Contains(screens, "(\"Tag\", \"Tag\", [\"Administrator\"]),");
            Expect.Contains(screens, "(\"Region\", \"Region\", null),");
            Expect.Contains(screens, "if (roles is not null && !_session.HasRole(roles))");
            Expect.Contains(read("Shop.Uno/Services/ApiClients.cs"), "public sealed class ApiClients(HttpClient http, AuthSession session)");

            string list = read("Shop.Uno/Views/RegionListPage.xaml");
            Expect.Contains(list, "Visibility=\"{x:Bind ViewModel.WriteVisibility}\"");
            Expect.Contains(list, "Visibility=\"{x:Bind WriteVisibility}\"");
            Expect.Contains(read("Shop.Uno/ViewModels/RegionListViewModel.cs"), "_api.Session.HasRole([\"Administrator\"])");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Without_auth_nothing_asks_for_a_session()
    {
        await GenerateAsync([], (read, files) =>
        {
            Assert.IsFalse(files.Contains("Shop.Uno/Services/AuthSession.cs"));
            Expect.DoesNotContain(read("Shop.Uno/MainPage.xaml.cs"), "AuthHandler");
            Expect.DoesNotContain(read("Shop.Uno/Views/RegionListPage.xaml"), "WriteVisibility");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task A_guarded_column_is_disabled_for_a_person_the_api_says_may_not_change_it()
    {
        await GenerateAsync(new() { ["ApiAuth"] = "true", ["GuardedColumns"] = "Customer.CreditLimit; by Employee.IsManager" }, (read, _) =>
        {
            Expect.Contains(read("Shop.Uno/Services/CustomerClient.cs"), "public async Task<Dictionary<string, bool>> GetGuardsAsync()");
            string dialog = read("Shop.Uno/Views/CustomerDetailDialog.xaml");
            Expect.Contains(dialog, "<ContentControl HorizontalContentAlignment=\"Stretch\" IsEnabled=\"{x:Bind ViewModel.CreditLimitAllowed, Mode=OneWay}\">");
            Expect.Contains(dialog, "Visibility=\"{x:Bind ViewModel.CreditLimitNoteVisibility, Mode=OneWay}\"");
            string model = read("Shop.Uno/ViewModels/CustomerDetailViewModel.cs");
            Expect.Contains(model, "CreditLimitAllowed = guards.GetValueOrDefault(\"creditLimit\");");
            Expect.Contains(model, "await _api.Customer.GetGuardsAsync()");
            Expect.DoesNotContain(read("Shop.Uno/Services/RegionClient.cs"), "GetGuardsAsync");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task The_master_detail_dialog_handles_a_guarded_column_like_the_plain_one()
    {
        await GenerateAsync(new() { ["ApiAuth"] = "true", ["GuardedColumns"] = "Region.Name; by Employee.IsManager" }, (read, _) =>
        {
            Expect.Contains(read("Shop.Uno/Views/RegionDetailMasterDialog.xaml"), "<ContentControl HorizontalContentAlignment=\"Stretch\" IsEnabled=\"{x:Bind ViewModel.NameAllowed, Mode=OneWay}\">");
            string model = read("Shop.Uno/ViewModels/RegionDetailMasterViewModel.cs");
            Expect.Contains(model, "private bool _nameAllowed;");
            Expect.Contains(model, "NameAllowed = guards.GetValueOrDefault(\"name\");");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task The_dashboard_is_the_first_screen_and_reads_the_api()
    {
        await GenerateAsync(new() { ["Dashboard"] = "true" }, (read, files) =>
        {
            foreach (string expected in new[] { "Shop.Uno/Services/DashboardClient.cs", "Shop.Uno/Views/DashboardPage.cs", "Shop.Uno/Views/DashboardWidgetViews.cs" })
                CollectionAssert.Contains(files, expected);
            Expect.Contains(read("Shop.Uno/Services/DashboardClient.cs"), DashboardWire.ClientGetCSharp);
            string screens = read("Shop.Uno/MainPage.Screens.cs");
            Expect.Contains(screens, "private const string FirstScreen = \"Dashboard\";");
            Expect.Contains(screens, "\"Dashboard\" => new DashboardPage(_api.Dashboard, GoTo),");
            Expect.Contains(screens, "private void GoTo(string tag)");
            Expect.Contains(read("Shop.Uno/Services/ApiClients.cs"), "public DashboardClient Dashboard => _dashboard ??= new(http);");
            Expect.DoesNotContain(read("Shop.Uno/Views/DashboardPage.cs"), "DbContext");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task A_junction_table_gets_an_editor_over_the_junction_routes()
    {
        await GenerateAsync([], (read, files) =>
        {
            foreach (string expected in new[] { "Shop.Uno/Services/PersonTagJunctionClient.cs", "Shop.Uno/Views/PersonTagJunctionEditor.xaml", "Shop.Uno/Views/PersonTagJunctionEditor.xaml.cs", "Shop.Uno/ViewModels/PersonTagJunctionEditorViewModel.cs" })
                CollectionAssert.Contains(files, expected);
            string client = read("Shop.Uno/Services/PersonTagJunctionClient.cs");
            Expect.Contains(client, "private const string Url = \"/api/persontags/junction\";");
            Expect.Contains(client, "public async Task LinkAsync(int anchorId, int targetId)");
            Expect.Contains(client, "public string DisplayText => Name ?? \"\";");
            Expect.Contains(read("Shop.Uno/Views/PersonTagJunctionEditor.xaml.cs"), "public PersonTagJunctionEditor(ApiClients api, int anchorId)");
            Expect.Contains(read("Shop.Uno/Services/ApiClients.cs"), "public PersonTagJunctionClient PersonTagJunction => _personTagJunction ??= new(http);");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task OData_gives_each_served_table_a_client_a_page_and_a_menu_entry()
    {
        await GenerateAsync(new() { ["OData"] = "true", ["ODataTables"] = "Customer" }, (read, files) =>
        {
            foreach (string expected in new[] { "Shop.Uno/Services/ODataSupport.cs", "Shop.Uno/Services/CustomerODataClient.cs", "Shop.Uno/Views/CustomerODataListPage.xaml", "Shop.Uno/Views/CustomerODataListPage.xaml.cs", "Shop.Uno/ViewModels/CustomerODataListViewModel.cs" })
                CollectionAssert.Contains(files, expected);
            Assert.IsFalse(files.Contains("Shop.Uno/Views/RegionODataListPage.xaml"), "only the tables ODataTables names are served");
            Expect.Contains(read("Shop.Uno/Services/CustomerODataClient.cs"), "public class CustomerODataClient(HttpClient http) : ODataClient<Customer, int>(http, \"customers\"");
            Expect.DoesNotContain(read("Shop.Uno/Services/ODataSupport.cs"), "JSRuntime");
            string model = read("Shop.Uno/ViewModels/CustomerODataListViewModel.cs");
            Expect.Contains(model, "public partial class CustomerODataListViewModel : ViewModelBase");
            Expect.Contains(model, "await OData.QueryPageAsync(new ODataPageQuery(PageNumber, PageSize, filters, ");
            Expect.Contains(read("Shop.Uno/Views/CustomerODataListPage.xaml"), "(OData)");
            string screens = read("Shop.Uno/MainPage.Screens.cs");
            Expect.Contains(screens, "Content = \"Customer (OData)\", Tag = \"CustomerOData\"");
            Expect.Contains(screens, "\"CustomerOData\" => new CustomerODataListPage(_api),");
            Expect.Contains(read("Shop.Uno/Services/ApiClients.cs"), "public CustomerODataClient CustomerOData => _customerOData ??= new(http);");
            // the REST page is still there beside it
            CollectionAssert.Contains(files, "Shop.Uno/Views/CustomerListPage.xaml");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ODataScreens_Replace_keeps_the_child_grids_because_the_OData_page_opens_the_same_master_detail_dialog()
    {
        await GenerateAsync(new() { ["OData"] = "true", ["ODataTables"] = "Region", ["ODataScreens"] = "Replace" }, (read, files) =>
        {
            string screens = read("Shop.Uno/MainPage.Screens.cs");
            Expect.Contains(screens, "\"Region\" => new RegionODataListPage(_api),");
            Expect.DoesNotContain(screens, "\"RegionOData\"");
            string page = read("Shop.Uno/Views/RegionODataListPage.xaml.cs");
            Expect.Contains(page, "new RegionDetailMasterDialog(_api");
            CollectionAssert.Contains(files, "Shop.Uno/Views/RegionDetailMasterDialog.xaml");
            Expect.Contains(read("Shop.Uno/ViewModels/RegionDetailMasterViewModel.cs"), "_api.Customer");
            return Task.CompletedTask;
        });
    }
}
