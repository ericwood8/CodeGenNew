using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The Rust stack: names, the type of every column, the statements written for each database, the plan, and the files of a project generated from a SQLite file. Compiling the output
/// needs a Rust toolchain, so that check is Docs/Verification/Test-RustBuild.ps1, not a unit test. </summary>
[TestClass]
public class RustTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    // ------------------------------------------------------------------ names

    [TestMethod]
    [DataRow("CustomerId", "customer_id")]
    [DataRow("BillingAddress1", "billing_address1")]
    [DataRow("RequireCustomerPO", "require_customer_po")]
    [DataRow("PONumber", "po_number")]
    [DataRow("customer-item", "customer_item")]
    [DataRow("E_DonateLeave", "e_donate_leave")]
    [DataRow("URL", "url")]
    [DataRow("2fa", "_2fa")]
    public void A_name_becomes_snake_case(string name, string expected) => Assert.AreEqual(expected, RustNames.Snake(name));

    [TestMethod]
    public void A_keyword_becomes_a_raw_identifier_and_the_words_that_cannot_be_raw_get_an_underscore()
    {
        Assert.AreEqual("r#type", RustNames.Ident("type"));
        Assert.AreEqual("r#match", RustNames.Ident("match"));
        Assert.AreEqual("self_", RustNames.Ident("self"));
        Assert.AreEqual("customer", RustNames.Ident("customer"));
    }

    [TestMethod]
    public void A_table_name_becomes_a_type_name_and_a_project_name_a_crate_name()
    {
        Assert.AreEqual("SalesInvoice", RustNames.Pascal("SalesInvoice"));
        Assert.AreEqual("EDonateLeave", RustNames.Pascal("E_DonateLeave"));
        Assert.AreEqual("CustomerItem", RustNames.Pascal("customer_item"));
        Assert.AreEqual("invoice_system", RustNames.Crate("InvoiceSystem"));
        Assert.AreEqual("invoice_system", ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "InvoiceSystem" }).RustCrateName);
        Assert.AreEqual("my_crate", Project(("RustCrateName", "my_crate")).RustCrateName);
    }

    // ------------------------------------------------------------------ types

    [TestMethod]
    public void Every_database_type_has_a_rust_type_or_is_left_out_on_purpose()
    {
        var unmapped = new[] { SqlDbType.Binary, SqlDbType.VarBinary, SqlDbType.Image, SqlDbType.Timestamp, SqlDbType.Xml, SqlDbType.Variant, SqlDbType.Udt, SqlDbType.Structured };
        foreach (var type in Enum.GetValues<SqlDbType>())
        {
            string? rust = RustTable.RustType(Sample.Column("C", type), SqlDialect.PostgreSql);
            if (unmapped.Contains(type))
                Assert.IsNull(rust, type.ToString());
            else if (type is SqlDbType.Int or SqlDbType.BigInt or SqlDbType.SmallInt or SqlDbType.TinyInt or SqlDbType.Bit or SqlDbType.Decimal or SqlDbType.Money or SqlDbType.SmallMoney
                     or SqlDbType.Float or SqlDbType.Real or SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.SmallDateTime or SqlDbType.DateTimeOffset
                     or SqlDbType.Time or SqlDbType.UniqueIdentifier or SqlDbType.Char or SqlDbType.VarChar or SqlDbType.NChar or SqlDbType.NVarChar or SqlDbType.Text or SqlDbType.NText)
                Assert.IsNotNull(rust, type.ToString());
        }
    }

    [TestMethod]
    [DataRow(SqlDbType.Int, "int", SqlDialect.PostgreSql, "i32")]
    [DataRow(SqlDbType.Int, "int unsigned", SqlDialect.MySql, "u32")]
    [DataRow(SqlDbType.BigInt, "bigint", SqlDialect.MySql, "i64")]
    [DataRow(SqlDbType.SmallInt, "tinyint", SqlDialect.MySql, "i8")]
    [DataRow(SqlDbType.SmallInt, "smallint unsigned", SqlDialect.MySql, "u16")]
    [DataRow(SqlDbType.SmallInt, "year", SqlDialect.MySql, null)]
    [DataRow(SqlDbType.TinyInt, "tinyint unsigned", SqlDialect.MySql, "u8")]
    [DataRow(SqlDbType.Decimal, "numeric(5,2)", SqlDialect.PostgreSql, "Decimal")]
    [DataRow(SqlDbType.Money, "money", SqlDialect.PostgreSql, "Decimal")]
    [DataRow(SqlDbType.Decimal, "DECIMAL(19,4)", SqlDialect.Sqlite, "f64")]
    [DataRow(SqlDbType.DateTime2, "timestamp", SqlDialect.PostgreSql, "NaiveDateTime")]
    [DataRow(SqlDbType.DateTimeOffset, "timestamptz", SqlDialect.PostgreSql, "DateTime<Utc>")]
    [DataRow(SqlDbType.Date, "date", SqlDialect.MySql, "NaiveDate")]
    [DataRow(SqlDbType.Time, "time", SqlDialect.MySql, "NaiveTime")]
    [DataRow(SqlDbType.UniqueIdentifier, "uuid", SqlDialect.PostgreSql, "Uuid")]
    [DataRow(SqlDbType.Bit, "boolean", SqlDialect.PostgreSql, "bool")]
    [DataRow(SqlDbType.VarChar, "varchar(50)", SqlDialect.PostgreSql, "String")]
    public void A_column_type_maps_to_the_rust_type_sqlx_reads(SqlDbType type, string declaration, SqlDialect dialect, string? expected) =>
        Assert.AreEqual(expected, RustTable.RustType(Sample.Column("C", type, sqlDeclaration: declaration), dialect));

    // ------------------------------------------------------------------ the statements

    private static TableModel Account(SqlDialect dialect, bool withParent = true)
    {
        string schema = dialect == SqlDialect.PostgreSql ? "public" : dialect == SqlDialect.Sqlite ? "main" : "acme";
        var status = new TableModel
        {
            SchemaName = schema, TableName = "Status", QuotedName = "Status", Dialect = dialect,
            Columns = [Sample.Column("StatusId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Description", SqlDbType.VarChar, characters: 20, ordinal: 2)],
            PrimaryKeyColumns = [Sample.Column("StatusId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)], ForeignKeys = [], ChildForeignKeys = []
        };
        var fk = Sample.ForeignKey("StatusId", "Status", "StatusId", "Description");
        var table = Sample.Table("Account",
        [
            Sample.Column("AccountId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Code", SqlDbType.VarChar, characters: 20, inUniqueIndex: true, ordinal: 2),
            Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 3),
            Sample.Column("StatusId", SqlDbType.Int, ordinal: 4),
            Sample.Column("Balance", SqlDbType.Money, nullable: true, currency: true, ordinal: 5),
            Sample.Column("Terms", SqlDbType.VarChar, nullable: true, characters: 10, enumType: dialect == SqlDialect.PostgreSql ? "public.PaymentTerms" : null, ordinal: 6),
            Sample.Column("CreatedOn", SqlDbType.DateTime2, createDateColumn: true, ordinal: 7)
        ], withParent ? [fk] : [], dialect: dialect);
        return new TableModel
        {
            SchemaName = schema, TableName = table.TableName, QuotedName = table.QuotedName, Dialect = dialect, Columns = table.Columns, PrimaryKeyColumns = table.PrimaryKeyColumns,
            ForeignKeys = [new ForeignKeyModel
            {
                ConstraintName = fk.ConstraintName, ReferencingColumns = fk.ReferencingColumns, ReferencedSchema = schema, ReferencedTable = "Status", ReferencedColumns = fk.ReferencedColumns,
                ReferencedDisplayColumns = fk.ReferencedDisplayColumns
            }],
            ChildForeignKeys = [], DisplayColumns = table.DisplayColumns, HasReferencedDisplayColumns = true, LookupShape = table.LookupShape
        };
    }

    [TestMethod]
    public void PostgreSql_quotes_numbers_its_placeholders_returns_the_row_and_casts_money_and_enums()
    {
        var t = RustTable.Of(Account(SqlDialect.PostgreSql), Project());

        Assert.IsTrue(t.Returning);
        Expect.Contains(t.SelectList, "\"Balance\"::numeric AS \"Balance\"");
        Expect.Contains(t.SelectList, "\"Terms\"::text AS \"Terms\"");
        Expect.Contains(t.Insert, "INSERT INTO \"public\".\"Account\" (\"Code\", \"Name\", \"StatusId\", \"Balance\", \"Terms\", \"CreatedOn\") VALUES ($1, $2, $3, $4::numeric::money, CAST($5::text AS \"public\".\"PaymentTerms\"), $6) RETURNING");
        Expect.Contains(t.Update, "SET \"Code\" = $1, \"Name\" = $2, \"StatusId\" = $3, \"Balance\" = $4::numeric::money, \"Terms\" = CAST($5::text AS \"public\".\"PaymentTerms\"), \"CreatedOn\" = $6 WHERE \"AccountId\" = $7 RETURNING");
        Assert.AreEqual("DELETE FROM \"public\".\"Account\" WHERE \"AccountId\" = $1", t.Delete);
        Assert.AreEqual("Decimal", t.Fields.Single(f => f.Name == "balance").BaseType);
        Assert.IsTrue(t.Fields.Single(f => f.Name == "balance").IsOption);
    }

    [TestMethod]
    public void MySql_has_no_returning_and_binds_by_position()
    {
        var t = RustTable.Of(Account(SqlDialect.MySql), Project());

        Assert.IsFalse(t.Returning);
        Expect.Contains(t.Insert, "INSERT INTO `Account` (`Code`, `Name`, `StatusId`, `Balance`, `Terms`, `CreatedOn`) VALUES (?, ?, ?, ?, ?, ?)");
        Expect.DoesNotContain(t.Insert, "RETURNING");
        Expect.Contains(t.Update, "WHERE `AccountId` = ?");
        Expect.DoesNotContain(t.SelectList, "::");
    }

    [TestMethod]
    public void Sqlite_reads_a_decimal_as_a_real_so_a_whole_number_stored_as_an_integer_decodes()
    {
        var t = RustTable.Of(Account(SqlDialect.Sqlite), Project());

        Expect.Contains(t.SelectList, "CAST(\"Balance\" AS REAL) AS \"Balance\"");
        Assert.AreEqual("f64", t.Fields.Single(f => f.Name == "balance").BaseType);
        Assert.IsTrue(t.Returning);
    }

    [TestMethod]
    public void The_search_has_a_filter_per_text_column_a_fixed_sort_list_and_a_foreign_key_that_sorts_by_its_parents_text()
    {
        var t = RustTable.Of(Account(SqlDialect.PostgreSql), Project());

        CollectionAssert.AreEqual(new[] { "code", "name", "terms" }, t.Filters.Select(f => f.Query).ToArray());
        var terms = t.Filters.Single(f => f.Query == "terms");
        Expect.Contains(terms.Before, "t.\"Terms\"::text ILIKE '%' || ");
        Expect.Contains(terms.After, " || '%' ESCAPE");
        string byStatus = t.Sorts.Single(s => s.Name == "statusid").Expression;
        Expect.Contains(byStatus, "(SELECT p.\"Description\" FROM \"public\".\"Status\" AS p WHERE p.\"StatusId\" = t.\"StatusId\" LIMIT 1)");
        Assert.IsTrue(t.Sorts.Any(s => s.Name == "accountid"));
        Expect.Contains(t.DefaultOrder, "t.\"Code\"");
        Expect.Contains(t.DefaultOrder, "t.\"AccountId\"");
        Assert.AreEqual("SELECT COUNT(*) FROM \"public\".\"Account\" AS t WHERE 1 = 1", t.SearchCount);
    }

    [TestMethod]
    public void A_clone_copies_the_row_gives_the_unique_text_a_bound_value_and_sets_the_create_date()
    {
        var t = RustTable.Of(Account(SqlDialect.PostgreSql), Project());

        Assert.IsNotNull(t.Clone);
        CollectionAssert.AreEqual(new[] { "code" }, t.Clone!.Overrides.Select(f => f.Name).ToArray());
        Expect.Contains(t.Clone.Sql, "INSERT INTO \"public\".\"Account\" (\"Code\", \"Name\", \"StatusId\", \"Balance\", \"Terms\", \"CreatedOn\") SELECT $1, src.\"Name\", src.\"StatusId\", src.\"Balance\", src.\"Terms\", now() FROM \"public\".\"Account\" AS src WHERE src.\"AccountId\" = $2 RETURNING \"AccountId\"");
    }

    [TestMethod]
    public void A_name_that_is_not_a_legal_identifier_or_a_navigation_is_handled()
    {
        var t = RustTable.Of(Account(SqlDialect.PostgreSql), Project());

        CollectionAssert.AreEqual(new[] { "status" }, t.Navigations.Select(n => n.Name).ToArray());
        Assert.AreEqual("/api/accounts", t.Route);
        Assert.AreEqual("account", t.Module);
        Assert.AreEqual("r##\"a\"#b\"##", RustTable.Raw("a\"#b"));
        Assert.AreEqual("r#\"a\"b\"#", RustTable.Raw("a\"b"));
    }

    [TestMethod]
    public void The_contains_condition_is_split_around_the_bound_value_for_every_database()
    {
        foreach (var dialect in new[] { SqlDialect.SqlServer, SqlDialect.PostgreSql, SqlDialect.MySql, SqlDialect.Sqlite })
        {
            var (before, after) = DialectInfo.For(dialect).ContainsParts("t.x");
            Assert.IsTrue(before.StartsWith("t.x "), dialect + ": " + before);
            Assert.IsTrue(after.Contains("ESCAPE"), dialect + ": " + after);
        }

        Assert.IsTrue(DialectInfo.For(SqlDialect.MySql).ContainsParts("c").After.Contains("ESCAPE '\\\\'"), "a backslash is itself an escape in a MySQL string, so the character is written twice");
    }

    // ------------------------------------------------------------------ the plan and the files

    [TestMethod]
    public void The_rust_templates_are_in_the_rust_stack_only_and_the_validator_comes_with_ApiValidation()
    {
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "public", Dialect = SqlDialect.PostgreSql, Tables = [Account(SqlDialect.PostgreSql)] };

        var plain = ProjectPlan.Build(templates, database, Project(("Stacks", "Rust")), ["Rust"]).Select(s => s.Template.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "RS_Mod", "RS_Repo", "RS_Routes", "RS_Struct" }, plain);

        var validated = ProjectPlan.Build(templates, database, Project(("ApiValidation", "true")), ["Rust"]).Select(s => s.Template.Name).ToList();
        CollectionAssert.Contains(validated, "RS_Validate");
        Assert.IsFalse(plain.Contains("RS_Validate"));

        var api = ProjectPlan.Build(templates, database, Project(), ["Api"]).Select(s => s.Template.Name).ToList();
        Assert.IsFalse(api.Any(n => n.StartsWith("RS_")));

        var sqlServer = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Dialect = SqlDialect.SqlServer, Tables = [Account(SqlDialect.SqlServer)] };
        Assert.AreEqual(0, ProjectPlan.Build(templates, sqlServer, Project(), ["Rust"]).Count, "nothing in the Rust stack is written for SQL Server");
    }

    [TestMethod]
    public void The_rust_stack_is_known_and_has_default_commands_and_an_output_folder()
    {
        CollectionAssert.Contains(ProjectPlan.KnownStacks.ToArray(), "Rust");
        Assert.AreEqual("cargo check", ProjectBuilder.DefaultCommand("Rust", "build"));
        Assert.AreEqual("cargo test", ProjectBuilder.DefaultCommand("Rust", "test"));
        Assert.AreEqual("Acme.Rust", Project().OutputFolderOf("Rust"));
        Assert.AreEqual("crate", Project(("OutputRust", "crate")).OutputFolderOf("Rust"));
        Assert.AreEqual(5080, Project().RustPort);
        Assert.AreEqual(9000, Project(("RustPort", "9000")).RustPort);
        Assert.AreEqual(9001, Project(("ApiPort", "9001")).RustPort);
    }

    private static string CreateDatabase(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "codegen_rust_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Status (StatusId INTEGER PRIMARY KEY, Description VARCHAR(40) NOT NULL)",
            """
            CREATE TABLE Customer (
                CustomerId INTEGER PRIMARY KEY, AccountNumber VARCHAR(20) NOT NULL UNIQUE, Name VARCHAR(50) NOT NULL, StatusId INTEGER NOT NULL REFERENCES Status (StatusId),
                CreditLimit DECIMAL(10,2) NULL CHECK (CreditLimit >= 0), Email VARCHAR(100) NULL, Type VARCHAR(10) NOT NULL CHECK (Type IN ('a', 'b')), CreatedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP)
            """
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    [TestMethod]
    public async Task A_sqlite_file_generates_a_rust_crate_with_the_models_repositories_routes_validators_and_essentials()
    {
        string database = CreateDatabase(out _);
        string output = Path.Combine(Path.GetTempPath(), "codegen_rust_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = ProjectSettings.FromValues(new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Rust", ["ApiValidation"] = "true", ["ApiDocs"] = "true"
            });
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));

            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = project, Stacks = ["Rust"], OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });

            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            foreach (string expected in new[]
            {
                "Shop.Rust/Cargo.toml", "Shop.Rust/.env.example", "Shop.Rust/.gitignore", "Shop.Rust/openapi.yaml", "Shop.Rust/src/main.rs", "Shop.Rust/src/lib.rs", "Shop.Rust/src/support.rs",
                "Shop.Rust/src/models/mod.rs", "Shop.Rust/src/models/customer.rs", "Shop.Rust/src/repos/customer.rs", "Shop.Rust/src/routes/customer.rs", "Shop.Rust/src/routes/mod.rs",
                "Shop.Rust/src/validation/customer.rs", "Shop.Rust/src/validation/mod.rs"
            })
                CollectionAssert.Contains(files, expected);
            Assert.IsFalse(files.Any(f => f.EndsWith(".sql") || f.Contains("/Apis/") || f.EndsWith(".cs")), "only Rust files and the OpenAPI document");

            string Read(string relative) => File.ReadAllText(Path.Combine(output, relative)).Replace("\r\n", "\n");
            string model = Read("Shop.Rust/src/models/customer.rs");
            Expect.Contains(model, "pub struct Customer {");
            Expect.Contains(model, "#[serde(rename = \"customerId\")]");
            Expect.Contains(model, "#[sqlx(rename = \"AccountNumber\")]");
            Expect.Contains(model, "pub credit_limit: Option<f64>,");
            Expect.Contains(model, "pub r#type: String,");
            Expect.Contains(model, "#[serde(with = \"crate::support::json_date_time\")]");
            Expect.Contains(model, "pub status: Option<serde_json::Value>,");

            string repo = Read("Shop.Rust/src/repos/customer.rs");
            Expect.Contains(repo, "const INSERT: &str = r#\"INSERT INTO \"Customer\"");
            Expect.Contains(repo, "CAST(\"CreditLimit\" AS REAL) AS \"CreditLimit\"");
            Expect.Contains(repo, "pub async fn clone_row(");
            Expect.Contains(repo, "fn sort_expression(name: &str)");

            string routes = Read("Shop.Rust/src/routes/customer.rs");
            Expect.Contains(routes, ".route(\"/api/customers/search\", get(search))");
            Expect.Contains(routes, "validate(&row)?;");
            Expect.Contains(Read("Shop.Rust/src/routes/mod.rs"), ".merge(customer::router())");
            Expect.Contains(Read("Shop.Rust/src/lib.rs"), "include_str!(\"../openapi.yaml\")");

            string validation = Read("Shop.Rust/src/validation/customer.rs");
            Expect.Contains(validation, "must be one of: a, b.");
            Expect.Contains(validation, "must be an email address.");
            Expect.Contains(validation, "must be at least 0");

            string cargo = Read("Shop.Rust/Cargo.toml");
            Expect.Contains(cargo, "name = \"shop\"");
            Expect.Contains(cargo, "\"sqlite\"");
            Expect.DoesNotContain(cargo, "rust_decimal");
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task The_tauri_shell_embeds_the_api_and_finds_the_front_end_from_the_projects_folders()
    {
        var react = await Repo.Cache.RunAsync(Repo.Template("RS_EssentialTauri_v1.tt"), ProjectSettings.FromValues(new Dictionary<string, string>
            { ["ProjectName"] = "Shop", ["Stacks"] = "Rust,React", ["OutputRust"] = "Shop.Rust", ["OutputReact"] = "web" }));
        var angular = await Repo.Cache.RunAsync(Repo.Template("RS_EssentialTauri_v1.tt"), ProjectSettings.FromValues(new Dictionary<string, string>
            { ["ProjectName"] = "Shop", ["Stacks"] = "Rust,Angular", ["OutputRust"] = "Shop.Rust" }));

        Assert.IsTrue(react.Success, string.Join(" | ", react.Errors));
        var files = GeneratedFiles.Split(react.GeneratedText!).ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Content.Replace("\r\n", "\n"));
        CollectionAssert.AreEquivalent(new[] { "desktop/Cargo.toml", "desktop/build.rs", "desktop/tauri.conf.json", "desktop/capabilities/default.json", "desktop/src/main.rs" }, files.Keys.ToArray());
        Expect.Contains(files["desktop/Cargo.toml"], "shop = { path = \"..\" }");
        Expect.Contains(files["desktop/src/main.rs"], "shop::serve(listener, Some(frontend))");
        Expect.Contains(files["desktop/src/main.rs"], "const DEFAULT_FRONTEND: &str = \"../../web/dist\";");
        Expect.Contains(GeneratedFiles.Split(angular.GeneratedText!).Single(f => f.RelativePath.EndsWith("main.rs")).Content, "../../frontend/dist/frontend/browser");
    }

    private sealed class SqlServerProvider(TableModel table) : ISchemaProvider
    {
        public Task<List<TableSummary>> ListTablesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<TableSummary> { new() { SchemaName = table.SchemaName, TableName = table.TableName, HasPrimaryKey = true } });
        public Task<List<ColumnSummary>> ListColumnSummariesAsync(string schemaName, string tableName, CancellationToken cancellationToken = default) => Task.FromResult(new List<ColumnSummary>());
        public Task<TableModel> BuildTableModelAsync(string schemaName, string tableName, bool includeRowData = false, bool includeReferencedDisplayColumns = false,
            CancellationToken cancellationToken = default) => Task.FromResult(table);
    }

    [TestMethod]
    public async Task A_run_against_sql_server_is_refused_with_the_reason_and_writes_nothing()
    {
        string output = Path.Combine(Path.GetTempPath(), "codegen_rust_sqlserver_" + Guid.NewGuid().ToString("N"));
        var report = await Repo.GenerateAsync(new SqlServerProvider(Account(SqlDialect.SqlServer)), new GenerateOptions
        {
            Project = Project(("Stacks", "Rust")), Stacks = ["Rust"], OutputDirectory = output, DatabaseName = "Acme", Schema = "acme"
        });

        Assert.IsFalse(report.Success);
        StringAssert.Contains(report.Errors.Single(), "not written for SQL Server");
        StringAssert.Contains(report.Errors.Single(), "sqlx");
        Assert.IsFalse(Directory.Exists(output));
    }
}
