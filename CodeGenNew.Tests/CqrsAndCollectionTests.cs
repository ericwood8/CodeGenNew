using System.Text.Json;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> CS_CqrsHandlers (commands, queries and handlers without a package) and the Postman and Bruno collections (MD_Postman, MD_Bruno), generated from a SQLite file. The handlers were
/// compiled and run against the PostgreSQL sample by hand (Docs/TemplateNotes/CS_CqrsHandlers_v1.md). </summary>
[TestClass]
public class CqrsAndCollectionTests
{
    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_cqrs_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Region (RegionId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
            "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY, AccountNumber VARCHAR(20) NOT NULL, Name VARCHAR(50) NOT NULL, RegionId INTEGER NOT NULL REFERENCES Region (RegionId), Notes VARCHAR(500) NULL)",
            "CREATE TABLE CustomerNote (CustomerId INTEGER NOT NULL REFERENCES Customer (CustomerId), NoteNumber INTEGER NOT NULL, Body VARCHAR(50) NOT NULL, PRIMARY KEY (CustomerId, NoteNumber))"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static async Task<Dictionary<string, string>> Generate(string only, params (string Key, string Value)[] extra)
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_cqrs_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api", ["PlanAlso"] = only,
                ["ContextName"] = "ShopContext", ["ApiPort"] = "6001"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await ProjectGenerator.RunAsync(provider, Repo.TemplatesDirectory, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = ["Api"], OutputDirectory = output, DatabaseName = database, Schema = "main", OnlyTemplates = only.Split(',')
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task The_cqrs_template_writes_commands_queries_and_handlers_for_the_tables_with_an_int_key()
    {
        var files = await Generate("CS_CqrsHandlers");

        string[] expected = ["Shop.Api/Application/Abstractions.cs", "Shop.Api/Application/CustomerHandlers.cs", "Shop.Api/Application/RegionHandlers.cs", "Shop.Api/Application/ApplicationRegistration.cs"];
        foreach (string path in expected)
            CollectionAssert.Contains(files.Keys.ToArray(), path);
        Assert.IsFalse(files.Keys.Any(f => f.Contains("CustomerNote")), "a composite-key table has no handlers");

        string handlers = files["Shop.Api/Application/CustomerHandlers.cs"];
        Expect.Contains(handlers, "namespace Shop.App.Application;");
        Expect.Contains(handlers, "public record CreateCustomerCommand(Customer Row) : ICommand<Customer>;");
        Expect.Contains(handlers, "public record UpdateCustomerCommand(int Id, Customer Row) : ICommand<Customer?>;");
        Expect.Contains(handlers, "public record DeleteCustomerCommand(int Id) : ICommand<DeleteOutcome>;");
        Expect.Contains(handlers, "public record GetCustomerListQuery(int PageNumber = 1, int PageSize = 20, string? SortBy = null, bool Descending = false, string? AccountNumber = null");
        Expect.Contains(handlers, "private readonly CustomerRepo repo = new(context);");
        Expect.Contains(handlers, "(\"accountnumber\", false) => rows.OrderBy(x => x.AccountNumber),");
        Expect.Contains(handlers, "rows = rows.Where(x => x.Name != null && x.Name.Contains(query.Name));");
        Expect.Contains(handlers, "int pageSize = Math.Clamp(query.PageSize, 1, 200);");
        Expect.DoesNotContain(handlers, "FluentValidation");

        string registration = files["Shop.Api/Application/ApplicationRegistration.cs"];
        Expect.Contains(registration, "services.AddScoped<ICommandHandler<CreateCustomerCommand, Customer>, CreateCustomerHandler>();");
        Expect.Contains(registration, "services.AddScoped<IQueryHandler<GetRegionListQuery, PagedList<Region>>, GetRegionListHandler>();");
        Expect.Contains(files["Shop.Api/Application/Abstractions.cs"], "public enum DeleteOutcome");
    }

    [TestMethod]
    public async Task The_cqrs_create_and_update_handlers_run_the_validator_when_the_project_validates()
    {
        var plain = await Generate("CS_CqrsHandlers");
        var validating = await Generate("CS_CqrsHandlers", ("ApiValidation", "true"), ("ApplicationNamespace", "Shop.Core.Application"));

        string handlers = validating["Shop.Api/Application/CustomerHandlers.cs"];
        Expect.Contains(handlers, "using FluentValidation;");
        Expect.Contains(handlers, "namespace Shop.Core.Application;");
        Expect.Contains(handlers, "public class CreateCustomerHandler(ShopContext context, IValidator<Customer> validator)");
        Expect.Contains(handlers, "await validator.ValidateAndThrowAsync(command.Row, cancellationToken);");
        Expect.DoesNotContain(plain["Shop.Api/Application/CustomerHandlers.cs"], "IValidator");
    }

    [TestMethod]
    public async Task The_postman_collection_is_valid_json_with_a_folder_per_table_and_the_requests_of_the_http_file()
    {
        var files = await Generate("MD_Postman");
        using var document = JsonDocument.Parse(files["Shop.Api/collections/Shop.postman_collection.json"]);
        var root = document.RootElement;

        Assert.AreEqual("Shop API", root.GetProperty("info").GetProperty("name").GetString());
        StringAssert.Contains(root.GetProperty("info").GetProperty("schema").GetString()!, "collection/v2.1.0");
        var folders = root.GetProperty("item").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
        CollectionAssert.AreEqual(new[] { "Customers", "Regions" }, folders);

        var customer = root.GetProperty("item")[0].GetProperty("item").EnumerateArray().ToDictionary(i => i.GetProperty("name").GetString()!);
        CollectionAssert.AreEquivalent(new[] { "All Customers", "One customer by its id", "Search Customers", "Add a customer", "Change a customer", "Remove a customer", "Copy a customer" }, customer.Keys.ToArray());
        var add = customer["Add a customer"].GetProperty("request");
        Assert.AreEqual("POST", add.GetProperty("method").GetString());
        using var body = JsonDocument.Parse(add.GetProperty("body").GetProperty("raw").GetString()!);
        StringAssert.StartsWith(body.RootElement.GetProperty("accountNumber").GetString()!, "Sample account");
        Assert.AreEqual("{{baseUrl}}/api/customers", add.GetProperty("url").GetProperty("raw").GetString());
        var change = customer["Change a customer"].GetProperty("request").GetProperty("body").GetProperty("raw").GetString()!;
        Expect.Contains(change, "\"customerId\": {{id}}");

        var query = customer["Search Customers"].GetProperty("request").GetProperty("url").GetProperty("query").EnumerateArray().Select(q => q.GetProperty("key").GetString()).ToList();
        CollectionAssert.Contains(query, "pageNumber");
        CollectionAssert.Contains(query, "sortBy");

        var variables = root.GetProperty("variable").EnumerateArray().ToDictionary(v => v.GetProperty("key").GetString()!, v => v.GetProperty("value").GetString());
        Assert.AreEqual("http://localhost:6001", variables["baseUrl"]);
        Assert.AreEqual("1", variables["id"]);
    }

    [TestMethod]
    public async Task The_bruno_collection_has_the_collection_file_an_environment_and_a_file_per_request()
    {
        var files = await Generate("MD_Bruno");
        string root = "Shop.Api/collections/bruno/";

        using var collection = JsonDocument.Parse(files[root + "bruno.json"]);
        Assert.AreEqual("collection", collection.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("Shop API", collection.RootElement.GetProperty("name").GetString());
        Expect.Contains(files[root + "environments/Local.bru"], "baseUrl: http://localhost:6001");
        Expect.Contains(files[root + "environments/Local.bru"], "id: 1");

        string add = files[root + "Customer/Add a customer.bru"];
        Expect.Contains(add, "name: Add a customer");
        Expect.Contains(add, "post {\n  url: {{baseUrl}}/api/customers\n  body: json\n  auth: none\n}");
        Expect.Contains(add, "body:json {\n  {\n    \"customerId\": 0,");
        Expect.Contains(files[root + "Customer/Change a customer.bru"], "put {\n  url: {{baseUrl}}/api/customers/{{id}}");
        Expect.Contains(files[root + "Customer/Remove a customer.bru"], "delete {\n  url: {{baseUrl}}/api/customers/{{id}}\n  body: none");
        Expect.Contains(files[root + "Customer/Search Customers.bru"], "/api/customers/search?accountNumber=&name=&pageNumber=1&pageSize=20&sortBy=accountNumber&sortDir=asc");
        Assert.IsFalse(files.Keys.Any(f => f.Contains("CustomerNote")), "a composite-key table has no requests");
        Assert.AreEqual(7, files.Keys.Count(f => f.StartsWith(root + "Customer/")));
    }

    [TestMethod]
    public void A_request_name_becomes_a_file_name_without_the_characters_a_file_system_refuses() =>
        Assert.AreEqual("A B C", ApiRequests.FileName("A/B:C").Replace("  ", " "));
}
