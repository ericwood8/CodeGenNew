using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> What the Angular app's .gitignore keeps out of version control. </summary>
[TestClass]
public class EssentialGitTests
{
    [TestMethod]
    public async Task The_Angular_gitignore_lists_what_ng_new_ignores_and_the_files_of_editors_and_systems()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("TS_EssentialGit_v1.tt"), ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var file = GeneratedFiles.Split(result.GeneratedText!).Single();
        Assert.AreEqual(".gitignore", file.RelativePath);
        string text = file.Content.ReplaceLineEndings("\n");

        foreach (string line in new[] { "/node_modules", "/dist", "/out-tsc", "/.angular/cache", "*.log", "npm-debug.log", "/coverage",
                                        ".idea/", "*.sublime-workspace", ".vscode/*", "!.vscode/settings.json", ".history/*", ".DS_Store", "Thumbs.db" })
            Assert.IsTrue(text.Split('\n').Contains(line), $"the .gitignore should list {line}");
    }

    [TestMethod]
    public async Task The_React_and_Blazor_gitignore_files_keep_editor_and_system_files_out_too()
    {
        foreach (string template in new[] { "TSX_EssentialGit_v1.tt", "BLZ_EssentialGit_v1.tt" })
        {
            var result = await Repo.Cache.RunAsync(Repo.Template(template), ProjectSettings.FromValues([new("ProjectName", "Acme")]));
            Assert.IsTrue(result.Success, template + ": " + string.Join(" | ", result.Errors));
            var lines = GeneratedFiles.Split(result.GeneratedText!).Single().Content.ReplaceLineEndings("\n").Split('\n');
            foreach (string line in new[] { ".idea/", ".DS_Store", "Thumbs.db", ".vscode/*" })
                Assert.IsTrue(lines.Contains(line), $"{template} should list {line}");
        }
    }
}
