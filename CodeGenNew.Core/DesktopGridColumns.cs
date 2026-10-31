using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Columns list of the WinUI3 and Uno list pages: the person shows or hides the columns <see cref="ChosenGridColumns"/> offers, and the choice is kept between runs. Both stacks write the same
/// C# (their XAML classes have the same names), so the text is here once: the store of the choice and the row control (written by the GridColumns templates), and the members of the list page and its
/// view model (written by the master screen templates). The rule that reads a saved choice is <see cref="ChosenGridColumns.Resolve"/>, which the store repeats in C# for the app, as the Blazor helpers do. </summary>
public static class DesktopGridColumns
{
    /// <summary> <c>ViewModels/GridColumnStore.cs</c>: the column record, the store of the choice (GridColumns.json in the local application data folder) and the cut of a long text. </summary>
    public static string StoreFile(string viewModelNamespace) => $$"""
using System.IO;
using System.Text.Json;

namespace {{viewModelNamespace}};

/// <summary> A column the Columns list offers: the name the grid sorts by and the saved choice holds, the caption of its header and whether the grid sorts by it. </summary>
public sealed record GridColumnInfo(string Name, string Caption, bool Sortable);

/// <summary> Remembers the columns each grid shows between runs in GridColumns.json in the local application data folder: a grid's name maps to the list of column names shown. A grid that shows its
/// starting columns has no entry. A file that cannot be read or written just means the choice is not remembered. </summary>
public static class GridColumnStore
{
    /// <summary> A long or multi-line text shows this many characters in a grid cell (the rest is cut and ends in three dots). </summary>
    public const int CutLength = {{ChosenGridColumns.CutLength}};

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "App",
        "GridColumns.json");

    /// <summary> The columns a grid shows: the saved ones the table still offers (in the table's order), or the starting columns when nothing valid was saved. </summary>
    public static List<string> Load(string gridKey, IReadOnlyList<string> offered, IReadOnlyList<string> initial)
    {
        lock (Gate)
        {
            var saved = ReadAll().GetValueOrDefault(gridKey);
            if (saved is null)
                return [.. initial];
            var kept = offered.Where(saved.Contains).ToList();
            return kept.Count > 0 ? kept : [.. initial];
        }
    }

    /// <summary> Keeps the choice of a grid, or forgets it when it is the starting columns again. </summary>
    public static void Save(string gridKey, IReadOnlyList<string> chosen, IReadOnlyList<string> initial)
    {
        lock (Gate)
        {
            var all = ReadAll();
            if (chosen.SequenceEqual(initial))
                all.Remove(gridKey);
            else
                all[gridKey] = [.. chosen];

            try
            {
                if (all.Count == 0)
                {
                    File.Delete(FilePath);
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(all, Json));
            }
            catch (Exception)
            {
                // the choice just is not remembered
            }
        }
    }

    /// <summary> The choice after one column is ticked or cleared: the table's order is kept, and the last shown column cannot be cleared. </summary>
    public static List<string> Toggle(IReadOnlyList<string> chosen, IReadOnlyList<string> offered, string name)
    {
        if (!offered.Contains(name))
            return [.. chosen];
        if (chosen.Contains(name))
            return chosen.Count == 1 ? [.. chosen] : chosen.Where(n => n != name).ToList();
        return offered.Where(n => n == name || chosen.Contains(n)).ToList();
    }

    /// <summary> A long or multi-line text as a grid cell shows it: the first characters, then three dots. </summary>
    public static string Cut(string? text)
    {
        text ??= "";
        return text.Length > CutLength ? text[..(CutLength - 3)] + "..." : text;
    }

    private static Dictionary<string, List<string>> ReadAll()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception)
        {
            // an unreadable file is the same as none
        }
        return [];
    }
}
""";

    /// <summary> <c>Views/GridCells.cs</c>: the cells of one grid row, an equal-width column per text, so the rows line up with the header the page builds for the shown columns. </summary>
    public static string CellsFile(string viewNamespace) => $$"""
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace {{viewNamespace}};

/// <summary> The cells of one grid row: one equal-width column for each text of <see cref="Cells"/>. The list page binds it to the texts of the columns that are shown. </summary>
public sealed class GridCells : Grid
{
    public static readonly DependencyProperty CellsProperty = DependencyProperty.Register(
        nameof(Cells), typeof(IReadOnlyList<string>), typeof(GridCells), new PropertyMetadata(null, (d, _) => ((GridCells)d).Build()));

    public IReadOnlyList<string>? Cells
    {
        get => (IReadOnlyList<string>?)GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    public GridCells() => ColumnSpacing = 8;

    private void Build()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        var cells = Cells ?? [];
        for (int i = 0; i < cells.Count; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new TextBlock { Text = cells[i], VerticalAlignment = VerticalAlignment.Center };
            SetColumn(text, i);
            Children.Add(text);
        }
    }
}
""";

