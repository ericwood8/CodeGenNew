using System.Collections.ObjectModel;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeGenNew.App.ViewModels;

/// <summary> One file group in the essentials checklist. </summary>
public partial class EssentialsGroupRowViewModel(EssentialsGroup group) : ObservableObject
{
    public EssentialsGroup Group { get; } = group;
    public string Name => Group.Name;
    public string Description => Group.Description;

    [ObservableProperty]
    private bool _isChecked = group.DefaultOn;
}

/// <summary> Backs the dialog the Essentials menu opens for one stack (WinUI, React, Angular, API): pick the project and the file groups, then write them. The files need no database
/// and no table; they are written under the output folder in the stack's project folder (OutputWinUI3, OutputReact ... of the project), and files that exist are kept unless
/// "Replace" is ticked. </summary>
public partial class EssentialsDialogViewModel : StatusMessageViewModel
{
    private readonly string _projectsDirectory;
    private readonly string _templatesDirectory;

    public EssentialsDialogViewModel(string stack, string menuText, string projectsDirectory, string templatesDirectory, string outputDirectory, string? activeProject)
    {
        Stack = stack;
        Title = menuText;
        _projectsDirectory = projectsDirectory;
        _templatesDirectory = templatesDirectory;
        OutputDirectory = outputDirectory;
        foreach (string name in ProjectSettings.ListProjects(projectsDirectory))
            Projects.Add(name);
        SelectedProject = activeProject is not null && Projects.Contains(activeProject, StringComparer.OrdinalIgnoreCase) ? activeProject : "";
        foreach (var group in EssentialsCatalog.Groups(templatesDirectory, stack))
            Groups.Add(new EssentialsGroupRowViewModel(group));
    }

    public string Stack { get; }
    public string Title { get; }
    public ObservableCollection<string> Projects { get; } = [];
    public ObservableCollection<EssentialsGroupRowViewModel> Groups { get; } = [];
    public ObservableCollection<ResultRow> Results { get; } = [];

    [ObservableProperty]
    private ResultRow? _selectedResult;

    public string Diff => SelectedResult?.Diff ?? "";
    public bool HasDiff => !string.IsNullOrEmpty(Diff);

    partial void OnSelectedResultChanged(ResultRow? value)
    {
        OnPropertyChanged(nameof(Diff));
        OnPropertyChanged(nameof(HasDiff));
    }

    [ObservableProperty]
    private string _selectedProject = "";

    [ObservableProperty]
    private string _outputDirectory = "";

    [ObservableProperty]
    private bool _replace;

    [ObservableProperty]
    private string _summary = "";

    /// <summary> The folder the files of a project are written to, as the person sees it before generating. </summary>
    public string TargetHint => "Files go to " + System.IO.Path.Combine(OutputDirectory, ProjectOrNone().OutputFolderOf(Stack));

    partial void OnSelectedProjectChanged(string value) => OnPropertyChanged(nameof(TargetHint));
    partial void OnOutputDirectoryChanged(string value) => OnPropertyChanged(nameof(TargetHint));

    private ProjectSettings ProjectOrNone()
    {
        if (SelectedProject.Length == 0)
            return ProjectSettings.None;
        try { return ProjectSettings.LoadNamed(_projectsDirectory, SelectedProject); }
        catch (Exception) { return ProjectSettings.None; }
    }

    /// <summary> Writes the ticked groups. Returns true when every group ran (a file that was kept because it exists is not a failure). </summary>
    public async Task<bool> GenerateAsync()
    {
        StatusMessage = "";
        Results.Clear();
        Summary = "";
        var chosen = Groups.Where(g => g.IsChecked).Select(g => g.Group).ToList();
        if (chosen.Count == 0)
        {
            StatusMessage = "Tick at least one group.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusMessage = "Choose an output location first (the Output Location button).";
            return false;
        }

        ProjectSettings project;
        try
        {
            project = SelectedProject.Length == 0 ? ProjectSettings.None : ProjectSettings.LoadNamed(_projectsDirectory, SelectedProject);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't read project '{SelectedProject}': {ex.Message}";
            return false;
        }

        var run = await EssentialsCatalog.GenerateAsync(chosen, project, OutputDirectory, Replace, withDiff: true);
        string root = System.IO.Path.GetFullPath(OutputDirectory);
        foreach (var outcome in run.Outcomes)
        {
            string shown = outcome.FullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? outcome.FullPath[root.Length..].TrimStart('\\', '/') : outcome.FullPath;
            Results.Add(new ResultRow($"{outcome.Kind}: {shown}" + (outcome.Diff is not null && outcome.Kind == FileOutcomeKind.Skipped ? " (differs: select it to see how)" : ""), outcome.Diff));
        }
        foreach (var (group, errors) in run.Failures)
            Results.Add(new ResultRow($"Failed: {group.Name}: {string.Join(" | ", errors)}"));
        foreach (string warning in run.Warnings)
            Results.Add(new ResultRow($"Warning: {warning}"));

        int created = run.Outcomes.Count(o => o.Kind == FileOutcomeKind.Created), updated = run.Outcomes.Count(o => o.Kind == FileOutcomeKind.Updated);
        int kept = run.Outcomes.Count(o => o.Kind is FileOutcomeKind.Skipped or FileOutcomeKind.Unchanged);
        Summary = $"{created} created, {updated} replaced, {kept} left as they were" + (run.Failures.Count > 0 ? $", {run.Failures.Count} group(s) failed" : "") + ".";
        if (run.Failures.Count > 0)
            StatusMessage = "Some groups failed; see the list.";
        else if (SelectedProject.Length == 0)
            StatusMessage = "No project chosen: names come from the defaults (MyApp). Pick a project to use its names.";
        return run.Success;
    }
}
