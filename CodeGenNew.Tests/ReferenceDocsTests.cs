using CodeGenNew.Core;

namespace CodeGenNew.Tests;

[TestClass]
public class ReferenceDocsTests
{
    private static string RepositoryFile(params string[] parts)
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "CodeGenNew.slnx")))
            directory = Path.GetDirectoryName(directory);
        Assert.IsNotNull(directory, "the repository root (CodeGenNew.slnx) was not found above the test folder");
        return Path.Combine([directory, .. parts]);
    }

    [TestMethod]
    public void Every_project_setting_key_has_a_hint()
    {
        var missing = ProjectSettings.Keys.Where(k => !ProjectSettingsHints.All.TryGetValue(k, out string? hint) || string.IsNullOrWhiteSpace(hint)).ToList();
        Assert.AreEqual(0, missing.Count, "keys without a hint in ProjectSettingsHints: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void Every_hint_belongs_to_a_known_key()
    {
        var unknown = ProjectSettingsHints.All.Keys.Where(h => !ProjectSettings.Keys.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.AreEqual(0, unknown.Count, "hints for keys that no longer exist: " + string.Join(", ", unknown));
    }

    [TestMethod]
    public void Reference_lists_every_project_setting_key()
    {
        string text = File.ReadAllText(RepositoryFile("Docs", "Reference.md"));
        var missing = ProjectSettings.Keys.Where(k => !text.Contains($"| `{k}` |", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(0, missing.Count, "Docs/Reference.md section 8 has no line for: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void Reference_lists_every_template_config_key()
    {
        string text = File.ReadAllText(RepositoryFile("Docs", "Reference.md"));
        var keys = Directory.GetFiles(RepositoryFile("Templates"), "*.tt.config")
            .SelectMany(File.ReadAllLines)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('='))
            .Select(l => l[..l.IndexOf('=')].Trim().Split('.')[0])   // OutputFolder.React is documented as OutputFolder.<Stack>
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.IsGreaterThan(10, keys.Count, "no .tt.config keys were found");
        var missing = keys.Where(k => !text.Contains($"`{k}", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(0, missing.Count, "Docs/Reference.md section 3 does not mention: " + string.Join(", ", missing));
    }
}
