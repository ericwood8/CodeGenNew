using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The Uno stack: its settings, which tables get a client, the text of every template and the files of a project generated from a SQLite file. Building the generated app needs the
/// Uno packages from NuGet, so that check was made by hand against the SQL Server sample (Docs/TemplateNotes/UNO_Screens_v1.md), not in this suite. </summary>
[TestClass]
public class UnoTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    [TestMethod]
    public void The_uno_stack_is_known_and_has_a_build_command_an_output_folder_and_a_dev_port()
    {
        CollectionAssert.Contains(ProjectPlan.KnownStacks.ToArray(), "Uno");
        CollectionAssert.Contains(ProjectSettingChoices.Stacks, "Uno");
        Assert.AreEqual("dotnet build -v q -f net10.0-desktop", ProjectBuilder.DefaultCommand("Uno", "build"));
        Assert.IsNull(ProjectBuilder.DefaultCommand("Uno", "test"));
        Assert.AreEqual("Acme.Uno", Project().OutputFolderOf("Uno"));
        Assert.AreEqual("mobile", Project(("OutputUno", "mobile")).OutputFolderOf("Uno"));
        Assert.AreEqual(5195, Project().DevPort("Uno"));
        Assert.AreEqual(8000, Project(("DevPort", "8000")).DevPort("Uno"));
        Assert.AreEqual("Acme.App", UnoNames.Namespace(Project()));
        Assert.AreEqual("Acme.App.Models", UnoNames.ModelsNamespace(Project()));
        Assert.AreEqual("Acme.App.Services", UnoNames.ServicesNamespace(Project()));
        Assert.AreEqual("echo hi", ProjectBuilder.CommandFor(Project(("BuildUno", "echo hi")), "Uno", "build"));
        Assert.IsTrue(KeyType.StackHandlesEveryKey("Uno"));
    }

    [TestMethod]
    public void The_essentials_menu_lists_the_uno_groups()
    {
        Assert.AreEqual("Uno", EssentialsCatalog.FindStack("uno"));
        var names = EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Uno").Select(g => g.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "App", "Auth", "Controls", "Git", "OData", "Project", "Support" }, names);
    }

    [TestMethod]
    public void Every_uno_template_belongs_to_the_uno_stack_only()
    {
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory).Where(t => t.Name.StartsWith("UNO_", StringComparison.Ordinal)).ToList();
        Assert.IsGreaterThanOrEqualTo(9, templates.Count);
        foreach (var template in templates)
            CollectionAssert.AreEqual(new[] { "Uno" }, template.Config.Stacks.ToArray(), template.Name);
    }

    // ------------------------------------------------------------------ a generated project

    private static string CreateDatabase(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "codegen_uno_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Region (RegionId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
            """
            CREATE TABLE Customer (
                CustomerId INTEGER PRIMARY KEY, AccountNumber VARCHAR(20) NOT NULL, Name VARCHAR(50) NOT NULL, RegionId INTEGER NOT NULL REFERENCES Region (RegionId),
                CreditLimit DECIMAL(10,2) NULL, IsTaxable BIT NOT NULL DEFAULT 0, Type VARCHAR(10) NOT NULL CHECK (Type IN ('a', 'b')), AddedOn DATETIME NOT NULL, Notes VARCHAR(500) NULL)
            """,
            "CREATE TABLE CustomerNote (CustomerId INTEGER NOT NULL REFERENCES Customer (CustomerId), NoteNumber INTEGER NOT NULL, Body VARCHAR(50) NOT NULL, PRIMARY KEY (CustomerId, NoteNumber))"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    [TestMethod]
    public async Task A_sqlite_file_generates_an_uno_app_with_models_clients_pages_dialogs_the_menu_and_essentials()
    {
        string database = CreateDatabase(out _);
        string output = Path.Combine(Path.GetTempPath(), "codegen_uno_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = ProjectSettings.FromValues(new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Uno", ["Screens"] = "Customer,Region", ["ApiPort"] = "6001", ["Dashboard"] = "false"
            });
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));

            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = project, Stacks = ["Uno"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });

            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            foreach (string expected in new[]
            {
                "Shop.Uno/Shop.Uno.csproj", "Shop.Uno/global.json", "Shop.Uno/Directory.Build.props", "Shop.Uno/GlobalUsings.cs", "Shop.Uno/app.manifest", "Shop.Uno/Package.appxmanifest",
                "Shop.Uno/Platforms/Desktop/Program.cs", "Shop.Uno/Platforms/WebAssembly/Program.cs", "Shop.Uno/Platforms/WebAssembly/WasmScripts/AppManifest.js", "Shop.Uno/Properties/launchSettings.json",
                "Shop.Uno/App.xaml", "Shop.Uno/App.xaml.cs", "Shop.Uno/MainPage.xaml", "Shop.Uno/MainPage.xaml.cs", "Shop.Uno/MainPage.Screens.cs", "Shop.Uno/.gitignore",
                "Shop.Uno/Services/ApiSupport.cs", "Shop.Uno/Services/ApiClients.cs", "Shop.Uno/Services/CustomerClient.cs", "Shop.Uno/Services/RegionClient.cs",
                "Shop.Uno/Models/Customer.cs", "Shop.Uno/Models/Region.cs",
                "Shop.Uno/Views/CustomerListPage.xaml", "Shop.Uno/Views/CustomerListPage.xaml.cs", "Shop.Uno/ViewModels/CustomerListViewModel.cs", "Shop.Uno/Views/PaginationBar.xaml", "Shop.Uno/ViewModels/GridSortStore.cs",
                "Shop.Uno/Views/CustomerDetailMasterDialog.xaml", "Shop.Uno/Views/CustomerDetailMasterDialog.xaml.cs", "Shop.Uno/ViewModels/CustomerDetailMasterViewModel.cs",
                "Shop.Uno/Views/RegionListPage.xaml", "Shop.Uno/Views/RegionDetailMasterDialog.xaml"
            })
                CollectionAssert.Contains(files, expected);
            Assert.IsFalse(files.Any(f => f.Contains("CustomerNote")), "a composite-key table gets no model, client or page");

            string Read(string relative) => File.ReadAllText(Path.Combine(output, relative)).Replace("\r\n", "\n");

            string model = Read("Shop.Uno/Models/Customer.cs");
            Expect.Contains(model, "namespace Shop.App.Models;");
            Expect.Contains(model, "public int CustomerId { get; set; }");
            Expect.Contains(model, "public string AccountNumber { get; set; } = \"\";");
            Expect.Contains(model, "public decimal? CreditLimit { get; set; }");
            Expect.Contains(model, "public DateTime AddedOn { get; set; }");
            Expect.Contains(model, "public string? Notes { get; set; }");

            string client = Read("Shop.Uno/Services/CustomerClient.cs");
            Expect.Contains(client, "namespace Shop.App.Services;");
            Expect.Contains(client, "public class CustomerClient(HttpClient http)");
            Expect.Contains(client, "private const string Url = \"/api/customers\";");
            Expect.Contains(client, "public async Task<PagedResult<Customer>> GetPageAsync(");
            Expect.Contains(client, "public async Task<List<CustomerLookup>> GetLookupAsync(");

            string clients = Read("Shop.Uno/Services/ApiClients.cs");
            Expect.Contains(clients, "public sealed class ApiClients(HttpClient http)");
            Expect.Contains(clients, "public CustomerClient Customer => _customer ??= new(http);");
            Expect.Contains(clients, "public RegionClient Region => _region ??= new(http);");
            Expect.DoesNotContain(clients, "CustomerNote");

            string screens = Read("Shop.Uno/MainPage.Screens.cs");
            Expect.Contains(screens, "private const string FirstScreen = \"Customer\";");
            Expect.Contains(screens, "\"Customer\" => new CustomerListPage(_api),");
            Assert.IsLessThan(screens.IndexOf("\"Region\" => ", StringComparison.Ordinal), screens.IndexOf("\"Customer\" => ", StringComparison.Ordinal), "the Screens setting orders the menu");

            string page = Read("Shop.Uno/Views/CustomerListPage.xaml.cs");
            Expect.Contains(page, "public CustomerListPage(ApiClients api)");
            Expect.Contains(page, "new CustomerDetailMasterDialog(_api, entity)");
            string listModel = Read("Shop.Uno/ViewModels/CustomerListViewModel.cs");
            Expect.Contains(listModel, "await Client.GetPageAsync(PageNumber, PageSize, filters, _sortColumn, _sortDescending)");
            Expect.Contains(listModel, "(await _api.Region.GetLookupAsync()).ToDictionary(");
            Expect.Contains(listModel, "catch (ApiException ex)");

            string dialog = Read("Shop.Uno/Views/CustomerDetailMasterDialog.xaml.cs");
            Expect.Contains(dialog, "public CustomerDetailMasterDialog(ApiClients api, Customer? editing = null)");
            string detailModel = Read("Shop.Uno/ViewModels/CustomerDetailMasterViewModel.cs");
            Expect.Contains(detailModel, "await _api.Customer.CreateAsync(entity);");
            Expect.Contains(detailModel, "await _api.Customer.UpdateAsync(entity.CustomerId, entity);");
            Expect.Contains(detailModel, "_api.Region.GetLookupAsync()");
            Expect.Contains(Read("Shop.Uno/Views/CustomerDetailMasterDialog.xaml"), "<ContentDialog");

            // the data layer is the API: nothing reaches for an EF context
            foreach (string relative in files.Where(f => f.StartsWith("Shop.Uno/") && (f.EndsWith(".cs") || f.EndsWith(".csproj"))))
            {
                string text = Read(relative);
                Expect.DoesNotContain(text, "Microsoft.EntityFrameworkCore");
                Expect.DoesNotContain(text, "DbContext");
            }

            string projectFile = Read("Shop.Uno/Shop.Uno.csproj");
            Expect.Contains(projectFile, "<Project Sdk=\"Uno.Sdk\">");
            Expect.Contains(projectFile, "net10.0-desktop;net10.0-browserwasm;net10.0-windows10.0.26100");
            Expect.Contains(projectFile, "<RootNamespace>Shop.App</RootNamespace>");
            Expect.Contains(Read("Shop.Uno/MainPage.xaml.cs"), "?? \"http://localhost:6001\"");
            Expect.Contains(Read("Shop.Uno/Properties/launchSettings.json"), "http://localhost:5195");
            Expect.Contains(Read("Shop.Uno/Services/ApiSupport.cs"), "public class ApiException(int status, string message)");
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task A_table_with_a_composite_key_is_refused_by_every_per_table_template_with_the_reason()
    {
        string database = CreateDatabase(out _);
        try
        {
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var table = await provider.BuildTableModelAsync("main", "CustomerNote");
            var project = Project(("DatabaseProvider", "Sqlite"));
            StringAssert.Contains(UnoNames.Refusal(table, project)!, "a Uno client");
            foreach (string template in new[] { "UNO_Model_v1.tt", "UNO_Client_v1.tt", "UNO_MasterScreen_v1.tt", "UNO_DetailScreen_v1.tt" })
            {
                var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
                Assert.IsFalse(result.Success, template);
                StringAssert.Contains(string.Join(" ", result.Errors), "no API of its own");
            }
        }
        finally
        {
            File.Delete(database);
        }
    }

    [TestMethod]
    [DataRow("API_EssentialProgram_v1.tt", "appsettings.json")]
    [DataRow("WinUI3_EssentialProject_v1.tt", "appsettings.json")]
    public async Task A_windows_path_in_the_connection_string_is_escaped_so_appsettings_stays_valid_json(string template, string file)
    {
        const string path = @"C:\data\shop ""v2""\shop.db";
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Project(("DatabaseProvider", "Sqlite"), ("DatabaseName", path)));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string json = GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith(file)).Content;
        string read = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("ConnectionStrings").GetProperty("DbConnectionString").GetString()!;
        Assert.AreEqual("Data Source=" + path, read);
    }

    [TestMethod]
    public async Task The_api_allows_the_uno_origin_and_says_so_in_its_cors_note()
    {
        async Task<string> Program(params (string Key, string Value)[] values)
        {
            var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(values));
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            return GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith("Program.cs")).Content.Replace("\r\n", "\n");
        }

        string unoOnly = await Program(("Stacks", "Api,Uno"));
        Expect.Contains(unoOnly, ".WithOrigins(\"http://localhost:5195\")");
        Expect.Contains(unoOnly, "// The Uno app calls this API from its own origin, which is allowed below.");

        string both = await Program(("Stacks", "Api,React,Uno"));
        Expect.Contains(both, "\"http://localhost:5173\", \"http://localhost:5195\"");
        Expect.Contains(both, "The Uno app calls this API directly, so its origin is allowed below.");

        string twoDirect = await Program(("Stacks", "Api,Blazor,Uno"));
        Expect.Contains(twoDirect, "The Blazor app and Uno app calls this API from its own origin");
    }
}
