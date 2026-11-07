using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> PartitionTables: the tables a script splits into one partition for each month, what a table that cannot be split is told, and the script of each database. </summary>
[TestClass]
public class PartitionPlanTests
{
    private static ColumnModel C(string name, SqlDbType type, bool nullable = false, bool primaryKey = false, int ordinal = 0, int? characters = null, bool identity = false) =>
        Sample.Column(name, type, nullable: nullable, primaryKey: primaryKey, ordinal: ordinal, characters: characters, identity: identity);

    private static TableModel Events(SqlDialect dialect, bool dateNullable = false, List<IndexModel>? indexes = null, List<ForeignKeyModel>? foreignKeys = null, string name = "WebhookEvent") => Sample.Table(name,
        [
            C(name + "Id", SqlDbType.Int, primaryKey: true, ordinal: 1, identity: true), C("Source", SqlDbType.VarChar, nullable: true, ordinal: 2, characters: 40), C("EventId", SqlDbType.VarChar, nullable: true, ordinal: 3, characters: 80),
            C("Body", SqlDbType.VarChar, ordinal: 4, characters: 4000), C("ReceivedOn", SqlDbType.DateTime, nullable: dateNullable, ordinal: 5), C("ProcessedOn", SqlDbType.DateTime, nullable: true, ordinal: 6)
        ], foreignKeys: foreignKeys, indexes: indexes ?? [new IndexModel("IX_WebhookEvent_Source_EventId", true, ["Source", "EventId"]), new IndexModel("IX_WebhookEvent_ReceivedOn", false, ["ReceivedOn"])], dialect: dialect);

    private static DatabaseModel Database(SqlDialect dialect, params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = dialect == SqlDialect.Sqlite ? "main" : "dbo", Dialect = dialect, Tables = tables.ToList() };