    /// <summary> The members a list view model gains: the columns offered and the ones it starts with, the shown ones (chosen and kept), toggling and resetting, and the texts of the shown cells of a row.
    /// The view model has the constants <c>GridKey</c> and the fields <c>_sortColumn</c> and <c>_sortDescending</c>, and sets <c>_chosenColumns</c> in its constructor from <c>GridColumnStore.Load</c>. </summary>
    public static string ViewModelMembers(ChosenGridColumns chosen, string? adminOnlyColumn = null, string? adminExpression = null)
    {
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        L("    // The columns the Columns list offers (every column of the form) and the ones the grid starts with. The choice is kept between runs (GridColumnStore).");
        L("    public static readonly GridColumnInfo[] OfferedColumns =");
        L("    [");
        foreach (var c in chosen.Offered)
            L($"        new({Quote(c.Name)}, {Quote(GridCaption.For(c.Column, Labels.Column(c.Name)))}, {(c.Sortable ? "true" : "false")}),");
        L("    ];");
        L($"    private static readonly string[] InitialColumns = [{string.Join(", ", chosen.Initial.Select(c => Quote(c.Name)))}];");
        L("    private static readonly string[] OfferedNames = [.. OfferedColumns.Select(c => c.Name)];");
        L("    private List<string> _chosenColumns = [.. InitialColumns];");
        L();
        if (adminOnlyColumn is null)
            L("    public bool IsOffered(string name) => true;");
        else
            L($"    public bool IsOffered(string name) => name != {Quote(adminOnlyColumn)} || {adminExpression};");
        L();
        L("    public bool IsShown(string name) => IsOffered(name) && _chosenColumns.Contains(name);");
        L();
        L("    /// <summary> The last shown column cannot be hidden: a grid with no column shows nothing to open a row from. </summary>");
        L("    public bool IsLastColumn(string name) => _chosenColumns.Count == 1 && _chosenColumns[0] == name;");
        L();
        L("    public IReadOnlyList<GridColumnInfo> ShownColumns => OfferedColumns.Where(c => IsShown(c.Name)).ToList();");
        L();
        L("    /// <summary> Shows or hides a column and keeps the choice. The caller reloads (page 1). </summary>");
        L("    public void ToggleColumn(string name) => ChooseColumns(GridColumnStore.Toggle(_chosenColumns, OfferedNames, name));");
        L();
        L("    /// <summary> Back to the starting columns, and forget the saved choice. The caller reloads (page 1). </summary>");
        L("    public void ResetColumns() => ChooseColumns([.. InitialColumns]);");
        L();
        L("    // A sort on a column that is no longer shown is dropped, as on the web.");
        L("    private void ChooseColumns(List<string> next)");
        L("    {");
        L("        _chosenColumns = next;");
        L("        GridColumnStore.Save(GridKey, _chosenColumns, InitialColumns);");
        L("        if (_sortColumn is not null && !IsShown(_sortColumn))");
        L("        {");
        L("            _sortColumn = null;");
        L("            _sortDescending = false;");
        L("            GridSortStore.Save(GridKey, null);");
        L("        }");
        L("        PageNumber = 1;");
        L("    }");
        L();
        L("    /// <summary> The texts of a row's shown columns, from the texts of every offered column (in the order of OfferedColumns). </summary>");
        L("    private List<string> VisibleCells(List<string> cells) => [.. OfferedColumns.Select((c, i) => (c.Name, Text: cells[i])).Where(x => IsShown(x.Name)).Select(x => x.Text)];");
        return o.ToString();
    }

    /// <summary> The members a list page gains: the header built for the shown columns (a button that sorts, or text, then the Actions column that the page's XAML holds) and the Columns menu. The page has
    /// <c>HeaderCells</c>, a <c>Grid</c> in the first column of its header, and the handler <c>OnSortHeaderClick</c>. </summary>
    public static string PageMembers(string viewModelType) => $$"""
    // The header: one cell for each shown column (a button that sorts, or text), rebuilt when the sort or the shown columns change.
    private void BuildHeader()
    {
        HeaderCells.Children.Clear();
        HeaderCells.ColumnDefinitions.Clear();
        var columns = ViewModel.ShownColumns;
        for (int i = 0; i < columns.Count; i++)
        {
            HeaderCells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            FrameworkElement header;
            if (columns[i].Sortable)
            {
                var button = new Button
                {
                    Content = columns[i].Caption + ViewModel.SortMark(columns[i].Name),
                    Tag = columns[i].Name,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Padding = new Thickness(0),
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                button.Click += OnSortHeaderClick;
                header = button;
            }
            else
            {
                header = new TextBlock { Text = columns[i].Caption, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            }
            Grid.SetColumn(header, i);
            HeaderCells.Children.Add(header);
        }
    }

    // The Columns menu: a check mark for each offered column (the last shown one cannot be cleared) and Reset.
    private void OnColumnsClick(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        foreach (var column in {{viewModelType}}.OfferedColumns.Where(c => ViewModel.IsOffered(c.Name)))
        {
            var item = new ToggleMenuFlyoutItem { Text = column.Caption, IsChecked = ViewModel.IsShown(column.Name), IsEnabled = !ViewModel.IsLastColumn(column.Name) };
            item.Click += async (_, _) => await ChangeColumnsAsync(() => ViewModel.ToggleColumn(column.Name));
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        var reset = new MenuFlyoutItem { Text = "Reset columns" };
        reset.Click += async (_, _) => await ChangeColumnsAsync(ViewModel.ResetColumns);
        flyout.Items.Add(reset);
        flyout.ShowAt((FrameworkElement)sender);
    }

    private async Task ChangeColumnsAsync(Action change)
    {
        change();
        await ViewModel.LoadAsync();
        BuildHeader();
    }
""";
}
