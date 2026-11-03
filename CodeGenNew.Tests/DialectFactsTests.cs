using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The facts about a database that writers ask <c>DialectInfo</c> instead of comparing the dialect themselves. </summary>
[TestClass]
public class DialectFactsTests
{
    [TestMethod]
    public void A_boolean_is_true_and_false_for_PostgreSQL_and_one_and_zero_elsewhere()
    {
        Assert.AreEqual("true", DialectInfo.For(SqlDialect.PostgreSql).BooleanText(true));
        Assert.AreEqual("false", DialectInfo.For(SqlDialect.PostgreSql).BooleanText(false));
        foreach (var dialect in new[] { SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Sqlite })
        {
            Assert.AreEqual("1", DialectInfo.For(dialect).BooleanText(true));
            Assert.AreEqual("0", DialectInfo.For(dialect).BooleanText(false));
        }
    }

    [TestMethod]
    public void Only_a_database_with_schemas_names_a_schema_that_is_not_the_default()
    {
        Assert.IsFalse(DialectInfo.For(SqlDialect.SqlServer).NamesSchema("dbo"));
        Assert.IsTrue(DialectInfo.For(SqlDialect.SqlServer).NamesSchema("sales"));
        Assert.IsFalse(DialectInfo.For(SqlDialect.PostgreSql).NamesSchema("PUBLIC"));
        Assert.IsTrue(DialectInfo.For(SqlDialect.PostgreSql).NamesSchema("sales"));
        Assert.IsFalse(DialectInfo.For(SqlDialect.MySql).NamesSchema("sales"));
        Assert.IsFalse(DialectInfo.For(SqlDialect.Sqlite).NamesSchema("main"));
    }

    [TestMethod]
    public void Only_PostgreSQL_casts_a_date_placeholder()
    {
        Assert.AreEqual("CAST($1 AS TIMESTAMP)", DialectInfo.For(SqlDialect.PostgreSql).TimestampPlaceholder(1));
        Assert.AreEqual("?", DialectInfo.For(SqlDialect.MySql).TimestampPlaceholder(1));
        Assert.AreEqual("@p1", DialectInfo.For(SqlDialect.SqlServer).TimestampPlaceholder(1));
    }

    [TestMethod]
    public void The_driver_prefix_the_date_type_and_the_returning_rule_follow_the_dialect()
    {
        Assert.AreEqual("Npgsql.", DialectInfo.For(SqlDialect.PostgreSql).DriverPrefix);
        Assert.AreEqual("MySqlConnector.", DialectInfo.For(SqlDialect.MySql).DriverPrefix);
        Assert.AreEqual("Microsoft.Data.SqlClient.", DialectInfo.For(SqlDialect.SqlServer).DriverPrefix);
        Assert.AreEqual("datetime2", DialectInfo.For(SqlDialect.SqlServer).DateTimeType);
        Assert.AreEqual("datetime", DialectInfo.For(SqlDialect.MySql).DateTimeType);
        Assert.AreEqual("timestamp", DialectInfo.For(SqlDialect.PostgreSql).DateTimeType);
        Assert.IsFalse(DialectInfo.For(SqlDialect.MySql).SupportsReturning);
        Assert.IsTrue(DialectInfo.For(SqlDialect.Sqlite).SupportsReturning);
    }
}
