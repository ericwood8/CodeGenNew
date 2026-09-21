using System.Reflection;
using System.Security.Cryptography;

namespace CodeGenNew.TemplateEngine;

public enum SeedOutcome
{
    /// <summary> File was missing and was created from the embedded default. Silent by design. </summary>
    Created,

    /// <summary> File was an unmodified copy of an older shipped version and was replaced by the newer one. </summary>
    Refreshed,

    /// <summary> File differs from the shipped one and was NOT ours to overwrite (customized, or unknown history);
    /// the shipped version was written beside it as "&lt;name&gt;.new" for the developer to diff/merge. </summary>
    KeptCustomized
}

public sealed record SeedNotice(string FileName, SeedOutcome Outcome, string Message);

/// <summary>
/// Makes both CodeGenNew.App and CodeGenNew.Cli "copy the EXE and go" deployable:
/// the Templates/*.tt(.config) files and SpecialLogicColumns.config are embedded resources baked
/// into each project's own assembly as a factory-default seed, and this class materializes them as real,
/// editable files on disk. Templates/Output directories are created the same way.
///
/// Per shipped file, comparing SHA-256 hashes of the file on disk, of the embedded copy, and of what this
/// class itself last wrote (remembered in Templates\SeededAssets.config):
///   - missing on disk                             -> create it (silent).
///   - on-disk == embedded                         -> nothing to do.
///   - on-disk == what we last wrote (!= embedded) -> the developer never touched it and a newer version
///                                                    shipped, so it is refreshed in place.
///   - anything else (edited, or no record of it)  -> NEVER overwritten; the shipped copy is written beside it
///                                                    as "&lt;name&gt;.new". This is also the outcome for a file from
///                                                    an install that predates SeededAssets.config, since there is
///                                                    no way to know whether it was customized.
/// Templates carry their version in the file name (SP_Save_v1.tt), so a new template version arrives as a new
/// file (SP_Save_v2.tt) and the older one is simply hidden from the menu by TemplateCatalog -- this refresh
/// path is for shipped files that keep the same name (configs, SpecialLogicColumns.config, in-place fixes).
/// </summary>
public static class DefaultAssetSeeder
{
    private const string StateFileName = "SeededAssets.config";

    private static readonly string[] DefaultTemplateFileNames =
    [
        "SP_Insert_v1.tt", "SP_Insert_v1.tt.config",
        "SP_Update_v1.tt", "SP_Update_v1.tt.config",
        "SP_Delete_v1.tt", "SP_Delete_v1.tt.config",
        "SP_Save_v1.tt", "SP_Save_v1.tt.config",
        "SP_Load_v1.tt", "SP_Load_v1.tt.config",
        "SP_Lookup_v1.tt", "SP_Lookup_v1.tt.config",
        "SP_Clone_v1.tt", "SP_Clone_v1.tt.config",
        "API_Crud_v1.tt", "API_Crud_v1.tt.config",
        "CS_Entity_v1.tt", "CS_Entity_v1.tt.config",
        "CS_Enum_v1.tt", "CS_Enum_v1.tt.config",
        "CS_Repo_v1.tt", "CS_Repo_v1.tt.config",
        "TS_Model_v1.tt", "TS_Model_v1.tt.config",
        "TS_Service_v1.tt", "TS_Service_v1.tt.config",
        "TS_Component_v1.tt", "TS_Component_v1.tt.config"
    ];

