using CodeGenNew.Core;
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

/// <summary> One file's result. <see cref="Stack"/> is the stack (or <c>Sql</c>) whose folder it went to; <see cref="Diff"/> is what differs between the file on disk and the generated text
/// (only when a diff was asked for and the file existed with other content); <see cref="Content"/> is the generated text. </summary>
public sealed record FileOutcome(string FullPath, FileOutcomeKind Kind, string? Stack = null, string? Diff = null, string? Content = null);

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
    /// edited by hand afterwards). <paramref name="dryRun"/>: nothing is written, the outcome says what would be. <paramref name="withDiff"/>: a file that exists with other content carries
    /// the line diff of what would change (or changed). </summary>
    public static async Task<List<FileOutcome>> WriteAsync(string folder, IEnumerable<(string RelativePath, string Content)> files, bool createOnly = false, bool dryRun = false,
        bool withDiff = false, string? stack = null, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<FileOutcome>();
        foreach (var (relativePath, content) in files)
        {
            string full = Path.GetFullPath(Path.Combine(folder, relativePath));
            if (!full.StartsWithIgnoreCase(Path.GetFullPath(folder)))
                throw new InvalidDataException($"The path '{relativePath}' leaves the output folder.");

            FileOutcomeKind kind;
            string? existing = File.Exists(full) ? await File.ReadAllTextAsync(full, cancellationToken) : null;
            if (existing is null)
                kind = dryRun ? FileOutcomeKind.WouldWrite : FileOutcomeKind.Created;
            else if (Same(existing, content))
                kind = FileOutcomeKind.Unchanged;
            else
                kind = createOnly ? FileOutcomeKind.Skipped : dryRun ? FileOutcomeKind.WouldWrite : FileOutcomeKind.Updated;

            string? diff = withDiff && existing is not null && kind != FileOutcomeKind.Unchanged ? TextDiff.Unified(existing, content, relativePath) : null;
            if (kind is FileOutcomeKind.Created or FileOutcomeKind.Updated)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, content, cancellationToken);
            }
            outcomes.Add(new FileOutcome(full, kind, stack, diff, content));
        }
        return outcomes;
    }

    private static bool Same(string a, string b) => a.Replace("\r\n", "\n") == b.Replace("\r\n", "\n");
}
