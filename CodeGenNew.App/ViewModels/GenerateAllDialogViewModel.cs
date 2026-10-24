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

/// <summary> One value of a technology drop-down: the words shown and the enum value of <see cref="StrategyRules"/> it stands for. </summary>
public sealed record ChoiceRow(string Label, int Value)
{
    public override string ToString() => Label;
}

/// <summary> One stack in the Generate All checklist. </summary>
public partial class StackRowViewModel(string stack, bool isChecked) : ObservableObject
{
    public string Stack { get; } = stack;

    [ObservableProperty]
    private bool _isChecked = isChecked;
}

/// <summary> Backs the Generate All dialog: every file of a project's stacks in one run (the engine behind <c>codegen generate</c>), for the connected database. The person picks the project, the
/// stacks and what to do besides generating (essentials, a dry run, deleting stale files, building and testing); the result lists every file with what happened to it.
/// "New from choices" in place of a project asks for the UI, API access mode, API style and API language instead, saves a project file from them and generates every table by its shape. </summary>
public partial class GenerateAllDialogViewModel : StatusMessageViewModel
{
    public const string NewProjectEntry = "New from choices";

    private readonly string _projectsDirectory;
    private readonly string _templatesDirectory;
    private readonly Func<ProjectSettings, ISchemaProvider> _providerFor;
    private readonly string _schema;
    private readonly string _databaseName;
    private readonly DispatcherQueue _dispatcher;
    private readonly SqlDialect _database;
    private readonly string? _databaseServer;
    private readonly string? _databaseUser;
    private readonly string _baseOutputDirectory;
    private string _defaultOutputDirectory = "";
    private bool _refreshing;

    public GenerateAllDialogViewModel(string projectsDirectory, string templatesDirectory, string outputDirectory, Func<ProjectSettings, ISchemaProvider> providerFor,
        string schema, string databaseName, SqlDialect database, string? databaseServer, string? databaseUser, DispatcherQueue dispatcher)
    {
        _projectsDirectory = projectsDirectory;
        _templatesDirectory = templatesDirectory;
        _providerFor = providerFor;
        _schema = schema;
        _databaseName = databaseName;
        _database = database;
        _databaseServer = databaseServer;
        _databaseUser = databaseUser;
        _dispatcher = dispatcher;
        _baseOutputDirectory = outputDirectory;
        Projects.Add(NewProjectEntry);
        foreach (string name in ProjectSettings.ListProjects(projectsDirectory))
            Projects.Add(name);
        foreach (string stack in ProjectPlan.KnownStacks)
            Stacks.Add(new StackRowViewModel(stack, false));
        foreach (var option in StrategyRules.UiOptions())
            UiChoices.Add(new ChoiceRow(option.Label, (int)option.Value));
        DatabaseText = database switch { SqlDialect.PostgreSql => "PostgreSQL", SqlDialect.MySql => "MySQL", SqlDialect.Sqlite => "SQLite", _ => "SQL Server" };
        _selectedProject = NewProjectEntry;
        ApplyDefaultOutputDirectory();
        RefreshChoices();
    }

    public ObservableCollection<string> Projects { get; } = [];
    public ObservableCollection<StackRowViewModel> Stacks { get; } = [];
    public ObservableCollection<ResultRow> Results { get; } = [];
    public ObservableCollection<ChoiceRow> UiChoices { get; } = [];
    public ObservableCollection<ChoiceRow> AccessChoices { get; } = [];
    public ObservableCollection<ChoiceRow> StyleChoices { get; } = [];
    public ObservableCollection<ChoiceRow> LanguageChoices { get; } = [];

    /// <summary> The database technology of the open connection, shown and not changeable. </summary>
    public string DatabaseText { get; }

    public bool IsNewProject => SelectedProject == NewProjectEntry;
    public bool IsSavedProject => !IsNewProject;
    public bool ApiChoicesEnabled => IsNewProject && SelectedUi is { } ui && ui.Value != (int)StrategyUi.WinUI3;

    [ObservableProperty]
    private string _selectedProject = "";

    [ObservableProperty]
    private string _outputDirectory = "";

    [ObservableProperty]
    private ChoiceRow? _selectedUi;

    [ObservableProperty]
    private ChoiceRow? _selectedAccess;

    [ObservableProperty]
    private ChoiceRow? _selectedStyle;

    [ObservableProperty]
    private ChoiceRow? _selectedLanguage;

