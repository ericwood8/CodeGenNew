using System.Data;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> SQL Server's timestamp is a row version the routines never copy or supply; another database's timestamp is an ordinary date. </summary>
[TestClass]
public class RowVersionTests
{
    [TestMethod]
    public void A_sql_server_timestamp_is_a_row_version_by_either_name_and_a_date_timestamp_is_not()
    {
        Assert.AreEqual(SqlDbType.Timestamp, SqlTypeClassifier.MapSqlTypeName("timestamp"));
        Assert.AreEqual(SqlDbType.Timestamp, SqlTypeClassifier.MapSqlTypeName("rowversion"));
        Assert.IsTrue(Sample.Column("RowVer", SqlDbType.Timestamp).IsRowVersionColumn);
        Assert.IsFalse(Sample.Column("Seen", SqlDbType.DateTime2, sqlDeclaration: "timestamp").IsRowVersionColumn, "PostgreSQL, MySQL and SQLite map timestamp to a date");
        Assert.IsFalse(Sample.Column("Name", SqlDbType.NVarChar, characters: 20).IsRowVersionColumn);
    }

    [TestMethod]
    public async Task The_insert_and_update_routines_leave_the_row_version_alone()
    {
        var table = Sample.Table("Widget",
        [
            Sample.Column("WidgetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("WidgetName", SqlDbType.VarChar, characters: 40, ordinal: 2),
            Sample.Column("RowVer", SqlDbType.Timestamp, ordinal: 3)
        ]);
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme")]);
        foreach (string template in new[] { "SP_Insert_v1.tt", "SP_Update_v1.tt" })
        {
            var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            Expect.DoesNotContain(result.GeneratedText!, "RowVer");
        }
    }
}
