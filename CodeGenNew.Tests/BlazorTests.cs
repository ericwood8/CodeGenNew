using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The Blazor stack: its settings, which tables get a client, the text of every template and the files of a project generated from a SQLite file. Building the generated app
/// needs the Blazor packages from NuGet, so that check was made by hand against the PostgreSQL sample (Docs/TemplateNotes/BLZ_Screens_v1.md), not in this suite. </summary>
[TestClass]
public class BlazorTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    [TestMethod]
    public void The_blazor_stack_is_known_and_has_a_build_command_an_output_folder_and_a_dev_port()
    {
        CollectionAssert.Contains(ProjectPlan.KnownStacks.ToArray(), "Blazor");
        CollectionAssert.Contains(ProjectSettingChoices.Stacks, "Blazor");
        Assert.AreEqual("dotnet build -v q", ProjectBuilder.DefaultCommand("Blazor", "build"));
        Assert.IsNull(ProjectBuilder.DefaultCommand("Blazor", "test"));
        Assert.AreEqual("Acme.Blazor", Project().OutputFolderOf("Blazor"));
        Assert.AreEqual("web", Project(("OutputBlazor", "web")).OutputFolderOf("Blazor"));
        Assert.AreEqual(5190, Project().DevPort("Blazor"));
        Assert.AreEqual(5173, Project().DevPort("React"));
        Assert.AreEqual(4200, Project().DevPort("Angular"));
        Assert.AreEqual(8000, Project(("DevPort", "8000")).DevPort("Blazor"));
        Assert.AreEqual("Acme.Blazor", BlazorNames.Namespace(Project()));
        Assert.AreEqual("echo hi", ProjectBuilder.CommandFor(Project(("BuildBlazor", "echo hi")), "Blazor", "build"));
    }

    [TestMethod]
    public void The_essentials_menu_lists_the_blazor_groups()
    {
        Assert.AreEqual("Blazor", EssentialsCatalog.FindStack("blazor"));
        var names = EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Blazor").Select(g => g.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "Git", "Layout", "Project", "Support" }, names);
    }

    [TestMethod]
    [DataRow("SalesInvoice", "SalesInvoice")]
    [DataRow("E_Customer", "Customer")]
    [DataRow("SY_Role", "Role")]
    public void A_table_is_named_without_its_prefix(string table, string expected) => Assert.AreEqual(expected, BlazorNames.TypeName(table));

    [TestMethod]
    public void The_api_route_is_the_plural_of_the_stem() => Assert.AreEqual("/api/customers", BlazorNames.ApiRoute("Customer"));

    // ------------------------------------------------------------------ a generated project

    private static string CreateDatabase(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "codegen_blz_" + Guid.NewGuid().ToString("N") + ".db");
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
    public async Task A_sqlite_file_generates_a_blazor_app_with_models_clients_pages_the_menu_and_essentials()
    {
        string database = CreateDatabase(out _);
        string output = Path.Combine(Path.GetTempPath(), "codegen_blz_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = ProjectSettings.FromValues(new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Blazor", ["Screens"] = "Customer,Region", ["ApiPort"] = "6001"
            });
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));

            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = project, Stacks = ["Blazor"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });

            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            foreach (string expected in new[]
            {
                "Shop.Blazor/Shop.Blazor.csproj", "Shop.Blazor/Program.cs", "Shop.Blazor/App.razor", "Shop.Blazor/_Imports.razor", "Shop.Blazor/.gitignore",
                "Shop.Blazor/wwwroot/index.html", "Shop.Blazor/wwwroot/appsettings.json", "Shop.Blazor/wwwroot/css/app.css", "Shop.Blazor/Properties/launchSettings.json",
                "Shop.Blazor/Layout/MainLayout.razor", "Shop.Blazor/Layout/NavMenu.razor", "Shop.Blazor/Pages/Home.razor", "Shop.Blazor/Services/ApiSupport.cs", "Shop.Blazor/Services/ApiClients.cs",
                "Shop.Blazor/Models/Customer.cs", "Shop.Blazor/Services/CustomerClient.cs", "Shop.Blazor/Pages/CustomerPage.razor", "Shop.Blazor/Models/Region.cs", "Shop.Blazor/Pages/RegionPage.razor"
            })
                CollectionAssert.Contains(files, expected);
            Assert.IsFalse(files.Any(f => f.Contains("CustomerNote")), "a composite-key table gets no model, client or page");

            string Read(string relative) => File.ReadAllText(Path.Combine(output, relative)).Replace("\r\n", "\n");

            string model = Read("Shop.Blazor/Models/Customer.cs");
            Expect.Contains(model, "namespace Shop.Blazor.Models;");
            Expect.Contains(model, "public int CustomerId { get; set; }");
            Expect.Contains(model, "public string AccountNumber { get; set; } = \"\";");
            Expect.Contains(model, "public decimal? CreditLimit { get; set; }");
            Expect.Contains(model, "public bool IsTaxable { get; set; }");
            Expect.Contains(model, "public DateTime AddedOn { get; set; }");
            Expect.Contains(model, "public string? Notes { get; set; }");

            string client = Read("Shop.Blazor/Services/CustomerClient.cs");
            Expect.Contains(client, "public class CustomerClient(HttpClient http)");
            Expect.Contains(client, "private const string Url = \"/api/customers\";");
            Expect.Contains(client, "public async Task<PagedResult<Customer>> GetPageAsync(");
            Expect.Contains(client, "/search?");
            Expect.Contains(client, "public async Task DeleteAsync(int id)");

            string page = Read("Shop.Blazor/Pages/CustomerPage.razor");
            Expect.Contains(page, "@page \"/customer\"");
            Expect.Contains(page, "@inject CustomerClient Client");
            Expect.Contains(page, "@inject RegionClient lookupRegionClient");
            Expect.Contains(page, "Search by Account Number");
            Expect.Contains(page, "<InputSelect id=\"customer-regionid\" @bind-Value=\"row.RegionId\">");
            Expect.Contains(page, "<InputText id=\"customer-accountnumber\" @bind-Value=\"row.AccountNumber\" maxlength=\"20\" required />");
            Expect.Contains(page, "<InputCheckbox id=\"customer-istaxable\" @bind-Value=\"row.IsTaxable\" />");
            Expect.Contains(page, "<InputDate id=\"customer-addedon\" @bind-Value=\"row.AddedOn\" required />");
            Expect.Contains(page, "<InputTextArea id=\"customer-notes\" @bind-Value=\"row.Notes\" maxlength=\"500\" />");
            Expect.Contains(page, "<option value=\"a\">a</option>");
            Expect.DoesNotContain(page, "@page of");   // @page is a Razor directive: the field is pageNumber

            string menu = Read("Shop.Blazor/Layout/NavMenu.razor");
            Expect.Contains(menu, "<NavLink href=\"customer\">Customer</NavLink>");
            Expect.Contains(menu, "<NavLink href=\"region\">Region</NavLink>");
            Assert.IsLessThan(menu.IndexOf("region", StringComparison.Ordinal), menu.IndexOf("customer", StringComparison.Ordinal), "the Screens setting orders the menu");
            Expect.Contains(Read("Shop.Blazor/Pages/Home.razor"), "NavigateTo(\"customer\", replace: true)");

            string registration = Read("Shop.Blazor/Services/ApiClients.cs");
            Expect.Contains(registration, "services.AddScoped<CustomerClient>();");
            Expect.Contains(registration, "services.AddScoped<RegionClient>();");
            Expect.DoesNotContain(registration, "CustomerNote");

            Expect.Contains(Read("Shop.Blazor/wwwroot/appsettings.json"), "\"ApiBaseUrl\": \"http://localhost:6001\"");
            Expect.Contains(Read("Shop.Blazor/Properties/launchSettings.json"), "http://localhost:5190");
            Expect.Contains(Read("Shop.Blazor/Shop.Blazor.csproj"), "Microsoft.NET.Sdk.BlazorWebAssembly");
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
            Assert.IsNotNull(BlazorNames.Refusal(table, project));
            StringAssert.Contains(BlazorNames.Refusal(table, project)!, "no API of its own");
            foreach (string template in new[] { "BLZ_Model_v1.tt", "BLZ_Client_v1.tt", "BLZ_Page_v1.tt" })
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
    public async Task The_api_allows_the_blazor_origin_and_says_so_in_its_cors_note()
    {
        async Task<string> Program(params (string Key, string Value)[] values)
        {
            var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(values));
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            return GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith("Program.cs")).Content.Replace("\r\n", "\n");
        }

        string blazorOnly = await Program(("Stacks", "Api,Blazor"));
        Expect.Contains(blazorOnly, ".WithOrigins(\"http://localhost:5190\")");
        Expect.Contains(blazorOnly, "// The Blazor app calls this API from its own origin, which is allowed below.");

        string both = await Program(("Stacks", "Api,React,Blazor"));
        Expect.Contains(both, "\"http://localhost:5173\", \"http://localhost:5190\"");
        Expect.Contains(both, "The React dev server proxies /api here (see frontend/vite.config.ts)");
        Expect.Contains(both, "The Blazor app calls this API directly, so its origin is allowed below.");

        string react = await Program(("Stacks", "Api,React"));
        Expect.Contains(react, "// The React dev server proxies /api here (see frontend/vite.config.ts), so CORS only matters if the front end is served elsewhere.");
        Expect.DoesNotContain(react, "Blazor");
    }
}