    /// <returns> Only things a developer might want to know about (Refreshed / KeptCustomized), plus Created
    /// entries for completeness; callers decide what to show. </returns>
    public static IReadOnlyList<SeedNotice> EnsureDefaultAssets(
        string templatesDirectory, string specialLogicColumnsConfigPath, string outputDirectory, Assembly resourceAssembly)
    {
        Directory.CreateDirectory(templatesDirectory);
        Directory.CreateDirectory(outputDirectory);

        string statePath = Path.Combine(templatesDirectory, StateFileName);
        var state = LoadState(statePath);
        var stateBefore = new Dictionary<string, string>(state, StringComparer.OrdinalIgnoreCase);
        var notices = new List<SeedNotice>();

        string[] resourceNames = resourceAssembly.GetManifestResourceNames();
        foreach (string fileName in DefaultTemplateFileNames)
            Seed(Path.Combine(templatesDirectory, fileName), fileName, ReadResource(resourceAssembly, resourceNames, fileName), state, notices);

        string specialLogicFileName = Path.GetFileName(specialLogicColumnsConfigPath);
        Seed(specialLogicColumnsConfigPath, specialLogicFileName, ReadResource(resourceAssembly, resourceNames, specialLogicFileName), state, notices);

        if (!state.OrderBy(kv => kv.Key).SequenceEqual(stateBefore.OrderBy(kv => kv.Key)))
            SaveState(statePath, state);

        return notices;
    }

    private static void Seed(
        string destinationPath, string fileName, byte[]? shipped, Dictionary<string, string> state, List<SeedNotice> notices)
    {
        if (shipped is null)
            return; // no embedded default for this file (shouldn't happen for the fixed list above).

        string shippedHash = Hash(shipped);

        if (!File.Exists(destinationPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath, shipped);
            state[fileName] = shippedHash;
            notices.Add(new SeedNotice(fileName, SeedOutcome.Created, $"Created {fileName}."));
            return;
        }

        string diskHash = Hash(File.ReadAllBytes(destinationPath));

        if (diskHash.Equals(shippedHash, StringComparison.OrdinalIgnoreCase))
        {
            state[fileName] = shippedHash; // up to date; remember it so a future shipped change can refresh it
            return;
        }

        if (state.TryGetValue(fileName, out string? lastWrittenHash) && diskHash.Equals(lastWrittenHash, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllBytes(destinationPath, shipped);
            state[fileName] = shippedHash;
            notices.Add(new SeedNotice(fileName, SeedOutcome.Refreshed,
                $"Updated {fileName} to the newer shipped version (your copy was unmodified)."));
            return;
        }

        // Customized, or we have no record of what this file was: never overwrite. Offer the shipped copy alongside.
        string newPath = destinationPath + ".new";
        if (!File.Exists(newPath) || !Hash(File.ReadAllBytes(newPath)).Equals(shippedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllBytes(newPath, shipped);
            notices.Add(new SeedNotice(fileName, SeedOutcome.KeptCustomized,
                $"{fileName} differs from the shipped version and was left alone; the shipped version is in {Path.GetFileName(newPath)} for you to compare or merge."));
        }
    }

    private static byte[]? ReadResource(Assembly assembly, string[] resourceNames, string fileName)
    {
        string? resourceName = resourceNames.FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase)
                                                                 || n.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static Dictionary<string, string> LoadState(string statePath)
    {
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(statePath))
            return state;

        foreach (string rawLine in File.ReadAllLines(statePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int pipeIndex = line.IndexOf('|');
            if (pipeIndex > 0)
                state[line[..pipeIndex].Trim()] = line[(pipeIndex + 1)..].Trim();
        }

        return state;
    }

    private static void SaveState(string statePath, Dictionary<string, string> state)
    {
        var lines = new List<string>
        {
            "# SeededAssets.config -- written by CodeGenNew. Do not edit by hand.",
            "# For each shipped file: the SHA-256 of the content CodeGenNew last wrote there. If the file on disk still",
            "# matches, you haven't customized it, so a newer shipped version may replace it. If it doesn't match, your",
            "# copy is never overwritten (the shipped version is written next to it as <name>.new instead).",
            "# Delete this file to make CodeGenNew treat every existing file as possibly customized.",
            "# Format: FileName|SHA256"
        };
        lines.AddRange(state.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}|{kv.Value}"));
        File.WriteAllLines(statePath, lines);
    }
}
