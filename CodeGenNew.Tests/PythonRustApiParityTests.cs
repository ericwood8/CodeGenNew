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

    private static async Task GenerateGatedAsync(string stack, string output)
    {
        string database = Path.Combine(Path.GetTempPath(), "codegen_gates_" + Guid.NewGuid().ToString("N") + ".db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ConnectionString))
        {
            connection.Open();
            foreach (string statement in new[]
            {
                "CREATE TABLE Employee (EmployeeId INTEGER PRIMARY KEY, FullName VARCHAR(40) NOT NULL, IsManager BIT NOT NULL, IsAdministrator BIT NOT NULL, IsActive BIT NOT NULL, Login VARCHAR(30) NOT NULL, PwdHash VARCHAR(200) NULL)",
                "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL, CustomerStatusId INTEGER NOT NULL, IgnoreBadStatus BIT NOT NULL DEFAULT 0, IgnoreBadStatusBy VARCHAR(30) NULL, IgnoreBadStatusOn DATETIME NULL, Notes VARCHAR(100) NULL)",
                "CREATE TABLE SalesInvoice (SalesInvoiceId INTEGER PRIMARY KEY AUTOINCREMENT, CustomerId INTEGER NOT NULL REFERENCES Customer (CustomerId), InvoiceStatusId INTEGER NOT NULL, Amount DECIMAL(10,2) NOT NULL)"
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
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stack, ["ApiAuth"] = "true", ["UserTable"] = "Employee",
                ["UserNameColumn"] = "Login", ["PasswordHashColumn"] = "PwdHash", ["UserAdminColumn"] = "IsAdministrator", ["UserRoleColumn"] = "IsManager",
                ["ChangeGates"] = "SalesInvoice.InvoiceStatusId; to 2,3,4; when Customer.CustomerStatusId=2; unless Customer.IgnoreBadStatus=1; start 1; message \"Customer is in bad standing.\"",
                ["GuardedColumns"] = "Customer.IgnoreBadStatus; by Employee.IsManager; stampBy IgnoreBadStatusBy; stampOn IgnoreBadStatusOn",
                ["ApiHttp"] = "true"
            };
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = [stack], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        }
        finally
        {
            try { File.Delete(database); } catch (IOException) { }
        }
    }

    [TestMethod]
    public async Task Python_checks_the_gates_and_guards_in_the_routes_of_a_gated_table()
    {
        string output = NewOutput();
        try
        {
            await GenerateGatedAsync("Python", output);
            string gates = Read(output, "Shop.Python/app/gates.py");
            Expect.Contains(gates, "def may_customer_ignore_bad_status(session: Session, user: CurrentUser) -> bool:");
            Expect.Contains(gates, "if moves and row.invoice_status_id in (2, 3, 4):");
            Expect.Contains(gates, "raise ApiError(409, \"Change not allowed\", problem_type=PROBLEM_CHANGE_NOT_ALLOWED, detail=\"Customer is in bad standing.\")");
            Expect.Contains(gates, "def after_clone_sales_invoice(session: Session, user: CurrentUser, copy) -> None:");

            string customer = Read(output, "Shop.Python/app/routes/customer.py");
            Expect.Contains(customer, "from app.gates import after_clone_customer, check_customer, may_customer_ignore_bad_status");
            Expect.Contains(customer, "@router.get(\"/api/customers/guards\")");
            Expect.Contains(customer, "check_customer(session, user, body, None)");
            Expect.Contains(customer, "check_customer(session, user, body, row)");
            Expect.Contains(customer, "after_clone_customer(session, user, row)");

            string employee = Read(output, "Shop.Python/app/routes/employee.py");
            Expect.DoesNotContain(employee, "app.gates");
            Assert.IsTrue(File.Exists(Path.Combine(output, "Shop.Python/http/SalesInvoice.http")), "the request files are written for a Python project");
        }
        finally { Clean(output); }
    }

    [TestMethod]
    public async Task Rust_checks_the_gates_and_guards_in_the_routes_of_a_gated_table()
    {
        string output = NewOutput();
        try
        {
            await GenerateGatedAsync("Rust", output);
            string gates = Read(output, "Shop.Rust/src/routes/gates.rs");
            Expect.Contains(gates, ".route(\"/api/customers/guards\", get(guards_customer))");
            Expect.Contains(gates, "if moves && [2, 3, 4].contains(&row.invoice_status_id) {");
            Expect.Contains(gates, "if (parent_customerid_customerstatusid == Some(2)) && !(parent_customerid_ignorebadstatus == Some(true)) {");
            Expect.Contains(gates, "ApiError::Problem(409, PROBLEM_CHANGE_NOT_ALLOWED, \"Change not allowed\", Some(\"Customer is in bad standing.\"))");

            string customer = Read(output, "Shop.Rust/src/routes/customer.rs");
            Expect.Contains(customer, "use crate::routes::gates::{after_clone_customer, check_customer};");
            Expect.Contains(customer, "check_customer(&pool, &user, &mut row, None).await?;");
            Expect.Contains(customer, "check_customer(&pool, &user, &mut row, Some(&current)).await?;");
            Expect.Contains(customer, "after_clone_customer(&pool, &user, &mut copy).await?;");
            Expect.Contains(Read(output, "Shop.Rust/src/routes/mod.rs"), ".merge(gates::router())");
            Expect.DoesNotContain(Read(output, "Shop.Rust/src/routes/employee.rs"), "gates");
            Expect.Contains(Read(output, "Shop.Rust/http/SalesInvoice.http"), "@host = http://localhost:5080");
        }
        finally { Clean(output); }
    }
}
