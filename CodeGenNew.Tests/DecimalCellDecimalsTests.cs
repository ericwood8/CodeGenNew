using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A decimal column that is not money shows its own decimals in the web grids (a rate of 0.085 is not shown as 0.09), and keeps two for the usual two-decimal column. </summary>
[TestClass]
public class DecimalCellDecimalsTests
{
    private static TableModel Rates() => Sample.Table("Rate", [
        Sample.Column("RateId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.VarChar, characters: 30, ordinal: 2),
        Sample.Column("Fraction", SqlDbType.Decimal, precision: 9, scale: 4, ordinal: 3),
        Sample.Column("Weight", SqlDbType.Decimal, precision: 9, scale: 2, ordinal: 4)]);

    private static async Task<string> Run(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Rates(), ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!;
    }

    [TestMethod]
    public void A_column_holds_at_least_two_decimals_and_at_most_six()
    {
        var table = Rates();
        Assert.AreEqual(4, table.Columns.First(c => c.Name == "Fraction").CellDecimals);
        Assert.AreEqual(2, table.Columns.First(c => c.Name == "Weight").CellDecimals);
    }

    [TestMethod]
    public async Task The_react_grid_shows_the_columns_own_decimals()
    {
        string text = await Run("TSX_Page_v1.tt");
        Expect.Contains(text, ".toFixed(4)");
        Expect.Contains(text, ".toFixed(2)");
    }

    [TestMethod]
    public async Task The_angular_grid_shows_the_columns_own_decimals()
    {
        string text = await Run("TS_Component_v1.tt");
        Expect.Contains(text, "number:'1.2-4'");
        Expect.Contains(text, "number:'1.2-2'");
    }

    [TestMethod]
    public async Task The_blazor_grid_shows_the_columns_own_decimals()
    {
        string text = await Run("BLZ_Page_v1.tt");
        Expect.Contains(text, "ToString(\"N4\")");
        Expect.Contains(text, "ToString(\"N2\")");
    }

    private static async Task<string> RunLine(string template)
    {
        var line = Sample.Table("InvoiceLine", [
            Sample.Column("InvoiceLineId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Description", SqlDbType.VarChar, characters: 30, ordinal: 2),
            Sample.Column("TaxRate", SqlDbType.Decimal, precision: 9, scale: 4, ordinal: 3),
            Sample.Column("Discount", SqlDbType.Decimal, precision: 9, scale: 4, nullable: true, ordinal: 4)]);
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("WinUI3Grid", "TableView"), new("TaxLines", "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory")]);
        var result = await Repo.Cache.RunAsync(Repo.Template(template), line, project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!;
    }

    [TestMethod]
    public async Task A_tax_rate_shows_as_a_percentage_in_every_grid_and_another_decimal_keeps_its_own_scale()
    {
        Expect.Contains(await RunLine("TSX_Page_v1.tt"), "* 100).toFixed(2)}%");
        Expect.Contains(await RunLine("TSX_Page_v1.tt"), ".toFixed(4)");
        Expect.Contains(await RunLine("TS_Component_v1.tt"), "* 100 | number:'1.0-2'");
        Expect.Contains(await RunLine("BLZ_Page_v1.tt"), "* 100).ToString(\"0.##\") + \"%\"");
        foreach (string template in new[] { "WinUI3_MasterScreen_v1.tt", "UNO_MasterScreen_v1.tt" })
        {
            string text = await RunLine(template);
            Expect.Contains(text, "* 100).ToString(\"0.##\") + \"%\"");
            Expect.Contains(text, "ToString(\"N4\")");
        }
    }

    [TestMethod]
    public async Task A_tax_rate_shows_as_a_percentage_on_the_web_forms()
    {
        Expect.Contains(await RunLine("TSX_Page_v1.tt"), "+(selectedRow.taxRate * 100).toFixed(2) + '%'");
        Expect.Contains(await RunLine("TS_Component_v1.tt"), "(selectedRow.taxRate * 100 | number:'1.0-2') + '%'");
        Expect.Contains(await RunLine("BLZ_Page_v1.tt"), ".TaxRate * 100).ToString(\"0.##\") + \"%\"");
    }

    [TestMethod]
    [DataRow("WinUI3_DetailScreen_v1.tt")]
    [DataRow("UNO_DetailScreen_v1.tt")]
    public async Task A_tax_rate_shows_as_a_percentage_on_the_desktop_forms(string template)
    {
        string text = await RunLine(template);
        Expect.Contains(text, "public string RatePercent(double? value)");
        Expect.Contains(text, "Text=\"{x:Bind ViewModel.RatePercent(ViewModel.TaxRate), Mode=OneWay}\"");
    }

    [TestMethod]
    public async Task A_form_without_a_tax_rate_has_no_percent_member()
    {
        Expect.DoesNotContain(await Run("WinUI3_DetailScreen_v1.tt"), "RatePercent");
    }
}