    [ObservableProperty]
    private string _accessHint = "";

    [ObservableProperty]
    private string _styleHint = "";

    [ObservableProperty]
    private string _languageHint = "";

    /// <summary> True after a new project's name was taken by a project with other settings: the person types another name. </summary>
    [ObservableProperty]
    private bool _needsProjectName;

    [ObservableProperty]
    private string _newProjectName = "";

    /// <summary> What is still missing before Generate can run, in words; empty when nothing is. </summary>
    public string MissingText
    {
        get
        {
            if (!IsNewProject)
                return "";
            var missing = StrategyRules.Missing(CurrentChoice()).ToList();
            if (string.IsNullOrWhiteSpace(OutputDirectory))
                missing.Add("output folder");
            if (NeedsProjectName && string.IsNullOrWhiteSpace(NewProjectName))
                missing.Add("project name");
            return missing.Count == 0 ? "" : $"Still to choose: {string.Join(", ", missing)}.";
        }
    }

    /// <summary> A saved project reports what is wrong when Generate runs, as it always did; a new one stays disabled until its choices, folder and name are present. </summary>
    public bool CanGenerate => !IsRunning && (!IsNewProject || MissingText.Length == 0);

    [ObservableProperty]
    private bool _essentials;

    [ObservableProperty]
    private bool _dryRun = true;

    [ObservableProperty]
    private bool _deleteStale;

    [ObservableProperty]
    private bool _overwriteEdited;

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

    private StrategyChoice CurrentChoice() => new(_database, Pick<StrategyUi>(SelectedUi), Pick<StrategyAccess>(SelectedAccess), Pick<StrategyStyle>(SelectedStyle), Pick<StrategyLanguage>(SelectedLanguage));

    private static T? Pick<T>(ChoiceRow? row) where T : struct, Enum => row is null ? null : (T)(object)row.Value;

    private void ApplyDefaultOutputDirectory()
    {
        string wanted = IsNewProject ? Path.Combine(_baseOutputDirectory, StrategyProjectFile.NameFor(_databaseName)) : _baseOutputDirectory;
        if (string.IsNullOrWhiteSpace(OutputDirectory) || OutputDirectory == _defaultOutputDirectory)
            OutputDirectory = wanted;
        _defaultOutputDirectory = wanted;
    }

    private static void Sync(ObservableCollection<ChoiceRow> target, IReadOnlyList<ChoiceRow> wanted)
    {
        if (target.Select(r => r.Value).SequenceEqual(wanted.Select(r => r.Value)))
            return;
        target.Clear();
        foreach (var row in wanted)
            target.Add(row);
    }

    private static string Hint(string intro, IEnumerable<(string Label, string? WhyNot)> options)
    {
        var refused = options.Where(o => o.WhyNot is not null).Select(o => $"{o.Label} is not offered: {o.WhyNot}").ToList();
        return refused.Count == 0 ? intro : intro + "\n" + string.Join("\n", refused);
    }

    /// <summary> Brings the four technology lists in line with the picks made: later picks an earlier one rules out are cleared, a list with one possible value takes it, and each list
    /// offers only what the rules allow (the tooltip says what is left out and why). </summary>
    private void RefreshChoices()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            var choice = StrategyRules.Narrow(CurrentChoice());
            var access = StrategyRules.AccessOptions(choice);
            var style = StrategyRules.StyleOptions(choice);
            var language = StrategyRules.LanguageOptions(choice);
            bool api = choice.Ui is not null && choice.Ui != StrategyUi.WinUI3;

            if (api && choice.Access is null && access.Count(o => o.IsOffered) == 1)
                choice = choice with { Access = access.First(o => o.IsOffered).Value };
            style = StrategyRules.StyleOptions(choice);
            if (api && choice.Style is null && choice.Access is not null && style.Count(o => o.IsOffered) == 1)
                choice = choice with { Style = style.First(o => o.IsOffered).Value };
            language = StrategyRules.LanguageOptions(choice);
            if (api && choice.Language is null && choice.Access is not null && choice.Style is not null && language.Count(o => o.IsOffered) == 1)
                choice = choice with { Language = language.First(o => o.IsOffered).Value };

