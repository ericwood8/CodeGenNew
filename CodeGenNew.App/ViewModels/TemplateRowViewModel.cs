using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodeGenNew.App.ViewModels;

/// <summary> One row of the Template Management screen (Docs/specs.md section 9.3). Editing either
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

    [ObservableProperty]
    private bool _tableOnly;

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
        _tableOnly = template.Config.TableOnly;
    }

    partial void OnRequiresPrimaryKeyChanged(bool value) => Save();
    partial void OnTableOnlyChanged(bool value) => Save();

    private void Save()
    {
        // This screen only owns the two checkboxes. Rewrite them, but carry over every other line as it is (extra
        // settings such as NeedsRowData / NeedsReferencedDisplayColumns and their comments), so toggling a checkbox
        // can never silently switch off something a template depends on.
        var preserved = new List<string>();
        if (File.Exists(_configPath))
        {
            foreach (string line in File.ReadAllLines(_configPath))
            {
                string trimmed = line.Trim();
                bool isOurs = trimmed.StartsWithIgnoreCase("# Restriction checkboxes for")
                              || trimmed.StartsWithIgnoreCase("RequiresPrimaryKey")
                              || trimmed.StartsWithIgnoreCase("TableOnly");
                if (!isOurs)
                    preserved.Add(line);
            }
        }

        var lines = new List<string>
        {
            "# Restriction checkboxes for " + Name + ".tt (see Docs/specs.md section 5.3).",
            $"RequiresPrimaryKey={RequiresPrimaryKey.ToString().ToLowerInvariant()}",
            $"TableOnly={TableOnly.ToString().ToLowerInvariant()}"
        };
        lines.AddRange(preserved);

        File.WriteAllLines(_configPath, lines);
    }
}
