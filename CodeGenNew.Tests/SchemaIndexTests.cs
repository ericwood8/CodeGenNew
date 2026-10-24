using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> The indexes a table has are read into the model so a template can tell which foreign keys are unindexed. Live tests on a scratch table, dropped again. </summary>
[TestClass]
public class SchemaIndexTests
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config");

    [TestMethod]
    public void An_index_covers_a_foreign_key_when_it_starts_with_exactly_its_columns()
    {
        var table = Sample.Table("Ticket", [Sample.Column("TicketId", System.Data.SqlDbType.Int, primaryKey: true, ordinal: 1)], indexes:
        [
            new IndexModel("PK_Ticket", true, ["TicketId"]),
            new IndexModel("IX_Ticket_Owner_Kind", false, ["OwnerId", "KindId"])
        ]);

        Assert.IsTrue(table.IsIndexed(["OwnerId"]));
        Assert.IsTrue(table.IsIndexed(["KindId", "OwnerId"]), "the order of the foreign key's own columns does not matter");
        Assert.IsFalse(table.IsIndexed(["KindId"]), "a column that is not first is not covered");
        Assert.IsFalse(table.IsIndexed(["OwnerId", "KindId", "Other"]));
    }

    private static void AssertIndexes(TableModel model, string ownerColumn, string kindColumn, string otherColumn)
    {
        Assert.IsTrue(model.Indexes.Any(i => i.IsUnique && i.Columns.Count == 1), "the primary key is an index");
        var composite = model.Indexes.Single(i => i.Columns.Count == 2);
        CollectionAssert.AreEqual(new[] { ownerColumn, kindColumn }, composite.Columns.ToArray());
        Assert.IsFalse(composite.IsUnique);
        Assert.IsTrue(model.IsIndexed([ownerColumn]));
        Assert.IsFalse(model.IsIndexed([kindColumn]));
        Assert.IsFalse(model.IsIndexed([otherColumn]));
    }

    /// <summary> What only the create script needs: the whole CHECK constraint (it names two columns) and, where the database has them, the partial index with its row condition. </summary>
    private static void AssertScriptParts(TableModel model, string? partialIndex, string? partialColumn)
    {
        var check = model.CheckConstraints.Single();
        Assert.AreEqual("ck_owner_kind", check.Name, ignoreCase: true);
        StringAssert.StartsWith(check.Definition, "CHECK ");
        StringAssert.Contains(check.Definition, "kind_id", StringComparison.OrdinalIgnoreCase);
        if (partialIndex is null)
        {
            Assert.AreEqual(0, model.PartialIndexes.Count);
            return;
        }
        var partial = model.PartialIndexes.Single();
        Assert.AreEqual(partialIndex, partial.Name);
        CollectionAssert.AreEqual(new[] { partialColumn! }, partial.Columns.ToArray());
        StringAssert.Contains(partial.Filter, partialColumn!, StringComparison.OrdinalIgnoreCase);
        Assert.IsFalse(model.Indexes.Any(i => i.Name == partialIndex), "the plain index list still leaves the partial index out");
    }

    /// <summary> The view and the trigger the create script writes back. </summary>
    private static void AssertViewAndTrigger(TableModel model, IReadOnlyList<ViewModel> views, string viewName, string triggerName)
    {
        var view = views.Single(v => v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));
        StringAssert.StartsWith(view.Definition, "CREATE ", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(view.Definition, "note", StringComparison.OrdinalIgnoreCase);
        var trigger = model.Triggers.Single();
        Assert.AreEqual(triggerName, trigger.Name, ignoreCase: true);
        StringAssert.Contains(trigger.Definition, "TRIGGER", StringComparison.OrdinalIgnoreCase);
        Assert.IsFalse(trigger.IsDisabled);
    }

    [TestMethod]
    public async Task SQL_Server_reads_the_key_columns_of_every_index()
    {
        string? host = Env("CODEGENNEW_SQLSERVER_HOST"), database = Env("CODEGENNEW_SQLSERVER_DATABASE");
        if (host is null || database is null)
            Assert.Inconclusive("Set CODEGENNEW_SQLSERVER_HOST and CODEGENNEW_SQLSERVER_DATABASE to run the SQL Server index test.");
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
            await Run("IF OBJECT_ID('dbo.codegen_index_test') IS NOT NULL DROP TABLE dbo.codegen_index_test");
            await Run("CREATE TABLE dbo.codegen_index_test (id int IDENTITY PRIMARY KEY, owner_id int NOT NULL, kind_id int NOT NULL, other_id int NULL, note nvarchar(50) NULL, CONSTRAINT ck_owner_kind CHECK (owner_id > 0 AND kind_id >= owner_id))");
            await Run("CREATE INDEX ix_owner_kind ON dbo.codegen_index_test (owner_id, kind_id) INCLUDE (note)");
            await Run("CREATE INDEX ix_filtered ON dbo.codegen_index_test (other_id) WHERE other_id IS NOT NULL");
            await Run("CREATE VIEW dbo.codegen_index_view AS SELECT id, note FROM dbo.codegen_index_test");
            await Run("CREATE TRIGGER dbo.codegen_index_trigger ON dbo.codegen_index_test AFTER INSERT AS SET NOCOUNT ON;");

            var provider = new SqlServerSchemaProvider(request, ConfigPath);
            var model = await provider.BuildTableModelAsync("dbo", "codegen_index_test");

            AssertIndexes(model, "owner_id", "kind_id", "other_id");
            AssertScriptParts(model, "ix_filtered", "other_id");
            AssertViewAndTrigger(model, await provider.ListViewsAsync("dbo"), "codegen_index_view", "codegen_index_trigger");
        }
        finally
        {
            await Run("IF OBJECT_ID('dbo.codegen_index_view') IS NOT NULL DROP VIEW dbo.codegen_index_view");
            await Run("IF OBJECT_ID('dbo.codegen_index_test') IS NOT NULL DROP TABLE dbo.codegen_index_test");
        }
    }

    [TestMethod]
    public async Task PostgreSQL_reads_the_key_columns_of_every_index()
    {
        string? host = Env("CODEGENNEW_PG_HOST"), database = Env("CODEGENNEW_PG_DATABASE"), user = Env("CODEGENNEW_PG_USER");
        if (host is null || database is null || user is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, _DATABASE, _USER and _PASSWORD to run the PostgreSQL index test.");
        var request = new ConnectionRequest { Provider = DatabaseProvider.PostgreSql, ServerName = host!, DatabaseName = database!, AuthMode = AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_PG_PASSWORD") };

        await using var connection = request.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP SCHEMA IF EXISTS codegen_index_test CASCADE");
            await Run("CREATE SCHEMA codegen_index_test");
            await Run("CREATE TABLE codegen_index_test.item (item_id serial PRIMARY KEY, owner_id int NOT NULL, kind_id int NOT NULL, other_id int NULL, note varchar(50) NULL, CONSTRAINT ck_owner_kind CHECK (owner_id > 0 AND kind_id >= owner_id))");
            await Run("CREATE INDEX ix_owner_kind ON codegen_index_test.item (owner_id, kind_id) INCLUDE (note)");
            await Run("CREATE INDEX ix_partial ON codegen_index_test.item (other_id) WHERE other_id IS NOT NULL");
            await Run("CREATE INDEX ix_expression ON codegen_index_test.item (lower(note))");
            await Run("CREATE VIEW codegen_index_test.item_view AS SELECT item_id, note FROM codegen_index_test.item");
            await Run("CREATE FUNCTION codegen_index_test.item_stamp() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN NEW.note := coalesce(NEW.note, 'x'); RETURN NEW; END $$");
            await Run("CREATE TRIGGER item_stamp BEFORE INSERT ON codegen_index_test.item FOR EACH ROW EXECUTE FUNCTION codegen_index_test.item_stamp()");

            var provider = new PostgresSchemaProvider(request, ConfigPath);
            var model = await provider.BuildTableModelAsync("codegen_index_test", "item");

            AssertIndexes(model, "owner_id", "kind_id", "other_id");
            AssertScriptParts(model, "ix_partial", "other_id");
            AssertViewAndTrigger(model, await provider.ListViewsAsync("codegen_index_test"), "item_view", "item_stamp");
            StringAssert.Contains(model.Triggers.Single().Definition, "CREATE OR REPLACE FUNCTION", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await Run("DROP SCHEMA IF EXISTS codegen_index_test CASCADE");
        }
    }

    [TestMethod]
    public async Task MySQL_reads_the_key_columns_of_every_index()
    {
        string? host = Env("CODEGENNEW_MYSQL_HOST"), database = Env("CODEGENNEW_MYSQL_DATABASE"), user = Env("CODEGENNEW_MYSQL_USER");
        if (host is null || database is null || user is null)
            Assert.Inconclusive("Set CODEGENNEW_MYSQL_HOST, _DATABASE, _USER and _PASSWORD to run the MySQL index test.");
        var request = new ConnectionRequest { Provider = DatabaseProvider.MySql, ServerName = host!, DatabaseName = database!, AuthMode = AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_MYSQL_PASSWORD") };

        await using var connection = request.CreateMySqlConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new MySqlConnector.MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP TABLE IF EXISTS codegen_index_test");
            await Run("CREATE TABLE codegen_index_test (id int AUTO_INCREMENT PRIMARY KEY, owner_id int NOT NULL, kind_id int NOT NULL, other_id int NULL, note varchar(50) NULL, CONSTRAINT ck_owner_kind CHECK (owner_id > 0 AND kind_id >= owner_id))");
            await Run("CREATE INDEX ix_owner_kind ON codegen_index_test (owner_id, kind_id)");
            await Run("CREATE VIEW codegen_index_view AS SELECT id, note FROM codegen_index_test");
            // a login without SUPER on a server with binary logging cannot create a trigger (error 1419); the view is still checked
            bool triggerMade = true;
            try { await Run("CREATE TRIGGER codegen_index_trigger BEFORE INSERT ON codegen_index_test FOR EACH ROW SET NEW.note = 'x'"); }
            catch (MySqlConnector.MySqlException problem) when (problem.Number == 1419) { triggerMade = false; }

            var provider = new MySqlSchemaProvider(request, ConfigPath);
            var model = await provider.BuildTableModelAsync(database!, "codegen_index_test");

            AssertIndexes(model, "owner_id", "kind_id", "other_id");
            AssertScriptParts(model, null, null);
            var views = await provider.ListViewsAsync(database!);
            if (triggerMade)
                AssertViewAndTrigger(model, views, "codegen_index_view", "codegen_index_trigger");
            Assert.IsTrue(views.Any(v => v.Name == "codegen_index_view"));
            Assert.IsFalse(views.Single(v => v.Name == "codegen_index_view").Definition.Contains('`' + database! + "`."), "the database name is left out of the view");
        }
        finally
        {
            await Run("DROP VIEW IF EXISTS codegen_index_view");
            await Run("DROP TABLE IF EXISTS codegen_index_test");
        }
    }
}
