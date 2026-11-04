using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> What differs when the MySQL path meets a MariaDB server: the flavour, the catalog's defaults, JSON as a checked text column and the types MySQL does not have. </summary>
[TestClass]
public class MariaDbTests
{
    private static DatabaseModel Database(string? serverVersion) =>
        new() { DatabaseName = "shop", SchemaName = "shop", Dialect = SqlDialect.MySql, Tables = [], ServerVersion = serverVersion };

    private static ProjectSettings Project(string? flavour) =>
        ProjectSettings.FromValues(flavour is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["MySqlFlavour"] = flavour });

    [TestMethod]
    public void The_server_version_text_tells_a_MariaDB_server_from_a_MySQL_one_and_gives_its_version()
    {
        Assert.IsTrue(Database("10.11.6-MariaDB-0ubuntu0.23.10.2").ServerIsMariaDb);
        Assert.AreEqual(new Version(10, 11, 0), Database("10.11.6-MariaDB-0ubuntu0.23.10.2").MariaDbVersion);
        Assert.AreEqual(new Version(10, 11, 0), Database("5.5.5-10.11.6-MariaDB").MariaDbVersion);
        Assert.IsFalse(Database("8.0.36").ServerIsMariaDb);
        Assert.IsFalse(Database(null).ServerIsMariaDb);
        Assert.AreEqual(new Version(10, 6, 0), Database(null).MariaDbVersion);
    }

    [TestMethod]
    public void The_flavour_setting_wins_and_Auto_asks_the_server()
    {
        Assert.IsTrue(Database("10.11.6-MariaDB").UsesMariaDb(Project(null)));
        Assert.IsTrue(Database("10.11.6-MariaDB").UsesMariaDb(Project("Auto")));
        Assert.IsFalse(Database("8.0.36").UsesMariaDb(Project(null)));
        Assert.IsTrue(Database("8.0.36").UsesMariaDb(Project("MariaDb")));
        Assert.IsFalse(Database("10.11.6-MariaDB").UsesMariaDb(Project("MySql")));
        Assert.IsFalse(new DatabaseModel { DatabaseName = "a", SchemaName = "a", Dialect = SqlDialect.PostgreSql, Tables = [], ServerVersion = "MariaDB" }.UsesMariaDb(Project("MariaDb")));
    }

    [TestMethod]
    public void A_run_that_read_a_MariaDB_server_carries_the_flavour_unless_the_project_says_another()
    {
        Assert.IsTrue(Project(null).ForServer("10.11.6-MariaDB").MariaDbSetting);
        Assert.IsTrue(Project("Auto").ForServer("10.11.6-MariaDB").MariaDbSetting);
        Assert.IsNull(Project(null).ForServer("8.0.36").MariaDbSetting);
        Assert.IsNull(Project(null).ForServer(null).MariaDbSetting);
        Assert.IsFalse(Project("MySql").ForServer("10.11.6-MariaDB").MariaDbSetting);
        Assert.IsTrue(Project("MariaDb").ForServer("8.0.36").MariaDbSetting);
    }

    [TestMethod]
    public async Task The_context_names_the_version_the_server_reported_when_the_flavour_is_Auto()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_DbContext_v1.tt"), Database("10.11.6-MariaDB"), ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Acme" }));

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "new MariaDbServerVersion(new Version(10, 11, 0))");
    }

    [TestMethod]
    [DataRow(null, "int", false, null)]
    [DataRow("NULL", "int", false, null)]
    [DataRow("'open'", "varchar", true, "'open'")]
    [DataRow("'it''s'", "varchar", true, "'it''s'")]
    [DataRow("0", "int", false, "0")]
    [DataRow("'2020-01-01'", "date", false, "2020-01-01")]
    [DataRow("current_timestamp()", "datetime", false, "getdate()")]
    [DataRow("uuid()", "char", true, "newid()")]
    public void A_MariaDB_default_is_read_in_the_form_a_MySQL_one_has(string? columnDefault, string dataType, bool isString, string? expected)
    {
        Assert.AreEqual(expected, MySqlSchemaProvider.NormalizeMariaDbDefault(columnDefault, "", isString), dataType);
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("NULL", null)]
    [DataRow("'open'", "open")]
    [DataRow("'it''s'", "it's")]
    [DataRow("0.00", "0.00")]
    [DataRow("current_timestamp()", "current_timestamp()")]
    public void The_stored_default_of_a_MariaDB_column_has_no_quotes_round_a_text(string? columnDefault, string? expected)
    {
        Assert.AreEqual(expected, MySqlSchemaProvider.MariaDbDefaultDefinition(columnDefault));
    }

    [TestMethod]
    public void A_column_with_a_json_valid_check_is_found_by_its_name()
    {
        var names = MySqlSchemaProvider.JsonCheckedColumns(["json_valid(`Body`)", "`Qty` > 0", "(json_valid(`Extra data`))"]);

        CollectionAssert.AreEquivalent(new[] { "Body" }, names.ToArray());
    }

    [TestMethod]
    public void MariaDB_reads_and_writes_a_spatial_column_without_the_axis_order_option()
    {
        var mysql = DialectInfo.For(SqlDialect.MySql);

        Assert.AreEqual("ST_GeomFromText(NULLIF(@p, ''), 4326, 'axis-order=long-lat')", mysql.SpatialFromText("@p", 4326, mariaDb: false));
        Assert.AreEqual("ST_GeomFromText(NULLIF(@p, ''), 0)", mysql.SpatialFromText("@p", 0, mariaDb: true));
        Assert.AreEqual("ST_AsText(`Loc`, 'axis-order=long-lat')", mysql.SpatialAsText("`Loc`", mariaDb: false));
        Assert.AreEqual("ST_AsText(`Loc`)", mysql.SpatialAsText("`Loc`", mariaDb: true));
    }

    [TestMethod]
    public void Only_a_check_that_is_nothing_but_json_valid_is_dropped_from_the_list()
    {
        Assert.IsTrue(MySqlSchemaProvider.IsJsonValidCheck("json_valid(`Settings`)"));
        Assert.IsTrue(MySqlSchemaProvider.IsJsonValidCheck("(json_valid(`Settings`))"));
        Assert.IsFalse(MySqlSchemaProvider.IsJsonValidCheck("json_valid(`Settings`) and `Qty` > 0"));
        Assert.IsFalse(MySqlSchemaProvider.IsJsonValidCheck("`Credit` >= 0"));
    }

    [TestMethod]
    [DataRow("uuid", "uuid(36)", 36, "uuid")]
    [DataRow("inet4", "inet4", 15, "inet4")]
    [DataRow("inet6", "inet6", 39, "inet6")]
    public void The_MariaDB_only_types_are_read_as_text(string dataType, string columnType, int length, string declaration)
    {
        var (sqlType, maxLength, _, _, text) = MySqlSchemaProvider.MapType(dataType, columnType, null, null, null);

        Assert.AreEqual("varchar", sqlType);
        Assert.AreEqual(length, maxLength);
        Assert.AreEqual(declaration, text);
    }
}
