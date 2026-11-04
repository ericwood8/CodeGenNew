using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> Reads a real MariaDB. Runs only when CODEGENNEW_MARIADB_HOST, _DATABASE, _USER and _PASSWORD are set (a throwaway server from <c>throwaway_db.py up --image mariadb:10.11</c>, with a
/// <c>Customer</c> table holding a text default, a JSON column, a uuid and an inet6 column, and a system-versioned <c>Price</c> table and a sequence); otherwise each test reports itself as inconclusive. Nothing is written. </summary>
[TestClass]
public class MariaDbIntegrationTests
{
    private static MySqlSchemaProvider Provider()
    {
        string? host = Environment.GetEnvironmentVariable("CODEGENNEW_MARIADB_HOST");
        string? database = Environment.GetEnvironmentVariable("CODEGENNEW_MARIADB_DATABASE");
        string? user = Environment.GetEnvironmentVariable("CODEGENNEW_MARIADB_USER");
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(database) || string.IsNullOrEmpty(user))
            Assert.Inconclusive("Set CODEGENNEW_MARIADB_HOST, CODEGENNEW_MARIADB_DATABASE, CODEGENNEW_MARIADB_USER and CODEGENNEW_MARIADB_PASSWORD to run the MariaDB integration tests.");

        var request = new ConnectionRequest
        {
            Provider = DatabaseProvider.MySql, ServerName = host, DatabaseName = database,
            AuthMode = AuthMode.SqlLogin, UserName = user, Password = Environment.GetEnvironmentVariable("CODEGENNEW_MARIADB_PASSWORD")
        };
        return new MySqlSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config"));
    }

    [TestMethod]
    public async Task The_server_says_it_is_MariaDB_and_the_database_model_carries_the_version()
    {
        var model = await Provider().BuildAsync(Environment.GetEnvironmentVariable("CODEGENNEW_MARIADB_DATABASE")!, Environment.GetEnvironmentVariable("CODEGENNEW_MARIADB_DATABASE")!);

        Assert.IsTrue(model.ServerIsMariaDb, model.ServerVersion);
        Assert.IsTrue(model.UsesMariaDb(ProjectSettings.FromValues(new Dictionary<string, string>())));
    }

    [TestMethod]
    public async Task A_system_versioned_table_is_listed_and_a_sequence_is_not()
    {
        var tables = await Provider().ListTablesAsync();

        Assert.IsTrue(tables.Any(t => t.TableName == "Price"), "the system-versioned table");
        Assert.IsFalse(tables.Any(t => t.TableName == "InvoiceNo"), "a sequence is not a table");
    }

    [TestMethod]
    public async Task A_system_versioned_table_is_marked_and_its_create_script_keeps_the_versioning()
    {
        string database = Environment.GetEnvironmentVariable("CODEGENNEW_MARIADB_DATABASE")!;
        var provider = Provider();
        var price = await provider.BuildTableModelAsync(database, "Price");
        var customer = await provider.BuildTableModelAsync(database, "Customer");

        Assert.IsTrue(price.IsSystemVersioned);
        Assert.IsFalse(customer.IsSystemVersioned);
        string script = SchemaScript.For(new DatabaseModel { DatabaseName = database, SchemaName = database, Dialect = SqlDialect.MySql, Tables = [price, customer] });
        Expect.Contains(script, ") WITH SYSTEM VERSIONING;");
    }

    [TestMethod]
    public async Task The_columns_of_the_customer_table_are_read_like_the_MySQL_ones()
    {
        string database = Environment.GetEnvironmentVariable("CODEGENNEW_MARIADB_DATABASE")!;
        var customer = await Provider().BuildTableModelAsync(database, "Customer");
        ColumnModel Column(string name) => customer.Columns.Single(c => c.Name == name);

        Assert.AreEqual("\"open\"", Column("Status").SuggestedCSharpDefaultValueLiteral, "a quoted default");
        Assert.IsNull(Column("Note").DatabaseDefaultSql, "the text NULL is not a default");
        Assert.AreEqual("json", Column("Settings").SqlTypeDeclaration, "longtext with a json_valid check");
        Assert.AreEqual("uuid", Column("Token").SqlTypeDeclaration);
        Assert.AreEqual("inet6", Column("LastIp").SqlTypeDeclaration);
        StringAssert.Contains(customer.ServerVersion, "MariaDB", "a one-table template learns the server from the table");
        Assert.IsTrue(Column("IsActive").IsBooleanColumn);
        Assert.IsTrue(Column("CustomerId").IsIdentity);
    }
}
