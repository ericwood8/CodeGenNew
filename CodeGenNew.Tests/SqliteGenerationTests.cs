using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> A whole project generated from a SQLite file: no routine is written, the EF replacements are, and the project names the SQLite provider. </summary>
[TestClass]
public class SqliteGenerationTests
{
    private static readonly string[] Shop =
    [
        "CREATE TABLE Status (StatusId INTEGER PRIMARY KEY, Description VARCHAR(40) NOT NULL)",
        """
        CREATE TABLE Customer (
            CustomerId INTEGER PRIMARY KEY, AccountNumber VARCHAR(20) NOT NULL UNIQUE, Name VARCHAR(50) NOT NULL,
            StatusId INTEGER NOT NULL REFERENCES Status (StatusId), CreditLimit DECIMAL(10,2) NULL CHECK (CreditLimit >= 0),
            CreateDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, IsPreferred BOOLEAN NOT NULL DEFAULT 1)
        """,
        "CREATE TABLE Tag (TagId INTEGER PRIMARY KEY, Label VARCHAR(30) NOT NULL)",
        "CREATE TABLE CustomerTag (CustomerId INTEGER NOT NULL REFERENCES Customer (CustomerId), TagId INTEGER NOT NULL REFERENCES Tag (TagId), PRIMARY KEY (CustomerId, TagId))"
    ];

    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_sqlite_gen_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in Shop)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    [TestMethod]
    public async Task A_sqlite_file_generates_a_project_with_ef_in_place_of_routines()
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_sqlite_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = ProjectSettings.FromValues(new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api", ["OutputApi"] = "Shop.Api"
            });
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config"));

            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = project, Stacks = ["Api"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });

            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();

            Assert.IsFalse(files.Any(f => f.EndsWith(".sql")), "SQLite has no routines to write: " + string.Join(", ", files.Where(f => f.EndsWith(".sql"))));
            foreach (string expected in new[]
            {
                "Shop.Api/Entities/Customer.cs", "Shop.Api/Entities/CustomerTag.cs", "Shop.Api/Repositories/CustomerRepo.cs", "Shop.Api/Repositories/CustomerSearchQuery.cs",
                "Shop.Api/Apis/CustomerSearchApi.cs", "Shop.Api/Apis/CustomerTagJunctionApi.cs", "Shop.Api/Shop.Api.csproj", "Shop.Api/appsettings.json"
            })
                CollectionAssert.Contains(files, expected);

            string context = File.ReadAllText(Path.Combine(output, "Shop.Api", "Data", "ShopContext.cs"));
            StringAssert.Contains(context, "options.UseSqlite(connectionString);");
            StringAssert.Contains(File.ReadAllText(Path.Combine(output, "Shop.Api", "Shop.Api.csproj")), "Microsoft.EntityFrameworkCore.Sqlite");
            StringAssert.Contains(File.ReadAllText(Path.Combine(output, "Shop.Api", "appsettings.json")), "Data Source=shop.db");
            string registration = File.ReadAllText(Path.Combine(output, "Shop.Api", "Apis", "ApiRegistration.cs"));
            StringAssert.Contains(registration, "new CustomerTagJunctionApi<CustomerTag>().Register(app);");
            Assert.IsFalse(File.ReadAllText(Path.Combine(output, "Shop.Api", "Repositories", "CustomerRepo.cs")).Contains("FromSqlRaw"));
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }
}
