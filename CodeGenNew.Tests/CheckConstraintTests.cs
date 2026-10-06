using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> What a database says in a CHECK constraint (a range, a list of values) becomes the limits of the number boxes and inputs, the validation attributes and the drop-down. </summary>
[TestClass]
public class CheckConstraintTests
{
    private static CheckRange Range(string definition, string column = "x") => CheckConstraintParser.ParseRange(definition, column)!;

    // ------------------------------------------------------------------ ranges, in the three spellings

    [TestMethod]
    [DataRow("([CreditLimit]>=(0))", "CreditLimit", 0.0, false, null, false)]
    [DataRow("([Rating]>=(1) AND [Rating]<=(5))", "Rating", 1.0, false, 5.0, false)]
    [DataRow("(([Points]>(0)))", "Points", 0.0, true, null, false)]
    [DataRow("((0)<=[Qty])", "Qty", 0.0, false, null, false)]
    [DataRow("([Discount]>=(-0.5) AND [Discount]<(10.25))", "Discount", -0.5, false, 10.25, true)]
    [DataRow("CHECK ((points > 0))", "points", 0.0, true, null, false)]
    [DataRow("CHECK (((points >= 0) AND (points <= 10)))", "points", 0.0, false, 10.0, false)]
    [DataRow("CHECK ((price > (0)::numeric))", "price", 0.0, true, null, false)]
    [DataRow("(`rating` between 1 and 5)", "rating", 1.0, false, 5.0, false)]
    [DataRow("(`credit_limit` >= 0)", "credit_limit", 0.0, false, null, false)]
    [DataRow("([Level]=(3))", "Level", 3.0, false, 3.0, false)]
    public void A_plain_range_check_gives_its_limits(string definition, string column, double min, bool minStrict, double? max, bool maxStrict)
    {
        var range = CheckConstraintParser.ParseRange(definition, column);

        Assert.IsNotNull(range, definition);
        Assert.AreEqual(min, range.Min);
        Assert.AreEqual(minStrict, range.MinStrict);
        Assert.AreEqual(max, range.Max);
        Assert.AreEqual(maxStrict, range.MaxStrict);
    }

    [TestMethod]
    [DataRow("([Rating]>=(1) AND [Rating]<=(5) OR [Rating]=(99))")]   // alternatives
    [DataRow("([Code]>len([Name]))")]                                    // another column and a function
    [DataRow("([Start]<=[End])")]
    [DataRow("([Status]='Open')")]                                       // a string comparison
    [DataRow("([Rating]<>(3))")]
    [DataRow("(abs([Rating])>=(1))")]
    [DataRow("([Other]>=(1))")]                                          // another column's check
    public void A_check_that_is_not_a_plain_range_on_the_column_gives_nothing(string definition)
    {
        Assert.IsNull(CheckConstraintParser.ParseRange(definition, "Rating"), definition);
    }

    [TestMethod]
    public void A_strict_bound_is_the_next_whole_number_for_an_integer_and_the_stricter_check_wins_when_combined()
    {
        var positive = Range("(([x]>(0)))");
        Assert.AreEqual(1, positive.IntegerMin);
        Assert.IsNull(positive.IntegerMax);
        Assert.AreEqual(4, Range("([x]<(5))").IntegerMax);
        Assert.AreEqual(5, Range("([x]<=(5))").IntegerMax);

        var combined = Range("([x]>=(0))").Combine(Range("([x]>=(2) AND [x]<=(9))"));
        Assert.AreEqual(2.0, combined.Min);
        Assert.AreEqual(9.0, combined.Max);
        var strictWins = Range("([x]>=(1))").Combine(Range("([x]>(1))"));
        Assert.IsTrue(strictWins.MinStrict);
    }

    // ------------------------------------------------------------------ lists

    [TestMethod]
    [DataRow("([Status]='Open' OR [Status]='Closed')", "Open|Closed")]
    [DataRow("(([Status]=N'Open') OR ([Status]=N'On hold') OR ([Status]='It''s'))", "Open|On hold|It's")]
    [DataRow("(`status` in (_utf8mb4'Open',_utf8mb4'Closed'))", "Open|Closed")]
    [DataRow("(`status` in (_cp850\\'Open\\',_cp850\\'Closed\\'))", "Open|Closed")]
    [DataRow("([Status]='Closed' OR [Status]='On hold' OR [Status]='Open')", "Closed|On hold|Open")]
    [DataRow("(`status` in ('A', 'B,C'))", "A|B,C")]
    public void A_list_check_gives_its_values(string definition, string expected)
    {
        CollectionAssert.AreEqual(expected.Split('|'), CheckConstraintParser.ParseList(definition, "Status") ?? CheckConstraintParser.ParseList(definition, "status"));
    }

