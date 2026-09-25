using System.Collections.ObjectModel;
using CodeGenNew.App.Services;
using CodeGenNew.TemplateEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeGenNew.App.ViewModels;

/// <summary> Backs the Template Management screen (Docs/specs.md section 9.3) -- a file-management
/// grid over Templates\*.tt, not an embedded editor; editing template content happens in whatever
/// external editor the developer already uses. </summary>
public partial class TemplateManagementViewModel : StatusMessageViewModel
{
    private readonly AppSettingsService _settings;
    private readonly string _templatesDirectory;

    public ObservableCollection<TemplateRowViewModel> Templates { get; } = [];

    [ObservableProperty]
    private TemplateRowViewModel? _selectedTemplate;

    public TemplateManagementViewModel(AppSettingsService settings)
    {
        _settings = settings;
        _templatesDirectory = settings.TemplatesDirectory;
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        StatusMessage = "";
        Templates.Clear();
        Directory.CreateDirectory(_templatesDirectory);
        foreach (var template in TemplateCatalog.DiscoverAll(_templatesDirectory)) // old versions included, so they can be seen and deleted
            Templates.Add(new TemplateRowViewModel(template));
    }

    public bool CreateTemplate(string name)
    {
        string ttPath = Path.Combine(_templatesDirectory, name + ".tt");
        if (File.Exists(ttPath))
        {
            StatusMessage = $"'{name}.tt' already exists.";
            return false;
        }

        File.WriteAllLines(ttPath,
        [
            "<#@ template language=\"C#\" #>",
            "<#@ parameter name=\"Model\" type=\"CodeGenNew.Core.TableModel\" #>",
            "<#@ import namespace=\"System.Linq\" #>",
            "<#@ import namespace=\"CodeGenNew.Core\" #>",
            "-- New template. Model.TableName, Model.Columns, Model.PrimaryKeyColumns, etc. are available.",
        ]);

        File.WriteAllLines(ttPath + ".config",
        [
            $"# Restriction checkboxes for {name}.tt (see Docs/specs.md section 5.3).",
            "RequiresPrimaryKey=true",
            "TableOnly=true"
        ]);

        Refresh();
        return true;
    }

    public bool RenameTemplate(TemplateRowViewModel row, string newName)
    {
        string newTtPath = Path.Combine(_templatesDirectory, newName + ".tt");
        if (File.Exists(newTtPath))
        {
            StatusMessage = $"'{newName}.tt' already exists.";
            return false;
        }

        File.Move(row.FilePath, newTtPath);
        string oldConfigPath = row.FilePath + ".config";
        if (File.Exists(oldConfigPath))
            File.Move(oldConfigPath, newTtPath + ".config");

        Refresh();
        return true;
    }

    public void DeleteTemplate(TemplateRowViewModel row)
    {
        if (File.Exists(row.FilePath))
            File.Delete(row.FilePath);
        string configPath = row.FilePath + ".config";
        if (File.Exists(configPath))
            File.Delete(configPath);

        Refresh();
    }

    public void OpenInEditor(TemplateRowViewModel row) =>
        EditorLocator.OpenFile(row.FilePath, _settings.Current.PreferredEditorPath);
}
