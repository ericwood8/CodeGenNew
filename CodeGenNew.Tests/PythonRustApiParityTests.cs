using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The junction and CSV routes of the Python and Rust APIs (PY_Junction, PY_Csv, RS_Junction, RS_Csv). </summary>
[TestClass]
public class PythonRustApiParityTests
{
    private static async Task<string> GenerateAsync(string stack, string output, params string[] settings)
    {
        string database = Path.Combine(Path.GetTempPath(), "codegen_parity_" + Guid.NewGuid().ToString("N") + ".db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ConnectionString))
        {
            connection.Open();
            foreach (string statement in new[]
            {
                "CREATE TABLE Person (PersonId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
                "CREATE TABLE Tag (TagId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
                "CREATE TABLE PersonTag (PersonId INTEGER NOT NULL REFERENCES Person (PersonId), TagId INTEGER NOT NULL REFERENCES Tag (TagId), CreateDate DATETIME NOT NULL, PRIMARY KEY (PersonId, TagId))",
                "CREATE TABLE Widget (WidgetId INTEGER PRIMARY KEY, Name VARCHAR(10) NOT NULL, Price DECIMAL(10,2) NOT NULL, InStock BIT NOT NULL, MadeOn DATE NULL)"
            })
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stack
            };
            for (int i = 0; i < settings.Length; i += 2)
                values[settings[i]] = settings[i + 1];
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = [stack], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            return output;
        }
        finally
        {
            try { File.Delete(database); } catch (IOException) { }
        }
    }

    private static string Read(string output, string relative) => File.ReadAllText(Path.Combine(output, relative)).Replace(new string([(char)13, (char)10]), ((char)10).ToString());

    private static string NewOutput() => Path.Combine(Path.GetTempPath(), "codegen_parity_out_" + Guid.NewGuid().ToString("N"));

    private static void Clean(string output)
    {
        try { Directory.Delete(output, true); } catch (IOException) { }
    }

    [TestMethod]
    public async Task Python_gets_junction_routes_and_lists_them_in_the_routers()
    {
        string output = NewOutput();
        try
        {
            await GenerateAsync("Python", output);
            string junction = Read(output, "Shop.Python/app/routes/person_tag_junction.py");
            Expect.Contains(junction, "@router.get(\"/api/persontags/junction/{anchor_id}\", response_model=list[PersonTagJunctionItem])");
            Expect.Contains(junction, "@router.post(\"/api/persontags/junction/link\")");
            Expect.Contains(junction, "@router.post(\"/api/persontags/junction/unlink\")");
            Expect.Contains(junction, "LINKS = table(\"PersonTag\", column(\"PersonId\"), column(\"TagId\"), column(\"CreateDate\"))");
            Expect.Contains(junction, "\"CreateDate\": datetime.now(),");
            Expect.Contains(Read(output, "Shop.Python/app/routes/__init__.py"), "person_tag_junction.router,");
            Assert.IsFalse(File.Exists(Path.Combine(output, "Shop.Python/app/routes/csv_files.py")));
        }
        finally { Clean(output); }
    }

    [TestMethod]
    public async Task Python_gets_csv_routes_first_in_the_routers_when_ApiCsv_is_on()
    {
        string output = NewOutput();
        try
        {
            await GenerateAsync("Python", output, "ApiCsv", "true", "ApiValidation", "true");
            string csv = Read(output, "Shop.Python/app/routes/csv_files.py");
            Expect.Contains(csv, "@router.get(spec.route + \"/export.csv\"");
            Expect.Contains(csv, "@router.post(spec.route + \"/import\"");
            Expect.Contains(csv, "CsvTable(\"/api/widgets\", \"widgets\", Widget, WidgetSchema, \"widget_id\", column_list(");
            Expect.Contains(csv, "(\"Name\", \"name\", \"str\", True, True, False, 10),");
            Expect.Contains(csv, "(\"WidgetId\", \"widget_id\", \"int\", False, False, False, 0),");
            Expect.Contains(csv, "from app.validation.widget import validate as validate_widget");
            string routers = Read(output, "Shop.Python/app/routes/__init__.py");
            Assert.IsTrue(routers.IndexOf("csv_files.router", StringComparison.Ordinal) < routers.IndexOf("person.router", StringComparison.Ordinal), routers);
        }
        finally { Clean(output); }
    }

    [TestMethod]
    public async Task Rust_gets_junction_routes_and_merges_them()
    {
        string output = NewOutput();
        try
        {
            await GenerateAsync("Rust", output);
            string junction = Read(output, "Shop.Rust/src/routes/person_tag_junction.rs");
            Expect.Contains(junction, ".route(\"/api/persontags/junction/{anchor_id}\", get(list_links))");
            Expect.Contains(junction, ".route(\"/api/persontags/junction/link\", post(link))");
            Expect.Contains(junction, "const TARGETS: &str = r#\"SELECT CAST(\"Name\" AS TEXT), \"TagId\" FROM \"Tag\" ORDER BY \"Name\", \"TagId\"\"#;");
            Expect.Contains(junction, "VALUES (?, ?, CURRENT_TIMESTAMP)");
            string routes = Read(output, "Shop.Rust/src/routes/mod.rs");
            Expect.Contains(routes, "pub mod person_tag_junction;");
            Expect.Contains(routes, ".merge(person_tag_junction::router())");
            Assert.IsFalse(File.Exists(Path.Combine(output, "Shop.Rust/src/routes/csv_files.rs")));
        }
        finally { Clean(output); }
    }

    [TestMethod]
    public async Task Rust_gets_csv_routes_and_an_all_or_nothing_insert_when_ApiCsv_is_on()
    {
        string output = NewOutput();
        try
        {
            await GenerateAsync("Rust", output, "ApiCsv", "true", "ApiValidation", "true");
            string csv = Read(output, "Shop.Rust/src/routes/csv_files.rs");
            Expect.Contains(csv, ".route(\"/api/widgets/export.csv\", get(export_widget))");
            Expect.Contains(csv, ".route(\"/api/widgets/import\", post(import_widget))");
            Expect.Contains(csv, "Column { name: \"Name\", json: \"name\", kind: Kind::Text, can_import: true, required: true, nullable: false, max_length: 10 },");
            Expect.Contains(csv, "widget_repo::insert_all(&pool, &rows)");
            Expect.Contains(Read(output, "Shop.Rust/src/repos/widget.rs"), "pub async fn insert_all(pool: &Pool, rows: &[Widget])");
            Expect.Contains(Read(output, "Shop.Rust/src/routes/mod.rs"), ".merge(csv_files::router())");
        }
        finally { Clean(output); }
    }
}