    [TestMethod]
    [DataRow("([Status]='Open' OR [Qty]>(1))")]
    [DataRow("([Status]='Open' AND [Status]<>'Closed')")]
    [DataRow("([Other]='A' OR [Other]='B')")]
    public void A_check_that_is_not_just_a_list_of_the_columns_values_gives_nothing(string definition)
    {
        Assert.IsNull(CheckConstraintParser.ParseList(definition, "Status"));
    }

    [TestMethod]
    public void A_constraint_names_its_column_only_when_it_mentions_exactly_one()
    {
        Assert.AreEqual("CreditLimit", CheckConstraintParser.SingleColumn("([CreditLimit]>=(0))"));
        Assert.AreEqual("credit_limit", CheckConstraintParser.SingleColumn("(`credit_limit` >= 0)"));
        Assert.IsNull(CheckConstraintParser.SingleColumn("([Start]<=[End])"));
        Assert.IsNull(CheckConstraintParser.SingleColumn("(1=1)"));
        Assert.AreEqual("Status", CheckConstraintParser.SingleColumn("([Status]='[x]' OR [Status]='y')"), "a bracket inside a value is not a column");
    }

    // ------------------------------------------------------------------ the limits reach the templates

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Product(CheckRange? credit, CheckRange? rating, CheckRange? weight) => Sample.Table("Product",
    [
        Sample.Column("ProductId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("CreditLimit", SqlDbType.Money, nullable: true, currency: true, ordinal: 3, check: credit),
        Sample.Column("Rating", SqlDbType.Int, ordinal: 4, check: rating),
        Sample.Column("Weight", SqlDbType.Decimal, precision: 8, scale: 2, ordinal: 5, check: weight)
    ]);

    private static async Task<string> Render(string template, TableModel table, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project ?? Project());
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public void A_check_narrows_the_whole_number_and_decimal_ranges_and_the_money_limits()
    {
        var table = Product(Range("([CreditLimit]>=(0) AND [CreditLimit]<=(5000))", "CreditLimit"), Range("([Rating]>=(1) AND [Rating]<=(5))", "Rating"), Range("([Weight]>(0))", "Weight"));
        var project = Project();

        var rating = project.RangeFor(table.Columns.Single(c => c.Name == "Rating"))!.Value;
        Assert.AreEqual((1L, 5L), (rating.Min, rating.Max));
        Assert.AreEqual((0.0, 999999.99), NumericClassifier.DecimalRange(table.Columns.Single(c => c.Name == "Weight")));   // a strict bound is taken as inclusive
        Assert.AreEqual((0.0, (double?)5000.0), project.MoneyLimits("Product", table.Columns.Single(c => c.Name == "CreditLimit")));
        Assert.AreEqual("Minimum=\"0\" Maximum=\"5000\" ", project.MoneyLimitAttributes("Product", table.Columns.Single(c => c.Name == "CreditLimit")));
        Assert.AreEqual(" min=\"0\" max=\"5000\"", project.MoneyLimitHtml("Product", table.Columns.Single(c => c.Name == "CreditLimit")));
    }

    [TestMethod]
    public void The_project_list_still_works_for_a_database_that_has_no_check_and_a_check_wins_when_stricter()
    {
        var plain = Product(null, null, null).Columns.Single(c => c.Name == "CreditLimit");
        var positive = Product(Range("([CreditLimit]>=(10))", "CreditLimit"), null, null).Columns.Single(c => c.Name == "CreditLimit");
        var project = Project(("NonNegativeColumns", "Product.CreditLimit"));

        Assert.AreEqual((0.0, (double?)null), project.MoneyLimits("Product", plain));
        Assert.AreEqual((10.0, (double?)null), project.MoneyLimits("Product", positive));
        Assert.AreEqual((null, (double?)null), Project().MoneyLimits("Product", plain));
    }

    [TestMethod]
    public async Task The_forms_and_the_validation_class_carry_what_the_database_checks()
    {
        var table = Product(Range("([CreditLimit]>=(0) AND [CreditLimit]<=(5000))", "CreditLimit"), Range("([Rating]>=(1) AND [Rating]<=(5))", "Rating"), Range("([Weight]>=(0))", "Weight"));

        string xaml = string.Join("\n", GeneratedFiles.Split(await Render("WinUI3_DetailScreen_v1.tt", table)).Where(f => f.RelativePath.EndsWith(".xaml")).Select(f => f.Content));
        Expect.Contains(xaml, "x:Name=\"CreditLimitBox\" Header=\"Credit Limit\" Value=\"{x:Bind ViewModel.CreditLimit, Mode=TwoWay}\"\n                       Minimum=\"0\" Maximum=\"5000\" ValidationMode");
        Expect.Contains(xaml.Split("Header=\"Rating\"")[1].Split("/>")[0], "Minimum=\"1\" Maximum=\"5\"");
        Expect.Contains(xaml.Split("x:Name=\"WeightBox\"")[1].Split("/>")[0], "Minimum=\"0\"");

        string react = await Render("TSX_Page_v1.tt", table);
        Expect.Contains(react, "min=\"0\"");
        Expect.Contains(react, "max=\"5000\"");
        Expect.Contains(react, "min=\"1\"");
        Expect.Contains(react, "max=\"5\"");

        string angular = string.Join("\n", GeneratedFiles.Split(await Render("TS_Component_v1.tt", table)).Select(f => f.Content));
        Expect.Contains(angular, "min=\"1\" max=\"5\"");
        Expect.Contains(angular, " min=\"0\" max=\"5000\"");

        string validation = await Render("CS_Validation_v1.tt", table);
        Expect.Contains(validation, "[Range(1, 5)]");
        Expect.Contains(validation, "[Range(0, 5000)]");
        Expect.Contains(validation, "[Range(0, double.MaxValue)]");

        string viewModel = string.Join("\n", GeneratedFiles.Split(await Render("WinUI3_DetailScreen_v1.tt", table)).Where(f => f.RelativePath.EndsWith("ViewModel.cs")).Select(f => f.Content));
        Expect.Contains(viewModel, "Rating must be between 1 and 5.");
    }

    [TestMethod]
    public async Task A_column_without_a_check_is_written_as_before()
    {
        var table = Product(null, null, null);

        string xaml = string.Join("\n", GeneratedFiles.Split(await Render("WinUI3_DetailScreen_v1.tt", table)).Where(f => f.RelativePath.EndsWith(".xaml")).Select(f => f.Content));
        Expect.DoesNotContain(xaml.Split("x:Name=\"CreditLimitBox\"")[1].Split("/>")[0], "Minimum");
        Expect.DoesNotContain(await Render("CS_Validation_v1.tt", table), "[Range(");
    }

    // ------------------------------------------------------------------ a live read of each database (a scratch table, dropped again)

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private const string Definitions = "rating >= 1 AND rating <= 5, credit_limit >= 0, status in a list";

    [TestMethod]
    public async Task SQL_Server_reads_check_ranges_and_lists_from_its_catalog()
    {
        string? host = Env("CODEGENNEW_SQLSERVER_HOST"), database = Env("CODEGENNEW_SQLSERVER_DATABASE");
        if (host is null || database is null)
            Assert.Inconclusive("Set CODEGENNEW_SQLSERVER_HOST and CODEGENNEW_SQLSERVER_DATABASE (Windows authentication, or also _USER and _PASSWORD) to run the SQL Server check test.");
        string? user = Env("CODEGENNEW_SQLSERVER_USER");
        var request = new ConnectionRequest
        {
            Provider = DatabaseProvider.SqlServer, ServerName = host!, DatabaseName = database!,
            AuthMode = user is null ? AuthMode.WindowsAuth : AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_SQLSERVER_PASSWORD")
        };

        await using var connection = request.CreateConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("IF OBJECT_ID('dbo.codegen_check_test') IS NOT NULL DROP TABLE dbo.codegen_check_test");
            await Run("""
                CREATE TABLE dbo.codegen_check_test (
                    id int IDENTITY PRIMARY KEY,
                    rating int NOT NULL CHECK (rating >= 1 AND rating <= 5),
                    credit_limit money NULL,
                    status nvarchar(20) NOT NULL CHECK (status IN ('Open', 'On hold', 'Closed')),
                    start_date date NULL,
                    end_date date NULL,
                    CONSTRAINT CK_codegen_check_limit CHECK (credit_limit >= 0),
                    CONSTRAINT CK_codegen_check_dates CHECK (start_date <= end_date))
                """);

            var model = await new SqlServerSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config")).BuildTableModelAsync("dbo", "codegen_check_test");

            Assert.AreEqual((1.0, 5.0), (model.Columns.Single(c => c.Name == "rating").Check!.Min!.Value, model.Columns.Single(c => c.Name == "rating").Check!.Max!.Value));
            Assert.AreEqual(0.0, model.Columns.Single(c => c.Name == "credit_limit").Check!.Min);   // a table-level constraint on one column counts
            CollectionAssert.AreEqual(new[] { "Open", "On hold", "Closed" }, model.Columns.Single(c => c.Name == "status").Choices!.OrderBy(v => v == "Open" ? 0 : v == "On hold" ? 1 : 2).ToArray());
            Assert.IsNull(model.Columns.Single(c => c.Name == "start_date").Check);                 // a check over two columns belongs to neither
            Assert.IsNull(model.Columns.Single(c => c.Name == "id").Check);
        }
        finally
        {
            await Run("IF OBJECT_ID('dbo.codegen_check_test') IS NOT NULL DROP TABLE dbo.codegen_check_test");
        }
    }

