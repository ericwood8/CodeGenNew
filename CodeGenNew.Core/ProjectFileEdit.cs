namespace CodeGenNew.Core;

/// <summary> Changes one setting of a project file's text and leaves every other line, comment and blank line as it was. </summary>
public static class ProjectFileEdit
{
    /// <summary> Adds <paramref name="item"/> to the comma-separated list setting <paramref name="key"/> (the line is added when the file has none); an item already there is not added twice. </summary>
    public static string AddListItem(string text, string key, string item)
    {
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        int index = lines.FindIndex(l => l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            while (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            lines.Add($"{key}={item}");
            return string.Join(newline, lines) + newline;
        }
        string line = lines[index];
        string current = line[(line.IndexOf('=') + 1)..].Trim();
        var items = current.Length == 0 ? [] : current.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!items.Contains(item, StringComparer.OrdinalIgnoreCase))
            items.Add(item);
        lines[index] = $"{key}={string.Join(",", items)}";
        return string.Join(newline, lines);
    }
}
