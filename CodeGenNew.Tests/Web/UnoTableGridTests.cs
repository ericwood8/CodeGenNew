using CodeGenNew.Core;

namespace CodeGenNew.Tests.Web;

/// <summary> WinUI3Grid=TableView on the Uno list page: the same table as the WinUI3 page, the package in the Uno project and the width store beside the column store. </summary>
[TestClass]
public class UnoTableGridTests
{
    private static TableModel ItemTable() => Sample.Table("Item", [
        Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ItemNumber", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
        Sample.Column("Quantity", System.Data.SqlDbType.Int, ordinal: 3),
        Sample.Column("IsActive", System.Data.SqlDbType.Bit, ordinal: 4)]);

    private static ProjectSettings Settings(string? grid) =>
        ProjectSettings.FromValues(grid is null ? [new("ProjectName", "Acme")] : [new("ProjectName", "Acme"), new("WinUI3Grid", grid)]);

    private static async Task<string> Page(string? grid)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("UNO_MasterScreen_v1.tt"), ItemTable(), Settings(grid));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public async Task WinUI3Grid_TableView_draws_the_rows_in_a_table_the_service_sorts()
    {
        string text = await Page("TableView");

        Expect.Contains(text, "xmlns:tv=\"using:WinUI.TableView\"");
        Expect.Contains(text, "<DataTemplate x:Key=\"RowActions\" x:DataType=\"local:ItemListRow\">");
        Expect.Contains(text, "<tv:TableView x:Name=\"Table\" ItemsSource=\"{x:Bind ViewModel.Rows}\"");
        Expect.Contains(text, "Sorting=\"OnTableSorting\" ClearSorting=\"OnTableClearSorting\"");
        Expect.DoesNotContain(text, "CanGroupColumns");   // the Uno build of the control has no such property
        Expect.DoesNotContain(text, "<ListView");
        Expect.Contains(text, "private void BuildColumns()");
        Expect.Contains(text, "e.Handled = true;");
        Expect.Contains(text, "public bool? SortedDescending(string column)");
        Expect.Contains(text, "RightAlignedColumns = [\"Quantity\"];");
        Expect.Contains(text, "private void SaveWidths()");
        Expect.DoesNotContain(text, "BuildHeader");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("ListView")]
    public async Task A_blank_grid_and_ListView_write_the_same_Uno_page(string grid)
    {
        string blank = await Page(null);
        string named = await Page(grid.Length == 0 ? null : grid);

        Assert.AreEqual(blank, named);
        Expect.Contains(blank, "<ListView Grid.Row=\"1\" ItemsSource=\"{x:Bind ViewModel.Rows}\"");
        Expect.DoesNotContain(blank, "TableView");
    }

    [TestMethod]
    public async Task The_Uno_project_references_the_package_and_the_width_store_is_written_only_for_the_table()
    {
        async Task<string> Run(string template, ProjectSettings settings)
        {
            var result = template == "UNO_GridColumns_v1.tt"
                ? await Repo.Cache.RunAsync(Repo.Template(template), new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday()] }, settings)
                : await Repo.Cache.RunAsync(Repo.Template(template), settings);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            return result.GeneratedText!.Replace("\r\n", "\n");
        }

        Expect.Contains(await Run("UNO_EssentialProject_v1.tt", Settings("TableView")), $"<PackageReference Include=\"WinUI.TableView\" Version=\"{ProjectSettings.WinUi3TableViewVersion}\" />");
        Expect.DoesNotContain(await Run("UNO_EssentialProject_v1.tt", Settings(null)), "WinUI.TableView");
        Expect.Contains(await Run("UNO_GridColumns_v1.tt", Settings("TableView")), "ViewModels/GridWidthStore.cs@@@");
        Expect.DoesNotContain(await Run("UNO_GridColumns_v1.tt", Settings(null)), "GridWidthStore");
    }

    [TestMethod]
    public async Task An_unknown_grid_stops_the_Uno_page()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("UNO_MasterScreen_v1.tt"), ItemTable(), Settings("DataGrid"));

        Assert.IsFalse(result.Success);
        Expect.Contains(string.Join(" | ", result.Errors), "WinUI3Grid=DataGrid is neither");
    }
}
