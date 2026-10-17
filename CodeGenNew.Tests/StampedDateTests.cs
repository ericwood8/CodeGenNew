using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The audit dates the server stamps: set in the statement or the handler (never from the body), shown with the time of day, and kept to the digits the column holds. </summary>
[TestClass]
public class StampedDateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Stock(SqlDialect dialect = SqlDialect.SqlServer, string datetime = "datetime2") => Sample.Table("Stock",
    [
        Sample.Column("StockId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Cube", SqlDbType.VarChar, characters: 40, ordinal: 2),
        Sample.Column("CreatedDate", SqlDbType.DateTime2, createDateColumn: true, defaultSql: "(sysdatetime())", sqlDeclaration: datetime, ordinal: 3),
        Sample.Column("ModifiedDate", SqlDbType.DateTime2, nullable: true, modifiedDateColumn: true, sqlDeclaration: datetime, ordinal: 4)
    ], dialect: dialect);

    private static async Task<string> Rendered(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_aspnet_api_stamps_with_a_time_that_has_no_kind()
    {
        string api = await Rendered("API_Crud_v1.tt", Stock(), Project());

        Expect.Contains(api, "protected override void Stamp(Stock row, bool isNew)");
        Expect.Contains(api, "row.ModifiedDate = LocalNow;");
        Expect.DoesNotContain(api, "row.CreatedDate");
        Expect.Contains(await CrudApiTests.Essential(), "DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)");
    }

    [TestMethod]
    public async Task The_python_api_makes_the_dates_optional_stamps_them_and_keeps_the_created_date_on_a_change()
    {
        var project = Project(("Stacks", "Python"));
        string schema = await Rendered("PY_Schema_v1.tt", Stock(), project);
        Expect.Contains(schema, "created_date: datetime | None = Field(None, alias=\"createdDate\")");
        string changes = schema[schema.IndexOf("def changes", StringComparison.Ordinal)..];
        Expect.DoesNotContain(changes, "created_date");
        Expect.DoesNotContain(changes, "modified_date");
        Expect.Contains(schema, "def columns(self)");

        string routes = await Rendered("PY_Routes_v1.tt", Stock(), project);
        Expect.Contains(routes, "from datetime import datetime");
        Expect.Contains(routes, "row.created_date = datetime.now()");
        Expect.Contains(routes, "row.modified_date = datetime.now()");
        Assert.IsLessThan(routes.IndexOf("row.modified_date = datetime.now()", StringComparison.Ordinal), routes.IndexOf("row.created_date = datetime.now()", StringComparison.Ordinal));
    }

    [TestMethod]
    public void The_rust_statements_set_the_dates_themselves_and_bind_nothing_for_them()
    {
        foreach (var (dialect, now, placeholder) in new[] { (SqlDialect.PostgreSql, "now()", "$"), (SqlDialect.MySql, "NOW()", "?") })
        {
            var t = RustTable.Of(Stock(dialect), Project());
            string first = dialect == SqlDialect.PostgreSql ? "$1" : "?", second = dialect == SqlDialect.PostgreSql ? "$2" : "?";
            Expect.Contains(t.Insert, $"VALUES ({first}, {now}, {second})");
            Expect.DoesNotContain(t.Update, "CreatedDate\" = " + placeholder);
            Expect.Contains(t.Update, $"= {now} WHERE");
            CollectionAssert.AreEqual(new[] { "cube", "modified_date" }, t.InsertBound.Select(f => f.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "cube" }, t.Updated.Select(f => f.Name).ToArray());
        }
    }

    [TestMethod]
    public void MySql_stamps_a_fractional_column_with_the_digits_it_holds()
    {
        string fractional = MySqlProcedures.Insert(Stock(SqlDialect.MySql, "datetime(6)"));
        Expect.Contains(fractional, "NOW(6)");
        string whole = MySqlProcedures.Insert(Stock(SqlDialect.MySql, "datetime"));
        Expect.Contains(whole, "NOW()");
        Expect.DoesNotContain(whole, "NOW(6)");
    }

    [TestMethod]
    public async Task A_grid_shows_a_stamped_date_with_its_time_of_day_and_a_plain_date_without()
    {
        var plain = Sample.Table("Stock",
        [
            Sample.Column("StockId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Cube", SqlDbType.VarChar, characters: 40, ordinal: 2),
            Sample.Column("Received", SqlDbType.Date, ordinal: 3)
        ]);

        Assert.AreEqual(CellKind.DateTime, GridCells.KindOf(Stock().Columns.Single(c => c.Name == "CreatedDate"), false));
        Assert.AreEqual(CellKind.Date, GridCells.KindOf(plain.Columns.Single(c => c.Name == "Received"), false));

        Expect.Contains(await Rendered("TSX_Page_v1.tt", Stock(), Project()), ".substring(0, 16).replace('T', ' ')");
        Expect.Contains(await Rendered("TS_Component_v1.tt", Stock(), Project()), "| date:'MM/dd/yyyy HH:mm'");
        Expect.Contains(await Rendered("BLZ_Page_v1.tt", Stock(), Project()), "ToString(\"yyyy-MM-dd HH:mm\")");
        Expect.DoesNotContain(await Rendered("TSX_Page_v1.tt", plain, Project()), "substring(0, 16)");
    }
}
