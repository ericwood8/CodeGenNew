using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> CS_EfConfiguration (an IEntityTypeConfiguration per table, applied by the context) and API_Csv (CSV export and import endpoints), generated from a SQLite file. Both were compiled and run
/// against the PostgreSQL sample by hand (Docs/TemplateNotes/CS_EfConfiguration_v1.md, API_Csv_v1.md). </summary>
[TestClass]
public class EfConfigurationAndCsvTests
{
    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_efcsv_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY, AccountNumber VARCHAR(20) NOT NULL, Name VARCHAR(50) NOT NULL, CreditLimit DECIMAL(10,2) NULL, IsTaxable BIT NOT NULL DEFAULT 0, AddedOn DATETIME NOT NULL, Notes VARCHAR(500) NULL)",
            "CREATE UNIQUE INDEX IX_Customer_AccountNumber ON Customer (AccountNumber)",
            "CREATE INDEX IX_Customer_Name_AddedOn ON Customer (Name, AddedOn)",
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
        string output = Path.Combine(Path.GetTempPath(), "codegen_efcsv_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api", ["ContextName"] = "ShopContext", ["ContextNamespace"] = "Shop.Data",
                ["EntityNamespace"] = "Shop.Entities"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
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
    public async Task The_ef_flag_writes_a_configuration_per_table_and_the_context_applies_them()
    {
        var files = await Generate("CS_EfConfiguration,CS_DbContext", ("EfConfigurations", "true"));

        string customer = files["Shop.Api/Data/Configurations/CustomerConfiguration.cs"];
        Expect.Contains(customer, "namespace Shop.Data.Configurations;");
        Expect.Contains(customer, "public class CustomerConfiguration : IEntityTypeConfiguration<Customer>");
        Expect.Contains(customer, "builder.ToTable(\"Customer\");");
        Expect.Contains(customer, "builder.HasKey(e => e.CustomerId);");
        Expect.Contains(customer, "builder.Property(e => e.Name).HasMaxLength(50).IsRequired();");
        Expect.Contains(customer, "builder.Property(e => e.CreditLimit).HasPrecision(10, 2);");
        Expect.Contains(customer, "builder.HasIndex(e => e.AccountNumber).HasDatabaseName(\"IX_Customer_AccountNumber\").IsUnique();");
        Expect.Contains(customer, "builder.HasIndex(e => new { e.Name, e.AddedOn }).HasDatabaseName(\"IX_Customer_Name_AddedOn\");");
        Expect.DoesNotContain(customer, "HasIndex(e => e.CustomerId)");

        Expect.Contains(files["Shop.Api/Data/Configurations/CustomerNoteConfiguration.cs"], "builder.HasKey(e => new { e.CustomerId, e.NoteNumber });");

        string context = files["Shop.Api/Data/ShopContext.cs"];
        Expect.Contains(context, "modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShopContext).Assembly);");
        Expect.Contains(context, "partial void OnModelCreatingPartial(ModelBuilder modelBuilder);");
        Expect.DoesNotContain(context, "HasKey");
    }

    [TestMethod]
    public async Task Without_the_ef_flag_the_context_keeps_its_composite_key_and_no_configuration_is_written()
    {
        var files = await Generate("CS_EfConfiguration,CS_DbContext");

        Assert.IsFalse(files.Keys.Any(f => f.Contains("Configuration")), "CS_EfConfiguration is opt in");
        string context = files["Shop.Api/Data/ShopContext.cs"];
        Expect.Contains(context, "modelBuilder.Entity<CustomerNote>().HasKey(e => new { e.CustomerId, e.NoteNumber });");
        Expect.DoesNotContain(context, "ApplyConfigurationsFromAssembly");
    }

    [TestMethod]
    public async Task The_csv_flag_writes_the_support_class_and_one_mapped_route_pair_per_table()
    {
        var files = await Generate("API_Csv", ("ApiCsv", "true"), ("ApiNamespace", "Shop.Apis"));

        string csv = files["Shop.Api/Apis/Csv.cs"];
        Expect.Contains(csv, "namespace Shop.Apis;");
        Expect.Contains(csv, "public static class CsvSupport");
        Expect.Contains(csv, "public const int MaxRows = 10000;");
        Expect.Contains(csv, "MapTable<Customer, ShopContext>(app, \"/api/customers\", \"customers\", [");
        Expect.Contains(csv, "new(\"Name\", true, true, false, 50)");
        Expect.Contains(csv, "new(\"Notes\", true, false, true, 500)");
        Expect.Contains(csv, "app.MapGet(route + \"/export.csv\"");
        Expect.Contains(csv, "app.MapPost(route + \"/import\"");
        Expect.DoesNotContain(csv, "CustomerNote");
        Expect.DoesNotContain(csv, "IValidator");
    }

    [TestMethod]
    public async Task With_validation_the_csv_import_runs_the_validator_of_the_table()
    {
        var files = await Generate("API_Csv", ("ApiCsv", "true"), ("ApiValidation", "true"));

        string csv = files["Shop.Api/Apis/Csv.cs"];
        Expect.Contains(csv, "using FluentValidation;");
        Expect.Contains(csv, "GetService<IValidator<T>>()");
        Expect.Contains(csv, "import.Errors.Add($\"Row {import.Lines[i]}: {failure.ErrorMessage}\");");
    }

    [TestMethod]
    public async Task Program_maps_the_csv_endpoints_only_for_a_project_that_asks()
    {
        async Task<string> Program(params (string Key, string Value)[] values)
        {
            var settings = values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme"));
            var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), ProjectSettings.FromValues(settings));
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            return GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith("Program.cs")).Content;
        }

        Expect.Contains(await Program(("ApiCsv", "true")), "app.MapCsvEndpoints();");
        Expect.DoesNotContain(await Program(), "MapCsvEndpoints");
    }

    [TestMethod]
    public void The_two_flags_imply_their_templates()
    {
        var settings = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Acme", ["ApiCsv"] = "true", ["EfConfigurations"] = "true" });
        CollectionAssert.IsSubsetOf(new[] { "API_Csv", "CS_EfConfiguration" }, settings.ImpliedPlanTemplates.ToArray());
        Assert.IsFalse(ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Acme" }).ImpliedPlanTemplates.Any(t => t is "API_Csv" or "CS_EfConfiguration"));
    }
}