    [TestMethod]
    public async Task PostgreSQL_reads_check_ranges()
    {
        string? host = Env("CODEGENNEW_PG_HOST"), database = Env("CODEGENNEW_PG_DATABASE"), user = Env("CODEGENNEW_PG_USER");
        if (host is null || database is null || user is null)
            Assert.Inconclusive("Set CODEGENNEW_PG_HOST, _DATABASE, _USER and _PASSWORD to run the PostgreSQL check test.");
        var request = new ConnectionRequest { Provider = DatabaseProvider.PostgreSql, ServerName = host!, DatabaseName = database!, AuthMode = AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_PG_PASSWORD") };

        await using var connection = request.CreatePostgresConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new Npgsql.NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP SCHEMA IF EXISTS codegen_check_test CASCADE");
            await Run("CREATE SCHEMA codegen_check_test");
            await Run("""
                CREATE TABLE codegen_check_test.item (
                    item_id serial PRIMARY KEY,
                    rating int NOT NULL CHECK (rating BETWEEN 1 AND 5),
                    price numeric(10,2) NOT NULL CHECK (price > 0),
                    credit_limit numeric(10,2) NULL CONSTRAINT ck_limit CHECK (credit_limit >= 0 AND credit_limit <= 5000))
                """);

            var model = await new PostgresSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config")).BuildTableModelAsync("codegen_check_test", "item");

            var rating = model.Columns.Single(c => c.Name == "rating").Check!;
            Assert.AreEqual((1.0, 5.0), (rating.Min!.Value, rating.Max!.Value));
            Assert.IsTrue(model.Columns.Single(c => c.Name == "price").Check!.MinStrict);
            var limit = model.Columns.Single(c => c.Name == "credit_limit").Check!;
            Assert.AreEqual((0.0, 5000.0), (limit.Min!.Value, limit.Max!.Value));
        }
        finally
        {
            await Run("DROP SCHEMA IF EXISTS codegen_check_test CASCADE");
        }
    }

