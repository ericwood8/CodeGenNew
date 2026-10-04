using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> What the database says about a table and its columns (a description, a comment) is read into the model, for the documents that show it. Live tests on a scratch table, dropped again. </summary>
[TestClass]
public class SchemaDescriptionTests
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    [TestMethod]
    public async Task SQL_Server_reads_the_MS_Description_of_a_table_and_its_columns()
    {
        string? host = Env("CODEGENNEW_SQLSERVER_HOST"), database = Env("CODEGENNEW_SQLSERVER_DATABASE");
        if (host is null || database is null)
            Assert.Inconclusive("Set CODEGENNEW_SQLSERVER_HOST and CODEGENNEW_SQLSERVER_DATABASE to run the SQL Server description test.");
        string? user = Env("CODEGENNEW_SQLSERVER_USER");
        var request = new ConnectionRequest
        {
            Provider = DatabaseProvider.SqlServer, ServerName = host!, DatabaseName = database!,
            AuthMode = user is null ? AuthMode.WindowsAuth : AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_SQLSERVER_PASSWORD")
        };

        await using var connection = request.CreateConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("IF OBJECT_ID('dbo.codegen_description_test') IS NOT NULL DROP TABLE dbo.codegen_description_test");
            await Run("CREATE TABLE dbo.codegen_description_test (id int IDENTITY PRIMARY KEY, amount money NULL, note nvarchar(50) NULL)");
            await Run("EXEC sp_addextendedproperty N'MS_Description', N'A scratch table.', N'SCHEMA', N'dbo', N'TABLE', N'codegen_description_test'");
            await Run("EXEC sp_addextendedproperty N'MS_Description', N'The amount, in dollars.', N'SCHEMA', N'dbo', N'TABLE', N'codegen_description_test', N'COLUMN', N'amount'");

            var model = await new SqlServerSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config")).BuildTableModelAsync("dbo", "codegen_description_test");

            Assert.AreEqual("A scratch table.", model.Description);
            Assert.AreEqual("The amount, in dollars.", model.Columns.Single(c => c.Name == "amount").Description);
            Assert.IsNull(model.Columns.Single(c => c.Name == "note").Description);
        }
        finally
        {
            await Run("IF OBJECT_ID('dbo.codegen_description_test') IS NOT NULL DROP TABLE dbo.codegen_description_test");
        }
    }

    [TestMethod]
    public async Task PostgreSQL_reads_COMMENT_ON_of_a_table_and_its_columns()
    {
        string? host = Env("CODEGENNEW_PG_HOST"), database = Env("CODEGENNEW_PG_DATABASE"), user = Env("CODEGENNEW_PG_USER");
        if (host is null || database is null || user is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, _DATABASE, _USER and _PASSWORD to run the PostgreSQL description test.");
        var request = new ConnectionRequest { Provider = DatabaseProvider.PostgreSql, ServerName = host!, DatabaseName = database!, AuthMode = AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_PG_PASSWORD") };

        await using var connection = request.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP SCHEMA IF EXISTS codegen_description_test CASCADE");
            await Run("CREATE SCHEMA codegen_description_test");
            await Run("CREATE TABLE codegen_description_test.item (item_id serial PRIMARY KEY, \"Amount\" numeric(10,2) NULL, note varchar(50) NULL)");
            await Run("COMMENT ON TABLE codegen_description_test.item IS 'A scratch table.'");
            await Run("COMMENT ON COLUMN codegen_description_test.item.\"Amount\" IS 'The amount, in dollars.'");

            var model = await new PostgresSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config")).BuildTableModelAsync("codegen_description_test", "item");

            Assert.AreEqual("A scratch table.", model.Description);
            Assert.AreEqual("The amount, in dollars.", model.Columns.Single(c => c.Name == "Amount").Description);
            Assert.IsNull(model.Columns.Single(c => c.Name == "note").Description);
        }
        finally
        {
            await Run("DROP SCHEMA IF EXISTS codegen_description_test CASCADE");
        }
    }

    [TestMethod]
    public async Task MySQL_reads_the_COMMENT_of_a_table_and_its_columns()
    {
        string? host = Env("CODEGENNEW_MYSQL_HOST"), database = Env("CODEGENNEW_MYSQL_DATABASE"), user = Env("CODEGENNEW_MYSQL_USER");
        if (host is null || database is null || user is null)
            Assert.Inconclusive("Set CODEGENNEW_MYSQL_HOST, _DATABASE, _USER and _PASSWORD to run the MySQL description test.");
        var request = new ConnectionRequest { Provider = DatabaseProvider.MySql, ServerName = host!, DatabaseName = database!, AuthMode = AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_MYSQL_PASSWORD") };

        await using var connection = request.CreateMySqlConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new MySqlConnector.MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP TABLE IF EXISTS codegen_description_test");
            await Run("CREATE TABLE codegen_description_test (id int AUTO_INCREMENT PRIMARY KEY, amount decimal(10,2) NULL COMMENT 'The amount, in dollars.', note varchar(50) NULL) COMMENT = 'A scratch table.'");

            var model = await new MySqlSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config")).BuildTableModelAsync(database!, "codegen_description_test");

            Assert.AreEqual("A scratch table.", model.Description);
            Assert.AreEqual("The amount, in dollars.", model.Columns.Single(c => c.Name == "amount").Description);
            Assert.IsNull(model.Columns.Single(c => c.Name == "note").Description);
        }
        finally
        {
            await Run("DROP TABLE IF EXISTS codegen_description_test");
        }
    }
}
