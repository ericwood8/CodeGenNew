using CodeGenNew.App.Services;
using CodeGenNew.SchemaIntrospection;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CodeGenNew.App.ViewModels;

/// <summary> One column shown, read-only, underneath an expanded table node (Docs/specs.md section 9.4).
/// Deliberately not a TreeView node of its own -- it's static content inside the
/// table's own DataTemplate, so it can't be selected or right-clicked. </summary>
public class ColumnRowViewModel(ColumnSummary summary, IconProvider icons)
{
    public BitmapImage Icon { get; } = icons.Column;
    public BitmapImage PrimaryKeyIcon { get; } = icons.PrimaryKey;
    public bool IsPrimaryKey { get; } = summary.IsPrimaryKey;
    public string DisplayText { get; } = $"{summary.Name}  ({summary.SqlTypeName}{(summary.IsNullable ? ", null" : "")})";
}
