using CodeGenNew.Core;
using CodeGenNew.Core.Desktop;

namespace CodeGenNew.Tests.Desktop;

/// <summary> A person can drag a column of the table grid to another place; the order is kept with the shown columns and the columns are built again in it. </summary>
[TestClass]
public class GridColumnOrderTests
{
    private static TableModel ItemTable() => Sample.Table("Item", [
        Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ItemNumber", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
        Sample.Column("Category", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 3),
        Sample.Column("IsActive", System.Data.SqlDbType.Bit, ordinal: 4)]);

    private static async Task<string> Page(string template, string? grid)
    {
        var settings = ProjectSettings.FromValues(grid is null ? [new("ProjectName", "Acme")] : [new("ProjectName", "Acme"), new("WinUI3Grid", grid)]);
        var result = await Repo.Cache.RunAsync(Repo.Template(template), ItemTable(), settings);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("UNO_MasterScreen_v1.tt")]
    public async Task The_table_grid_lets_a_column_be_dragged_and_builds_the_columns_again_in_the_new_order(string template)
    {
        string text = await Page(template, "TableView");

        Expect.Contains(text, "CanReorderColumns=\"True\" ColumnReordered=\"OnTableColumnReordered\"");
        Expect.Contains(text, "private void OnTableColumnReordered(object? sender, TableViewColumnReorderedEventArgs e)");
        Expect.Contains(text, "ViewModel.SetColumnOrder(Table.Columns.Select(c => c.Tag as string).OfType<string>().ToList());");
        Expect.Contains(text, "            await ViewModel.LoadAsync();\n            BuildColumns();");
        Expect.Contains(text, "CanReorder = false");   // the Actions column stays last
        Expect.Contains(text, "public void SetColumnOrder(IReadOnlyList<string> order)");
        Expect.Contains(text, "private List<string> VisibleCells(List<string> cells) => [.. _chosenColumns.Where(IsShown).Select(n => cells[Array.IndexOf(OfferedNames, n)])];");
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("UNO_MasterScreen_v1.tt")]
    public async Task The_ListView_grid_has_no_drag_but_keeps_the_saved_order(string template)
    {
        string text = await Page(template, null);

        Expect.DoesNotContain(text, "OnTableColumnReordered");
        Expect.DoesNotContain(text, "CanReorderColumns");
        Expect.Contains(text, "_chosenColumns.Where(IsShown)");
    }

    [TestMethod]
    public void The_store_keeps_the_saved_order_and_a_column_that_is_ticked_goes_before_the_next_one_the_table_offers()
    {
        string store = DesktopGridColumns.StoreFile("Acme.ViewModels");

        Expect.Contains(store, "var kept = saved.Where(offered.Contains).Distinct().ToList();");
        Expect.Contains(store, "public static List<string> Reorder(IReadOnlyList<string> chosen, IReadOnlyList<string> order)");
        Expect.Contains(store, "int position = next.FindIndex(n => order.IndexOf(n) > order.IndexOf(name));");
        Expect.DoesNotContain(store, "return offered.Where(n => n == name || chosen.Contains(n)).ToList();");
    }
}