    [TestMethod]
    public async Task MySQL_reads_check_ranges_and_lists()
    {
        string? host = Env("CODEGENNEW_MYSQL_HOST"), database = Env("CODEGENNEW_MYSQL_DATABASE"), user = Env("CODEGENNEW_MYSQL_USER");
        if (host is null || database is null || user is null)
            Assert.Inconclusive("Set CODEGENNEW_MYSQL_HOST, _DATABASE, _USER and _PASSWORD to run the MySQL check test.");
        var request = new ConnectionRequest { Provider = DatabaseProvider.MySql, ServerName = host!, DatabaseName = database!, AuthMode = AuthMode.SqlLogin, UserName = user, Password = Env("CODEGENNEW_MYSQL_PASSWORD") };

        await using var connection = request.CreateMySqlConnection();
        await connection.OpenAsync();
        async Task Run(string sql) { await using var command = new MySqlConnector.MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        try
        {
            await Run("DROP TABLE IF EXISTS codegen_check_test");
            await Run("""
                CREATE TABLE codegen_check_test (
                    id int AUTO_INCREMENT PRIMARY KEY,
                    rating int NOT NULL,
                    credit_limit decimal(10,2) NULL,
                    status varchar(20) NOT NULL,
                    CONSTRAINT ck_codegen_rating CHECK (rating BETWEEN 1 AND 5),
                    CONSTRAINT ck_codegen_limit CHECK (credit_limit >= 0),
                    CONSTRAINT ck_codegen_status CHECK (status IN ('Open', 'Closed')))
                """);

            var model = await new MySqlSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config")).BuildTableModelAsync(database!, "codegen_check_test");

            var rating = model.Columns.Single(c => c.Name == "rating").Check!;
            Assert.AreEqual((1.0, 5.0), (rating.Min!.Value, rating.Max!.Value));
            Assert.AreEqual(0.0, model.Columns.Single(c => c.Name == "credit_limit").Check!.Min);
            CollectionAssert.AreEqual(new[] { "Open", "Closed" }, model.Columns.Single(c => c.Name == "status").Choices!);
        }
        finally
        {
            await Run("DROP TABLE IF EXISTS codegen_check_test");
        }
    }
}