            ChoiceRow[] Offered<T>(IEnumerable<StrategyOption<T>> options) where T : struct, Enum =>
                api ? [.. options.Where(o => o.IsOffered).Select(o => new ChoiceRow(o.Label, Convert.ToInt32(o.Value)))] : [];
            Sync(AccessChoices, Offered(StrategyRules.AccessOptions(choice with { Access = null, Style = null, Language = null })));
            Sync(StyleChoices, Offered(StrategyRules.StyleOptions(choice with { Style = null, Language = null })));
            Sync(LanguageChoices, Offered(StrategyRules.LanguageOptions(choice with { Language = null })));

            SelectedUi = UiChoices.FirstOrDefault(r => r.Value == (int?)choice.Ui);
            SelectedAccess = AccessChoices.FirstOrDefault(r => r.Value == (int?)choice.Access);
            SelectedStyle = StyleChoices.FirstOrDefault(r => r.Value == (int?)choice.Style);
            SelectedLanguage = LanguageChoices.FirstOrDefault(r => r.Value == (int?)choice.Language);

            var open = choice with { Access = null, Style = null, Language = null };
            AccessHint = api ? Hint("How the API reaches the database.", StrategyRules.AccessOptions(open).Select(o => (o.Label, o.WhyNot)))
                : "WinUI3 reads the database directly, so it takes no API choice. Pick another UI to choose one.";
            StyleHint = api ? Hint("How the API is called.", StrategyRules.StyleOptions(choice with { Style = null, Language = null }).Select(o => (o.Label, o.WhyNot)))
                : AccessHint;
            LanguageHint = api ? Hint("What the API is written in.", StrategyRules.LanguageOptions(choice with { Language = null }).Select(o => (o.Label, o.WhyNot)))
                : AccessHint;
        }
        finally
        {
            _refreshing = false;
        }
        OnPropertyChanged(nameof(ApiChoicesEnabled));
        RefreshState();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(MissingText));
        OnPropertyChanged(nameof(CanGenerate));
    }

    partial void OnSelectedUiChanged(ChoiceRow? value) => RefreshChoices();
    partial void OnSelectedAccessChanged(ChoiceRow? value) => RefreshChoices();
    partial void OnSelectedStyleChanged(ChoiceRow? value) => RefreshChoices();
    partial void OnSelectedLanguageChanged(ChoiceRow? value) => RefreshChoices();
    partial void OnOutputDirectoryChanged(string value) => RefreshState();
    partial void OnNeedsProjectNameChanged(bool value) => RefreshState();
    partial void OnNewProjectNameChanged(string value) => RefreshState();
    partial void OnIsRunningChanged(bool value) => RefreshState();

    /// <summary> Ticks the stacks the chosen project names (its Stacks setting) when the project changes. </summary>
    partial void OnSelectedProjectChanged(string value)
    {
        NeedsProjectName = false;
        OnPropertyChanged(nameof(IsNewProject));
        OnPropertyChanged(nameof(IsSavedProject));
        OnPropertyChanged(nameof(ApiChoicesEnabled));
        ApplyDefaultOutputDirectory();
        RefreshState();
        if (value.Length == 0 || value == NewProjectEntry)
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

    /// <summary> The project a "New from choices" run uses: the choices checked by <see cref="StrategyRules"/>, saved as a project file named after the database (or the name the person typed when
    /// that one is taken by a project with other settings; a project is never overwritten). A dry run saves nothing. Null, with the reason in the status line, when the run cannot go on. </summary>
    private ProjectSettings? ProjectFromChoices()
    {
        var choice = CurrentChoice();
        if (StrategyRules.Refusal(choice) is { } refusal)
        {
            StatusMessage = refusal;
            return null;
        }
        if (StrategyRules.Missing(choice) is { Count: > 0 } missing)
        {
            StatusMessage = $"Still to choose: {string.Join(", ", missing)}.";
            return null;
        }
        string name = NeedsProjectName ? NewProjectName.Trim() : StrategyProjectFile.NameFor(_databaseName);
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusMessage = "Type a project name without characters that are invalid in a file name.";
            return null;
        }

        try
        {
            var fromChoices = StrategyRules.ToSettings(choice, name, _databaseName, _databaseServer, _databaseUser);
            switch (StrategyProjectFile.Save(_projectsDirectory, name, fromChoices, write: !DryRun))
            {
                case StrategySaveResult.NameTaken:
                    if (!NeedsProjectName)
                        NewProjectName = FreeName(name);
                    NeedsProjectName = true;
                    StatusMessage = $"A project named '{name}' already exists with other settings, and a project is never overwritten. Type another name and press Generate again.";
                    return null;
                case StrategySaveResult.Saved:
                    NeedsProjectName = false;
                    Results.Add(new ResultRow(DryRun ? $"Project '{name}' would be saved (dry run)." : $"Saved project '{name}'; pick it in the Project list to run again."));
                    if (!DryRun && !Projects.Contains(name, StringComparer.OrdinalIgnoreCase))
                        Projects.Add(name);
                    return fromChoices;
                default:
                    NeedsProjectName = false;
                    return ProjectSettings.LoadNamed(_projectsDirectory, name);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save project '{name}': {ex.Message}";
            return null;
        }
    }

    private string FreeName(string name)
    {
        for (int number = 2; ; number++)
        {
            if (!File.Exists(ProjectSettings.FilePathFor(_projectsDirectory, name + number)))
                return name + number;
        }
    }

    public async Task<bool> GenerateAsync()
    {
        StatusMessage = "";
        Results.Clear();
        Summary = "";
        if (IsNewProject && StrategyRules.Missing(CurrentChoice()) is { Count: > 0 } notChosen)
        {
            StatusMessage = $"Still to choose: {string.Join(", ", notChosen)}.";
            return false;
        }
        if (!IsNewProject && SelectedProject.Length == 0)
        {
            StatusMessage = "Choose a project: it says which stacks to generate and what the files are called.";
            return false;
        }
        if (!IsNewProject && !Stacks.Any(s => s.IsChecked))
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
        List<string> stacks;
        if (IsNewProject)
        {
            if (ProjectFromChoices() is not { } fromChoices)
                return false;
            project = fromChoices;
            stacks = [.. project.Stacks];
        }
        else
        {
            stacks = Stacks.Where(s => s.IsChecked).Select(s => s.Stack).ToList();
            try { project = ProjectSettings.LoadNamed(_projectsDirectory, SelectedProject); }
            catch (Exception ex)
            {
                StatusMessage = $"Couldn't read project '{SelectedProject}': {ex.Message}";
                return false;
            }
        }

        IsRunning = true;
        try
        {
            var provider = _providerFor(project);
            var options = new GenerateOptions
            {
                Project = project, Stacks = stacks, OutputDirectory = OutputDirectory, DatabaseName = _databaseName, Schema = _schema,
                DryRun = DryRun, Essentials = Essentials, DeleteStale = DeleteStale, WithDiff = true, Force = OverwriteEdited, ReportTableShapes = true
            };
            var report = await Task.Run(() => ProjectGenerator.RunAsync(provider, _templatesDirectory, options, message => _dispatcher.TryEnqueue(() => Progress = message)));

            string root = Path.GetFullPath(OutputDirectory);
            string Shown(string full) => full.StartsWithIgnoreCase(root) ? full[root.Length..].TrimStart('\\', '/') : full;
            foreach (var file in report.AllFiles.Where(f => f.Kind != FileOutcomeKind.Unchanged))
                Results.Add(new ResultRow(file.Kind switch
                {
                    FileOutcomeKind.Edited => $"Edited, not overwritten: {Shown(file.FullPath)}",
                    FileOutcomeKind.Unknown => $"Not recorded by an earlier run, not overwritten: {Shown(file.FullPath)}",
                    _ => $"{file.Kind}: {Shown(file.FullPath)}"
                }, file.Diff));
            foreach (var stale in report.Stale)
                Results.Add(new ResultRow(stale.Deleted ? $"Deleted (stale): {stale.Path}" : $"Stale: {stale.Path}" + (stale.Edited ? " (edited, left alone)" : "")));
            foreach (string refusal in report.Refusals)
                Results.Add(new ResultRow($"Declined: {refusal}"));
            foreach (string note in report.TableNotes)
                Results.Add(new ResultRow($"Note: {note}"));
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

            int keptFiles = report.Count(FileOutcomeKind.Edited) + report.Count(FileOutcomeKind.Unknown);
            Summary = $"{report.Count(FileOutcomeKind.Created)} created, {report.Count(FileOutcomeKind.Updated)} updated, {report.Count(FileOutcomeKind.WouldWrite)} would be written, " +
                      $"{report.Count(FileOutcomeKind.Unchanged)} unchanged, " +
                      (keptFiles > 0 ? $"{keptFiles} kept (tick Overwrite my edits to replace them), " : "") +
                      $"{report.Refusals.Count} declined" + (report.Stale.Count > 0 ? $", {report.Stale.Count} stale" : "") +
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
