using System.Collections.ObjectModel;
using CodeGenNew.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeGenNew.App.ViewModels;

/// <summary> One editable setting on the Project Settings screen: a key from ProjectSettings.Keys and its explicit value. </summary>
public partial class ProjectSettingRowViewModel(string key, string hint) : ObservableObject
{
    public string Key { get; } = key;
    public string Hint { get; } = hint;

    [ObservableProperty]
    private string _value = "";
}

/// <summary> Backs the Project Settings screen: pick or name a project, edit its settings, save them to
/// Projects\&lt;ProjectName&gt;.config. </summary>
public partial class ProjectSettingsDialogViewModel : StatusMessageViewModel
{
    private static readonly Dictionary<string, string> Hints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ViewNamespace"] = "blank = <ProjectName>.App.Views",
        ["ViewModelNamespace"] = "blank = <ProjectName>.App.ViewModels",
        ["ContextName"] = "blank = <ProjectName>Context",
        ["ContextNamespace"] = "blank = <ProjectName>.App.Data",
        ["ApiNamespace"] = "blank = <ProjectName>.ApiService.Apis",
        ["EnumNamespace"] = "blank = <ProjectName>.App.Enums",
        ["RepoNamespace"] = "blank = <ProjectName>.App.Repositories",
        ["EntityNamespace"] = "blank = <ProjectName>.App.Entities",
        ["MinYear"] = $"blank = {ProjectSettings.DefaultMinYear}",
        ["MaxYear"] = $"blank = {ProjectSettings.DefaultMaxYear}",
        ["ViewsFolder"] = "blank = the template's own folder",
        ["ViewModelsFolder"] = "blank = the template's own folder",
        ["Usings"] = "comma-separated namespaces every generated file should use",
        ["DetailMasterTables"] = "comma-separated table names whose Add/Edit dialog is a Detail-Master dialog (with child grids)",
        ["NoLookupParents"] = "comma-separated table names shown as a number, not a drop-down",
        ["NoRepositoryTables"] = "comma-separated table names that get no repository",
        ["NoApiTables"] = "comma-separated table names that get no API",
        ["NoNavigationTables"] = "comma-separated table names that get no navigation properties"
    };

    private readonly string _projectsDirectory;

    public ObservableCollection<string> Projects { get; } = [];
    /// <summary> Settings that only the WinUI3 templates read; they get their own tab. Everything else is on General. </summary>
    private static readonly HashSet<string> WinUI3Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ViewNamespace", "ViewModelNamespace", "ViewsFolder", "ViewModelsFolder", "DetailMasterTables"
    };

    /// <summary> Every editable setting, in ProjectSettings.Keys order -- what Load and TrySave work on. </summary>
    public ObservableCollection<ProjectSettingRowViewModel> Rows { get; } = [];
    /// <summary> The same row objects, split by tab: edits made on either tab are edits to the one row. </summary>
    public ObservableCollection<ProjectSettingRowViewModel> GeneralRows { get; } = [];
    public ObservableCollection<ProjectSettingRowViewModel> WinUI3Rows { get; } = [];

    [ObservableProperty]
    private string _projectName = "";

    public ProjectSettingsDialogViewModel(string projectsDirectory, string? activeProject)
    {
        _projectsDirectory = projectsDirectory;
        foreach (string name in ProjectSettings.ListProjects(projectsDirectory))
            Projects.Add(name);
        foreach (string key in ProjectSettings.Keys.Where(k => k != "ProjectName"))
        {
            var row = new ProjectSettingRowViewModel(key, Hints.GetValueOrDefault(key, ""));
            Rows.Add(row);
            (WinUI3Keys.Contains(key) ? WinUI3Rows : GeneralRows).Add(row);
        }

        if (!string.IsNullOrEmpty(activeProject) && Projects.Contains(activeProject, StringComparer.OrdinalIgnoreCase))
            Load(activeProject);
    }

    /// <summary> Fills the form from the named project's file (called when a project is picked from the list). </summary>
    public void Load(string name)
    {
        ProjectName = name;
        try
        {
            var settings = ProjectSettings.LoadNamed(_projectsDirectory, name);
            foreach (var row in Rows)
                row.Value = settings.Values.GetValueOrDefault(row.Key, "");
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't read project '{name}': {ex.Message}";
        }
    }

    /// <summary> Validates and writes Projects\&lt;ProjectName&gt;.config; returns false (with StatusMessage set) when it can't. </summary>
    public bool TrySave()
    {
        string name = ProjectName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "A project name is required.";
            return false;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusMessage = "The project name can't contain characters that are invalid in a file name.";
            return false;
        }
        foreach (string yearKey in new[] { "MinYear", "MaxYear" })
        {
            string text = Rows.First(r => r.Key == yearKey).Value;
            if (text.Trim().Length > 0 && !int.TryParse(text, out _))
            {
                StatusMessage = $"{yearKey} must be a whole number.";
                return false;
            }
        }

        var values = Rows.Select(r => new KeyValuePair<string, string>(r.Key, r.Value))
            .Append(new("ProjectName", name));
        try
        {
            Directory.CreateDirectory(_projectsDirectory);
            File.WriteAllText(ProjectSettings.FilePathFor(_projectsDirectory, name), ProjectSettings.FromValues(values).ToFileText());
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save the project file: {ex.Message}";
            return false;
        }
    }
}
