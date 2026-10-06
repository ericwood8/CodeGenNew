using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The audit trail (SP_AuditTable) and temporal table (SP_TemporalTable) scripts and the settings that choose their tables. </summary>
[TestClass]
public class AuditTemplateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Probe(SqlDialect dialect = SqlDialect.SqlServer) => Sample.Table("Probe",
    [
        Sample.Column("ProbeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("Amount", SqlDbType.Decimal, nullable: true, precision: 10, scale: 2, ordinal: 3),
        Sample.Column("CreateDate", SqlDbType.DateTime, ordinal: 4),
        Sample.Column("ModifiedDate", SqlDbType.DateTime, nullable: true, ordinal: 5)
    ], dialect: dialect);

    private static async Task<(bool Success, string Text)> Run(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        return (result.Success, result.Success ? result.GeneratedText!.Replace("\r\n", "\n") : string.Join(" | ", result.Errors));
    }

    [TestMethod]
    public async Task SQL_Server_gets_a_history_table_and_one_trigger_for_updates_and_deletes()
    {
        var (ok, text) = await Run("SP_AuditTable_v1.tt", Probe(), Project());

        Assert.IsTrue(ok, text);
        Expect.Contains(text, "IF OBJECT_ID(N'dbo.Probe_History', N'U') IS NULL");
        Expect.Contains(text, "[AuditId] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Probe_History] PRIMARY KEY,");
        Expect.Contains(text, "[AuditUser] nvarchar(128) NULL,");
        Expect.Contains(text, "CREATE OR ALTER TRIGGER [dbo].[TR_Probe_Audit]\nON [dbo].[Probe] AFTER UPDATE, DELETE");
        Expect.Contains(text, "SELECT CASE WHEN EXISTS (SELECT 1 FROM inserted) THEN 'U' ELSE 'D' END, SYSUTCDATETIME(), SUSER_SNAME(), [ProbeId], [Name], [Amount], [CreateDate], [ModifiedDate]");
        Expect.Contains(text, "FROM deleted;");
    }

    [TestMethod]
    public async Task PostgreSQL_gets_a_function_and_a_trigger()
    {
        var (ok, text) = await Run("SP_AuditTable_v1.tt", Probe(SqlDialect.PostgreSql), Project());

        Assert.IsTrue(ok, text);
        Expect.Contains(text, "CREATE TABLE IF NOT EXISTS \"dbo\".\"Probe_History\"");
        Expect.Contains(text, "\"AuditId\" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,");
        Expect.Contains(text, "CREATE OR REPLACE FUNCTION \"dbo\".\"Probe_Audit\"() RETURNS trigger");
        Expect.Contains(text, "VALUES (CASE TG_OP WHEN 'UPDATE' THEN 'U' ELSE 'D' END, now() AT TIME ZONE 'utc', current_user, OLD.\"ProbeId\", OLD.\"Name\", OLD.\"Amount\", OLD.\"CreateDate\", OLD.\"ModifiedDate\");");
        Expect.Contains(text, "AFTER UPDATE OR DELETE ON \"dbo\".\"Probe\"\nFOR EACH ROW EXECUTE FUNCTION \"dbo\".\"Probe_Audit\"();");
    }

    [TestMethod]
    public async Task MySQL_and_SQLite_get_one_trigger_each_for_update_and_delete()
    {
        var (mysqlOk, mysql) = await Run("SP_AuditTable_v1.tt", Probe(SqlDialect.MySql), Project());
        var (sqliteOk, sqlite) = await Run("SP_AuditTable_v1.tt", Probe(SqlDialect.Sqlite), Project());

        Assert.IsTrue(mysqlOk && sqliteOk, mysql + sqlite);
        Expect.Contains(mysql, "DROP TRIGGER IF EXISTS `TR_Probe_Audit_U`;\nCREATE TRIGGER `TR_Probe_Audit_U` AFTER UPDATE ON `Probe` FOR EACH ROW");
        Expect.Contains(mysql, "VALUES ('D', UTC_TIMESTAMP(6), USER(), OLD.`ProbeId`, OLD.`Name`, OLD.`Amount`, OLD.`CreateDate`, OLD.`ModifiedDate`);");
        Expect.Contains(sqlite, "CREATE TRIGGER \"TR_Probe_Audit_D\" AFTER DELETE ON \"Probe\" FOR EACH ROW\nBEGIN");
        Expect.Contains(sqlite, "VALUES ('U', strftime('%Y-%m-%d %H:%M:%f', 'now'), NULL, OLD.\"ProbeId\", OLD.\"Name\", OLD.\"Amount\", OLD.\"CreateDate\", OLD.\"ModifiedDate\");\nEND;");
    }

    [TestMethod]
    public async Task A_table_without_audit_columns_a_clashing_column_and_a_temporal_table_are_refused_with_reasons()
    {
        var plain = Sample.Table("Probe", [Sample.Column("ProbeId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("CreateDate", SqlDbType.DateTime, ordinal: 2)]);
        var (notAudit, notAuditText) = await Run("SP_AuditTable_v1.tt", plain, Project());
        var (both, bothText) = await Run("SP_AuditTable_v1.tt", Probe(), Project(("TemporalTables", "Probe")));
        var clashing = Sample.Table("Probe",
        [
            Sample.Column("ProbeId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("AuditDate", SqlDbType.DateTime, ordinal: 2),
            Sample.Column("CreateDate", SqlDbType.DateTime, ordinal: 3),
            Sample.Column("ModifiedDate", SqlDbType.DateTime, nullable: true, ordinal: 4)
        ]);
        var (clash, clashText) = await Run("SP_AuditTable_v1.tt", clashing, Project());

        Assert.IsFalse(notAudit);
        Expect.Contains(notAuditText, "has no audit columns");
        Assert.IsFalse(both);
        Expect.Contains(bothText, "also in TemporalTables");
        Assert.IsFalse(clash);
        Expect.Contains(clashText, "already has a column called AuditDate");
    }

    [TestMethod]
    public async Task A_computed_column_and_a_SQL_Server_timestamp_are_left_out_of_the_history()
    {
        var table = Sample.Table("Probe",
        [
            Sample.Column("ProbeId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("RowVer", SqlDbType.Timestamp, ordinal: 3),
            Sample.Column("CreateDate", SqlDbType.DateTime, ordinal: 4),
            Sample.Column("ModifiedDate", SqlDbType.DateTime, nullable: true, ordinal: 5)
        ]);
        var (ok, text) = await Run("SP_AuditTable_v1.tt", table, Project());

        Assert.IsTrue(ok, text);
        Expect.DoesNotContain(text, "RowVer");
    }

    [TestMethod]
    public async Task A_temporal_table_adds_the_period_columns_and_turns_versioning_on()
    {
        var (ok, text) = await Run("SP_TemporalTable_v1.tt", Probe(), Project(("TemporalTables", "Probe")));

        Assert.IsTrue(ok, text);
        Expect.Contains(text, "IF (SELECT temporal_type FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.Probe', N'U')) = 0");
        Expect.Contains(text, "[SysStartTime] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL CONSTRAINT [DF_Probe_SysStartTime] DEFAULT SYSUTCDATETIME(),");
        Expect.Contains(text, "PERIOD FOR SYSTEM_TIME ([SysStartTime], [SysEndTime]);");
        Expect.Contains(text, "ALTER TABLE [dbo].[Probe] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[Probe_History]));");
    }

    [TestMethod]
    public async Task A_temporal_table_is_refused_for_another_database_no_key_or_a_text_column()
    {
        var (pg, pgText) = await Run("SP_TemporalTable_v1.tt", Probe(SqlDialect.PostgreSql), Project(("TemporalTables", "Probe")));
        var noKey = Sample.Table("Probe", [Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 1)]);
        var (key, keyText) = await Run("SP_TemporalTable_v1.tt", noKey, Project(("TemporalTables", "Probe")));
        var withText = Sample.Table("Probe",
        [
            Sample.Column("ProbeId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("Body", SqlDbType.Text, ordinal: 2)
        ]);
        var (text, textText) = await Run("SP_TemporalTable_v1.tt", withText, Project(("TemporalTables", "Probe")));

        Assert.IsFalse(pg);
        Expect.Contains(pgText, "it is for a table read from SQL Server");
        Assert.IsFalse(key);
        Expect.Contains(keyText, "no primary key");
        Assert.IsFalse(text);
        Expect.Contains(textText, "cannot hold text, ntext or image columns");
    }

    [TestMethod]
    public void The_temporal_setting_adds_its_template_and_the_plan_runs_the_audit_trail_for_audit_tables_only()
    {
        CollectionAssert.DoesNotContain(Project().ImpliedPlanTemplates.ToArray(), "SP_TemporalTable");
        CollectionAssert.Contains(Project(("TemporalTables", "Probe")).ImpliedPlanTemplates.ToArray(), "SP_TemporalTable");

        var other = Sample.Table("Other", [Sample.Column("OtherId", SqlDbType.Int, primaryKey: true, ordinal: 1)]);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Probe(), other] };
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);
        var plan = ProjectPlan.Build(templates, database, Project(("PlanAlso", "SP_AuditTable"), ("TemporalTables", "Other")), ["Api"]).ToDictionary(s => s.Template.Name);

        CollectionAssert.AreEqual(new[] { "Probe" }, plan["SP_AuditTable"].TableNames.ToArray());
        CollectionAssert.AreEqual(new[] { "Other" }, plan["SP_TemporalTable"].TableNames.ToArray());
        CollectionAssert.DoesNotContain(ProjectPlan.Build(templates, database, Project(), ["Api"]).Select(s => s.Template.Name).ToList(), "SP_AuditTable");
    }

    [TestMethod]
    public void A_table_is_an_audit_table_when_it_has_a_creation_column_and_a_change_column()
    {
        Assert.IsTrue(AuditTableShape.IsAuditTable(["Id", "CreateDate", "ModifiedDate"]));
        Assert.IsTrue(AuditTableShape.IsAuditTable(["Id", "CreatedBy", "UpdatedOn"]));
        Assert.IsTrue(AuditTableShape.IsAuditTable(["Id", "create_user", "LastChangedDate"]));
        Assert.IsFalse(AuditTableShape.IsAuditTable(["Id", "CreateDate"]), "a creation column alone is not a trail of changes");
        Assert.IsFalse(AuditTableShape.IsAuditTable(["Id", "ModifiedBy"]));
        Assert.IsFalse(AuditTableShape.IsAuditTable(["Id", "CreateDate", "IsBeingModified"]));
        Assert.IsTrue(Probe().IsAuditTable);
    }

    [TestMethod]
    public void The_menu_offers_the_audit_trail_for_an_audit_table_only()
    {
        var template = TemplateCatalog.Discover(Repo.TemplatesDirectory).Single(t => t.Name == "SP_AuditTable");

        Assert.IsTrue(template.Config.RequiresAuditTable);
        Assert.IsTrue(template.AppliesTo(tableHasPrimaryKey: true, isView: false, isAuditTable: true));
        Assert.IsFalse(template.AppliesTo(tableHasPrimaryKey: true, isView: false, isAuditTable: false));
        Assert.IsNull(template.Config.Refuse(Probe()));
        Expect.Contains(template.Config.Refuse(Sample.Table("Plain", [Sample.Column("PlainId", SqlDbType.Int, primaryKey: true, ordinal: 1)]))!, "audit columns");
    }

    [TestMethod]
    public void A_MAX_column_is_declared_MAX_and_not_with_a_length_of_zero()
    {
        Assert.AreEqual("nvarchar(MAX)", SqlTypeClassifier.BuildDeclaration("nvarchar", SqlDbType.NVarChar, -1, 0, 0));
        Assert.AreEqual("varchar(MAX)", SqlTypeClassifier.BuildDeclaration("varchar", SqlDbType.VarChar, -1, 0, 0));
        Assert.AreEqual("nvarchar(50)", SqlTypeClassifier.BuildDeclaration("nvarchar", SqlDbType.NVarChar, 100, 0, 0));
    }
}
