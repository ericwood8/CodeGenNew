using System.Collections.ObjectModel;
using CodeGenNew.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeGenNew.App.ViewModels;

/// <summary> One editable setting on the Project Settings screen: a key from ProjectSettings.Keys and its explicit value. </summary>
public partial class ProjectSettingRowViewModel : ObservableObject
{
    private readonly bool _isBoolean;
    private bool _syncing;

    public ProjectSettingRowViewModel(string key, string hint)
    {
        Key = key;
        Hint = hint;
        _isBoolean = ProjectSettingsHints.BooleanKeys.Contains(key);
        if (!_isBoolean && ProjectSettingChoices.All.TryGetValue(key, out var choices))
        {
            Kind = choices.Kind;
            Choices = choices.Choices;
        }
        else
        {
            Kind = _isBoolean ? SettingKind.Boolean : ProjectSettingChoices.Numbers.ContainsKey(key) ? SettingKind.Number : SettingKind.Text;
            Choices = [];
        }
        if (ProjectSettingChoices.Numbers.TryGetValue(key, out var range))
        {
            NumberMin = range.Min;
            NumberMax = range.Max;
        }
        if (Kind == SettingKind.Multi)
        {
            foreach (var choice in Choices)
                Options.Add(new SettingOptionViewModel(choice, WriteOptions));
        }
        Caption = ProjectSettingsHints.Caption(key) + (_isBoolean ? "?" : "");
    }

    public string Key { get; }
    public string Hint { get; }
    public SettingKind Kind { get; }
    public SettingChoice[] Choices { get; }
    /// <summary> The check boxes of a setting that takes any of a list of values. </summary>
    public ObservableCollection<SettingOptionViewModel> Options { get; } = [];
    /// <summary> The words of the key apart; a true or false setting reads as a question beside its check box. </summary>
    public string Caption { get; }

    public bool IsText => Kind == SettingKind.Text;
    public bool IsBoolean => Kind == SettingKind.Boolean;
    public bool IsRadio => Kind == SettingKind.Radio;
    public bool IsDropdown => Kind == SettingKind.Dropdown;
    public bool IsMulti => Kind == SettingKind.Multi;
    public bool IsNumber => Kind == SettingKind.Number;
    public double NumberMin { get; }
    public double NumberMax { get; }

    /// <summary> The number box of a whole-number setting; NaN (an empty box) is a blank value, which means the default. </summary>
    public double NumberValue
    {
        get => int.TryParse(Value.Trim(), out int number) ? number : double.NaN;
        set => Value = double.IsNaN(value) ? "" : ((int)Math.Round(value)).ToString();
    }
    public bool ShowCaption => Kind != SettingKind.Boolean;

    [ObservableProperty]
    private string _value = "";

    /// <summary> The check box of a true or false setting: checked writes "true", unchecked writes "false". </summary>
    public bool IsChecked
    {
        get => Value.Trim().ToLowerInvariant() is "true" or "1" or "yes";
        set => Value = value ? "true" : "false";
    }

    /// <summary> The choice a single-choice setting holds; none when the file holds a value that is not on the list, which stays as it is until another choice is picked. </summary>
    public SettingChoice? SelectedChoice
    {
        get => Choices.FirstOrDefault(c => c.Value.Equals(Value.Trim(), StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
                Value = value.Value;
        }
    }

    /// <summary> The text on the drop-down button of a multi-choice setting: the ticked values, or a prompt when none is. </summary>
    public string OptionsSummary => Value.Trim().Length == 0 ? "None selected (choose)" : string.Join(", ", Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(OptionsSummary));
        OnPropertyChanged(nameof(NumberValue));
        if (Kind != SettingKind.Multi)
            return;
        _syncing = true;
        var named = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var option in Options)
            option.IsChecked = named.Contains(option.Value, StringComparer.OrdinalIgnoreCase);
        _syncing = false;
    }

    /// <summary> Writes the ticked options in list order; a name in the file that is not on the list is kept after them. </summary>
    private void WriteOptions()
    {
        if (_syncing)
            return;
        var kept = Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(name => !Options.Any(o => o.Value.Equals(name, StringComparison.OrdinalIgnoreCase)));
        Value = string.Join(",", Options.Where(o => o.IsChecked).Select(o => o.Value).Concat(kept));
    }
}

/// <summary> One check box of a setting that takes any of a list of values. </summary>
public partial class SettingOptionViewModel(SettingChoice choice, Action changed) : ObservableObject
{
    public string Value { get; } = choice.Value;
    public string Label { get; } = choice.Label;

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary> Backs the Project Settings screen: pick or name a project, edit its settings, save them to
/// Projects\&lt;ProjectName&gt;.config. </summary>
public partial class ProjectSettingsDialogViewModel : StatusMessageViewModel
{
    private static readonly IReadOnlyDictionary<string, string> Hints = ProjectSettingsHints.All;

    private readonly string _projectsDirectory;

    public ObservableCollection<string> Projects { get; } = [];
    /// <summary> Every editable setting, in ProjectSettings.Keys order -- what Load and TrySave work on. </summary>
    public ObservableCollection<ProjectSettingRowViewModel> Rows { get; } = [];
    /// <summary> The same row objects, split by tab (ProjectSettingGroups): an edit on a tab is an edit to the one row. </summary>
    public IReadOnlyList<(string Title, string Description, IReadOnlyList<ProjectSettingRowViewModel> Rows)> Tabs { get; }

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
        }
        Tabs = ProjectSettingGroups.All
            .Select(g => (g.Title, g.Description, (IReadOnlyList<ProjectSettingRowViewModel>)Rows
                .Where(r => ProjectSettingGroups.TabOf(r.Key) == g.Title)
                .OrderBy(r => Array.FindIndex(g.Keys, k => k.Equals(r.Key, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : int.MaxValue)
                .ToList()))
            .ToList();

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
