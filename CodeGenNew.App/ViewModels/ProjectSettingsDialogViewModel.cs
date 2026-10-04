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
        ["CurrencyCode"] = "ISO currency code for money fields; blank = USD",
        ["ViewsFolder"] = "blank = the template's own folder",
        ["ViewModelsFolder"] = "blank = the template's own folder",
        ["Usings"] = "comma-separated namespaces every generated file should use",
        ["DetailMasterTables"] = "comma-separated table names whose Add/Edit dialog is a Detail-Master dialog (with child grids)",
        ["HiddenParents"] = "comma-separated tables whose foreign key columns the TypeScript forms hide",
        ["ModelFileOverrides"] = "Table=file pairs for model files not named after the table, e.g. DepartmentTeam=department,ProjectTask=project",
        ["BaseEntity"] = "base class of generated entities; blank = BaseEntity",
        ["BaseNameActiveEntity"] = "base class for Name + IsActive tables; blank = BaseNameActiveEntity",
        ["EnumTables"] = "comma-separated enum tables (no entity, repository or API); blank = decided from each table's shape",
        ["EnumNameSuffixes"] = "comma-separated name endings that mark an enum table; blank = Type, Types, Code, Codes, Status, Kind",
        ["EnumMaxRows"] = $"a lookup table with more rows than this is not an enum; blank = {ProjectSettings.DefaultEnumMaxRows}",
        ["NoLookupParents"] = "overrides EnumTables for this one question: tables whose foreign key is a number box, not a drop-down",
        ["NoRepositoryTables"] = "overrides EnumTables for this one question: tables that get no repository",
        ["NoApiTables"] = "overrides EnumTables for this one question: tables that get no API or TypeScript model/screen",
        ["NoNavigationTables"] = "overrides EnumTables for this one question: tables referenced without a navigation property",
        ["NonNegativeColumns"] = "comma-separated money columns that can never be negative (CreditLimit, or Item.Cost for one table): their number box gets a minimum of 0",
        ["ApiFolder"] = "React / Angular folder for the api modules, relative to the source folder; blank = api",
        ["ModelsFolder"] = "React / Angular folder for the TypeScript models; blank = models",
        ["ServicesFolder"] = "Angular folder for the services; blank = services",
        ["ComponentsFolder"] = "React / Angular folder for the shared components and Angular screens; blank = components",
        ["PagesFolder"] = "React folder for the pages; blank = pages",
        ["AngularVersion"] = "major version of Angular, e.g. 22; blank = output that every version from 18 accepts",
        ["IgnoredColumns"] = "comma-separated columns to leave out (Tags, or Place.Location): a type CodeGenNew cannot map, such as an array or geometry",
        ["ListingName"] = "WinUI3_DirectoryListing: the class stem, e.g. Document (DocumentListPage); blank = Document",
        ["ListingFolder"] = "WinUI3_DirectoryListing: the folder whose files are listed (for example %LocalAppData%/Project/Name); blank = under LocalAppData",
        ["ListingPattern"] = "WinUI3_DirectoryListing: which files are listed; blank = *.*",
        ["Stacks"] = "stacks to generate: Api, WinUI3, React, Angular (comma-separated), e.g. Api,React",
        ["PlanAlso"] = "templates to run in a whole-project generate although their config leaves them out, e.g. SP_Insert,SP_Update",
        ["OutputApi"] = "folder of the API project under the output folder; blank = <ProjectName>.Api",
        ["OutputWinUI3"] = "folder of the WinUI3 app under the output folder; blank = <ProjectName>.App",
        ["OutputReact"] = "folder of the React app; blank = frontend",
        ["OutputAngular"] = "folder of the Angular app; blank = frontend",
        ["OutputSql"] = "folder of the generated SQL; blank = sql",
        ["AppNamespace"] = "root namespace of the WinUI3 app; blank = <ProjectName>.App",
        ["DatabaseProvider"] = "SqlServer, PostgreSql or MySql (appsettings.json and the package reference); blank = SqlServer",
        ["DatabaseServer"] = "server for appsettings.json; blank = localhost",
        ["DatabaseName"] = "database for appsettings.json; blank = the project name",
        ["DatabaseUser"] = "login for appsettings.json (never the password); blank = Windows authentication",
        ["ApiPort"] = "port the API listens on; blank = 5080",
        ["DevPort"] = "port of the front end's dev server; blank = 5173 (React) or 4200 (Angular)",
        ["ProjectTitle"] = "the web app's title; blank = the project name in words",
        ["BuildApi"] = "command that builds the API after a generate; blank = dotnet build -v q (none skips it)",
        ["BuildWinUI3"] = "command that builds the WinUI3 app after a generate; blank = dotnet build -v q",
        ["BuildReact"] = "command that builds the React app; blank = npm run build",
        ["BuildAngular"] = "command that builds the Angular app; blank = npm run build",
        ["TestApi"] = "command that tests the API; blank = no tests",
        ["TestWinUI3"] = "command that tests the WinUI3 app; blank = no tests",
        ["TestReact"] = "command that tests the React app; blank = npm test",
        ["TestAngular"] = "command that tests the Angular app; blank = npm test -- --watch=false",
        ["DbSetNames"] = "Plural = Customers, blank = the table name (Customer)"
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
