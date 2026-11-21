using CodeGenNew.Core;

namespace CodeGenNew.Tests.Desktop;

/// <summary> GridGroupBy (a Group by toggle on the WinUI3 and Uno list) and GridExportTables (an Export all button): the settings, and the pages that gain them only when asked. </summary>
[TestClass]
public class GridGroupAndExportTests
{
    private static TableModel ItemTable() => Sample.Table("Item", [
        Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ItemNumber", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
        Sample.Column("Category", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 3),
        Sample.Column("IsActive", System.Data.SqlDbType.Bit, ordinal: 4)]);

    private static ProjectSettings Settings(params (string, string)[] extra) =>
        ProjectSettings.FromValues([new("ProjectName", "Acme"), new("WinUI3Grid", "TableView"), .. extra.Select(e => new KeyValuePair<string, string>(e.Item1, e.Item2))]);

    private static async Task<string> Page(string template, params (string, string)[] extra)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), ItemTable(), Settings(extra));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public void The_settings_name_a_column_per_table_and_the_tables_that_export()
    {
        var settings = Settings(("GridGroupBy", "Item:Category, Order:Status"), ("GridExportTables", "Customer"));

        Assert.AreEqual("Category", settings.GridGroupColumn("item"));
        Assert.AreEqual("Status", settings.GridGroupColumn("Order"));
        Assert.IsNull(settings.GridGroupColumn("Customer"));
        Assert.IsTrue(settings.GridExportAll("customer"));
        Assert.IsFalse(settings.GridExportAll("Item"));
        Assert.IsTrue(Settings(("GridExportTables", "*")).GridExportAll("Item"));
        Assert.IsNull(Settings().GridGroupColumn("Item"));
        Assert.IsFalse(Settings().GridExportAll("Item"));
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("UNO_MasterScreen_v1.tt")]
    public async Task Without_the_settings_the_list_has_neither_the_toggle_nor_the_button(string template)
    {
        string text = await Page(template);

        Expect.DoesNotContain(text, "GroupToggle");
        Expect.DoesNotContain(text, "ApplyGrouping");
        Expect.DoesNotContain(text, "Export all");
        Expect.DoesNotContain(text, "ExportAllAsync");
        Expect.DoesNotContain(text, "\n@@\n");   // the marker of a line left out
        Expect.Contains(text, "        });\n        ShowSortMarks();\n    }");   // BuildColumns ends as it did before the settings existed
    }

    [TestMethod]
    public async Task WinUI3_groups_by_the_column_and_exports_every_page()
    {
        string text = await Page("WinUI3_MasterScreen_v1.tt", ("GridGroupBy", "Item:Category"), ("GridExportTables", "Item"));

        Expect.Contains(text, "<ToggleButton x:Name=\"GroupToggle\" Content=\"Group\" IsChecked=\"True\" Click=\"OnGroupToggled\" />");
        Expect.Contains(text, "Click=\"OnExportAllClick\"");
        Expect.Contains(text, "        ApplyGrouping();\n        ShowSortMarks();");
        Expect.Contains(text, "if (columns[i].Name == \"Category\")");
        Expect.Contains(text, "Table.GroupDescriptions.Add(new GroupDescription(\"VisibleCells\"");
        Expect.Contains(text, "((ItemListRow)row).VisibleCells[index]");
        Expect.DoesNotContain(text, "#if WINDOWS");
        Expect.Contains(text, "GroupToggle.Visibility = index >= 0 ? Visibility.Visible : Visibility.Collapsed;");
        Expect.Contains(text, "public async Task<int> ExportAllAsync(string path)");
        Expect.Contains(text, "private const int ExportPageSize = 1000;");
        Expect.Contains(text, "ExportActiveWindow()");
        Expect.Contains(text, "FileTypeChoices.Add(\"CSV\"");
    }

    [TestMethod]
    public async Task Uno_groups_only_on_the_Windows_head()
    {
        string text = await Page("UNO_MasterScreen_v1.tt", ("GridGroupBy", "Item:Category"), ("GridExportTables", "*"));

        Expect.Contains(text, "#if WINDOWS\n        Table.GroupDescriptions.Clear();");
        Expect.Contains(text, "#else\n        GroupToggle.Visibility = Visibility.Collapsed;\n#endif");
        Expect.Contains(text, "WinRT.Interop.WindowNative.GetWindowHandle(window)");
        Expect.Contains(text, "public async Task<int> ExportAllAsync(string path)");
        Expect.DoesNotContain(text, "ExportActiveWindow");
    }

    [TestMethod]
    public async Task A_table_that_is_not_named_gets_nothing()
    {
        string text = await Page("WinUI3_MasterScreen_v1.tt", ("GridGroupBy", "Other:Category"), ("GridExportTables", "Other"));

        Expect.DoesNotContain(text, "GroupToggle");
        Expect.DoesNotContain(text, "Export all");
    }

    [TestMethod]
    public async Task Grouping_needs_the_table_grid_but_export_does_not()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), ItemTable(),
            ProjectSettings.FromValues([new("ProjectName", "Acme"), new("GridGroupBy", "Item:Category"), new("GridExportTables", "Item")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!;

        Expect.DoesNotContain(text, "GroupToggle");
        Expect.Contains(text, "ExportAllAsync");
    }
}
