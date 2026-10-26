using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> A table the database gives no primary key still gets a key from a unique index or the KeyColumns setting; one with neither says what to do. </summary>
[TestClass]
public class TableKeyChoiceTests
{
    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config");

    private static async Task<TableModel> ReadAsync(string table, string[] statements, params string[] keyColumns)
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_key_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString))
            {
                connection.Open();
                foreach (string statement in statements)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            }
            var provider = new SqliteSchemaProvider(new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = path }, ConfigPath) { KeyColumns = keyColumns };
            return await provider.BuildTableModelAsync("main", table);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task A_unique_index_on_not_null_columns_is_the_key()
    {
        var model = await ReadAsync("Memo", ["CREATE TABLE Memo (Code TEXT NOT NULL, Note TEXT)", "CREATE UNIQUE INDEX UX_Memo_Code ON Memo (Code)"]);

        CollectionAssert.AreEqual(new[] { "Code" }, model.PrimaryKeyColumns.Select(c => c.Name).ToList());
        Assert.AreEqual("the unique index UX_Memo_Code", model.KeySource);
        Assert.IsNull(model.KeyAdvice);
    }

    [TestMethod]
    public async Task The_unique_index_with_the_fewest_columns_wins_and_an_index_with_a_nullable_column_is_passed_over()
    {
        var model = await ReadAsync("Line", ["CREATE TABLE Line (A INT NOT NULL, B INT NOT NULL, C INT)",
            "CREATE UNIQUE INDEX UX_Line_AB ON Line (A, B)", "CREATE UNIQUE INDEX UX_Line_C ON Line (C)", "CREATE UNIQUE INDEX UX_Line_B ON Line (B)"]);

        CollectionAssert.AreEqual(new[] { "B" }, model.PrimaryKeyColumns.Select(c => c.Name).ToList());
        Assert.AreEqual("the unique index UX_Line_B", model.KeySource);
    }

    [TestMethod]
    public async Task A_declared_primary_key_is_left_alone()
    {
        var model = await ReadAsync("Item", ["CREATE TABLE Item (ItemId INTEGER PRIMARY KEY, Code TEXT NOT NULL)", "CREATE UNIQUE INDEX UX_Item_Code ON Item (Code)"], "Item=Code");

        CollectionAssert.AreEqual(new[] { "ItemId" }, model.PrimaryKeyColumns.Select(c => c.Name).ToList());
        Assert.IsNull(model.KeySource);
    }

    [TestMethod]
    public async Task The_KeyColumns_setting_names_the_key_even_for_two_columns_and_comes_before_an_index()
    {
        var model = await ReadAsync("Line", ["CREATE TABLE Line (OrderId INT NOT NULL, LineNo INT NOT NULL, Code TEXT NOT NULL)", "CREATE UNIQUE INDEX UX_Line_Code ON Line (Code)"], "line=OrderId+LineNo");

        CollectionAssert.AreEqual(new[] { "OrderId", "LineNo" }, model.PrimaryKeyColumns.Select(c => c.Name).ToList());
        Assert.AreEqual("the KeyColumns setting", model.KeySource);
        Assert.AreEqual(PrimaryKeyShape.Composite, model.PrimaryKeyShape);
    }

    [TestMethod]
    public async Task A_table_with_no_key_and_an_Id_column_without_duplicates_is_told_the_setting_to_paste()
    {
        var model = await ReadAsync("Customer_Memos", ["CREATE TABLE Customer_Memos (Id INT NOT NULL, Memo TEXT)", "INSERT INTO Customer_Memos VALUES (1, 'a'), (2, 'b')"]);

        Assert.IsEmpty(model.PrimaryKeyColumns);
        StringAssert.Contains(model.KeyAdvice, "no primary key and no unique index");
        StringAssert.Contains(model.KeyAdvice, "KeyColumns=Customer_Memos=Id (Id has no duplicate value today)");
    }

    [TestMethod]
    public async Task An_Id_column_with_duplicates_or_NULL_is_not_offered_as_a_key()
    {
        var duplicates = await ReadAsync("Memo", ["CREATE TABLE Memo (Id INT NOT NULL, Note TEXT)", "INSERT INTO Memo VALUES (1, 'a'), (1, 'b')"]);
        StringAssert.Contains(duplicates.KeyAdvice, "Id has duplicate values");
        Assert.DoesNotContain("KeyColumns=Memo=Id", duplicates.KeyAdvice);

        var nullable = await ReadAsync("Memo", ["CREATE TABLE Memo (Id INT, Note TEXT)"]);
        StringAssert.Contains(nullable.KeyAdvice, "Id allows NULL");
    }

    [TestMethod]
    public async Task A_table_with_nothing_to_go_on_is_told_to_add_a_key_or_name_the_columns()
    {
        var model = await ReadAsync("Journal", ["CREATE TABLE Journal (Entry TEXT, Posted TEXT)"]);

        StringAssert.Contains(model.KeyAdvice, "KeyColumns=Journal=<the columns that identify a row>");
    }

    [TestMethod]
    public async Task A_KeyColumns_entry_naming_a_missing_or_nullable_column_leaves_the_table_without_a_key_and_says_why()
    {
        var missing = await ReadAsync("Memo", ["CREATE TABLE Memo (Id INT NOT NULL)"], "Memo=Nope");
        Assert.IsEmpty(missing.PrimaryKeyColumns);
        StringAssert.Contains(missing.KeyAdvice, "KeyColumns names Nope, which is not a column of the table");

        var nullable = await ReadAsync("Memo", ["CREATE TABLE Memo (Id INT)"], "Memo=Id");
        Assert.IsEmpty(nullable.PrimaryKeyColumns);
        StringAssert.Contains(nullable.KeyAdvice, "KeyColumns names Id, which allows NULL");
    }

    [TestMethod]
    public async Task The_run_notes_say_where_a_key_came_from_and_what_a_skipped_table_needs()
    {
        var indexed = await ReadAsync("Memo", ["CREATE TABLE Memo (Code TEXT NOT NULL)", "CREATE UNIQUE INDEX UX_Memo_Code ON Memo (Code)"]);
        var keyless = await ReadAsync("Journal", ["CREATE TABLE Journal (Entry TEXT)"]);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "main", Tables = [indexed, keyless] };
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme")]);

        var notes = TableShapeNotes.Of([], database, project, ["Api"]);

        CollectionAssert.Contains(notes, "Memo: the database declares no primary key; Code from the unique index UX_Memo_Code is its key. A real primary key in the database would be better.");
        Assert.IsTrue(notes.Any(n => n.StartsWith("Journal: no template of the chosen technologies fits its shape (no primary key and no unique index; add a key in the database, or set KeyColumns=Journal=")));
    }

    private static RawColumn Raw(string name, bool identity = false, bool nullable = false, bool key = false) =>
        new(name, 1, "int", 0, 10, 0, nullable, identity, null, null, null, null, key, false);

    private static KeyPick Pick(List<RawColumn> columns, IReadOnlyList<IndexRow>? indexes = null, string[]? declared = null) =>
        KeyChooser.Choose(columns, indexes ?? [], declared, name => name);

    [TestMethod]
    public void An_identity_column_is_the_key_when_nothing_else_names_one()
    {
        var pick = Pick([Raw("Id", identity: true), Raw("Memo")]);

        CollectionAssert.AreEqual(new[] { "Id" }, pick.Columns!.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList());
        Assert.AreEqual("the identity column", pick.Source);
    }

    [TestMethod]
    public void A_unique_index_and_the_setting_come_before_the_identity_column()
    {
        var columns = new List<RawColumn> { Raw("Id", identity: true), Raw("Code"), Raw("Other") };

        Assert.AreEqual("the unique index UX_Code", Pick(columns, [new("UX_Code", true, ["Code"])]).Source);
        var declared = Pick(columns, [new("UX_Code", true, ["Code"])], ["Other"]);
        Assert.AreEqual("the KeyColumns setting", declared.Source);
        CollectionAssert.AreEqual(new[] { "Other" }, declared.Columns!.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void A_table_without_an_identity_a_unique_index_or_a_setting_has_no_key()
    {
        Assert.IsNull(Pick([Raw("Id"), Raw("Memo")]).Columns);
        Assert.IsNull(Pick([Raw("Id", identity: true, nullable: true)]).Columns, "a column that allows NULL cannot be a key");
        Assert.IsNull(Pick([Raw("A", identity: true), Raw("B", identity: true)]).Columns, "two identity columns say nothing about which row is which");
    }

    [TestMethod]
    public void A_setting_that_cannot_be_used_is_reported_even_when_another_key_serves()
    {
        var pick = Pick([Raw("Id", identity: true)], declared: ["Nope"]);

        Assert.AreEqual("the identity column", pick.Source, "the identity column still serves");
        StringAssert.Contains(pick.Problem, "Nope");
        Assert.IsNull(Pick([Raw("Id"), Raw("Memo")], declared: ["Nope"]).Columns);
        StringAssert.Contains(Pick([Raw("Id"), Raw("Memo")], declared: ["Nope"]).Problem, "Nope");
    }

    [TestMethod]
    public void The_setting_is_read_as_a_list_of_table_equals_columns()
    {
        var project = ProjectSettings.FromValues([new("KeyColumns", "Customer_Memos=Id, Line=OrderId+LineNo")]);

        CollectionAssert.AreEqual(new[] { "Customer_Memos=Id", "Line=OrderId+LineNo" }, project.KeyColumns);
        Assert.IsEmpty(ProjectSettings.FromValues([]).KeyColumns);
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    [TestMethod]
    public async Task SQL_Server_takes_the_one_identity_column_of_a_table_with_no_primary_key_as_its_key()
    {
        string? host = Env("CODEGENNEW_SQLSERVER_HOST"), database = Env("CODEGENNEW_SQLSERVER_DATABASE");
        if (host is null || database is null)
            Assert.Inconclusive("Set CODEGENNEW_SQLSERVER_HOST and CODEGENNEW_SQLSERVER_DATABASE to run the SQL Server key test.");
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
            await Run("IF OBJECT_ID('dbo.codegen_key_test') IS NOT NULL DROP TABLE dbo.codegen_key_test");
            await Run("CREATE TABLE dbo.codegen_key_test (memo_no int IDENTITY(1,1) NOT NULL, note nvarchar(50) NULL)");

            var model = await new SqlServerSchemaProvider(request, ConfigPath).BuildTableModelAsync("dbo", "codegen_key_test");

            CollectionAssert.AreEqual(new[] { "memo_no" }, model.PrimaryKeyColumns.Select(c => c.Name).ToList());
            Assert.AreEqual("the identity column", model.KeySource);
            Assert.IsNull(model.KeyAdvice);
        }
        finally
        {
            await Run("IF OBJECT_ID('dbo.codegen_key_test') IS NOT NULL DROP TABLE dbo.codegen_key_test");
        }
    }
}
