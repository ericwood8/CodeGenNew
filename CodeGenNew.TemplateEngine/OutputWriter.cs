namespace CodeGenNew.TemplateEngine;

/// <summary> What happened to one file a generation wanted to write. </summary>
public enum FileOutcomeKind
{
    /// <summary> The file did not exist and was written. </summary>
    Created,
    /// <summary> The file existed with different content and was replaced. </summary>
    Updated,
    /// <summary> The file already holds this content (line endings ignored): nothing was written. </summary>
    Unchanged,
    /// <summary> The file exists and the run only creates missing files: it was left alone (its content differs). </summary>
    Skipped,
    /// <summary> A dry run: the file would have been created or updated. </summary>
    WouldWrite
}

public sealed record FileOutcome(string FullPath, FileOutcomeKind Kind);

/// <summary> Puts generated files on disk. A file whose text is the same as what is already there (line endings ignored, so a CRLF checkout counts as unchanged) is not touched, which keeps
/// "what changed" honest and leaves the editor's open file alone. </summary>
public static class OutputWriter
{
    /// <summary> The files a template's output stands for: the marker-delimited files, or the one file named by the template's own rule (<paramref name="tableName"/> fills its {Table}). </summary>
    public static List<(string RelativePath, string Content)> FilesOf(TemplateInfo template, string tableName, string generatedText) =>
        GeneratedFiles.HasMarkers(generatedText)
            ? GeneratedFiles.Split(generatedText)
            : [(template.BuildFileName(tableName), generatedText)];

    /// <summary> Writes <paramref name="files"/> under <paramref name="folder"/>. <paramref name="createOnly"/>: only files that do not exist are written (the essentials default: those files are
    /// edited by hand afterwards). <paramref name="dryRun"/>: nothing is written, the outcome says what would be. </summary>
    public static async Task<List<FileOutcome>> WriteAsync(string folder, IEnumerable<(string RelativePath, string Content)> files, bool createOnly = false, bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<FileOutcome>();
        foreach (var (relativePath, content) in files)
        {
            string full = Path.GetFullPath(Path.Combine(folder, relativePath));
            if (!full.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The path '{relativePath}' leaves the output folder.");

            FileOutcomeKind kind;
            if (!File.Exists(full))
                kind = dryRun ? FileOutcomeKind.WouldWrite : FileOutcomeKind.Created;
            else if (Same(await File.ReadAllTextAsync(full, cancellationToken), content))
                kind = FileOutcomeKind.Unchanged;
            else
                kind = createOnly ? FileOutcomeKind.Skipped : dryRun ? FileOutcomeKind.WouldWrite : FileOutcomeKind.Updated;

            if (kind is FileOutcomeKind.Created or FileOutcomeKind.Updated)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, content, cancellationToken);
            }
            outcomes.Add(new FileOutcome(full, kind));
        }
        return outcomes;
    }

    private static bool Same(string a, string b) => a.Replace("\r\n", "\n") == b.Replace("\r\n", "\n");
}
