using CodeGenNew.Core;

namespace CodeGenNew.Tests.Desktop;

/// <summary> GridTotalColumns (a page total line under the WinUI3 and Uno table grid) and GridFrozenColumns (the first columns stay in place): the settings, and the pages that gain them only when asked. </summary>
[TestClass]
public class GridTotalsAndFrozenTests
{
    private static TableModel LineTable() => Sample.Table("Line", [
        Sample.Column("LineId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Description", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
        Sample.Column("Quantity", System.Data.SqlDbType.Int, ordinal: 3),
        Sample.Column("UnitPrice", System.Data.SqlDbType.Decimal, nullable: true, ordinal: 4)]);

    private static ProjectSettings Settings(params (string, string)[] extra) =>
        ProjectSettings.FromValues([new("ProjectName", "Acme"), new("WinUI3Grid", "TableView"), .. extra.Select(e => new KeyValuePair<string, string>(e.Item1, e.Item2))]);

    private static async Task<string> Page(string template, params (string, string)[] extra)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), LineTable(), Settings(extra));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public void The_settings_name_the_total_columns_and_the_frozen_count_per_table()
    {
        var settings = Settings(("GridTotalColumns", "Line:Quantity, Line:UnitPrice, Order:Total"), ("GridFrozenColumns", "Line:2, Order:x"));

        CollectionAssert.AreEqual(new[] { "Quantity", "UnitPrice" }, settings.GridTotalColumns("line").ToArray());
        Assert.AreEqual(0, settings.GridTotalColumns("Customer").Count);
        Assert.AreEqual(2, settings.GridFrozenColumns("Line"));
        Assert.AreEqual(0, settings.GridFrozenColumns("Order"));
        Assert.AreEqual(0, Settings().GridFrozenColumns("Line"));
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("UNO_MasterScreen_v1.tt")]
    public async Task Without_the_settings_the_list_has_no_total_line_and_no_frozen_columns(string template)
    {
        string text = await Page(template);

        Expect.DoesNotContain(text, "TotalsText");
        Expect.DoesNotContain(text, "FrozenColumnCount");
        Expect.DoesNotContain(text, "\n@@\n");
        Expect.Contains(text, "        });\n        ShowSortMarks();\n    }");
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("UNO_MasterScreen_v1.tt")]
    public async Task The_list_sums_the_number_columns_of_the_page_and_freezes_the_first_columns(string template)
    {
        string text = await Page(template, ("GridTotalColumns", "Line:Quantity,Line:UnitPrice,Line:Description"), ("GridFrozenColumns", "Line:1"));

        Expect.Contains(text, "<TextBlock Grid.Row=\"1\" Margin=\"8,0\" FontWeight=\"Bold\" Text=\"{x:Bind ViewModel.TotalsText, Mode=OneWay}\" />");
        Expect.Contains(text, "<Border Grid.Row=\"2\" Background=");
        Expect.Contains(text, "Grid.RowSpan=\"3\"");
        Expect.Contains(text, "private string _totalsText = \"\";");
        Expect.Contains(text, "if (IsShown(\"Quantity\"))");
        Expect.Contains(text, "rows.Sum(r => (decimal)(r.Quantity)).ToString(\"N0\")");
        Expect.Contains(text, "rows.Sum(r => (decimal)(r.UnitPrice ?? 0)).ToString(\"N2\")");
        Expect.DoesNotContain(text, "IsShown(\"Description\")");   // text is not summed
        Expect.Contains(text, "TotalsText = totals.Count == 0 ? \"\" : \"Page total: \" + string.Join(\", \", totals);");
        Expect.Contains(text, "Table.FrozenColumnCount = Math.Min(1, columns.Count);");
    }
}
