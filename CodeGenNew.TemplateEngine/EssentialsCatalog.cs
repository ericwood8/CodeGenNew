using CodeGenNew.Core;

namespace CodeGenNew.TemplateEngine;

/// <summary> One file group of an essentials set: the files every app of a stack needs, written by a no-database template (<see cref="TemplateConfig.NoDatabase"/>) from the project settings. </summary>
public sealed record EssentialsGroup(string Stack, string Name, string Description, bool DefaultOn, TemplateInfo Template)
{
    /// <summary> The text the group is listed with in a checklist: its name, then what it writes. </summary>
    public string Title => $"{Name}: {Description}";
}

/// <summary> The stacks that have essentials and their file groups, found from the templates' configs: <c>NoDatabase=true</c>, <c>Stacks=WinUI3</c>, <c>EssentialsGroup=App</c> and a
/// <c>Description</c>. A template of the user's own that carries those keys joins the menu. </summary>
public static class EssentialsCatalog
{
    /// <summary> The stacks in menu order, with the name the Essentials menu shows. </summary>
    public static IReadOnlyList<(string Stack, string MenuText)> Stacks { get; } =
    [
        ("WinUI3", "WinUI essentials"),
        ("React", "React essentials"),
        ("Angular", "Angular essentials"),
        ("Api", "API essentials")
    ];

    /// <summary> The stack named by <paramref name="text"/> (<c>winui</c>, <c>WinUI3</c>, <c>react</c> ...), or null. </summary>
    public static string? FindStack(string text)
    {
        string wanted = text.Trim().Replace(" ", "").ToLowerInvariant();
        if (wanted is "winui" or "winui3" or "winui-3") return "WinUI3";
        return Stacks.Select(s => s.Stack).FirstOrDefault(s => s.Equals(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary> The groups of one stack, in a stable order (the template's name). </summary>
    public static List<EssentialsGroup> Groups(string templatesDirectory, string stack) => All(templatesDirectory)
        .Where(g => g.Stack.Equals(stack, StringComparison.OrdinalIgnoreCase)).ToList();

    public static List<EssentialsGroup> All(string templatesDirectory) => TemplateCatalog.Discover(templatesDirectory)
        .Where(t => t.Config.NoDatabase && t.Config.EssentialsGroup is not null)
        .SelectMany(t => t.Config.Stacks.Select(stack => new EssentialsGroup(stack, t.Config.EssentialsGroup!, t.Config.Description ?? "", t.Config.EssentialsDefault, t)))
        .OrderBy(g => g.Stack, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Template.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary> Where a group's files go: the output folder, the stack's project folder from the settings (<c>OutputWinUI3</c> ...), then the template's own sub-folder. </summary>
    public static string TargetFolder(string outputDirectory, ProjectSettings project, EssentialsGroup group) =>
        Path.Combine(outputDirectory, project.OutputFolderOf(group.Stack), group.Template.Config.OutputFolderFor(group.Stack));

    /// <summary> Runs the chosen groups and writes their files. Existing files are left alone unless <paramref name="replace"/> is set, because these files are edited by hand after the
    /// first generation. A group whose template fails reports its errors and does not stop the others. </summary>
    public static async Task<EssentialsRun> GenerateAsync(IEnumerable<EssentialsGroup> groups, ProjectSettings project, string outputDirectory, bool replace = false, bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var run = new EssentialsRun();
        foreach (var group in groups)
        {
            var result = await TemplateRunner.RunAsync(group.Template.FilePath, project, cancellationToken);
            if (!result.Success)
            {
                run.Failures.Add((group, result.Errors));
                continue;
            }
            var files = OutputWriter.FilesOf(group.Template, project.ProjectName ?? "Project", result.GeneratedText!);
            run.Outcomes.AddRange(await OutputWriter.WriteAsync(TargetFolder(outputDirectory, project, group), files, createOnly: !replace, dryRun: dryRun, cancellationToken));
        }
        return run;
    }
}

public sealed class EssentialsRun
{
    public List<FileOutcome> Outcomes { get; } = [];
    public List<(EssentialsGroup Group, IReadOnlyList<string> Errors)> Failures { get; } = [];
    public bool Success => Failures.Count == 0;
}
