using System.Collections.ObjectModel;
using CodeGenNew.App.Services;
using CodeGenNew.SchemaIntrospection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CodeGenNew.App.ViewModels;

/// <summary> One table node in the TreeView (Docs/specs.md section 9.4). Its columns
/// are shown inline, underneath its own name, inside this same node's content rather than as separate
/// TreeView nodes -- they're plain read-only text with no selection/right-click of their own, which is
/// what keeps a column from ever being "highlighted" and offered the table's right-click menu. </summary>
public partial class TableNodeViewModel : ObservableObject
{
    private static readonly SolidColorBrush ReservedWordBrush = new(Colors.Red);

    private readonly IconProvider _icons;
    private readonly Func<TableSummary, CancellationToken, Task<List<ColumnSummary>>> _loadColumns;
    private bool _columnsLoaded;

    public TableSummary Summary { get; }

    public string SchemaName => Summary.SchemaName;
    public string TableName => Summary.TableName;
    public string DisplayName => TableName;

    /// <summary> Table/column names colliding with a SQL Server or C# reserved word are shown in red. </summary>
    public bool IsReservedWordCollision => Summary.IsReservedWordName || Summary.IsCSharpReservedWordName;

    public Brush TextBrush => IsReservedWordCollision ? ReservedWordBrush : ThemeBrushes.DefaultText;

    public BitmapImage Icon { get; }

    public ObservableCollection<ColumnRowViewModel> Columns { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoadingColumns;

    /// <summary> Segoe MDL2 Assets chevron glyph -- right when collapsed, down when expanded. </summary>
    public string ExpandGlyph => IsExpanded ? "" : "";

    public TableNodeViewModel(TableSummary summary, IconProvider icons, Func<TableSummary, CancellationToken, Task<List<ColumnSummary>>> loadColumns)
    {
        Summary = summary;
        Icon = icons.ForTable(summary.HasPrimaryKey, summary.HasUniqueIndex);
        _icons = icons;
        _loadColumns = loadColumns;
    }

    [RelayCommand]
    private async Task ToggleExpandAsync()
    {
        IsExpanded = !IsExpanded;
        if (!IsExpanded || _columnsLoaded)
            return;

        IsLoadingColumns = true;
        try
        {
            var summaries = await _loadColumns(Summary, CancellationToken.None);
            Columns.Clear();
            foreach (var summary in summaries)
                Columns.Add(new ColumnRowViewModel(summary, _icons));
            _columnsLoaded = true;
        }
        catch (Exception ex)
        {
            Columns.Clear();
            Columns.Add(new ColumnRowViewModel(new ColumnSummary { Name = "(failed to load columns)", SqlTypeName = ex.Message }, _icons));
        }
        finally
        {
            IsLoadingColumns = false;
        }
    }
}
