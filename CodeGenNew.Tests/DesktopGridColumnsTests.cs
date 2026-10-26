using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The Columns list of the WinUI3 and Uno list pages: one set of C# text in Core, the store and row control written by the GridColumns templates, the members the master screens add. </summary>
[TestClass]
public class DesktopGridColumnsTests
{
    private static ProjectSettings Project() => ProjectSettings.FromValues([new("ProjectName", "Acme")]);

    private static async Task<string> RunSupport(string template)
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday()] };
        var result = await Repo.Cache.RunAsync(Repo.Template(template), database, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    private static async Task<string> RunPage(string template, TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    [DataRow("WinUI3_GridColumns_v1.tt")]
    [DataRow("UNO_GridColumns_v1.tt")]
    public async Task Both_desktop_stacks_write_the_same_store_and_row_control_from_Core(string template)
    {
        string text = await RunSupport(template);

        Expect.Contains(text, "ViewModels/GridColumnStore.cs@@@");
        Expect.Contains(text, "Views/GridCells.cs@@@");
        Expect.Contains(text, $"public const int CutLength = {ChosenGridColumns.CutLength};");
        Expect.Contains(text, "\"GridColumns.json\"");
        Expect.Contains(text, "return kept.Count > 0 ? kept : [.. initial];");   // the rule of ChosenGridColumns.Resolve
        Expect.Contains(text, "return chosen.Count == 1 ? [.. chosen] : chosen.Where(n => n != name).ToList();");   // the last shown column stays
        Expect.Contains(text, "public sealed class GridCells : Grid");
    }

    [TestMethod]
    public async Task The_two_stacks_write_the_same_files()
    {
        static string WithoutNamespaces(string text) => string.Join("\n", text.Split('\n').Where(line => !line.StartsWith("namespace ")));
        string winui3 = WithoutNamespaces(await RunSupport("WinUI3_GridColumns_v1.tt"));
        string uno = WithoutNamespaces(await RunSupport("UNO_GridColumns_v1.tt"));

        Assert.AreEqual(winui3, uno);
    }

    [TestMethod]
    [DataRow("WinUI3_MasterScreen_v1.tt")]
    [DataRow("UNO_MasterScreen_v1.tt")]
    public async Task A_master_screen_offers_every_column_starts_with_the_grid_columns_and_cuts_a_long_text(string template)
    {
        var table = Sample.Table("Item", [
            Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("ItemNumber", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
            Sample.Column("Notes", System.Data.SqlDbType.VarChar, characters: 400, ordinal: 3)]);

        string text = await RunPage(template, table);

        Expect.Contains(text, "new(\"ItemNumber\", \"Item #\", true),");
        Expect.Contains(text, "new(\"Notes\", \"Notes\", false),");   // long text is offered and does not sort
        Expect.Contains(text, "private static readonly string[] InitialColumns = [\"ItemNumber\"];");
        Expect.Contains(text, "GridColumnStore.Cut(e.Notes");
        Expect.Contains(text, "<Button Content=\"Columns\" Click=\"OnColumnsClick\" />");
        Expect.Contains(text, "<Grid x:Name=\"HeaderCells\"");
        Expect.Contains(text, "_chosenColumns = GridColumnStore.Load(GridKey, OfferedNames, InitialColumns);");
        Expect.Contains(text, "GridSortStore.Load(GridKey, SortableColumns.Where(IsShown).ToList());");
        // hiding the sorted column drops the sort, as on the web
        Expect.Contains(text, "if (_sortColumn is not null && !IsShown(_sortColumn))");
        Expect.DoesNotContain(text, "Text=\"{x:Bind Cells[");
    }

    [TestMethod]
    public void The_view_model_members_name_the_shown_columns_and_the_texts_of_a_row()
    {
        var table = Sample.Holiday();
        string members = DesktopGridColumns.ViewModelMembers(ChosenGridColumns.For(FormModel.For(table, Project())));

        Expect.Contains(members, "public static readonly GridColumnInfo[] OfferedColumns =");
        Expect.Contains(members, "public IReadOnlyList<GridColumnInfo> ShownColumns => OfferedColumns.Where(c => IsShown(c.Name)).ToList();");
        Expect.Contains(members, "private List<string> VisibleCells(List<string> cells)");
        Expect.DoesNotContain(members, "--");
    }
}
