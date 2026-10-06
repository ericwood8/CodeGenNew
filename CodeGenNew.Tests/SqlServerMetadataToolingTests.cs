using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The SQL Server scripts written from the catalog: a key sequence in place of IDENTITY, replication triggers and a bulk update of one column everywhere. </summary>
[TestClass]
public class SqlServerMetadataToolingTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Gadget() => Sample.Table("Gadget",
    [
        Sample.Column("GadgetId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("Fnd", SqlDbType.VarChar, nullable: true, characters: 10, ordinal: 3)
    ]);

    private static DatabaseModel Database(SqlDialect dialect = SqlDialect.SqlServer, params TableModel[] tables) => new()
    {
        DatabaseName = "Acme",
        SchemaName = "dbo",
        Dialect = dialect,
        Tables = tables.Length > 0 ? tables.ToList() : [Gadget(), Sample.DonateLeave()]
    };

    private static async Task<TemplateResult> Run(string template, TableModel table, ProjectSettings project) => await Repo.Cache.RunAsync(Repo.Template(template), table, project);
    private static async Task<TemplateResult> Run(string template, DatabaseModel database, ProjectSettings project) => await Repo.Cache.RunAsync(Repo.Template(template), database, project);

    private static string Text(TemplateResult result)
    {
        Assert.IsTrue(result.Success, "errors=" + result.Errors.Count + ": " + string.Join(" | ", result.Errors) + " text=" + result.GeneratedText);
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------ the key sequence

    [TestMethod]
    public async Task The_sequence_script_makes_the_table_seeds_each_counter_and_writes_GetNextID()
    {
        string sql = Text(await Run("SP_KeySequence_v1.tt", Database(), Project(("KeySequenceTables", "Gadget"))));

        Expect.Contains(sql, "@@@FILE KeySequence.sql@@@");
        Expect.Contains(sql, "CREATE TABLE [dbo].[AutoInc] (");
        Expect.Contains(sql, "CONSTRAINT [PK_AutoInc] PRIMARY KEY ([TableName], [FieldName])");
        Expect.Contains(sql, "IF NOT EXISTS (SELECT 1 FROM [dbo].[AutoInc] WHERE [TableName] = 'Gadget' AND [FieldName] = 'GadgetId')");
        Expect.Contains(sql, "SELECT 'Gadget', 'GadgetId', ISNULL(MAX([GadgetId]), 0) FROM [dbo].[Gadget];");
        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[GetNextID]");
        Expect.Contains(sql, "@NextId    int OUTPUT");
        Expect.Contains(sql, "WITH (TABLOCKX)");
        Expect.DoesNotContain(sql, "E_DonateLeave");   // not listed
    }

    [TestMethod]
    public async Task The_sequence_table_has_the_projects_name()
    {
        string sql = Text(await Run("SP_KeySequence_v1.tt", Database(), Project(("KeySequenceTables", "Gadget"), ("KeySequenceTable", "KeyCounter"))));

        Expect.Contains(sql, "CREATE TABLE [dbo].[KeyCounter] (");
        Expect.DoesNotContain(sql, "AutoInc");
    }

    [TestMethod]
    public async Task The_sequence_script_refuses_what_it_cannot_serve()
    {
        Assert.IsFalse((await Run("SP_KeySequence_v1.tt", Database(), Project())).Success, "no tables listed");
        var unknown = await Run("SP_KeySequence_v1.tt", Database(), Project(("KeySequenceTables", "Nothing")));
        Assert.IsTrue(unknown.Errors.Any(e => e.Contains("Nothing")));
        var identity = await Run("SP_KeySequence_v1.tt", Database(), Project(("KeySequenceTables", "E_DonateLeave")));
        Assert.IsTrue(identity.Errors.Any(e => e.Contains("IDENTITY")));
        Assert.IsFalse((await Run("SP_KeySequence_v1.tt", Database(SqlDialect.PostgreSql), Project(("KeySequenceTables", "Gadget")))).Success, "SQL Server only");
    }

    [TestMethod]
    public async Task A_listed_tables_insert_asks_the_sequence_for_its_key()
    {
        string sql = Text(await Run("SP_Insert_v1.tt", Gadget(), Project(("KeySequenceTables", "Gadget"))));

        Expect.DoesNotContain(sql, "@pGadgetId");   // no key parameter
        Expect.Contains(sql, "DECLARE @NewKey int;");
        Expect.Contains(sql, "EXEC [dbo].[GetNextID] 'Gadget', 'GadgetId', @NewKey OUTPUT;");
        Expect.Contains(sql, "[GadgetId],");
        Expect.Contains(sql, "@NewKey,");
        Expect.Contains(sql, "SELECT @NewKey AS [GadgetId];");
    }

    [TestMethod]
    public async Task A_table_that_is_not_listed_keeps_its_own_key_rules()
    {
        string sql = Text(await Run("SP_Insert_v1.tt", Gadget(), Project()));

        Expect.Contains(sql, "@pGadgetId int");
        Expect.DoesNotContain(sql, "GetNextID");
    }

    [TestMethod]
    public async Task A_listed_table_with_an_identity_key_is_refused_by_its_insert()
    {
        var result = await Run("SP_Insert_v1.tt", Sample.DonateLeave(), Project(("KeySequenceTables", "E_DonateLeave")));

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("KeySequenceTables")));
    }

    // ------------------------------------------------------------------ the replication triggers

    [TestMethod]
    public async Task The_three_triggers_copy_each_change_to_every_target()
    {
        string sql = Text(await Run("SP_ReplicationTriggers_v1.tt", Sample.DonateLeave(), Project(("ReplicationTargets", "east.Sales, west.Sales"))));

        Expect.Contains(sql, "CREATE OR ALTER TRIGGER [dbo].[TR_E_DonateLeave_Replicate_I]");
        Expect.Contains(sql, "ON [dbo].[E_DonateLeave] AFTER INSERT");
        Expect.Contains(sql, "DECLARE insert_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT [DonateLeaveId], [DonateFrom_EmployeeId], [DonateTo_EmployeeId], [WhenDonated], [HoursDonated], [Note] FROM inserted;");
        Expect.Contains(sql, "EXEC [east].[Sales].[dbo].[E_DonateLeave_Replicate_Insert] @pDonateLeaveId, @pDonateFrom_EmployeeId");
        Expect.Contains(sql, "EXEC [west].[Sales].[dbo].[E_DonateLeave_Replicate_Insert] @pDonateLeaveId, @pDonateFrom_EmployeeId");

        Expect.Contains(sql, "ON [dbo].[E_DonateLeave] AFTER UPDATE");
        Expect.Contains(sql, "IF UPDATE([DonateLeaveId])");
        Expect.Contains(sql, "ROLLBACK TRANSACTION;");
        Expect.Contains(sql, "IF UPDATE([DonateFrom_EmployeeId]) OR UPDATE([DonateTo_EmployeeId]) OR UPDATE([WhenDonated]) OR UPDATE([HoursDonated]) OR UPDATE([Note])");
        Expect.Contains(sql, "[E_DonateLeave_Replicate_Update]");

        Expect.Contains(sql, "ON [dbo].[E_DonateLeave] AFTER DELETE");
        Expect.Contains(sql, "SELECT [DonateLeaveId] FROM deleted;");
        Expect.Contains(sql, "EXEC [east].[Sales].[dbo].[E_DonateLeave_Replicate_Delete] @pDonateLeaveId;");
    }

    [TestMethod]
    public async Task A_column_a_trigger_cannot_read_is_left_out()
    {
        var table = Sample.Table("Memo",
        [
            Sample.Column("MemoId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("Body", SqlDbType.Text, nullable: true, ordinal: 3)
        ]);

        string sql = Text(await Run("SP_ReplicationTriggers_v1.tt", table, Project(("ReplicationTargets", "east.Sales"))));

        Expect.Contains(sql, "SELECT [MemoId], [Title] FROM inserted;");
        Expect.DoesNotContain(sql, "[Body]");
    }

    [TestMethod]
    public async Task The_triggers_refuse_a_missing_target_a_malformed_target_and_a_table_without_a_key()
    {
        Assert.IsFalse((await Run("SP_ReplicationTriggers_v1.tt", Sample.DonateLeave(), Project())).Success);
        var bad = await Run("SP_ReplicationTriggers_v1.tt", Sample.DonateLeave(), Project(("ReplicationTargets", "east")));
        Assert.IsTrue(bad.Errors.Any(e => e.Contains("east")));
        var keyless = Sample.Table("Log", [Sample.Column("Text", SqlDbType.NVarChar, characters: 50, ordinal: 1)]);
        var noKey = await Run("SP_ReplicationTriggers_v1.tt", keyless, Project(("ReplicationTargets", "east.Sales")));
        Assert.IsTrue(noKey.Errors.Any(e => e.Contains("primary key")));
    }

    [TestMethod]
    public async Task The_triggers_refuse_a_table_read_from_another_database()
    {
        var source = Sample.DonateLeave();
        var postgres = new TableModel
        {
            SchemaName = source.SchemaName, TableName = source.TableName, QuotedName = source.QuotedName, Dialect = SqlDialect.PostgreSql,
            Columns = source.Columns, PrimaryKeyColumns = source.PrimaryKeyColumns, ForeignKeys = [], ChildForeignKeys = []
        };

        var result = await Run("SP_ReplicationTriggers_v1.tt", postgres, Project(("ReplicationTargets", "east.Sales")));

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("SQL Server")));
    }

    // ------------------------------------------------------------------ the bulk update

    [TestMethod]
    public async Task The_bulk_update_rewrites_the_column_in_every_table_that_has_it()
    {
        var other = Sample.Table("Ledger",
        [
            Sample.Column("LedgerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("ACCT", SqlDbType.VarChar, characters: 10, ordinal: 2)
        ]);

        string sql = Text(await Run("SP_BulkUpdate_v1.tt", Database(SqlDialect.SqlServer, Gadget(), other, Sample.DonateLeave()), Project(("BulkUpdateColumns", "Fnd,Acct"))));

        Expect.Contains(sql, "CREATE OR ALTER PROCEDURE [dbo].[BulkUpdateColumns]");
        Expect.Contains(sql, "UPDATE [dbo].[Gadget] SET [Fnd] = UPPER([Fnd]) WHERE [Fnd] IS NOT NULL;");
        Expect.Contains(sql, "UPDATE [dbo].[Ledger] SET [ACCT] = UPPER([ACCT]) WHERE [ACCT] IS NOT NULL;");
        Expect.DoesNotContain(sql, "UPDATE [dbo].[E_DonateLeave]");
        Expect.Contains(sql, "(2 columns)");
    }

    [TestMethod]
    public async Task The_bulk_update_uses_the_projects_expression_and_leaves_key_columns_alone()
    {
        var keyed = Sample.Table("Code", [Sample.Column("Fnd", SqlDbType.VarChar, primaryKey: true, characters: 10, ordinal: 1)]);

        string sql = Text(await Run("SP_BulkUpdate_v1.tt", Database(SqlDialect.SqlServer, Gadget(), keyed), Project(("BulkUpdateColumns", "Fnd"), ("BulkUpdateExpression", "LTRIM(RTRIM({column}))"))));

        Expect.Contains(sql, "UPDATE [dbo].[Gadget] SET [Fnd] = LTRIM(RTRIM([Fnd])) WHERE [Fnd] IS NOT NULL;");
        Expect.DoesNotContain(sql, "[dbo].[Code]");
    }

    [TestMethod]
    public async Task The_bulk_update_refuses_no_columns_an_expression_without_the_column_and_another_database()
    {
        Assert.IsFalse((await Run("SP_BulkUpdate_v1.tt", Database(), Project())).Success);
        Assert.IsFalse((await Run("SP_BulkUpdate_v1.tt", Database(), Project(("BulkUpdateColumns", "Fnd"), ("BulkUpdateExpression", "UPPER(x)")))).Success);
        Assert.IsFalse((await Run("SP_BulkUpdate_v1.tt", Database(SqlDialect.MySql), Project(("BulkUpdateColumns", "Fnd")))).Success);
    }

    // ------------------------------------------------------------------ the plan leaves them out unless asked

    [TestMethod]
    public void The_scripts_are_part_of_a_plan_only_when_the_project_names_them()
    {
        foreach (string name in new[] { "SP_KeySequence_v1.tt.config", "SP_ReplicationTriggers_v1.tt.config", "SP_BulkUpdate_v1.tt.config" })
        {
            var config = TemplateConfig.Load(Repo.Template(name));
            Assert.IsFalse(config.InPlan, name);
            CollectionAssert.AreEqual(new[] { "SqlServer" }, config.Dialects.ToArray(), name);
        }
    }
}
