using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> The key a table with no primary key takes (a unique index, then its identity column), read from a real PostgreSQL and MySQL server. They run only when the integration variables of those servers are set
/// (CODEGENNEW_PG_* and CODEGENNEW_MYSQL_*); otherwise each reports itself as inconclusive. Each makes throwaway tables and drops them again. </summary>
[TestClass]
public class TableKeyChoiceServerTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config");

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static ConnectionRequest Request(DatabaseProvider provider, string prefix)
    {
        string? host = Env($"CODEGENNEW_{prefix}_HOST"), database = Env($"CODEGENNEW_{prefix}_DATABASE"), user = Env($"CODEGENNEW_{prefix}_USER");
        if (host is null || database is null || user is null)
            Assert.Inconclusive($"Set CODEGENNEW_{prefix}_HOST, CODEGENNEW_{prefix}_DATABASE, CODEGENNEW_{prefix}_USER and CODEGENNEW_{prefix}_PASSWORD to run this test.");
        return new ConnectionRequest { Provider = provider, ServerName = host!, DatabaseName = database!, AuthMode = AuthMode.SqlLogin, UserName = user, Password = Env($"CODEGENNEW_{prefix}_PASSWORD") };
    }

    /// <param name="promotes"> MySQL reports the first unique index of NOT NULL columns as the table's primary key itself, so the key is there with no source of ours. </param>
    private static async Task Check(ISchemaProvider provider, string schema, bool promotes = false)
    {
        var unique = await provider.BuildTableModelAsync(schema, "codegen_k_unique");
        CollectionAssert.AreEqual(new[] { "code" }, unique.PrimaryKeyColumns.Select(c => c.Name).ToList());
        if (!promotes)
            StringAssert.StartsWith(unique.KeySource, "the unique index");

        var serial = await provider.BuildTableModelAsync(schema, "codegen_k_serial");
        CollectionAssert.AreEqual(new[] { "memo_no" }, serial.PrimaryKeyColumns.Select(c => c.Name).ToList());
        Assert.AreEqual("the identity column", serial.KeySource);

        var expression = await provider.BuildTableModelAsync(schema, "codegen_k_expression");
        Assert.IsEmpty(expression.PrimaryKeyColumns, "an index on an expression says nothing about the column");
        StringAssert.Contains(expression.KeyAdvice, "no primary key and no unique index");

        var nullable = await provider.BuildTableModelAsync(schema, "codegen_k_nullable");
        Assert.IsEmpty(nullable.PrimaryKeyColumns, "a unique index on a column that allows NULL is not a key");

        var advice = await provider.BuildTableModelAsync(schema, "codegen_k_advice");
        Assert.IsEmpty(advice.PrimaryKeyColumns);
        StringAssert.Contains(advice.KeyAdvice, "KeyColumns=codegen_k_advice=id (id has no duplicate value today)");
    }

    [TestMethod]
    public async Task PostgreSQL_takes_a_unique_index_or_a_serial_column_as_the_key_and_ignores_partial_and_expression_indexes()
    {
        var request = Request(DatabaseProvider.PostgreSql, "PG");
        await using var connection = request.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        string[] tables = ["codegen_k_unique", "codegen_k_serial", "codegen_k_expression", "codegen_k_nullable", "codegen_k_advice"];
        try
        {
            foreach (string t in tables)
                await Run($"DROP TABLE IF EXISTS public.{t}");
            await Run("CREATE TABLE public.codegen_k_unique (code varchar(20) NOT NULL, title text)");
            await Run("CREATE UNIQUE INDEX ux_codegen_k_unique ON public.codegen_k_unique (code)");
            await Run("CREATE TABLE public.codegen_k_serial (memo_no serial NOT NULL, note text)");
            await Run("CREATE TABLE public.codegen_k_expression (code varchar(20) NOT NULL, flag int NOT NULL)");
            await Run("CREATE UNIQUE INDEX ux_codegen_k_expression ON public.codegen_k_expression (lower(code))");
            await Run("CREATE UNIQUE INDEX ux_codegen_k_expression_partial ON public.codegen_k_expression (code) WHERE flag > 0");
            await Run("CREATE TABLE public.codegen_k_nullable (code varchar(20), title text)");
            await Run("CREATE UNIQUE INDEX ux_codegen_k_nullable ON public.codegen_k_nullable (code)");

            await Run("CREATE TABLE public.codegen_k_advice (id int NOT NULL, note text)");
            await Run("INSERT INTO public.codegen_k_advice VALUES (1, 'a'), (2, 'b')");

            await Check(new PostgresSchemaProvider(request, ConfigPath), "public");
        }
        finally
        {
            foreach (string t in tables)
                await Run($"DROP TABLE IF EXISTS public.{t}");
        }
    }

    [TestMethod]
    public async Task MySQL_takes_a_unique_index_or_an_auto_increment_column_as_the_key_and_ignores_a_functional_index()
    {
        var request = Request(DatabaseProvider.MySql, "MYSQL");
        await using var connection = request.CreateMySqlConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        string[] tables = ["codegen_k_unique", "codegen_k_serial", "codegen_k_expression", "codegen_k_nullable", "codegen_k_advice"];
        try
        {
            foreach (string t in tables)
                await Run($"DROP TABLE IF EXISTS {t}");
            await Run("CREATE TABLE codegen_k_unique (code varchar(20) NOT NULL, title text, UNIQUE KEY ux_codegen_k_unique (code))");
            await Run("CREATE TABLE codegen_k_serial (memo_no int NOT NULL AUTO_INCREMENT, note text, KEY ix_codegen_k_serial (memo_no))");
            await Run("CREATE TABLE codegen_k_expression (code varchar(20) NOT NULL, flag int NOT NULL, UNIQUE KEY ux_codegen_k_expression ((lower(code)), flag))");
            await Run("CREATE TABLE codegen_k_nullable (code varchar(20) NULL, title text, UNIQUE KEY ux_codegen_k_nullable (code))");

            await Run("CREATE TABLE codegen_k_advice (id int NOT NULL, note text)");
            await Run("INSERT INTO codegen_k_advice VALUES (1, 'a'), (2, 'b')");

            await Check(new MySqlSchemaProvider(request, ConfigPath), Env("CODEGENNEW_MYSQL_DATABASE")!, promotes: true);
        }
        finally
        {
            foreach (string t in tables)
                await Run($"DROP TABLE IF EXISTS {t}");
        }
    }
}