    private static ProjectSettings Project(string tables, SqlDialect dialect = SqlDialect.SqlServer, Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string> { ["AccessMode"] = "Ef", ["DatabaseProvider"] = dialect.ToString(), ["PartitionTables"] = tables };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public void The_settings_have_their_defaults_and_the_template_is_in_the_plan()
    {
        var project = Project("WebhookEvent,PageView:ViewedOn");
        CollectionAssert.AreEqual(new[] { ("WebhookEvent", (string?)null), ("PageView", "ViewedOn") }, PartitionPlan.Entries(project).ToArray());
        Assert.AreEqual(3, project.PartitionMonthsAhead);
        Assert.AreEqual(0, project.PartitionKeepMonths);
        var tuned = Project("WebhookEvent", extra: new() { ["PartitionMonthsAhead"] = "6", ["PartitionKeepMonths"] = "12" });
        Assert.AreEqual(6, tuned.PartitionMonthsAhead);
        Assert.AreEqual(12, tuned.PartitionKeepMonths);
        CollectionAssert.Contains(project.ImpliedPlanTemplates.ToList(), "SP_PartitionJob");
    }

    [TestMethod]
    public void A_table_that_cannot_be_split_is_told_why()
    {
        string One(DatabaseModel database, string tables) => PartitionPlan.Resolve(database, Project(tables, database.Dialect)).Problems.Single();

        StringAssert.Contains(One(Database(SqlDialect.Sqlite, Events(SqlDialect.Sqlite)), "WebhookEvent"), "SQLite has no partitions");
        StringAssert.Contains(One(Database(SqlDialect.SqlServer, Events(SqlDialect.SqlServer)), "Missing"), "is not a table");
        StringAssert.Contains(One(Database(SqlDialect.SqlServer, Events(SqlDialect.SqlServer, dateNullable: true)), "WebhookEvent"), "must not allow NULL");
        StringAssert.Contains(One(Database(SqlDialect.SqlServer, Events(SqlDialect.SqlServer)), "WebhookEvent:Body"), "is not a date and time column");
        StringAssert.Contains(One(Database(SqlDialect.SqlServer, Events(SqlDialect.SqlServer, foreignKeys: [new ForeignKeyModel { ConstraintName = "FK_Event_Source", ReferencingColumns = ["Source"], ReferencedSchema = "dbo", ReferencedTable = "Source", ReferencedColumns = ["Source"] }])), "WebhookEvent"), "foreign keys");
        var noDate = Sample.Table("Plain", [C("PlainId", SqlDbType.Int, primaryKey: true, ordinal: 1), C("Name", SqlDbType.VarChar, ordinal: 2, characters: 40)]);
        StringAssert.Contains(One(Database(SqlDialect.SqlServer, noDate), "Plain"), "no date and time column");
        Assert.IsEmpty(PartitionPlan.Resolve(Database(SqlDialect.SqlServer, Events(SqlDialect.SqlServer)), Project("WebhookEvent")).Problems);
    }

    [TestMethod]
    public void A_unique_index_without_the_date_becomes_a_plain_index_and_is_said_so()
    {
        var job = PartitionPlan.Resolve(Database(SqlDialect.SqlServer, Events(SqlDialect.SqlServer)), Project("WebhookEvent")).Jobs.Single();
        CollectionAssert.AreEqual(new[] { "IX_WebhookEvent_Source_EventId" }, job.LooseUnique.ToArray());
        Assert.AreEqual("ReceivedOn", job.Date.Name);

        var withDate = Events(SqlDialect.SqlServer, indexes: [new IndexModel("UX_Event", true, ["Source", "EventId", "ReceivedOn"])]);
        Assert.IsEmpty(PartitionPlan.Resolve(Database(SqlDialect.SqlServer, withDate), Project("WebhookEvent")).Jobs.Single().LooseUnique);
    }

    [TestMethod]
    public void The_sql_server_script_makes_the_function_and_scheme_moves_the_key_and_adds_the_procedure()
    {
        var database = Database(SqlDialect.SqlServer, Events(SqlDialect.SqlServer));
        string script = PartitionScript.Text(database, Project("WebhookEvent", extra: new() { ["PartitionKeepMonths"] = "24" }));

        Assert.IsFalse(script.Contains('§'), "every token is filled");
        Assert.Contains("CREATE PARTITION FUNCTION pf_WebhookEvent_Month (datetime) AS RANGE RIGHT FOR VALUES", script);
        Assert.Contains("CREATE PARTITION SCHEME ps_WebhookEvent_Month AS PARTITION pf_WebhookEvent_Month ALL TO ([PRIMARY])", script);
        Assert.Contains("PRIMARY KEY CLUSTERED ([WebhookEventId], [ReceivedOn]) ON ps_WebhookEvent_Month ([ReceivedOn])", script);
        Assert.Contains("CREATE INDEX [IX_WebhookEvent_Source_EventId] ON [dbo].[WebhookEvent] ([Source], [EventId]) ON ps_WebhookEvent_Month ([ReceivedOn]);", script, "the unique index without the date is plain");
        Assert.Contains("which no partitioned table can enforce", script);
        Assert.Contains("CREATE OR ALTER PROCEDURE [dbo].[PartitionWebhookEvent] @MonthsAhead int = 3, @KeepMonths int = 24", script);
        Assert.Contains("TRUNCATE TABLE [dbo].[WebhookEvent] WITH (PARTITIONS (2));", script);
        Assert.Contains("ALTER PARTITION FUNCTION pf_WebhookEvent_Month() MERGE RANGE (@oldest);", script);
        Assert.Contains("is partitioned already", script, "running it twice leaves the table alone");
    }

    [TestMethod]
    public void The_postgresql_script_copies_into_a_partitioned_table_and_keeps_the_sequences()
    {
        var database = Database(SqlDialect.PostgreSql, Events(SqlDialect.PostgreSql));
        string script = PartitionScript.Text(database, Project("WebhookEvent", SqlDialect.PostgreSql));

        Assert.IsFalse(script.Contains('§'), "every token is filled");
        Assert.Contains("CREATE OR REPLACE PROCEDURE \"dbo\".\"PartitionWebhookEvent\"(p_ahead integer DEFAULT 3, p_keep integer DEFAULT 0, p_from date DEFAULT NULL)", script);
        Assert.Contains("PARTITION BY RANGE (\"ReceivedOn\");", script);
        Assert.Contains("ADD PRIMARY KEY (\"WebhookEventId\", \"ReceivedOn\");", script);
        Assert.Contains("PARTITION OF \"dbo\".\"WebhookEvent\" DEFAULT;", script);
        Assert.Contains("INSERT INTO \"dbo\".\"WebhookEvent\" OVERRIDING SYSTEM VALUE SELECT * FROM \"dbo\".\"WebhookEvent_unpartitioned\";", script);
        Assert.Contains("CREATE INDEX \"IX_WebhookEvent_Source_EventId\" ON \"dbo\".\"WebhookEvent\" (\"Source\", \"EventId\");", script);
        Assert.Contains("cron.schedule('PartitionWebhookEvent'", script);
    }

    [TestMethod]
    public void The_mysql_script_changes_the_key_and_partitions_in_one_alter()
    {
        var database = Database(SqlDialect.MySql, Events(SqlDialect.MySql));
        string script = PartitionScript.Text(database, Project("WebhookEvent", SqlDialect.MySql));

        Assert.IsFalse(script.Contains('§'), "every token is filled");
        Assert.Contains("DROP PRIMARY KEY, ADD PRIMARY KEY (`WebhookEventId`, `ReceivedOn`), DROP INDEX `IX_WebhookEvent_Source_EventId`, ADD INDEX `IX_WebhookEvent_Source_EventId` (`Source`, `EventId`) PARTITION BY RANGE COLUMNS (`ReceivedOn`)", script);
        Assert.Contains("PARTITION pmax VALUES LESS THAN (MAXVALUE)", script);
        Assert.Contains("REORGANIZE PARTITION pmax INTO", script);
        Assert.Contains("CREATE PROCEDURE `PartitionWebhookEvent`(IN p_ahead INT, IN p_keep INT)", script);
        Assert.Contains("CALL `PartitionWebhookEventConvert`(3);", script);
    }

    [TestMethod]
    public void A_table_with_the_key_and_date_composite_key_looks_converted_already()
    {
        var converted = Sample.Table("WebhookEvent",
        [
            C("WebhookEventId", SqlDbType.Int, primaryKey: true, ordinal: 1), C("ReceivedOn", SqlDbType.DateTime, primaryKey: true, ordinal: 2), C("Body", SqlDbType.VarChar, ordinal: 3, characters: 100)
        ]);
        Assert.IsTrue(PartitionPlan.LooksPartitioned(converted, Project("WebhookEvent")));
        Assert.IsFalse(PartitionPlan.LooksPartitioned(converted, Project("Other")));
        Assert.IsFalse(PartitionPlan.LooksPartitioned(Events(SqlDialect.SqlServer), Project("WebhookEvent")));
    }
}
