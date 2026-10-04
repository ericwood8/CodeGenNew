using System.Text.RegularExpressions;

namespace CodeGenNew.Tests;

/// <summary> The repository is public: no project, machine or person name from where the generator was developed may remain in a file that is shipped. </summary>
[TestClass]
public class LeftoverNamesTests
{
    // Written as patterns that do not match themselves, so this file can be scanned like every other.
    private static readonly Regex[] Forbidden =
    [
        new("Time" + "Entry", RegexOptions.IgnoreCase),
        new("Critical" + "Viewer", RegexOptions.IgnoreCase),
        new("CodeGen" + "Possibilities", RegexOptions.IgnoreCase),
        new("ERICS" + "MINIPC", RegexOptions.IgnoreCase),
        new("Eric" + "Work", RegexOptions.IgnoreCase),
        new(@"Users[\\/]eric" + @"w\b", RegexOptions.IgnoreCase),
        new("Hello" + "Postgres", RegexOptions.IgnoreCase),
        new("Claude" + "Code" + @"\b"),
    ];

    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", ".claude", ".vs", "node_modules", "Output", "TestResults", "packages"];
    private static readonly string[] TextExtensions =
        [".cs", ".tt", ".config", ".json", ".md", ".txt", ".xaml", ".csproj", ".slnx", ".props", ".targets", ".ps1", ".psm1", ".sh", ".yml", ".yaml", ".sql", ".gitignore", ".gitattributes", ".editorconfig", ".http"];

    private static IEnumerable<string> ShippedFiles(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder))
            if (TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) || Path.GetFileName(file).StartsWith('.'))
                yield return file;
        foreach (string directory in Directory.EnumerateDirectories(folder))
            if (!SkippedFolders.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
                foreach (string file in ShippedFiles(directory))
                    yield return file;
    }

    [TestMethod]
    public void No_file_of_the_repository_names_the_projects_machines_or_people_it_was_developed_on()
    {
        var found = new List<string>();
        foreach (string file in ShippedFiles(Repo.Root))
        {
            string text = File.ReadAllText(file);
            foreach (var pattern in Forbidden)
                if (pattern.Match(text) is { Success: true } match)
                    found.Add($"{Path.GetRelativePath(Repo.Root, file)}: {match.Value}");
        }

        Assert.AreEqual(0, found.Count, "Leftover names:\n" + string.Join("\n", found));
    }

    [TestMethod]
    public void The_scan_looks_at_the_templates_and_the_documents()
    {
        var files = ShippedFiles(Repo.Root).Select(f => Path.GetRelativePath(Repo.Root, f)).ToList();

        Assert.IsTrue(files.Any(f => f.EndsWith("API_Crud_v1.tt")), "templates are scanned");
        Assert.IsTrue(files.Contains("README.md"), "the readme is scanned");
        Assert.IsTrue(files.Any(f => f.EndsWith("LeftoverNamesTests.cs")), "the test project is scanned");
    }
}
