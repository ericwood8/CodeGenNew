using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace CodeGenNew.App.Services;

/// <summary> Theme-resource brush lookups shared across ViewModels (MainViewModel's status text,
/// TableNodeViewModel's table-name text) that used to each look up "TextFillColorPrimaryBrush" with
/// their own inline fallback -- one had it fall back to white, the other to black, an inconsistency
/// nothing caught since the fallback only matters if the resource dictionary somehow isn't loaded yet. </summary>
internal static class ThemeBrushes
{
    /// <summary> The theme's normal text color, for a status/name text block that isn't otherwise
    /// highlighted (a warning, a reserved-word collision, ...). </summary>
    public static Brush DefaultText { get; } = (Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush)
        ?? new SolidColorBrush(Colors.White);
}
