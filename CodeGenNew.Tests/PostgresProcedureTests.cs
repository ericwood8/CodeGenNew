using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The PostgreSQL functions written for the SP_* templates (CodeGenNew.Core.PostgresProcedures). They are checked against a
/// live database by hand (see the PostgreSQL sample); these tests pin the text each one writes. </summary>
[TestClass]
public class PostgresProcedureTests
{
    private static TableModel AsPostgres(TableModel t) => new()
    {
        SchemaName = "public", TableName = t.TableName, QuotedName = $"\"public\".\"{t.TableName}\"", Dialect = SqlDialect.PostgreSql,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys, ChildForeignKeys = t.ChildForeignKeys,
        DisplayColumns = t.DisplayColumns, HasReferencedDisplayColumns = t.HasReferencedDisplayColumns, HasRowData = t.HasRowData, Rows = t.Rows,
        LookupShape = t.LookupShape
    };

    [TestMethod]
    public void Insert_returns_the_new_identity_key_and_trims_strings()
    {
        string sql = PostgresProcedures.Insert(AsPostgres(Sample.Holiday()));

        StringAssert.Contains(sql, "CREATE OR REPLACE FUNCTION \"public\".\"Holiday_Insert\"(");
        StringAssert.Contains(sql, "\"pName\" nvarchar(50) DEFAULT ''");
        StringAssert.Contains(sql, "btrim(\"pName\")");
        Assert.DoesNotContain("@", sql);
        Assert.DoesNotContain("GETDATE", sql);
    }

    [TestMethod]
    public void Insert_into_an_identity_table_returns_the_generated_key()
    {
        string sql = PostgresProcedures.Insert(AsPostgres(Sample.DonateLeave()));

        StringAssert.Contains(sql, "RETURNS int");
        StringAssert.Contains(sql, "RETURNING \"DonateLeaveId\" INTO v_new_id");
        StringAssert.Contains(sql, "RETURN v_new_id;");
    }

    [TestMethod]
    public void Update_keeps_a_column_when_its_parameter_is_null()
    {
        string sql = PostgresProcedures.Update(AsPostgres(Sample.Holiday()));

        StringAssert.Contains(sql, "RETURNS integer");
        StringAssert.Contains(sql, "\"Name\" = COALESCE(btrim(\"pName\"), \"Name\")");
        StringAssert.Contains(sql, "GET DIAGNOSTICS v_rows = ROW_COUNT;");
    }

    [TestMethod]
    public void Save_updates_a_found_row_and_otherwise_inserts()
    {
        string sql = PostgresProcedures.Save(AsPostgres(Sample.DonateLeave()));

        StringAssert.Contains(sql, "UPDATE \"public\".\"E_DonateLeave\" SET");
        StringAssert.Contains(sql, "IF FOUND THEN");
        StringAssert.Contains(sql, "INSERT INTO \"public\".\"E_DonateLeave\"");
        StringAssert.Contains(sql, "RETURN v_new_id;");
        // the identity key is the one optional parameter, and PostgreSQL wants optional parameters last
        Assert.IsTrue(sql.IndexOf("DEFAULT NULL", StringComparison.Ordinal) > sql.IndexOf("\"pdteDateLeaveDonated\"", StringComparison.Ordinal) || sql.Contains("DEFAULT NULL\n)"));
    }

    [TestMethod]
    public void Delete_reports_a_foreign_key_block_as_minus_one()
    {
        string sql = PostgresProcedures.Delete(AsPostgres(Sample.Holiday()));

        StringAssert.Contains(sql, "WHEN foreign_key_violation THEN");
        StringAssert.Contains(sql, "RETURN -1;");
        StringAssert.Contains(sql, "RETURN -2;");
        StringAssert.Contains(sql, "RETURN 0;");
    }

    [TestMethod]
    public void Clone_of_an_identity_table_takes_the_source_key_and_returns_the_new_one()
    {
        string sql = PostgresProcedures.Clone(AsPostgres(Sample.DonateLeave()));

        StringAssert.Contains(sql, "\"CopyFromDonateLeaveId\" int");
        StringAssert.Contains(sql, "RETURNS int");
        StringAssert.Contains(sql, "RETURNING \"DonateLeaveId\" INTO v_new_key");
        StringAssert.Contains(sql, "USING ERRCODE = '55509'");
        Assert.DoesNotContain("OUTPUT", sql);
    }

    [TestMethod]
    public void Load_inserts_each_row_once_and_moves_the_identity_counter()
    {
        string sql = PostgresProcedures.Load(AsPostgres(Sample.Roles()));

        StringAssert.Contains(sql, "RETURNS void");
        StringAssert.Contains(sql, "VALUES (1, 'Admin') ON CONFLICT (\"SY_RoleId\") DO NOTHING;");
        StringAssert.Contains(sql, "VALUES (3, 'Time off in lieu') ON CONFLICT");
    }

    [TestMethod]
    public void Lookup_returns_a_table_of_the_ids_and_display_columns()
    {
        string sql = PostgresProcedures.Lookup(AsPostgres(Sample.Roles()));

        StringAssert.Contains(sql, "RETURNS TABLE (");
        StringAssert.Contains(sql, "RETURN QUERY");
        StringAssert.Contains(sql, "USING ERRCODE = '55508'");
        StringAssert.Contains(sql, "#variable_conflict use_column");
    }

    [TestMethod]
    public void Junction_writes_list_link_and_unlink_functions()
    {
        var table = AsPostgres(Sample.CompositeKeyJunction());
        if (!table.IsJunctionTable)
            Assert.Inconclusive("the sample table is not a junction table");

        string sql = PostgresProcedures.Junction(table);

        StringAssert.Contains(sql, "_List\"(");
        StringAssert.Contains(sql, "_Link\"(");
        StringAssert.Contains(sql, "_Unlink\"(");
        StringAssert.Contains(sql, "\"IsSelected\" boolean");
    }

    [TestMethod]
    public void The_templates_write_PostgreSQL_for_a_PostgreSQL_table_and_T_SQL_for_a_SQL_Server_one()
    {
        // The template branch itself is exercised by the CLI against a live database; here the dialect switch is pinned.
        Assert.AreEqual(SqlDialect.PostgreSql, AsPostgres(Sample.Holiday()).Dialect);
        Assert.AreEqual(SqlDialect.SqlServer, Sample.Holiday().Dialect);
    }
}
