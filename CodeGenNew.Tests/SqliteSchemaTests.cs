using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> Reading a SQLite file: the statement text, the declared types, and a real database built in a temporary file (these tests need nothing installed, so they always run). </summary>
[TestClass]
public class SqliteSchemaTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config");

    // ------------------------------------------------------------------ the statement text

    [TestMethod]
    public void The_check_constraints_come_from_the_statement_text_whatever_the_quoting()
    {
        var parsed = SqliteDdl.Parse("""
            CREATE TABLE "Item" (
                "Id" INTEGER PRIMARY KEY,
                [Price] DECIMAL(10,2) CHECK ("Price" >= 0 AND "Price" <= 100),
                Kind TEXT CHECK (Kind IN ('a,b', 'it''s (ok)')),
                Qty INT, -- a comment, with a comma (and a parenthesis
                `Note` TEXT DEFAULT 'CHECK (x)',
                CONSTRAINT qty_positive CHECK (Qty > 0)
            ) WITHOUT ROWID, STRICT;
            """);

        Assert.IsTrue(parsed.WithoutRowId);
        Assert.IsTrue(parsed.Strict);
        Assert.HasCount(3, parsed.Checks);
        Assert.AreEqual(("Price", "\"Price\" >= 0 AND \"Price\" <= 100"), (parsed.Checks[0].Column, parsed.Checks[0].Expression));
        Assert.AreEqual("Kind", parsed.Checks[1].Column);
        Assert.AreEqual("Kind IN ('a,b', 'it''s (ok)')", parsed.Checks[1].Expression);
        Assert.IsNull(parsed.Checks[2].Column, "a table constraint names no column of its own");
        Assert.AreEqual("Qty > 0", parsed.Checks[2].Expression);
    }

    [TestMethod]
    public void A_statement_with_no_checks_and_no_options_is_plain()
    {
        var parsed = SqliteDdl.Parse("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");

        Assert.IsEmpty(parsed.Checks);
        Assert.IsFalse(parsed.WithoutRowId);
        Assert.IsFalse(parsed.Strict);
        Assert.IsEmpty(SqliteDdl.Parse(null).Checks);
    }

    // ------------------------------------------------------------------ the declared types

    [TestMethod]
    [DataRow("INTEGER", "int", 0, 0, 0)]
    [DataRow("INT", "int", 0, 0, 0)]
    [DataRow("BIGINT", "bigint", 0, 0, 0)]
    [DataRow("UNSIGNED BIG INT", "bigint", 0, 0, 0)]
    [DataRow("SMALLINT", "smallint", 0, 0, 0)]
    [DataRow("TINYINT", "smallint", 0, 0, 0)]
    [DataRow("MEDIUMINT", "int", 0, 0, 0)]
    [DataRow("BOOLEAN", "bit", 0, 0, 0)]
    [DataRow("TEXT", "varchar", -1, 0, 0)]
    [DataRow("VARCHAR(50)", "varchar", 50, 0, 0)]
    [DataRow("NVARCHAR(30)", "varchar", 30, 0, 0)]
    [DataRow("CHAR(3)", "char", 3, 0, 0)]
    [DataRow("CLOB", "varchar", -1, 0, 0)]
    [DataRow("REAL", "real", 0, 0, 0)]
    [DataRow("DOUBLE PRECISION", "float", 0, 0, 0)]
    [DataRow("FLOAT", "float", 0, 0, 0)]
    [DataRow("DECIMAL(10,2)", "decimal", 0, 10, 2)]
    [DataRow("NUMERIC", "decimal", 0, 38, 4)]
    [DataRow("MONEY", "decimal", 0, 19, 4)]
    [DataRow("DATE", "date", 0, 0, 0)]
    [DataRow("DATETIME", "datetime2", 0, 0, 0)]
    [DataRow("TIMESTAMP", "datetime2", 0, 0, 0)]
    [DataRow("TIME", "time", 0, 0, 0)]
    [DataRow("UUID", "uniqueidentifier", 0, 0, 0)]
    [DataRow("BLOB", "varbinary", -1, 0, 0)]
    [DataRow("", "sql_variant", 0, 0, 0)]
    public void A_declared_type_is_read_the_way_orms_read_it(string declared, string sqlType, int length, int precision, int scale)
    {
        var mapped = SqliteSchemaProvider.MapType(declared);

        Assert.AreEqual((sqlType, length, precision, scale), (mapped.SqlTypeName, mapped.MaxLength, mapped.Precision, mapped.Scale));
    }

    [TestMethod]
    public void A_default_is_put_in_the_form_the_generator_reads()
    {
        Assert.AreEqual("getdate()", SqliteSchemaProvider.NormalizeDefault("CURRENT_TIMESTAMP"));
        Assert.AreEqual("getdate()", SqliteSchemaProvider.NormalizeDefault("datetime('now')"));
        Assert.AreEqual("1", SqliteSchemaProvider.NormalizeDefault("TRUE"));
        Assert.AreEqual("'x'", SqliteSchemaProvider.NormalizeDefault("'x'"));
        Assert.IsNull(SqliteSchemaProvider.NormalizeDefault(null));
    }

    // ------------------------------------------------------------------ a real file

    private static string CreateDatabase(params string[] statements)
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_sqlite_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static SqliteSchemaProvider Provider(string path) =>
        new(new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = path }, ConfigPath);

    private static readonly string[] Shop =
    [
        "CREATE TABLE CustomerStatus (CustomerStatusId INTEGER PRIMARY KEY, Description VARCHAR(40) NOT NULL)",
        """
        CREATE TABLE Customer (
            CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
            AccountNumber VARCHAR(20) NOT NULL UNIQUE,
            Name VARCHAR(50) NOT NULL,
            Email VARCHAR(100),
            CreditLimit DECIMAL(10,2) CHECK (CreditLimit >= 0 AND CreditLimit <= 5000),
            Tier VARCHAR(10) NOT NULL CHECK (Tier IN ('Gold', 'Silver', 'Bronze')),
            IsActive INTEGER NOT NULL DEFAULT 1,
            Rating INTEGER CHECK (Rating BETWEEN 1 AND 5),
            Added DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CustomerStatusId INTEGER NOT NULL REFERENCES CustomerStatus,
            Notes TEXT
        )
        """,
        "CREATE INDEX IX_Customer_Name ON Customer (Name, Email)",
        "CREATE TABLE Tag (TagId INTEGER PRIMARY KEY, Label VARCHAR(30) NOT NULL)",
        "CREATE TABLE CustomerTag (CustomerId INTEGER NOT NULL REFERENCES Customer (CustomerId), TagId INTEGER NOT NULL REFERENCES Tag (TagId), PRIMARY KEY (CustomerId, TagId)) WITHOUT ROWID"
    ];

    [TestMethod]
    public async Task The_table_list_says_which_tables_are_keyed_junctions_or_parents()
    {
        string path = CreateDatabase(Shop);
        try
        {
            var tables = (await Provider(path).ListTablesAsync()).ToDictionary(t => t.TableName);

            Assert.HasCount(4, tables);
            Assert.AreEqual(PrimaryKeyShape.SingleInt, tables["Customer"].PrimaryKeyShape);
            Assert.AreEqual(PrimaryKeyShape.Composite, tables["CustomerTag"].PrimaryKeyShape);
            Assert.IsTrue(tables["CustomerTag"].IsJunctionTable);
            Assert.IsFalse(tables["Customer"].IsJunctionTable);
            Assert.IsTrue(tables["CustomerStatus"].HasChildForeignKeys);
            Assert.IsTrue(tables["Customer"].HasChildForeignKeys);
            Assert.IsFalse(tables["CustomerTag"].HasChildForeignKeys);
            Assert.AreEqual("main", tables["Customer"].SchemaName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task A_table_is_read_with_its_types_keys_checks_defaults_and_indexes()
    {
        string path = CreateDatabase(Shop);
        try
        {
            var model = await Provider(path).BuildTableModelAsync("main", "Customer");

            Assert.AreEqual(SqlDialect.Sqlite, model.Dialect);
            var id = model.Columns.Single(c => c.Name == "CustomerId");
            Assert.IsTrue(id.IsIdentity);
            Assert.IsTrue(id.IsPrimaryKey);
            Assert.AreEqual(SqlDbType.Int, id.SqlType);
            Assert.AreEqual(PrimaryKeyShape.SingleInt, model.PrimaryKeyShape);

            var name = model.Columns.Single(c => c.Name == "Name");
            Assert.AreEqual((SqlDbType.VarChar, 50, false), (name.SqlType, name.MaxLength, name.IsNullable));
            Assert.IsTrue(model.Columns.Single(c => c.Name == "Email").IsNullable);
            Assert.IsTrue(model.Columns.Single(c => c.Name == "AccountNumber").IsInUniqueIndex);

            var credit = model.Columns.Single(c => c.Name == "CreditLimit");
            Assert.AreEqual((SqlDbType.Decimal, 10, 2), (credit.SqlType, credit.Precision, credit.Scale));
            Assert.AreEqual(0, credit.Check?.Min);
            Assert.AreEqual(5000, credit.Check?.Max);

            var tier = model.Columns.Single(c => c.Name == "Tier");
            CollectionAssert.AreEqual(new[] { "Gold", "Silver", "Bronze" }, tier.Choices);

            var active = model.Columns.Single(c => c.Name == "IsActive");
            Assert.AreEqual(SqlDbType.Bit, active.SqlType, "an integer named Is... is a flag");
            Assert.AreEqual(1, model.Columns.Single(c => c.Name == "Rating").Check?.Min);
            Assert.AreEqual(5, model.Columns.Single(c => c.Name == "Rating").Check?.Max);
            Assert.AreEqual(SqlDbType.DateTime2, model.Columns.Single(c => c.Name == "Added").SqlType);
            Assert.IsTrue(model.Columns.Single(c => c.Name == "Notes").IsLongTextColumn, "unbounded TEXT is long text, as in PostgreSQL");

            var foreignKey = model.ForeignKeys.Single();
            Assert.AreEqual("CustomerStatus", foreignKey.ReferencedTable);
            CollectionAssert.AreEqual(new[] { "CustomerStatusId" }, foreignKey.ReferencingColumns);
            CollectionAssert.AreEqual(new[] { "CustomerStatusId" }, foreignKey.ReferencedColumns, "a reference written without columns points at the other key");
            Assert.AreEqual("CustomerTag", model.ChildForeignKeys.Single().ReferencingTable);

            Assert.IsTrue(model.IsIndexed(["Name"]), "the composite index starts with Name");
            Assert.IsTrue(model.IsIndexed(["CustomerId"]), "the row id alias counts as indexed");
            Assert.IsFalse(model.IsIndexed(["Email"]));
            Assert.IsFalse(model.IsIndexed(["CustomerStatusId"]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task A_junction_table_without_a_row_id_has_its_composite_key_and_two_foreign_keys()
    {
        string path = CreateDatabase(Shop);
        try
        {
            var model = await Provider(path).BuildTableModelAsync("main", "CustomerTag");

            Assert.IsTrue(model.IsJunctionTable);
            Assert.HasCount(2, model.PrimaryKeyColumns);
            Assert.IsFalse(model.Columns.Any(c => c.IsIdentity));
            Assert.HasCount(2, model.ForeignKeys);
            Assert.IsTrue(model.IsIndexed(["CustomerId", "TagId"]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task The_file_is_opened_read_only()
    {
        string path = CreateDatabase(Shop);
        try
        {
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = path };
            await using var connection = request.CreateSqliteConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE should_not_exist (id INTEGER)";

            await Assert.ThrowsAsync<SqliteException>(async () => await command.ExecuteNonQueryAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task A_missing_file_is_an_error_not_a_new_empty_database()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_missing_" + Guid.NewGuid().ToString("N") + ".db");

        await Assert.ThrowsAsync<FileNotFoundException>(async () => await Provider(path).ListTablesAsync());
        Assert.IsFalse(File.Exists(path));
    }
}

/// <summary> What the command line asks for when the database is a file. </summary>
[TestClass]
public class SqliteCommandLineTests
{
    [TestMethod]
    public void A_sqlite_file_needs_only_its_path()
    {
        var options = CodeGenNew.Cli.ArgumentParser.Parse(["--provider", "Sqlite", "-d", "shop.db", "-t", "customer", "-T", "CS_Entity.tt"]);

        Assert.AreEqual(DatabaseProvider.Sqlite, options.Provider);
        Assert.AreEqual("main", options.Schema);
        Assert.IsNull(CodeGenNew.Cli.ArgumentParser.MissingConnection(options), "no server, login or password");

        var withoutFile = CodeGenNew.Cli.ArgumentParser.Parse(["--provider", "Sqlite", "-T", "CS_Entity.tt"]);
        Assert.AreEqual("-d/--database is required.", CodeGenNew.Cli.ArgumentParser.MissingConnection(withoutFile));

        var sqlServer = CodeGenNew.Cli.ArgumentParser.Parse(["-d", "x", "-T", "CS_Entity.tt"]);
        Assert.AreEqual("-S/--server is required.", CodeGenNew.Cli.ArgumentParser.MissingConnection(sqlServer), "the other databases still need a server");
    }
}
