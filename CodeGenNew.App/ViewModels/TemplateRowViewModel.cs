using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeGenNew.App.ViewModels;

/// <summary> One row of the Template Management screen (Docs/ARCHITECTURE.md section 7). Editing either
/// checkbox writes the .tt.config file immediately. </summary>
public partial class TemplateRowViewModel : ObservableObject
{
    /// <summary> The file name without ".tt", version suffix included (e.g. "SP_Save_v1") -- this is what Rename/Delete act on. </summary>
    public string Name { get; }
    public string FilePath { get; }
    private readonly string _configPath;

    /// <summary> Old versions stay listed (so they can be deleted) but are marked, since the menu no longer offers them. </summary>
    public string DisplayName { get; }
    public string SupersededTip { get; }

    [ObservableProperty]
    private bool _requiresPrimaryKey;

    // Arms/confirms the Delete button inline (first click arms it, second click deletes) instead of a
    // nested ContentDialog, since WinUI only allows one ContentDialog open at a time and this row lives
    // inside the already-open Manage Templates dialog (see TemplateManagementDialog.xaml.cs).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteButtonText))]
    private bool _confirmingDelete;

    public string DeleteButtonText => ConfirmingDelete ? "Confirm Delete?" : "Delete";

    public TemplateRowViewModel(TemplateInfo template)
    {
        Name = template.FileStem;
        DisplayName = template.IsSuperseded ? $"{template.FileStem}  (old)" : template.FileStem;
        SupersededTip = template.IsSuperseded
            ? $"Superseded by v{template.SupersededByVersion} -- not shown in the right-click menu. Safe to delete."
            : "";
        FilePath = template.FilePath;
        _configPath = template.FilePath + ".config";
        _requiresPrimaryKey = template.Config.RequiresPrimaryKey;
    }

    partial void OnRequiresPrimaryKeyChanged(bool value) => Save();

    private void Save()
    {
        // The checkbox owns the header comment and the Requires line. Every other line is kept as it is, and the other items of Requires are carried over from the file.
        var current = TemplateConfig.Load(_configPath);
        var preserved = new List<string>();
        if (File.Exists(_configPath))
        {
            foreach (string line in File.ReadAllLines(_configPath))
            {
                string trimmed = line.Trim();
                bool isOurs = trimmed.StartsWithIgnoreCase("# Restriction checkbox")
                              || System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^(Requires\w*|TableOnly)\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!isOurs)
                    preserved.Add(line);
            }
        }

        var requires = new List<string>();
        if (RequiresPrimaryKey) requires.Add("PrimaryKey");
        if (current.RequiresSingleKey) requires.Add("SingleKey");
        if (current.RequiresNotNameActiveTable) requires.Add("NotNameActive");
        if (current.RequiresJunctionTable) requires.Add("JunctionTable");
        if (current.RequiresChildTables) requires.Add("ChildTables");
        if (current.RequiresAuditTable) requires.Add("AuditTable");

        var lines = new List<string> { "# Restriction checkbox for " + Name + ".tt (see Docs/Reference.md section 3)." };
        if (current.Scope == TemplateScope.Table)
            lines.Add("Requires=" + string.Join(",", requires));
        lines.AddRange(preserved);

        File.WriteAllLines(_configPath, lines);
    }
}
