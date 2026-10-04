using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeGenNew.Generation;

/// <summary> One file the plan wrote: its path under the output folder, the stack (or <c>Sql</c>) it belongs to and the hash of the text that was generated. </summary>
public sealed record ManifestEntry(string Path, string Stack, string Sha256);

/// <summary> The list of files the last whole-project generation wrote, kept as <c>.codegen-manifest.json</c> in the output folder. The next run compares it with what the plan produces now: a file
/// that was written before and is not produced any more belongs to a table or a template that is gone (a "stale" file), and its hash says whether anyone edited it since. </summary>
public sealed class GenerationManifest
{
    public const string FileName = ".codegen-manifest.json";

    public List<ManifestEntry> Entries { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string PathFor(string outputDirectory) => System.IO.Path.Combine(outputDirectory, FileName);

    public static GenerationManifest Load(string outputDirectory)
    {
        string path = PathFor(outputDirectory);
        if (!File.Exists(path))
            return new GenerationManifest();
        try { return JsonSerializer.Deserialize<GenerationManifest>(File.ReadAllText(path), Options) ?? new GenerationManifest(); }
        catch (JsonException) { return new GenerationManifest(); }
    }

    public void Save(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Entries = Entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
        File.WriteAllText(PathFor(outputDirectory), JsonSerializer.Serialize(this, Options));
    }

    /// <summary> The hash of a file's text with the line endings normalised, so a CRLF checkout of an unedited file hashes like the generated text. </summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")))).ToLowerInvariant();

    public static string Relative(string outputDirectory, string fullPath) =>
        System.IO.Path.GetRelativePath(outputDirectory, fullPath).Replace('\\', '/');
}

/// <summary> A file an earlier run wrote that the plan no longer produces. <see cref="Edited"/>: its text differs from what was generated (someone changed it), so it is never deleted for you. </summary>
public sealed record StaleFile(string Path, string Stack, bool Exists, bool Edited, bool Deleted);
