using System.Data;
using System.Diagnostics;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The Python stack: names, the type of every column, the plan, the text of every template and the files of a project generated from a SQLite file. When a Python interpreter is on the
/// path every generated file is also byte-compiled; running the API needs FastAPI, SQLAlchemy and Pydantic, so that was checked by hand (Docs/TemplateNotes/PY_Routes_v1.md). </summary>
[TestClass]
public class PythonTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    [TestMethod]
    [DataRow("CustomerId", "customer_id")]
    [DataRow("BillingAddress1", "billing_address1")]
    [DataRow("class", "class_")]
    [DataRow("metadata", "metadata_")]
    [DataRow("copy", "copy_")]
    [DataRow("Name", "name")]
    public void A_column_name_becomes_a_legal_snake_case_attribute(string name, string expected) => Assert.AreEqual(expected, PythonNames.Ident(PythonNames.Snake(name)));

    [TestMethod]
    [DataRow("SalesInvoice", "SalesInvoice")]
    [DataRow("E_DonateLeave", "EDonateLeave")]
    [DataRow("customer_item", "CustomerItem")]
    public void A_table_name_becomes_a_class_name(string table, string expected) => Assert.AreEqual(expected, PythonNames.Pascal(table));

    [TestMethod]
    public void A_string_is_a_python_literal() => Assert.AreEqual("\"say \\\"hi\\\"\\n\"", PythonNames.Str("say \"hi\"\n"));

    private static ColumnModel Column(SqlDbType type, string declaration, int? length = null, int? precision = null, int? scale = null) => Sample.Column("Value", type, characters: length, precision: precision, scale: scale, sqlDeclaration: declaration);

    [TestMethod]
    [DataRow(SqlDbType.Int, "int", "int", "Integer")]
    [DataRow(SqlDbType.BigInt, "bigint", "int", "BigInteger")]
    [DataRow(SqlDbType.SmallInt, "smallint", "int", "SmallInteger")]
    [DataRow(SqlDbType.TinyInt, "tinyint", "int", "SmallInteger")]
    [DataRow(SqlDbType.Bit, "bit", "bool", "Boolean")]
    [DataRow(SqlDbType.Float, "float", "float", "Float")]
    [DataRow(SqlDbType.Date, "date", "date", "Date")]
    [DataRow(SqlDbType.DateTime2, "datetime2", "datetime", "DateTime")]
    [DataRow(SqlDbType.DateTimeOffset, "datetimeoffset", "datetime", "DateTime(timezone=True)")]
    [DataRow(SqlDbType.Time, "time", "time", "Time")]
    [DataRow(SqlDbType.UniqueIdentifier, "uniqueidentifier", "UUID", "Uuid")]
    [DataRow(SqlDbType.Money, "money", "Decimal", "Numeric(19, 4)")]
    [DataRow(SqlDbType.Text, "text", "str", "Text")]
    public void Every_sql_type_has_a_python_annotation_and_a_sqlalchemy_type(SqlDbType type, string declaration, string annotation, string sqlAlchemy)
    {
        var mapped = PythonTable.TypeOf(Column(type, declaration), SqlDialect.SqlServer);
        Assert.IsNotNull(mapped);
        Assert.AreEqual(annotation, mapped.Value.Annotation);
        Assert.AreEqual(sqlAlchemy, mapped.Value.SqlAlchemy);
    }

    [TestMethod]
    public void A_decimal_and_a_string_carry_their_size_and_a_binary_column_has_no_type()
    {
        Assert.AreEqual("Numeric(10, 2)", PythonTable.TypeOf(Column(SqlDbType.Decimal, "decimal(10,2)", precision: 10, scale: 2), SqlDialect.SqlServer)!.Value.SqlAlchemy);
        Assert.AreEqual("String(50)", PythonTable.TypeOf(Column(SqlDbType.VarChar, "varchar(50)", length: 50), SqlDialect.SqlServer)!.Value.SqlAlchemy);
        Assert.IsNull(PythonTable.TypeOf(Column(SqlDbType.VarBinary, "varbinary(max)"), SqlDialect.SqlServer));
        Assert.IsNull(PythonTable.TypeOf(Column(SqlDbType.Money, "money"), SqlDialect.PostgreSql), "a PostgreSQL money column is left out");
    }

    [TestMethod]
    public void The_python_stack_is_known_and_has_a_default_build_an_output_folder_and_essentials()
    {
        CollectionAssert.Contains(ProjectPlan.KnownStacks.ToArray(), "Python");
        CollectionAssert.Contains(ProjectSettingChoices.Stacks, "Python");
        Assert.AreEqual("python -m compileall -q app", ProjectBuilder.DefaultCommand("Python", "build"));
        Assert.IsNull(ProjectBuilder.DefaultCommand("Python", "test"));
        Assert.AreEqual("Acme.Python", Project().OutputFolderOf("Python"));
        Assert.AreEqual("api", Project(("OutputPython", "api")).OutputFolderOf("Python"));
        Assert.AreEqual("Python", EssentialsCatalog.FindStack("python"));
        CollectionAssert.AreEquivalent(new[] { "App", "Project" }, EssentialsCatalog.Groups(Repo.TemplatesDirectory, "Python").Select(g => g.Name).ToList());
        CollectionAssert.Contains(Project(("ApiValidation", "true")).ImpliedPlanTemplates.ToArray(), "PY_Validate");
    }

    // ------------------------------------------------------------------ a generated project

    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_py_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Region (RegionId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)",
            """
            CREATE TABLE Customer (
                CustomerId INTEGER PRIMARY KEY, AccountNumber VARCHAR(20) NOT NULL UNIQUE, Name VARCHAR(50) NOT NULL, RegionId INTEGER NOT NULL REFERENCES Region (RegionId),
                CreditLimit DECIMAL(10,2) NULL CHECK (CreditLimit >= 0), Email VARCHAR(100) NULL, Type VARCHAR(10) NOT NULL CHECK (Type IN ('a', 'b')), IsTaxable BIT NOT NULL DEFAULT 0,
                CreatedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, Notes VARCHAR(500) NULL)
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

    private static async Task<Dictionary<string, string>> Generate(bool validate, string output)
    {
        string database = CreateDatabase();
        try
        {
            var project = ProjectSettings.FromValues(new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Python,Blazor", ["Screens"] = "Customer,Region",
                ["ApiPort"] = "6001", ["ApiValidation"] = validate ? "true" : "false"
            });
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = project, Stacks = ["Python"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(database);
        }
    }

    [TestMethod]
    public async Task A_sqlite_file_generates_a_python_api_with_models_schemas_routes_and_essentials()
    {
        string output = Path.Combine(Path.GetTempPath(), "codegen_py_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = await Generate(validate: false, output);

            foreach (string expected in new[]
            {
                "Shop.Python/app/__init__.py", "Shop.Python/app/db.py", "Shop.Python/app/support.py", "Shop.Python/app/main.py", "Shop.Python/requirements.txt", "Shop.Python/.env.example",
                "Shop.Python/.gitignore", "Shop.Python/README.md", "Shop.Python/app/models/__init__.py", "Shop.Python/app/models/customer.py", "Shop.Python/app/models/region.py",
                "Shop.Python/app/schemas/customer.py", "Shop.Python/app/routes/__init__.py", "Shop.Python/app/routes/customer.py", "Shop.Python/app/routes/region.py"
            })
                CollectionAssert.Contains(files.Keys.ToArray(), expected);
            Assert.IsFalse(files.Keys.Any(f => f.Contains("customer_note") || f.EndsWith(".cs") || f.EndsWith(".sql")), "only the Python files; a composite-key table is not served");
            Assert.IsFalse(files.Keys.Any(f => f.Contains("/validation/")), "validation is opt in");

            string model = files["Shop.Python/app/models/customer.py"];
            Expect.Contains(model, "class Customer(Base):");
            Expect.Contains(model, "__tablename__ = \"Customer\"");
            Expect.Contains(model, "customer_id: Mapped[int] = mapped_column(\"CustomerId\", Integer, primary_key=True, autoincrement=True)");
            Expect.Contains(model, "account_number: Mapped[str] = mapped_column(\"AccountNumber\", String(20), nullable=False)");
            Expect.Contains(model, "credit_limit: Mapped[Decimal | None] = mapped_column(\"CreditLimit\", Numeric(10, 2), nullable=True)");
            Expect.Contains(model, "is_taxable: Mapped[bool] = mapped_column(\"IsTaxable\", Boolean, nullable=False)");
            Expect.Contains(model, "notes: Mapped[str | None] = mapped_column(\"Notes\", String(500), nullable=True)");
            Expect.Contains(model, "from sqlalchemy import ");

            string schema = files["Shop.Python/app/schemas/customer.py"];
            Expect.Contains(schema, "class CustomerSchema(BaseModel):");
            Expect.Contains(schema, "customer_id: int = Field(0, alias=\"customerId\")");
            Expect.Contains(schema, "account_number: str = Field(alias=\"accountNumber\")");
            Expect.Contains(schema, "credit_limit: JsonDecimal | None = Field(None, alias=\"creditLimit\")");
            Expect.Contains(schema, "def columns(self) -> dict[str, Any]:");
            Expect.Contains(schema, "def changes(self) -> dict[str, Any]:");
            Expect.DoesNotContain(schema.Split("def changes")[1], "customer_id");

            string routes = files["Shop.Python/app/routes/customer.py"];
            Expect.Contains(routes, "@router.get(\"/api/customers\", response_model=list[CustomerSchema])");
            Expect.Contains(routes, "@router.get(\"/api/customers/search\", response_model=Page[CustomerSchema])");
            Expect.Contains(routes, "@router.post(\"/api/customers\", response_model=CustomerSchema, status_code=201)");
            Expect.Contains(routes, "@router.put(\"/api/customers/{id}\", response_model=CustomerSchema)");
            Expect.Contains(routes, "The id in the URL and the id in the body differ.");
            Expect.Contains(routes, "@router.post(\"/api/customers/{id}/clone\", response_model=CustomerSchema, status_code=201)");
            Expect.Contains(routes, "account_number=suggest_free(session, Customer.account_number, source.account_number, 20),");
            Expect.Contains(routes, "Query(None, alias=\"accountNumber\")");
            Expect.Contains(routes, "\"accountnumber\": Customer.account_number,");
            Expect.DoesNotContain(routes, "validate(body)");
            Expect.Contains(files["Shop.Python/app/routes/__init__.py"], "routers = [");
            Expect.Contains(files["Shop.Python/app/routes/__init__.py"], "    customer.router,");

            string db = files["Shop.Python/app/db.py"];
            Expect.Contains(db, "\"sqlite:///\" + os.environ.get(\"DATABASE_FILE\", \"shop.db\")");
            Expect.DoesNotContain(db, "password=");
            string main = files["Shop.Python/app/main.py"];
            Expect.Contains(main, "uvicorn app.main:app --port 6001");
            Expect.Contains(main, "\"http://localhost:5190\"");
            Expect.Contains(files["Shop.Python/requirements.txt"], "sqlalchemy>=2.0,<2.1");
            Expect.DoesNotContain(files["Shop.Python/requirements.txt"], "psycopg");

            await CompileAsync(output);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task With_validation_the_routes_call_the_rules_written_from_the_schema()
    {
        string output = Path.Combine(Path.GetTempPath(), "codegen_py_val_" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = await Generate(validate: true, output);

            string validation = files["Shop.Python/app/validation/customer.py"];
            Expect.Contains(validation, "def validate(row: CustomerSchema) -> None:");
            Expect.Contains(validation, "if not row.account_number.strip():");
            Expect.Contains(validation, "if len(row.account_number) > 20:");
            Expect.Contains(validation, "must be one of: a, b.");
            Expect.Contains(validation, "must be an email address.");
            Expect.Contains(validation, "must be at least 0");
            Expect.Contains(validation, "raise ApiValidationError(problems)");
            CollectionAssert.Contains(files.Keys.ToArray(), "Shop.Python/app/validation/__init__.py");

            string routes = files["Shop.Python/app/routes/customer.py"];
            Expect.Contains(routes, "from app.validation.customer import validate");
            Expect.Contains(routes, "validate(body)");

            await CompileAsync(output);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    // Byte-compiles every generated file with the interpreter on the path; without one the check is skipped.
    private static async Task CompileAsync(string output)
    {
        try
        {
            var start = new ProcessStartInfo("python", $"-m compileall -q \"{Path.Combine(output, "Shop.Python")}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            using var process = Process.Start(start)!;
            string text = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 9009 || text.Contains("was not found", StringComparison.OrdinalIgnoreCase) && process.ExitCode != 0)
                return;
            Assert.AreEqual(0, process.ExitCode, "python -m compileall: " + text);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // no interpreter on the path
        }
    }
}
