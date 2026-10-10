using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The small findings of the comparison with a real server (item 105): a table in NoApiTables is left out of the front end's plan instead of failing the run, the container's API address variable,
/// and seed data for the tables that are not enums (CS_Seed, CS_SeedData, CSharpLiteral). </summary>
[TestClass]
public class SmallFindingsTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static string DatabaseFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_small_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Region (RegionId INTEGER PRIMARY KEY AUTOINCREMENT, Title VARCHAR(50) NOT NULL, Code VARCHAR(5) NOT NULL, IsActive BIT NOT NULL DEFAULT 1, Note VARCHAR(50) NULL)",
            "CREATE TABLE Country (Alpha3Code VARCHAR(3) PRIMARY KEY, Name VARCHAR(50) NOT NULL, Population INTEGER NULL, Founded DATE NULL)",
            "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY AUTOINCREMENT, RegionId INTEGER NOT NULL REFERENCES Region (RegionId), Name VARCHAR(50) NOT NULL, Email VARCHAR(80) NULL)",
            "INSERT INTO Region (Title, Code, Note) VALUES ('North ''up''', 'N', NULL), ('South', 'S', 'down\\under')",
            "INSERT INTO Country (Alpha3Code, Name, Population, Founded) VALUES ('USA', 'United States', 331000000, '1776-07-04'), ('CAN', 'Canada', NULL, NULL)"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static async Task<(GenerateReport Report, Dictionary<string, string> Files)> Generate(string[] stacks, string? only, params (string Key, string Value)[] extra)
    {
        string database = DatabaseFile();
        string output = Path.Combine(Path.GetTempPath(), "codegen_small_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = string.Join(",", stacks), ["ContextName"] = "ShopContext",
                ["ContextNamespace"] = "Shop.Data", ["EntityNamespace"] = "Shop.Entities", ["AccessMode"] = "Ef"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = stacks, OutputDirectory = output, DatabaseName = database, Schema = "main", OnlyTemplates = only?.Split(',')
            });
            var files = Directory.Exists(output)
                ? Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"))
                : [];
            return (report, files);
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [TestMethod]
    public async Task A_table_in_NoApiTables_is_left_out_of_the_front_end_plan_without_an_error()
    {
        var (report, files) = await Generate(["Angular", "React"], "TS_Model,TS_Service,TSX_Api", ("NoApiTables", "Region"));
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        Assert.IsEmpty(report.Errors);
        Assert.IsFalse(files.Keys.Any(f => f.Contains("region", StringComparison.OrdinalIgnoreCase)), string.Join(", ", files.Keys));
        Assert.IsTrue(files.Keys.Any(f => f.EndsWith("customer.service.ts")), string.Join(", ", files.Keys));
        Assert.IsTrue(files.Keys.Any(f => f.EndsWith("customer.ts")), string.Join(", ", files.Keys));

        var (_, all) = await Generate(["Angular"], "TS_Model,TS_Service");
        Assert.IsTrue(all.Keys.Any(f => f.EndsWith("region.service.ts")), "without the setting the table has its model and service");
    }

    [TestMethod]
    public void The_front_end_model_service_and_api_module_run_for_the_client_tables()
    {
        foreach (string template in new[] { "TS_Model_v1.tt", "TS_Service_v1.tt", "TSX_Api_v1.tt" })
            StringAssert.Contains(File.ReadAllText(Repo.Template(template + ".config")), "PlanTables=Client", template);
    }

    [TestMethod]
    public async Task The_container_reads_the_API_from_the_variable_the_project_names()
    {
        foreach (string template in new[] { "TS_EssentialDocker_v1.tt", "TSX_EssentialDocker_v1.tt" })
        {
            var result = await Repo.Cache.RunAsync(Repo.Template(template), Project(("DockerUpstreamVariable", "services__shopapi__https__0")));
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            string text = result.GeneratedText!.ReplaceLineEndings("\n");
            Expect.Contains(text, "ENV services__shopapi__https__0=http://api:8080");
            Expect.Contains(text, "proxy_pass ${services__shopapi__https__0};");
            Expect.DoesNotContain(text, "upstream api_backend");
            Expect.DoesNotContain(text, "API_UPSTREAM");

            var plain = await Repo.Cache.RunAsync(Repo.Template(template), Project());
            string normal = plain.GeneratedText!.ReplaceLineEndings("\n");
            Expect.Contains(normal, "ENV API_UPSTREAM=api:8080");
            Expect.Contains(normal, "upstream api_backend {\n    server ${API_UPSTREAM};\n    keepalive 16;\n}");
            Expect.Contains(normal, "proxy_pass http://api_backend;");

            var named = await Repo.Cache.RunAsync(Repo.Template(template), Project(("DockerUpstreamVariable", "api_upstream")));
            Expect.Contains(named.GeneratedText!, "proxy_pass ${api_upstream};");
        }
        Assert.AreEqual("API_UPSTREAM", Project().DockerUpstreamVariable);
        Assert.IsFalse(Project().DockerUpstreamIsAddress);
        Assert.IsFalse(Project(("DockerUpstreamVariable", "")).DockerUpstreamIsAddress);
        Assert.AreEqual("Web", ProjectSettingGroups.TabOf("DockerUpstreamVariable"));
    }

    private static ColumnModel Column(SqlDbType type, bool boolean = false, bool text = false) =>
        Sample.Column("C", type, characters: text ? 50 : null);

    [TestMethod]
    public void A_value_becomes_a_C_sharp_literal_of_the_columns_type()
    {
        Assert.AreEqual("5", CSharpLiteral.Format(Column(SqlDbType.Int), 5L), "a SQLite integer is read as a long");
        Assert.AreEqual("5L", CSharpLiteral.Format(Column(SqlDbType.BigInt), 5));
        Assert.AreEqual("200", CSharpLiteral.Format(Column(SqlDbType.TinyInt), 200L));
        Assert.AreEqual("true", CSharpLiteral.Format(Column(SqlDbType.Bit), 1L));
        Assert.AreEqual("false", CSharpLiteral.Format(Column(SqlDbType.Bit), false));
        Assert.AreEqual("1.5m", CSharpLiteral.Format(Column(SqlDbType.Decimal), 1.5));
        Assert.AreEqual("2.25d", CSharpLiteral.Format(Column(SqlDbType.Float), 2.25));
        Assert.AreEqual("new DateTime(1776, 7, 4)", CSharpLiteral.Format(Column(SqlDbType.Date), "1776-07-04"));
        Assert.AreEqual("new DateTime(2026, 10, 10, 13, 5, 9)", CSharpLiteral.Format(Column(SqlDbType.DateTime), new DateTime(2026, 10, 10, 13, 5, 9)));
        Assert.AreEqual("new DateTime(2026, 10, 10, 13, 5, 9).AddTicks(1234)", CSharpLiteral.Format(Column(SqlDbType.DateTime2), new DateTime(2026, 10, 10, 13, 5, 9).AddTicks(1234)));
        Assert.AreEqual("Guid.Parse(\"0f8fad5b-d9cb-469f-a165-70867728950e\")", CSharpLiteral.Format(Column(SqlDbType.UniqueIdentifier), "0F8FAD5B-D9CB-469F-A165-70867728950E"));
        Assert.AreEqual("\"say \\\"hi\\\"\\r\\n\\t\\\\ \\u0001\"", CSharpLiteral.Format(Column(SqlDbType.NVarChar, text: true), "say \"hi\"\r\n\t\\ \u0001"));
    }

    [TestMethod]
    public void A_value_with_no_literal_is_left_out()
    {
        Assert.IsNull(CSharpLiteral.Format(Column(SqlDbType.Int), null));
        Assert.IsNull(CSharpLiteral.Format(Column(SqlDbType.Int), DBNull.Value));
        Assert.IsNull(CSharpLiteral.Format(Column(SqlDbType.Int), "not a number"));
        Assert.IsNull(CSharpLiteral.Format(Column(SqlDbType.VarBinary), new byte[] { 1, 2 }));
        Assert.IsNull(CSharpLiteral.Format(Column(SqlDbType.Float), double.NaN));
    }

    [TestMethod]
    public async Task SeedTables_writes_a_seed_class_per_table_and_the_class_that_runs_them()
    {
        var (report, files) = await Generate(["Api"], "CS_Seed,CS_SeedData", ("SeedTables", "Region,Country"));
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        CollectionAssert.AreEquivalent(new[] { "Shop.Api/Seeding/RegionSeed.cs", "Shop.Api/Seeding/CountrySeed.cs", "Shop.Api/Seeding/SeedData.cs" }, files.Keys.Where(f => !f.StartsWith(".")).ToArray());

        string region = files["Shop.Api/Seeding/RegionSeed.cs"];
        Expect.Contains(region, "namespace Shop.Data;");
        Expect.Contains(region, "using Shop.Entities;");
        Expect.Contains(region, "/// <summary> The 2 rows Region held when CS_Seed wrote this.");
        Expect.Contains(region, "public static class RegionSeed");
        // the identity key is left out, text is escaped, a NULL is left out and a bit is a bool
        Expect.Contains(region, "new Region { Title = \"North 'up'\", Code = \"N\", IsActive = true },");
        Expect.Contains(region, "new Region { Title = \"South\", Code = \"S\", IsActive = true, Note = \"down\\\\under\" },");
        Expect.DoesNotContain(region, "RegionId =");
        Expect.DoesNotContain(region, "numbered again");
        Expect.Contains(region, "if (await context.Set<Region>().AnyAsync(cancellationToken))\n            return;");
        Expect.Contains(region, "public static async Task SeedAsync(ShopContext context, CancellationToken cancellationToken = default)");

        // a text key is written, in key order, and a date and an int come in their own types
        string country = files["Shop.Api/Seeding/CountrySeed.cs"];
        Assert.IsLessThan(country.IndexOf("Alpha3Code = \"USA\"", StringComparison.Ordinal), country.IndexOf("Alpha3Code = \"CAN\"", StringComparison.Ordinal), "CAN before USA");
        Expect.Contains(country, "new Country { Alpha3Code = \"USA\", Name = \"United States\", Population = 331000000, Founded = new DateTime(1776, 7, 4) },");
        Expect.Contains(country, "new Country { Alpha3Code = \"CAN\", Name = \"Canada\" },");

        string data = files["Shop.Api/Seeding/SeedData.cs"];
        Assert.IsLessThan(data.IndexOf("CountrySeed.SeedAsync", StringComparison.Ordinal), data.IndexOf("RegionSeed.SeedAsync", StringComparison.Ordinal), "the order of the setting");
        Expect.Contains(data, "public static async Task SeedGeneratedDataAsync(this WebApplication app)");
        Expect.Contains(data, "GetRequiredService<ShopContext>()");
    }

    [TestMethod]
    public async Task A_seed_says_when_an_identity_key_is_numbered_again_and_refuses_what_it_cannot_seed()
    {
        var region = Sample.Table("Region",
        [
            Sample.Column("RegionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2)
        ], rows: [[3, "North"], [7, "South"]]);
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Seed_v1.tt"), region, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(text, "The keys the rows had (3, 7) are numbered again from 1 in this order");
        Expect.Contains(text, "namespace Acme.App.Data;");

        var empty = Sample.Table("Region", [Sample.Column("RegionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)], rows: []);
        var emptyResult = await Repo.Cache.RunAsync(Repo.Template("CS_Seed_v1.tt"), empty, Project());
        Assert.IsFalse(emptyResult.Success);
        StringAssert.Contains(string.Join(" ", emptyResult.Errors), "the table is empty");

        var notRead = Sample.Table("Region", [Sample.Column("RegionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)]);
        var notReadResult = await Repo.Cache.RunAsync(Repo.Template("CS_Seed_v1.tt"), notRead, Project());
        Assert.IsFalse(notReadResult.Success);
        StringAssert.Contains(string.Join(" ", notReadResult.Errors), "its rows were not read");

        var many = Sample.Table("Region", [Sample.Column("RegionId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)], rows: Enumerable.Range(1, 1001).Select(i => new object?[] { i }).ToList());
        var manyResult = await Repo.Cache.RunAsync(Repo.Template("CS_Seed_v1.tt"), many, Project());
        Assert.IsFalse(manyResult.Success);
        StringAssert.Contains(string.Join(" ", manyResult.Errors), "at most 1000 rows");
    }

    [TestMethod]
    public void SeedTables_adds_both_templates_to_the_plan_and_names_a_plan_set()
    {
        Assert.IsEmpty(Project().SeedTables);
        CollectionAssert.AreEqual(new[] { "Region", "Country" }, Project(("SeedTables", "Region, Country")).SeedTables);
        CollectionAssert.IsSubsetOf(new[] { "CS_Seed", "CS_SeedData" }, Project(("SeedTables", "Region")).ImpliedPlanTemplates.ToArray());
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToArray(), "CS_Seed");
        Assert.AreEqual("Tables", ProjectSettingGroups.TabOf("SeedTables"));
        Assert.AreEqual(PlanTableSet.Seed, TemplateConfig.Load(Repo.Template("CS_Seed_v1.tt.config")).PlanTables);
        Assert.IsFalse(TemplateConfig.Load(Repo.Template("CS_Seed_v1.tt.config")).InPlan);
    }
}
