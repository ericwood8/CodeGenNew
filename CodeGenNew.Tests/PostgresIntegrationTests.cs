using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> Reads the schema of a real PostgreSQL copy of the InvoiceSystem sample database. They need a live server, so they run only when
/// CODEGENNEW_PG_HOST (host or host:port), CODEGENNEW_PG_DATABASE, CODEGENNEW_PG_USER and CODEGENNEW_PG_PASSWORD are set (the database is the one
/// created by the PostgreSQL sample's CreateInvoiceSystemPg.sql and SeedInvoiceSystemPg.sql); otherwise each test reports itself as inconclusive.
/// Nothing is written to the database, except by the enum and snake_case tests, which create a throwaway schema and drop it again. </summary>
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

        Assert.HasCount(2, model.Rows);
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

    [TestMethod]
    public async Task A_snake_case_schema_reads_as_pascal_names_over_the_real_ones_and_its_generated_functions_run()
    {
        // Writes a throwaway schema (dropped again whatever happens): snake_case tables, an unsupported array column and a bare numeric.
        var request = Request();
        if (request is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, CODEGENNEW_PG_DATABASE, CODEGENNEW_PG_USER and CODEGENNEW_PG_PASSWORD to run the PostgreSQL integration tests.");

        await using var connection = request!.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP SCHEMA IF EXISTS codegen_snake_test CASCADE");
            await Run("CREATE SCHEMA codegen_snake_test");
            await Run("""
                CREATE TABLE codegen_snake_test.customer_account (
                    customer_account_id serial PRIMARY KEY,
                    account_number varchar(20) NOT NULL UNIQUE,
                    created_date timestamp NOT NULL DEFAULT now(),
                    credit_limit numeric,
                    labels text[],
                    span interval,
                    visits tsrange)
                """);
            await Run("""
                CREATE TABLE codegen_snake_test.customer_note (
                    customer_note_id serial PRIMARY KEY,
                    customer_account_id int NOT NULL REFERENCES codegen_snake_test.customer_account,
                    note_text varchar(100) NOT NULL)
                """);
            string config = Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config");

            var plain = new PostgresSchemaProvider(request, config, NamingStyle.Pascal);
            var model = await plain.BuildTableModelAsync("codegen_snake_test", "customer_account");

            Assert.AreEqual("CustomerAccount", model.TableName);
            Assert.AreEqual("customer_account", model.DbTableName);
            var id = model.Columns.Single(c => c.Name == "CustomerAccountId");
            Assert.AreEqual("customer_account_id", id.DbName);
            Assert.IsTrue(id.IsIdentity);
            Assert.AreEqual("AccountNumber", model.Columns.Single(c => c.DbName == "account_number").Name);

            // a bare numeric is a decimal of 38 digits and 4 places; the SQL keeps "numeric"
            var limit = model.Columns.Single(c => c.Name == "CreditLimit");
            Assert.AreEqual((SqlDbType.Decimal, 38, 4, "numeric"), (limit.SqlType, (int)limit.Precision!, (int)limit.Scale!, limit.SqlTypeDeclaration));
            Assert.AreEqual(2, NumericClassifier.CurrencyDigits(limit));

            // an array, an interval, a range and a shape are text with their type; a type with no mapping (tsrange) is reported, and listing it leaves it out
            Assert.AreEqual("text[]", model.Columns.Single(c => c.Name == "Labels").DbTextType);
            Assert.AreEqual("interval", model.Columns.Single(c => c.Name == "Span").DbTextType);
            CollectionAssert.AreEqual(new[] { "Visits" }, model.UnsupportedColumns.Select(c => c.Name).ToList());
            var ignoring = new PostgresSchemaProvider(request, config, NamingStyle.Pascal) { IgnoredColumns = ["visits"] };
            var without = await ignoring.BuildTableModelAsync("codegen_snake_test", "customer_account");
            Assert.IsFalse(without.Columns.Any(c => c.DbName == "visits"));
            Assert.IsFalse(without.UnsupportedColumns.Any());
            var byGeneratedName = await new PostgresSchemaProvider(request, config, NamingStyle.Pascal) { IgnoredColumns = ["CustomerAccount.Visits"] }.BuildTableModelAsync("codegen_snake_test", "customer_account");
            Assert.IsFalse(byGeneratedName.Columns.Any(c => c.DbName == "visits"));
            var primaryKeyKept = await new PostgresSchemaProvider(request, config, NamingStyle.Pascal) { IgnoredColumns = ["customer_account_id"] }.BuildTableModelAsync("codegen_snake_test", "customer_account");
            Assert.IsTrue(primaryKeyKept.Columns.Any(c => c.DbName == "customer_account_id"), "a primary key column is never left out");

            // the child table: the foreign key and its parent keep the real names for SQL
            var note = await plain.BuildTableModelAsync("codegen_snake_test", "customer_note", includeReferencedDisplayColumns: true);
            var fk = note.ForeignKeys.Single();
            Assert.AreEqual("CustomerAccount", fk.ReferencedTable);
            Assert.AreEqual("customer_account", fk.ReferencedDbTable);

            // every function the generator writes for the table runs in the database: the real names are what the SQL says
            foreach (string template in new[] { "SP_Insert_v1.tt", "SP_Update_v1.tt", "SP_Save_v1.tt", "SP_Delete_v1.tt", "SP_Clone_v1.tt", "SP_Search_v1.tt", "SP_Lookup_v1.tt" })
            {
                var result = await Repo.Cache.RunAsync(Repo.Template(template), without, ProjectSettings.FromValues([new("ProjectName", "Snake")]));
                Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
                await Run(result.GeneratedText!);
            }
            await using var count = new Npgsql.NpgsqlCommand("SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'codegen_snake_test'", connection);
            Assert.IsGreaterThanOrEqualTo(7, (long)(await count.ExecuteScalarAsync())!);
        }
        finally
        {
            await Run("DROP SCHEMA IF EXISTS codegen_snake_test CASCADE");
        }
    }

    [TestMethod]
    public async Task Array_interval_range_and_shape_columns_are_text_with_their_type_and_their_functions_cast_it()
    {
        // Writes a throwaway schema (dropped again whatever happens).
        var request = Request();
        if (request is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, CODEGENNEW_PG_DATABASE, CODEGENNEW_PG_USER and CODEGENNEW_PG_PASSWORD to run the PostgreSQL integration tests.");

        await using var connection = request!.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        async Task<string?> Scalar(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); return (await command.ExecuteScalarAsync())?.ToString(); }
        try
        {
            await Run("DROP SCHEMA IF EXISTS codegen_text_test CASCADE");
            await Run("CREATE SCHEMA codegen_text_test");
            await Run("""
                CREATE TABLE codegen_text_test.place (
                    place_id serial PRIMARY KEY,
                    place_name varchar(50) NOT NULL UNIQUE,
                    tags text[] NOT NULL DEFAULT '{}',
                    scores int4[], weights numeric[], flags bool[], ids uuid[], days date[],
                    span interval,
                    budget int4range, big int8range, cost numrange, period daterange,
                    spot point, edge lseg, area box, ring circle, route path, zone polygon, axis line)
                """);

            var provider = new PostgresSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config"), NamingStyle.Pascal);
            var model = await provider.BuildTableModelAsync("codegen_text_test", "place");

            var expected = new Dictionary<string, string>
            {
                ["Tags"] = "text[]", ["Scores"] = "int4[]", ["Weights"] = "numeric[]", ["Flags"] = "bool[]", ["Ids"] = "uuid[]", ["Days"] = "date[]", ["Span"] = "interval",
                ["Budget"] = "int4range", ["Big"] = "int8range", ["Cost"] = "numrange", ["Period"] = "daterange",
                ["Spot"] = "point", ["Edge"] = "lseg", ["Area"] = "box", ["Ring"] = "circle", ["Route"] = "path", ["Zone"] = "polygon", ["Axis"] = "line"
            };
            foreach (var (name, type) in expected)
            {
                var column = model.Columns.Single(c => c.Name == name);
                Assert.AreEqual(type, column.DbTextType, name);
                Assert.AreEqual(SqlDbType.VarChar, column.SqlType, name);
                Assert.IsTrue(column.IsStringColumn, name);
                Assert.AreEqual($"varchar({column.PgText!.MaxLength})", column.SqlTypeDeclaration, name);
                Assert.IsFalse(column.IsLongTextColumn, name);
            }
            Assert.IsFalse(model.UnsupportedColumns.Any());
            Assert.IsFalse(model.Columns.Single(c => c.Name == "Tags").IsNullable);

            // every function the generator writes for the table runs, and the row they write is read back in the database's own notation
            var project = ProjectSettings.FromValues([new("ProjectName", "Text")]);
            foreach (string template in new[] { "SP_Insert_v1.tt", "SP_Update_v1.tt", "SP_Save_v1.tt", "SP_Delete_v1.tt", "SP_Clone_v1.tt", "SP_Search_v1.tt", "SP_Lookup_v1.tt" })
            {
                var result = await Repo.Cache.RunAsync(Repo.Template(template), model, project);
                Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
                await Run(result.GeneratedText!);
            }
            string args = "'First', '', '{1,2}', '{1.5}', '{t,f}', null, '{2024-01-02}', '90 minutes', '[3,9)', null, null, '[2024-01-01,2024-01-05]', '(1,2)', null, '(1,1),(2,2)', '<(0,0),1>', '[(1,1),(2,2)]', '((0,0),(0,1),(1,1))', '{1,2,3}'";
            await Run($"SELECT codegen_text_test.\"Place_Insert\"({args})");
            Assert.AreEqual("{}", await Scalar("SELECT tags::text FROM codegen_text_test.place WHERE place_name = 'First'"));
            Assert.AreEqual("01:30:00", await Scalar("SELECT span::text FROM codegen_text_test.place WHERE place_name = 'First'"));
            Assert.AreEqual("[2024-01-01,2024-01-06)", await Scalar("SELECT period::text FROM codegen_text_test.place WHERE place_name = 'First'"));
            Assert.AreEqual("(2,2),(1,1)", await Scalar("SELECT area::text FROM codegen_text_test.place WHERE place_name = 'First'"));
        }
        finally
        {
            await Run("DROP SCHEMA IF EXISTS codegen_text_test CASCADE");
        }
    }

    [TestMethod]
    public async Task A_length_or_like_check_on_a_text_column_is_its_notation()
    {
        // Writes a throwaway schema (dropped again whatever happens).
        var request = Request();
        if (request is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, CODEGENNEW_PG_DATABASE, CODEGENNEW_PG_USER and CODEGENNEW_PG_PASSWORD to run the PostgreSQL integration tests.");

        await using var connection = request!.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP SCHEMA IF EXISTS codegen_checks_test CASCADE");
            await Run("CREATE SCHEMA codegen_checks_test");
            await Run("""
                CREATE TABLE codegen_checks_test.part (
                    part_id serial PRIMARY KEY,
                    code varchar(10) CHECK (code LIKE 'AB%'),
                    five varchar(10) CHECK (length(five) = 5),
                    spread varchar(10) CHECK (char_length(spread) >= 3 AND char_length(spread) <= 8),
                    other varchar(10) CHECK (length(other) > 2 OR other = 'x'),
                    big numeric(5,2) CHECK (big >= 0 AND big <= 100000))
                """);

            var model = await new PostgresSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config"), NamingStyle.Pascal).BuildTableModelAsync("codegen_checks_test", "part");
            ColumnModel Of(string name) => model.Columns.Single(c => c.Name == name);

            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch("AB1", "^(?:" + Of("Code").CheckNotation!.Pattern + ")$"));
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch("XB1", "^(?:" + Of("Code").CheckNotation!.Pattern + ")$"));
            Assert.AreEqual("text of 5 characters", Of("Five").CheckNotation!.Noun);
            Assert.AreEqual("text of 3 to 8 characters", Of("Spread").CheckNotation!.Noun);
            Assert.IsNull(Of("Other").CheckNotation, "an OR is left to the database");
            var rule = Of("Big").RuleOf(ProjectSettings.FromValues([new("ProjectName", "Acme")]))!;
            Assert.AreEqual((0.0, 999.99), (rule.Min, rule.Max), "the check is wider than numeric(5,2)");
        }
        finally
        {
            await Run("DROP SCHEMA IF EXISTS codegen_checks_test CASCADE");
        }
    }

    [TestMethod]
    public async Task A_foreign_keys_delete_rule_is_read()
    {
        // Writes a throwaway schema (dropped again whatever happens).
        var request = Request();
        if (request is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, CODEGENNEW_PG_DATABASE, CODEGENNEW_PG_USER and CODEGENNEW_PG_PASSWORD to run the PostgreSQL integration tests.");

        await using var connection = request!.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP SCHEMA IF EXISTS codegen_rules_test CASCADE");
            await Run("CREATE SCHEMA codegen_rules_test");
            await Run("CREATE TABLE codegen_rules_test.parent (parent_id serial PRIMARY KEY)");
            await Run("""
                CREATE TABLE codegen_rules_test.child (
                    child_id serial PRIMARY KEY,
                    plain_id int NOT NULL REFERENCES codegen_rules_test.parent,
                    restrict_id int NOT NULL REFERENCES codegen_rules_test.parent ON DELETE RESTRICT,
                    cascade_id int NOT NULL REFERENCES codegen_rules_test.parent ON DELETE CASCADE,
                    null_id int NULL REFERENCES codegen_rules_test.parent ON DELETE SET NULL)
                """);

            var model = await new PostgresSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config"), NamingStyle.Pascal).BuildTableModelAsync("codegen_rules_test", "child");
            ForeignKeyDeleteRule RuleOf(string column) => model.ForeignKeys.Single(f => f.ReferencingColumns[0] == column).DeleteRule;

            Assert.AreEqual(ForeignKeyDeleteRule.NoAction, RuleOf("PlainId"));
            Assert.AreEqual(ForeignKeyDeleteRule.Restrict, RuleOf("RestrictId"));
            Assert.AreEqual(ForeignKeyDeleteRule.Cascade, RuleOf("CascadeId"));
            Assert.AreEqual(ForeignKeyDeleteRule.SetNull, RuleOf("NullId"));
        }
        finally
        {
            await Run("DROP SCHEMA IF EXISTS codegen_rules_test CASCADE");
        }
    }
}
