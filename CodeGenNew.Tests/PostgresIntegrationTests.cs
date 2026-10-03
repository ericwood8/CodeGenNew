using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> Reads the schema of a real PostgreSQL copy of the InvoiceSystem sample database. They need a live server, so they run only when
/// CODEGENNEW_PG_HOST (host or host:port), CODEGENNEW_PG_DATABASE, CODEGENNEW_PG_USER and CODEGENNEW_PG_PASSWORD are set (the database is the one
/// created by the PostgreSQL sample's CreateInvoiceSystemPg.sql and SeedInvoiceSystemPg.sql); otherwise each test reports itself as inconclusive.
/// Nothing is written to the database, except by the enum test, which creates a throwaway schema and drops it again. </summary>
[TestClass]
public class PostgresIntegrationTests
{
    private static ConnectionRequest? Request()
    {
        string? host = Environment.GetEnvironmentVariable("CODEGENNEW_PG_HOST");
        string? database = Environment.GetEnvironmentVariable("CODEGENNEW_PG_DATABASE");
        string? user = Environment.GetEnvironmentVariable("CODEGENNEW_PG_USER");
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(database) || string.IsNullOrEmpty(user))
            return null;

        return new ConnectionRequest
        {
            Provider = DatabaseProvider.PostgreSql, ServerName = host, DatabaseName = database,
            AuthMode = AuthMode.SqlLogin, UserName = user, Password = Environment.GetEnvironmentVariable("CODEGENNEW_PG_PASSWORD")
        };
    }

    private static PostgresSchemaProvider Provider()
    {
        var request = Request();
        if (request is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, CODEGENNEW_PG_DATABASE, CODEGENNEW_PG_USER and CODEGENNEW_PG_PASSWORD to run the PostgreSQL integration tests.");
        return new PostgresSchemaProvider(request!, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config"));
    }

    [TestMethod]
    public async Task The_table_list_has_the_sample_tables_with_their_key_shapes()
    {
        var tables = await Provider().ListTablesAsync();

        var customer = tables.Single(t => t.TableName == "Customer");
        Assert.AreEqual("public", customer.SchemaName);
        Assert.IsTrue(customer.HasPrimaryKey);
        Assert.AreEqual(PrimaryKeyShape.SingleInt, customer.PrimaryKeyShape);
        Assert.IsTrue(customer.HasChildForeignKeys); // invoices, summaries and purchases point at a customer
        Assert.IsFalse(tables.Single(t => t.TableName == "CustomerItem").IsJunctionTable); // it carries a PurchasedDate
    }

    [TestMethod]
    public async Task A_table_model_has_typed_columns_keys_and_foreign_keys()
    {
        var model = await Provider().BuildTableModelAsync("public", "Customer", includeReferencedDisplayColumns: true);

        Assert.AreEqual(SqlDialect.PostgreSql, model.Dialect);
        Assert.AreEqual("\"public\".\"Customer\"", model.QuotedName);
        var id = model.Columns.Single(c => c.Name == "CustomerId");
        Assert.IsTrue(id.IsPrimaryKey);
        Assert.IsTrue(id.IsIdentity);
        Assert.AreEqual(SqlDbType.Int, id.SqlType);

        Assert.AreEqual(SqlDbType.Bit, model.Columns.Single(c => c.Name == "IsTaxable").SqlType);
        Assert.AreEqual("boolean", model.Columns.Single(c => c.Name == "IsTaxable").SqlTypeDeclaration);
        Assert.AreEqual(SqlDbType.DateTime2, model.Columns.Single(c => c.Name == "DateAdded").SqlType);
        Assert.AreEqual(SqlDbType.Decimal, model.Columns.Single(c => c.Name == "FinanceChargeRate").SqlType);
        Assert.AreEqual(2, model.Columns.Single(c => c.Name == "FinanceChargeRate").Scale);

        var status = model.ForeignKeys.Single(f => f.ReferencedTable == "CustomerStatus");
        CollectionAssert.AreEqual(new[] { "CustomerStatusId" }, status.ReferencingColumns);
        CollectionAssert.AreEqual(new[] { "Description" }, status.ReferencedDisplayColumns);
        Assert.IsTrue(model.ChildForeignKeys.Any(c => c.ReferencingTable == "CustomerItem"));
    }

    [TestMethod]
    public async Task Row_data_comes_back_for_a_small_table()
    {
        var model = await Provider().BuildTableModelAsync("public", "CustomerStatus", includeRowData: true);

        Assert.AreEqual(2, model.Rows.Count);
        Assert.IsTrue(model.Rows.Any(r => Equals(r[1], "Good Standing")));
    }

    [TestMethod]
    public async Task A_native_enum_and_a_check_list_give_their_values_as_choices()
    {
        // The one test that writes: a throwaway schema, dropped again whatever happens.
        var request = Request();
        if (request is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, CODEGENNEW_PG_DATABASE, CODEGENNEW_PG_USER and CODEGENNEW_PG_PASSWORD to run the PostgreSQL integration tests.");

        await using var connection = request!.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP SCHEMA IF EXISTS codegen_enum_test CASCADE");
            await Run("CREATE SCHEMA codegen_enum_test");
            await Run("CREATE TYPE codegen_enum_test.ticket_status AS ENUM ('Open', 'In progress', 'Won''t fix')");
            await Run("""
                CREATE TABLE codegen_enum_test.ticket (
                    ticket_id serial PRIMARY KEY,
                    title varchar(50) NOT NULL,
                    status codegen_enum_test.ticket_status NOT NULL,
                    priority text NOT NULL CHECK (priority IN ('Low', 'Medium', 'High')),
                    points int CHECK (points > 0))
                """);

            var model = await Provider().BuildTableModelAsync("codegen_enum_test", "ticket");

            var status = model.Columns.Single(c => c.Name == "status");
            CollectionAssert.AreEqual(new[] { "Open", "In progress", "Won't fix" }, status.Choices!);
            Assert.AreEqual("codegen_enum_test.ticket_status", status.DbEnumType);
            Assert.AreEqual(SqlDbType.VarChar, status.SqlType);
            Assert.AreEqual("varchar(11)", status.SqlTypeDeclaration);

            var priority = model.Columns.Single(c => c.Name == "priority");
            CollectionAssert.AreEqual(new[] { "Low", "Medium", "High" }, priority.Choices!);
            Assert.IsNull(priority.DbEnumType);
            Assert.AreEqual(6, priority.MaxLength);               // an unbounded text column is as long as its longest value, so it is not "long text"
            Assert.IsFalse(priority.IsLongTextColumn);

            Assert.IsNull(model.Columns.Single(c => c.Name == "points").Choices);   // a range is not a list
            Assert.IsNull(model.Columns.Single(c => c.Name == "title").Choices);
        }
        finally
        {
            await Run("DROP SCHEMA IF EXISTS codegen_enum_test CASCADE");
        }
    }
}
