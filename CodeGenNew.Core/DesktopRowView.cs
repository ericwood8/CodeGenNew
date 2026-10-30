namespace CodeGenNew.Core;

/// <summary> The read-only view of one row that a desktop list of a log table opens (the Uno and WinUI3 master screens): a dialog with a label and the whole value of every column, so a long message that the grid cuts can be read.
/// A log table has no edit dialog, so this is the only thing its View button opens. </summary>
public static class DesktopRowView
{
    /// <summary> The statements that show the dialog for <c>entity</c>: a scrolling, selectable text with one "Label: value" line per column. </summary>
    public static string ShowBody(TableModel table, string title)
    {
        var lines = table.Columns.Select(c => $"                \"{(c.IsPrimaryKey ? "Id" : Labels.Column(c.Name)).Replace("\\", "\\\\").Replace("\"", "\\\"")}: \" + Show(entity.{c.CSharpName()})");
        return $$"""
        var text = new TextBlock
        {
            Text = string.Join("\n\n", new[]
            {
{{string.Join(",\n", lines)}}
            }),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "{{title.Replace("\"", "\\\"")}}",
            Content = new ScrollViewer { Content = text, MaxHeight = 480 },
            CloseButtonText = "Close"
        };
        await dialog.ShowAsync();
""";
    }

    /// <summary> The value as the person reads it: a date and time to the second, a yes or no, a number in the current culture, nothing for a missing value. </summary>
    public const string ShowMethod = """
    private static string Show(object? value) => value switch
    {
        null => "",
        DateTime time => time.ToString("yyyy-MM-dd HH:mm:ss"),
        bool flag => flag ? "Yes" : "No",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.CurrentCulture),
        _ => value.ToString() ?? ""
    };
""";
}
