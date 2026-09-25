namespace CodeGenNew.TemplateEngine;

/// <summary> Turns a template's output into files on disk. Most templates produce ONE file, named by TemplateInfo.BuildFileName.
/// A template that must produce several -- or a file inside sub-folders, as the Angular TS_ templates do -- says so itself by
/// writing a marker line before each file:
///
///     @@@FILE components/holiday/holiday.component.ts@@@
///     ...that file's text...
///     @@@FILE components/holiday/holiday.component.html@@@
///     ...
///
/// The text after a marker line, up to the next marker (or the end), is that file's content; a file may be empty (a CSS file
/// that is meant to be empty is a marker followed straight by the next marker). The path is relative to the output directory,
/// may use / or \ and contains sub-folders, which are created; it can never be rooted or climb out with "..".
/// Output with no marker at all is the ordinary single file. </summary>
public static class GeneratedFiles
{
    private const string Prefix = "@@@FILE ";
    private const string Suffix = "@@@";

    /// <summary> True when the template wrote at least one marker line. </summary>
    public static bool HasMarkers(string text) => text.Split('\n').Any(IsMarker);

    /// <summary> Splits marker-delimited output into (relative path, content) pairs. Text before the first marker is ignored
    /// unless it is more than whitespace, which is a template bug and is reported. </summary>
    public static List<(string RelativePath, string Content)> Split(string text)
    {
        var files = new List<(string, string)>();
        string? path = null;
        var content = new System.Text.StringBuilder();

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (IsMarker(rawLine))
            {
                if (path is not null)
                    files.Add((path, content.ToString()));
                path = ValidatePath(line[Prefix.Length..^Suffix.Length].Trim());
                content.Clear();
            }
            else if (path is null)
            {
                if (line.Trim().Length > 0)
                    throw new InvalidDataException("The template wrote text before its first @@@FILE marker: " + line.Trim());
            }
            else
            {
                content.Append(rawLine).Append('\n');
            }
        }

        if (path is not null)
        {
            // the split added one line break after the last line that the template never wrote
            string last = content.ToString();
            files.Add((path, last.EndsWith('\n') ? last[..^1] : last));
        }
        return files;
    }

    /// <summary> Writes the output under outputDirectory and returns the full path of each file written, in order. </summary>
    public static async Task<List<string>> WriteAsync(
        string outputDirectory, TemplateInfo template, string tableName, string generatedText, CancellationToken cancellationToken = default)
    {
        var written = new List<string>();

        if (!HasMarkers(generatedText))
        {
            Directory.CreateDirectory(outputDirectory);
            string single = Path.Combine(outputDirectory, template.BuildFileName(tableName));
            await File.WriteAllTextAsync(single, generatedText, cancellationToken);
            written.Add(single);
            return written;
        }

        foreach (var (relativePath, content) in Split(generatedText))
        {
            string full = Path.GetFullPath(Path.Combine(outputDirectory, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content, cancellationToken);
            written.Add(full);
        }
        return written;
    }

    private static bool IsMarker(string rawLine)
    {
        string line = rawLine.TrimEnd('\r').Trim();
        return line.StartsWith(Prefix, StringComparison.Ordinal) && line.EndsWith(Suffix, StringComparison.Ordinal) && line.Length > Prefix.Length + Suffix.Length;
    }

    private static string ValidatePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Length == 0 || Path.IsPathRooted(path) || normalized.StartsWith('/') || normalized.Contains(':')
            || normalized.Split('/').Any(part => part is ".." or "" or "."))
            throw new InvalidDataException($"The template asked for an unsafe file path '{path}': it must be relative to the output folder, with no '..'.");
        return normalized;
    }
}
