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
    WouldWrite,
    /// <summary> The file differs from the text the last run wrote (someone edited it): it was not overwritten. </summary>
    Edited,
    /// <summary> The file differs from the new text and no earlier run recorded its hash, so it cannot be told from a hand edit: it was not overwritten. </summary>
    Unknown
}

/// <summary> What a run does with a file that exists with other content than the new text. </summary>
public enum OverwritePolicy
{
    /// <summary> Replace it. </summary>
    Overwrite,
    /// <summary> Replace it only when it still holds what the last run wrote; keep an edited or unrecorded file. </summary>
    KeepEdited,
    /// <summary> As <see cref="KeepEdited"/>, and write the new text beside the kept file as <c>.generated</c>. </summary>
    KeepEditedWriteBeside
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
    /// the line diff of what would change (or changed). <paramref name="policy"/> with <paramref name="recordedHash"/> (the hash the last run recorded for a full path, null when none):
    /// a file whose text no longer matches that hash is kept. </summary>
    public static async Task<List<FileOutcome>> WriteAsync(string folder, IEnumerable<(string RelativePath, string Content)> files, bool createOnly = false, bool dryRun = false,
        bool withDiff = false, string? stack = null, CancellationToken cancellationToken = default, OverwritePolicy policy = OverwritePolicy.Overwrite, Func<string, string?>? recordedHash = null)
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
            else if (createOnly)
                kind = FileOutcomeKind.Skipped;
            else if (policy != OverwritePolicy.Overwrite && KeptKind(existing, recordedHash?.Invoke(full)) is { } kept)
                kind = kept;
            else
                kind = dryRun ? FileOutcomeKind.WouldWrite : FileOutcomeKind.Updated;

            string? diff = withDiff && existing is not null && kind != FileOutcomeKind.Unchanged ? TextDiff.Unified(existing, content, relativePath) : null;
            if (kind is FileOutcomeKind.Created or FileOutcomeKind.Updated)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, content, cancellationToken);
            }
            else if (kind is FileOutcomeKind.Edited or FileOutcomeKind.Unknown && policy == OverwritePolicy.KeepEditedWriteBeside && !dryRun)
                await File.WriteAllTextAsync(full + BesideSuffix, content, cancellationToken);
            outcomes.Add(new FileOutcome(full, kind, stack, diff, content));
        }
        return outcomes;
    }

    /// <summary> The extension added to a kept file's name when the new text is written beside it. </summary>
    public const string BesideSuffix = ".generated";

    private static FileOutcomeKind? KeptKind(string existing, string? recorded) =>
        recorded is null ? FileOutcomeKind.Unknown
        : Hash(existing) != recorded ? FileOutcomeKind.Edited
        : null;

    /// <summary> The hash of a text with the line endings normalised, so a CRLF checkout of an unedited file hashes like the generated text. </summary>
    public static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")))).ToLowerInvariant();

    private static bool Same(string a, string b) => a.Replace("\r\n", "\n") == b.Replace("\r\n", "\n");
}
