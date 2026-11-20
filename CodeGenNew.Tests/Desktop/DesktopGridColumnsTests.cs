using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;
using CodeGenNew.Core.Desktop;

namespace CodeGenNew.Tests.Desktop;

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
        Expect.Contains(text, "<Button Content=\"Columns\" FontWeight=\"Bold\" Background=\"{ThemeResource SystemControlBackgroundBaseLowBrush}\" Click=\"OnColumnsClick\" />");
        Expect.Contains(text, "<Grid x:Name=\"HeaderCells\"");
        Expect.Contains(text, "_chosenColumns = GridColumnStore.Load(GridKey, OfferedNames, InitialColumns);");
        Expect.Contains(text, "GridSortStore.Load(GridKey, SortableColumns.Where(IsShown).ToList());");
        // hiding the sorted column drops the sort, as on the web
        Expect.Contains(text, "if (_sortColumn is not null && !IsShown(_sortColumn))");
        Expect.DoesNotContain(text, "Text=\"{x:Bind Cells[");
    }

    private static TableModel ItemTable() => Sample.Table("Item", [
        Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ItemNumber", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
        Sample.Column("Notes", System.Data.SqlDbType.VarChar, characters: 400, ordinal: 3)]);

    [TestMethod]
    [DataRow("")]
    [DataRow("ListView")]
    [DataRow("listview")]
    public async Task The_winui3_master_screen_writes_the_same_page_whether_the_grid_is_blank_or_ListView(string grid)
    {
        var settings = grid.Length == 0 ? Project() : ProjectSettings.FromValues([new("ProjectName", "Acme"), new("WinUI3Grid", grid)]);
        var blank = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), ItemTable(), Project());
        var named = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), ItemTable(), settings);
        Assert.IsTrue(blank.Success && named.Success, string.Join(" | ", blank.Errors.Concat(named.Errors)));
        Assert.AreEqual(blank.GeneratedText, named.GeneratedText);
        Expect.Contains(named.GeneratedText!, "<ListView Grid.Row=\"1\" ItemsSource=\"{x:Bind ViewModel.Rows}\"");
    }

    [TestMethod]
    public async Task The_winui3_master_screen_refuses_a_grid_it_does_not_know()
    {
        var settings = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("WinUI3Grid", "DataGrid")]);
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), ItemTable(), settings);

        Assert.IsFalse(result.Success);
        Expect.Contains(string.Join(" | ", result.Errors), "WinUI3Grid=DataGrid is neither");
    }

    private static async Task<(string Xaml, string Code)> TablePage(params (string Key, string Value)[] more)
    {
        var settings = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("WinUI3Grid", "TableView"), .. more.Select(m => new KeyValuePair<string, string>(m.Key, m.Value))]);
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), ItemTable(), settings);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var files = result.GeneratedText!.Replace("\r\n", "\n").Split("@@@FILE ", StringSplitOptions.RemoveEmptyEntries).Skip(0).ToDictionary(f => f[..f.IndexOf("@@@")], f => f[(f.IndexOf("@@@\n") + 4)..]);
        return (files["Views/ItemListPage.xaml"], files["Views/ItemListPage.xaml.cs"]);
    }

    [TestMethod]
    public async Task WinUI3Grid_TableView_draws_the_rows_in_a_table_whose_sort_the_database_does()
    {
        var (xaml, code) = await TablePage();

        Expect.Contains(xaml, "xmlns:tv=\"using:WinUI.TableView\"");
        Expect.Contains(xaml, "<DataTemplate x:Key=\"RowActions\" x:DataType=\"local:ItemListRow\">");
        Expect.Contains(xaml, "<Button Content=\"Edit\" FontWeight=\"Bold\" Background=\"{ThemeResource SystemControlBackgroundBaseLowBrush}\" Tag=\"{x:Bind Id}\" Click=\"OnEditClick\" />");
        Expect.Contains(xaml, "<tv:TableView x:Name=\"Table\" ItemsSource=\"{x:Bind ViewModel.Rows}\"");
        Expect.Contains(xaml, "Sorting=\"OnTableSorting\" ClearSorting=\"OnTableClearSorting\"");
        Expect.Contains(xaml, "CanFilterColumns=\"False\"");   // a filter or a sort of the table's own would only touch the page it holds
        Expect.DoesNotContain(xaml, "<ListView");
        Expect.DoesNotContain(xaml, "HeaderCells");

        Expect.Contains(code, "using WinUI.TableView;");
        Expect.Contains(code, "private void BuildColumns()");
        Expect.Contains(code, "Path = new PropertyPath($\"VisibleCells[{i}]\")");
        Expect.Contains(code, "e.Handled = true;");   // the table's own sort never runs
        Expect.Contains(code, "ViewModel.SortBy(name);");
        Expect.Contains(code, "ChangeColumnsAsync");
        Expect.Contains(code, "        BuildColumns();\n    }");   // the Columns menu rebuilds the table's columns
        Expect.DoesNotContain(code, "BuildHeader");
        Expect.DoesNotContain(code, "OnSortHeaderClick");
    }

    [TestMethod]
    public async Task The_table_right_aligns_the_columns_shown_as_numbers_and_remembers_the_widths_a_person_gives_them()
    {
        var table = Sample.Table("Item", [
            Sample.Column("ItemId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("ItemNumber", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
            Sample.Column("Quantity", System.Data.SqlDbType.Int, ordinal: 3),
            Sample.Column("Weight", System.Data.SqlDbType.Decimal, ordinal: 4),
            Sample.Column("IsActive", System.Data.SqlDbType.Bit, ordinal: 5),
            Sample.Column("Added", System.Data.SqlDbType.Date, ordinal: 6)]);
        var settings = ProjectSettings.FromValues([new("ProjectName", "Acme"), new("WinUI3Grid", "TableView")]);
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), table, settings);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.Replace("\r\n", "\n");

        var aligned = System.Text.RegularExpressions.Regex.Match(text, "RightAlignedColumns = \\[(.*?)\\];").Groups[1].Value;
        Expect.Contains(aligned, "\"Quantity\"");
        Expect.Contains(aligned, "\"Weight\"");
        Expect.DoesNotContain(aligned, "\"ItemNumber\"");   // text
        Expect.DoesNotContain(aligned, "\"IsActive\"");   // Yes or No
        Expect.DoesNotContain(aligned, "\"Added\"");   // a date
        Expect.Contains(text, "<Style x:Key=\"RightAlignedText\" TargetType=\"TextBlock\">");
        Expect.Contains(text, "ElementStyle = ItemListViewModel.RightAlignedColumns.Contains(columns[i].Name)");
        Expect.Contains(text, "header.HorizontalContentAlignment = HorizontalAlignment.Right;");

        Expect.Contains(text, "private void SaveWidths()");
        Expect.Contains(text, "GridWidthStore.Load(\"Item\")");
        Expect.Contains(text, "Table.AddHandler(UIElement.PointerReleasedEvent");
        Expect.Contains(text, "GridWidthStore.Forget(\"Item\")");   // Reset columns forgets the widths

        var ordinary = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), table, Project());
        Expect.DoesNotContain(ordinary.GeneratedText!, "RightAligned");
        Expect.DoesNotContain(ordinary.GeneratedText!, "GridWidthStore");
    }

    [TestMethod]
    public async Task The_width_store_is_written_only_for_the_table_grid()
    {
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [Sample.Holiday()] };
        var plain = await Repo.Cache.RunAsync(Repo.Template("WinUI3_GridColumns_v1.tt"), database, Project());
        var table = await Repo.Cache.RunAsync(Repo.Template("WinUI3_GridColumns_v1.tt"), database, ProjectSettings.FromValues([new("ProjectName", "Acme"), new("WinUI3Grid", "TableView")]));
        Assert.IsTrue(plain.Success && table.Success, string.Join(" | ", plain.Errors.Concat(table.Errors)));

        Expect.DoesNotContain(plain.GeneratedText!, "GridWidthStore");
        string text = table.GeneratedText!.Replace("\r\n", "\n");
        Expect.Contains(text, "ViewModels/GridWidthStore.cs@@@");
        Expect.Contains(text, "\"GridWidths.json\"");
        Expect.Contains(text, "public static class GridWidthStore");
    }

    [TestMethod]
    public async Task The_actions_of_a_row_are_the_same_buttons_in_the_ListView_and_in_the_table()
    {
        var shapes = new[] { Array.Empty<(string, string)>(), [("DefaultRowTables", "Item:IsDefault")] };
        foreach (var more in shapes)
        {
            var (xaml, _) = await TablePage(more);
            var listSettings = ProjectSettings.FromValues([new("ProjectName", "Acme"), .. more.Select(m => new KeyValuePair<string, string>(m.Item1, m.Item2))]);
            var list = await Repo.Cache.RunAsync(Repo.Template("WinUI3_MasterScreen_v1.tt"), ItemTable(), listSettings);
            string listXaml = list.GeneratedText!.Replace("\r\n", "\n");
            foreach (string button in new[] { "Edit", "Delete" })
            {
                string line = $"<Button Content=\"{button}\" FontWeight=\"Bold\" Background=\"{{ThemeResource SystemControlBackgroundBaseLowBrush}}\" Tag=\"{{x:Bind Id}}\" Click=\"On{button}Click\" />";
                Expect.Contains(listXaml, line);
                Expect.Contains(xaml, line);
            }
        }
    }

    [TestMethod]
    public async Task WinUI3Grid_TableView_adds_the_package_to_the_app_project_and_blank_does_not()
    {
        async Task<string> Project(params (string, string)[] more)
        {
            var settings = ProjectSettings.FromValues([new("ProjectName", "Acme"), .. more.Select(m => new KeyValuePair<string, string>(m.Item1, m.Item2))]);
            var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_EssentialProject_v1.tt"), settings);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            return result.GeneratedText!;
        }

        Expect.Contains(await Project(("WinUI3Grid", "TableView")), $"<PackageReference Include=\"WinUI.TableView\" Version=\"{ProjectSettings.WinUi3TableViewVersion}\" />");
        Expect.DoesNotContain(await Project(), "WinUI.TableView");
    }

    [TestMethod]
    public void The_grid_setting_is_a_radio_on_the_WinUI3_tab_with_a_hint_and_a_reference_row()
    {
        Assert.AreEqual(Core.Settings.SettingKind.Radio, Core.Settings.ProjectSettingChoices.All["WinUI3Grid"].Kind);
        Assert.AreEqual("WinUI3", Core.Settings.ProjectSettingGroups.TabOf("WinUI3Grid"));
        Assert.IsTrue(Core.Settings.ProjectSettingsHints.All["WinUI3Grid"].Length > 0);
        Assert.IsNull(Project().WinUI3Grid);
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
