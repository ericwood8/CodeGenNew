using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The project's value rules in the places that copy or rewrite rows (the clone routines and the bulk update), and the validation attributes on the request type of a table and on the Blazor model. </summary>
[TestClass]
public class GuardsAndAttributesEverywhereTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static readonly (string Key, string Value)[] Rules = [("NonNegativeColumns", "Quantity,Weight"), ("NotFutureDateColumns", "Stock.ShippedOn")];

    private static TableModel Stock(SqlDialect dialect = SqlDialect.SqlServer) => Sample.Table("Stock",
    [
        Sample.Column("StockId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Cube", SqlDbType.VarChar, characters: 40, ordinal: 2),
        Sample.Column("Quantity", SqlDbType.Int, ordinal: 3),
        Sample.Column("Weight", SqlDbType.Decimal, nullable: true, precision: 9, scale: 2, ordinal: 4),
        Sample.Column("ShippedOn", SqlDbType.Date, nullable: true, ordinal: 5),
        Sample.Column("BillingZip", SqlDbType.VarChar, nullable: true, characters: 20, ordinal: 6),
        Sample.Column("ContactEmail", SqlDbType.VarChar, nullable: true, characters: 120, ordinal: 7)
    ], dialect: dialect);

    private static async Task<string> Rendered(string template, object model, ProjectSettings project)
    {
        var result = model is DatabaseModel database
            ? await Repo.Cache.RunAsync(Repo.Template(template), database, project)
            : await Repo.Cache.RunAsync(Repo.Template(template), (TableModel)model, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    // ------------------------------------------------------------------ clone

    [TestMethod]
    public async Task A_clone_refuses_a_source_row_that_breaks_a_rule_in_every_database()
    {
        var project = Project(Rules);

        string sqlServer = await Rendered("SP_Clone_v1.tt", Stock(), project);
        Expect.Contains(sqlServer, "IF EXISTS (SELECT 1 FROM [dbo].[Stock] AS [src] WHERE [src].[StockId] = @CopyFromStockId AND [src].[Quantity] IS NOT NULL AND [src].[Quantity] < 0)");
        Expect.Contains(sqlServer, "THROW 50000, '[Quantity] must not be negative.', 1;");
        Expect.Contains(sqlServer, "CAST([src].[ShippedOn] AS DATE) > CAST(GETDATE() AS DATE)");
        Assert.IsLessThan(sqlServer.IndexOf("INSERT INTO", StringComparison.Ordinal), sqlServer.IndexOf("[Quantity] must not be negative", StringComparison.Ordinal), "the check comes before the copy");

        string postgres = await Rendered("SP_Clone_v1.tt", Stock(SqlDialect.PostgreSql), project);
        Expect.Contains(postgres, "AND \"src\".\"Quantity\" IS NOT NULL AND \"src\".\"Quantity\" < '0') THEN");
        Expect.Contains(postgres, "RAISE EXCEPTION '\"Quantity\" must not be negative.';");

        string mySql = await Rendered("SP_Clone_v1.tt", Stock(SqlDialect.MySql), project);
        Expect.Contains(mySql, "AND `src`.`Quantity` IS NOT NULL AND `src`.`Quantity` < 0) THEN");
        Expect.Contains(mySql, "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '`Quantity` must not be negative.';");
        Expect.Contains(mySql, "DATE(`src`.`ShippedOn`) > CURDATE()");

        foreach (var dialect in new[] { SqlDialect.SqlServer, SqlDialect.PostgreSql, SqlDialect.MySql })
            Expect.DoesNotContain(await Rendered("SP_Clone_v1.tt", Stock(dialect), Project()), "must not be negative");
    }

    // ------------------------------------------------------------------ stored values

    [TestMethod]
    public async Task An_update_leaves_a_value_it_does_not_change_alone_in_every_database()
    {
        var project = Project(Rules);

        string sqlServer = await Rendered("SP_Update_v1.tt", Stock(), project);
        Expect.Contains(sqlServer, "TRY_CAST(@pQuantity AS decimal(38, 10)) < 0 AND NOT EXISTS (SELECT 1 FROM [dbo].[Stock] WHERE [StockId] = @pStockId AND [Quantity] = TRY_CAST(@pQuantity AS decimal(38, 10))))");
        Expect.Contains(sqlServer, "AND NOT EXISTS (SELECT 1 FROM [dbo].[Stock] WHERE [StockId] = @pStockId AND CAST([ShippedOn] AS DATE) = TRY_CAST(@pShippedOn AS DATE))");

        string save = await Rendered("SP_Save_v1.tt", Stock(), project);
        Expect.Contains(save, "@pQuantity < 0 AND NOT EXISTS (SELECT 1 FROM [dbo].[Stock] WHERE [StockId] = @pStockId AND [Quantity] = @pQuantity))");
        string insert = await Rendered("SP_Insert_v1.tt", Stock(), project);
        Expect.DoesNotContain(insert, "NOT EXISTS (SELECT 1 FROM [dbo].[Stock] WHERE [StockId]");

        string postgres = await Rendered("SP_Update_v1.tt", Stock(SqlDialect.PostgreSql), project);
        Expect.Contains(postgres, "< '0' AND NOT EXISTS (SELECT 1 FROM \"dbo\".\"Stock\" WHERE \"StockId\" = ");
        Expect.Contains(postgres, "\"ShippedOn\"::date = ");

        string mySql = await Rendered("SP_Update_v1.tt", Stock(SqlDialect.MySql), project);
        Expect.Contains(mySql, "< 0 AND NOT EXISTS (SELECT 1 FROM `Stock` WHERE `StockId` = ");
        Expect.Contains(mySql, "DATE(`ShippedOn`) = DATE(");

        Expect.DoesNotContain(await Rendered("SP_Insert_v1.tt", Stock(SqlDialect.PostgreSql), project), "NOT EXISTS (SELECT 1 FROM \"dbo\".\"Stock\"");
    }

    [TestMethod]
    public async Task The_api_judges_an_update_only_where_the_value_changed()
    {
        var project = Project(Rules.Append(("ApiAnnotations", "true")).ToArray());
        var program = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), project);
        Assert.IsTrue(program.Success, string.Join(" | ", program.Errors));
        string text = program.GeneratedText!.ReplaceLineEndings("\n");

        Expect.Contains(text, "public static IResult? Check<T>(T row, T? stored = null) where T : class");
        Expect.Contains(text, ".Where(p => stored is null || Changed(row, stored, p.Member))");
        Expect.Contains(text, "return problems.Count == 0 ? null : Results.ValidationProblem(problems);");

        string crud = await CrudApiTests.Essential(Rules.Append(("ApiAnnotations", "true")).ToArray());
        Expect.Contains(crud, "private async Task<TEntity?> StoredAsync(");
    }

    // ------------------------------------------------------------------ bulk update

    [TestMethod]
    public async Task The_bulk_update_stops_and_undoes_a_rewrite_that_leaves_a_column_outside_its_rule()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Dialect = SqlDialect.SqlServer, Tables = [Stock()] };
        var project = Project(("BulkUpdateColumns", "Quantity"), ("BulkUpdateExpression", "{column} - 100"), ("NonNegativeColumns", "Quantity"));

        string sql = await Rendered("SP_BulkUpdate_v1.tt", database, project);

        Expect.Contains(sql, "UPDATE [dbo].[Stock] SET [Quantity] = [Quantity] - 100 WHERE [Quantity] IS NOT NULL;");
        Expect.Contains(sql, "IF EXISTS (SELECT 1 FROM [dbo].[Stock] WHERE [Quantity] IS NOT NULL AND [Quantity] < 0)");
        Expect.Contains(sql, "THROW 50000, 'Stock: [Quantity] must not be negative. (after the rewrite)', 1;");
        Assert.IsLessThan(sql.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal), sql.IndexOf("(after the rewrite)", StringComparison.Ordinal), "the check comes before the commit");
        Expect.DoesNotContain(await Rendered("SP_BulkUpdate_v1.tt", database, Project(("BulkUpdateColumns", "Quantity"), ("BulkUpdateExpression", "{column} - 100"))), "after the rewrite");
    }

    // ------------------------------------------------------------------ attributes

    [TestMethod]
    public async Task A_decimal_with_only_a_minimum_gets_a_range_with_the_types_own_maximum()
    {
        string metadata = await Rendered("CS_Validation_v1.tt", Stock(), Project(Rules));

        Expect.Contains(metadata, "[Range(0, double.MaxValue)]\n    public decimal? Weight { get; set; }");
        Expect.Contains(metadata, "[RegularExpression(@\"^(?:" + ColumnRules.PostalPattern + ")$\", ErrorMessage = \"Billing Zip is not a postal code.\")]");
    }

    [TestMethod]
    public async Task The_request_type_carries_the_attributes_when_the_api_runs_them()
    {
        var project = Project(Rules.Append(("ApiAnnotations", "true")).Append(("Dtos", "true")).ToArray());

        string input = await Rendered("CS_DtoInput_v1.tt", Stock(), project);

        Expect.Contains(input, "using System.ComponentModel.DataAnnotations;");
        Expect.Contains(input, "    [Required]\n    [Display(Name = \"Cube\", Description = \"Cube\")]\n    [StringLength(40)]\n    public string Cube { get; set; } = \"\";");
        Expect.Contains(input, "[EmailAddress]\n    public string? ContactEmail { get; set; }");
        Expect.Contains(input, "[Range(0, double.MaxValue)]\n    public decimal? Weight { get; set; }");
        Expect.DoesNotContain(await Rendered("CS_DtoInput_v1.tt", Stock(), Project(Rules.Append(("Dtos", "true")).ToArray())), "[Required]");
    }

    [TestMethod]
    public async Task The_blazor_model_and_form_check_the_attributes_when_the_api_runs_them()
    {
        var project = Project(Rules.Append(("ApiAnnotations", "true")).ToArray());

        string model = await Rendered("BLZ_Model_v1.tt", Stock(), project);
        Expect.Contains(model, "using System.ComponentModel.DataAnnotations;");
        Expect.Contains(model, "[Required]\n    [Display(Name = \"Cube\", Description = \"Cube\")]\n    [StringLength(40)]\n    public string Cube { get; set; } = \"\";");
        Expect.Contains(model, "[Range(0, double.MaxValue)]\n    public decimal? Weight { get; set; }");
        Expect.DoesNotContain(model, "[Display(Name = \"Stock Id\"");

        string page = await Rendered("BLZ_Page_v1.tt", Stock(), project);
        Expect.Contains(page, "<EditForm Model=\"row\" OnValidSubmit=\"SaveAsync\">\n            <DataAnnotationsValidator />");

        Expect.DoesNotContain(await Rendered("BLZ_Model_v1.tt", Stock(), Project(Rules)), "[Required]");
        Expect.DoesNotContain(await Rendered("BLZ_Page_v1.tt", Stock(), Project(Rules)), "DataAnnotationsValidator");
    }
}
