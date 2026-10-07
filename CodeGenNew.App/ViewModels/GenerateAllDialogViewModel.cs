using System.Collections.ObjectModel;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace CodeGenNew.App.ViewModels;

/// <summary> One line of a generation's result; a file that exists with other content carries what differs. </summary>
public sealed record ResultRow(string Text, string? Diff = null)
{
    public override string ToString() => Text;
}

/// <summary> One stack in the Generate All checklist. </summary>
public partial class StackRowViewModel(string stack, bool isChecked) : ObservableObject
{
    public string Stack { get; } = stack;

    [ObservableProperty]
    private bool _isChecked = isChecked;
}

/// <summary> Backs the Generate All dialog: every file of a project's stacks in one run (the engine behind <c>codegen generate</c>), for the connected database. The person picks the project, the
/// stacks and what to do besides generating (essentials, a dry run, deleting stale files, building and testing); the result lists every file with what happened to it. </summary>
public partial class GenerateAllDialogViewModel : StatusMessageViewModel
{
    private readonly string _projectsDirectory;
    private readonly string _templatesDirectory;
    private readonly Func<ProjectSettings, ISchemaProvider> _providerFor;
    private readonly string _schema;
    private readonly string _databaseName;
    private readonly DispatcherQueue _dispatcher;

    public GenerateAllDialogViewModel(string projectsDirectory, string templatesDirectory, string outputDirectory, string? activeProject, Func<ProjectSettings, ISchemaProvider> providerFor,
        string schema, string databaseName, DispatcherQueue dispatcher)
    {
        _projectsDirectory = projectsDirectory;
        _templatesDirectory = templatesDirectory;
        _providerFor = providerFor;
        _schema = schema;
        _databaseName = databaseName;
        _dispatcher = dispatcher;
        OutputDirectory = outputDirectory;
        foreach (string name in ProjectSettings.ListProjects(projectsDirectory))
            Projects.Add(name);
        foreach (string stack in ProjectPlan.KnownStacks)
            Stacks.Add(new StackRowViewModel(stack, false));
        SelectedProject = activeProject is not null && Projects.Contains(activeProject, StringComparer.OrdinalIgnoreCase) ? activeProject : "";
    }

    public ObservableCollection<string> Projects { get; } = [];
    public ObservableCollection<StackRowViewModel> Stacks { get; } = [];
    public ObservableCollection<ResultRow> Results { get; } = [];

    [ObservableProperty]
    private string _selectedProject = "";

    [ObservableProperty]
    private string _outputDirectory = "";

    [ObservableProperty]
    private bool _essentials;

    [ObservableProperty]
    private bool _dryRun = true;

    [ObservableProperty]
    private bool _deleteStale;

    [ObservableProperty]
    private bool _build;

    [ObservableProperty]
    private bool _test;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _progress = "";

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private ResultRow? _selectedResult;

    public string Diff => SelectedResult?.Diff ?? "";
    public bool HasDiff => !string.IsNullOrEmpty(Diff);

    partial void OnSelectedResultChanged(ResultRow? value)
    {
        OnPropertyChanged(nameof(Diff));
        OnPropertyChanged(nameof(HasDiff));
    }

    /// <summary> Ticks the stacks the chosen project names (its Stacks setting) when the project changes. </summary>
    partial void OnSelectedProjectChanged(string value)
    {
        if (value.Length == 0)
            return;
        try
        {
            var project = ProjectSettings.LoadNamed(_projectsDirectory, value);
            foreach (var row in Stacks)
                row.IsChecked = project.Stacks.Contains(row.Stack, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // a project that cannot be read is reported when Generate runs
        }
    }

    public async Task<bool> GenerateAsync()
    {
        StatusMessage = "";
        Results.Clear();
        Summary = "";
        if (SelectedProject.Length == 0)
        {
            StatusMessage = "Choose a project: it says which stacks to generate and what the files are called.";
            return false;
        }
        var stacks = Stacks.Where(s => s.IsChecked).Select(s => s.Stack).ToList();
        if (stacks.Count == 0)
        {
            StatusMessage = "Tick at least one stack.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            StatusMessage = "Choose an output folder (the stack folders go under it).";
            return false;
        }

        ProjectSettings project;
        try { project = ProjectSettings.LoadNamed(_projectsDirectory, SelectedProject); }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't read project '{SelectedProject}': {ex.Message}";
            return false;
        }

        IsRunning = true;
        try
        {
            var provider = _providerFor(project);
            var options = new GenerateOptions
            {
                Project = project, Stacks = stacks, OutputDirectory = OutputDirectory, DatabaseName = _databaseName, Schema = _schema,
                DryRun = DryRun, Essentials = Essentials, DeleteStale = DeleteStale, WithDiff = true
            };
            var report = await Task.Run(() => ProjectGenerator.RunAsync(provider, _templatesDirectory, options, message => _dispatcher.TryEnqueue(() => Progress = message)));

            string root = Path.GetFullPath(OutputDirectory);
            string Shown(string full) => full.StartsWithIgnoreCase(root) ? full[root.Length..].TrimStart('\\', '/') : full;
            foreach (var file in report.AllFiles.Where(f => f.Kind != FileOutcomeKind.Unchanged))
                Results.Add(new ResultRow($"{file.Kind}: {Shown(file.FullPath)}", file.Diff));
            foreach (var stale in report.Stale)
                Results.Add(new ResultRow(stale.Deleted ? $"Deleted (stale): {stale.Path}" : $"Stale: {stale.Path}" + (stale.Edited ? " (edited, left alone)" : "")));
            foreach (string refusal in report.Refusals)
                Results.Add(new ResultRow($"Declined: {refusal}"));
            foreach (string warning in report.Warnings.Concat(report.Essentials?.Warnings ?? []))
                Results.Add(new ResultRow($"Warning: {warning}"));
            foreach (string error in report.Errors)
                Results.Add(new ResultRow($"Error: {error}"));

            bool built = true;
            if ((Build || Test) && !DryRun)
            {
                Progress = "Building...";
                var steps = await Task.Run(() => ProjectBuilder.RunAsync(project, stacks, OutputDirectory, Build, Test, message => _dispatcher.TryEnqueue(() => Progress = message)));
                foreach (var step in steps)
                    Results.Add(new ResultRow($"{(step.Success ? "ok" : "FAILED")} {step.Stack} {step.Step}: {step.Command}", step.Summary.Length > 0 ? step.Summary : null));
                built = steps.All(s => s.Success);
            }

            Summary = $"{report.Count(FileOutcomeKind.Created)} created, {report.Count(FileOutcomeKind.Updated)} updated, {report.Count(FileOutcomeKind.WouldWrite)} would be written, " +
                      $"{report.Count(FileOutcomeKind.Unchanged)} unchanged, {report.Refusals.Count} declined" + (report.Stale.Count > 0 ? $", {report.Stale.Count} stale" : "") +
                      (DryRun ? " (dry run: nothing was written)" : "") + ".";
            if (!report.Success || !built)
                StatusMessage = "Something failed; see the list.";
            return report.Success && built;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Generation failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsRunning = false;
            Progress = "";
        }
    }
}
