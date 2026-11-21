namespace CodeGenNew.Core.Desktop;

/// <summary> What a WinUI3 or Uno list page with <c>WinUI3Grid=TableView</c> gains from <c>GridGroupBy</c> (a Group by toggle that groups the rows of the page) and
/// <c>GridExportTables</c> (an Export all button that writes every matching row to a CSV file). The C# is the same on both stacks except the file picker's window handle,
/// so the text is here once. </summary>
public static class DesktopGridExtras
{
    internal const string Skip = "@@";

    internal static string StripSkipped(this string text) => string.Join("\n", text.Split('\n').Where(line => line.TrimEnd('\r') != Skip));

    /// <summary> The page's grouping: <c>ApplyGrouping</c> (called after the columns are built) groups by the column's text, which is item N of the row's VisibleCells, and the toggle
    /// switches it. The package has grouping only in its Windows build, so the Uno page hides the toggle elsewhere (<paramref name="windowsOnly"/>). </summary>
    public static string GroupMembers(string rowType, string column, bool windowsOnly) => $$"""
    // Grouping by {{column}}: the groups are made from the rows of the page on show, and the toggle on the toolbar switches them.
    private void ApplyGrouping()
    {
{{(windowsOnly ? "#if WINDOWS" : Skip)}}
        Table.GroupDescriptions.Clear();
        var columns = ViewModel.ShownColumns;
        int index = -1;
        for (int i = 0; i < columns.Count; i++)
            if (columns[i].Name == "{{column}}")
                index = i;
        GroupToggle.Visibility = index >= 0 ? Visibility.Visible : Visibility.Collapsed;   // a column the person hid from the grid cannot group it
        if (index < 0)
            return;
        GroupToggle.Content = "Group by " + columns[index].Caption.TrimEnd('.');
        if (GroupToggle.IsChecked == true)
            Table.GroupDescriptions.Add(new GroupDescription("VisibleCells", WinUI.TableView.SortDirection.Ascending, null, row => (({{rowType}})row).VisibleCells[index]));
{{(windowsOnly ? "#else\n        GroupToggle.Visibility = Visibility.Collapsed;\n#endif" : Skip)}}
    }

    private void OnGroupToggled(object sender, RoutedEventArgs e) => ApplyGrouping();

""".StripSkipped();

    /// <summary> The number columns of a table that <c>GridTotalColumns</c> names and the grid offers, as (name, caption, C# type, nullable). Other names drop out. </summary>
    public static List<(string Name, string Caption, string Type, bool Nullable)> TotalColumns(ProjectSettings project, string table, IEnumerable<ColumnModel> offered) =>
        [.. project.GridTotalColumns(table)
            .Select(name => offered.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name)))
            .OfType<ColumnModel>()
            .Where(c => !c.IsIdentity && c.CSharpBase() is "int" or "long" or "short" or "byte" or "decimal" or "double" or "float")
            .Select(c => (c.Name, GridCaption.For(c, Labels.Column(c.Name)), c.CSharpBase(), c.IsNullable))];

    /// <summary> The line under the grid: "Page total: Quantity 12, Amount 1,234.50". </summary>
    public const string TotalsProperty = """
    [ObservableProperty]
    private string _totalsText = "";

""";

    /// <summary> The statements that make <c>TotalsText</c> from <c>rows</c>, the rows of the page that was read, for the total columns the person has not hidden. </summary>
    public static string TotalsLoadCode(IReadOnlyList<(string Name, string Caption, string Type, bool Nullable)> columns)
    {
        string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var o = new System.Text.StringBuilder("        var totals = new List<string>();\n");
        foreach (var c in columns)
        {
            string value = c.Nullable ? $"r.{c.Name} ?? 0" : $"r.{c.Name}";
            string format = c.Type is "decimal" or "double" or "float" ? "\"N2\"" : "\"N0\"";
            o.Append($"        if (IsShown({Quote(c.Name)}))\n");
            o.Append($"            totals.Add({Quote(c.Caption.TrimEnd('.'))} + \" \" + rows.Sum(r => (decimal)({value})).ToString({format}));\n");
        }
        o.Append("        TotalsText = totals.Count == 0 ? \"\" : \"Page total: \" + string.Join(\", \", totals);\n");
        return o.ToString();
    }

    /// <summary> The view model's Export all: it reads the pages of 1000 rows through the ordinary load (so the search, the sort and the shown columns are the grid's), collects
    /// the shown cells, then reads the page that was on show again. Returns the number of rows written, or -1 when a page could not be read. </summary>
    public const string ExportViewModelMembers = """
    private const int ExportPageSize = 1000;

    /// <summary> Writes the shown columns of every row the search matches, all pages, in the grid's sort order, to a CSV file (UTF-8 with a byte order mark, which Excel reads). </summary>
    public async Task<int> ExportAllAsync(string path)
    {
        int page = PageNumber, size = PageSize;
        var lines = new List<string> { string.Join(",", ShownColumns.Select(c => CsvField(c.Caption))) };
        try
        {
            PageSize = ExportPageSize;
            for (int number = 1; ; number++)
            {
                PageNumber = number;
                await LoadAsync();
                if (ErrorMessage is not null)
                    return -1;
                lines.AddRange(Rows.Select(r => string.Join(",", r.VisibleCells.Select(CsvField))));
                if (number >= TotalPages)
                    break;
            }
        }
        finally
        {
            PageSize = size;
            PageNumber = page;
            await LoadAsync();
        }
        await File.WriteAllLinesAsync(path, lines, new System.Text.UTF8Encoding(true));
        return lines.Count - 1;
    }

    private static string CsvField(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

""";

    /// <summary> The page's Export all click: the person picks the file, the view model writes it, a dialog says how many rows. <paramref name="uno"/> initializes the picker with the
    /// window the Uno way (Windows head only), WinUI3 with the active window. </summary>
    public static string ExportPageMembers(string title, bool uno) => $$"""
    private async void OnExportAllClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "{{title}}" };
{{(uno
    ? "#if WINDOWS\n        if (App.MainWindow is { } window)\n            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));\n#endif"
    : "        WinRT.Interop.InitializeWithWindow.Initialize(picker, ExportActiveWindow());")}}
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        var target = await picker.PickSaveFileAsync();
        if (target is null)
            return;
        int count = await ViewModel.ExportAllAsync(target.Path);
        if (count < 0)
            return;
        await new ContentDialog { XamlRoot = XamlRoot, Title = "Export all", Content = $"{count} rows written to {target.Path}", CloseButtonText = "OK" }.ShowAsync();
    }
{{(uno ? "" : "\n    [System.Runtime.InteropServices.DllImport(\"user32.dll\", EntryPoint = \"GetActiveWindow\")]\n    private static extern IntPtr ExportActiveWindow();")}}

""".StripSkipped();
}
