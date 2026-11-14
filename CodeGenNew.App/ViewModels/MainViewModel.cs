using System.Collections.ObjectModel;
using CodeGenNew.App.Services;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CodeGenNew.App.ViewModels;

/// <summary> The main screen's ViewModel (Docs/ARCHITECTURE.md section 7): the TreeView's data, the current
/// connection, and running a template against the selected table. CodeGenNew never writes to the target
/// database itself (section 1.1) -- SqlServerSchemaProvider only ever reads schema metadata here. </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppSettingsService _settings;
    private ConnectionRequest? _connectionRequest;

    // Nocturne theme's error red (Themes/NocturneTheme.xaml), not a harsh pure red -- stays legible and
    // accessible against the dark background.
    private static readonly SolidColorBrush WarningBrush = new(Color.FromArgb(255, 242, 103, 122));

    public IconProvider Icons { get; }
    public ObservableCollection<TableNodeViewModel> Tables { get; } = [];

    /// <summary> The schema most of the connected database's tables are in (the first one when tied), worked out once per connection; null until tables are loaded. </summary>
    public string? MainSchema { get; private set; }

    [ObservableProperty]
    private string _databaseLabel = "(not connected)";

    /// <summary> The provider and login the connection was made with, under the database name. </summary>
    [ObservableProperty]
    private string _databaseDetail = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Connect to a database to get started.";

    /// <summary> Red only for the "spCanDelete was not found" warning appended in ConnectAsync; every
    /// other status message (progress, success, errors already worded as such) keeps the normal text color. </summary>
    public Brush StatusBrush => StatusMessage.ContainsIgnoreCase("spCanDelete was not found")
        ? WarningBrush
        : ThemeBrushes.DefaultText;

    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(StatusBrush));

    [ObservableProperty]
    private TableNodeViewModel? _selectedTable;

    public MainViewModel(AppSettingsService settings)
    {
        _settings = settings;
        Icons = new IconProvider();
    }

    public async Task ConnectAsync(ConnectionRequest request)
    {
        IsBusy = true;
        StatusMessage = $"Connecting to {request.ServerName}\\{request.DatabaseName}...";
        try
        {
            var schemaProvider = SchemaProviderFactory.Create(request, _settings.SpecialLogicColumnsConfigPath);
            var summaries = await schemaProvider.ListTablesAsync();

            _connectionRequest = request;
            DatabaseLabel = $"{request.ServerName} \\ {request.DatabaseName}";
            DatabaseDetail = request.DescribeConnection();
            IsConnected = true;

            Tables.Clear();
            foreach (var summary in summaries)
                Tables.Add(new TableNodeViewModel(summary, Icons, LoadColumnSummariesAsync));

            MainSchema = Tables.Count == 0 ? null : Tables.GroupBy(t => t.SchemaName).OrderByDescending(g => g.Count()).First().Key;

            StatusMessage = $"Loaded {Tables.Count} table(s).";

            // spCanDelete is a SQL Server stored procedure from the original author's tooling; there is nothing to check on PostgreSQL.
            if (request.Provider == DatabaseProvider.SqlServer)
            {
                await using var probeConnection = request.CreateConnection();
                await probeConnection.OpenAsync();
                var (status, wasCached) = await SpCanDeleteVerifier.GetOrVerifyAsync(
                    probeConnection, request.ServerName, request.DatabaseName, _settings.SpCanDeleteVerificationConfigPath);
                if (!wasCached)
                {
                    StatusMessage += status == SpCanDeleteStatus.Verified
                        ? " spCanDelete verified on this database."
                        : " Note: spCanDelete was not found (or doesn't match the expected signature) on this database.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary> Reloads the table list (and re-collapses/reloads any expanded columns) from the
    /// connected database. Disabled via CanRefresh until a connection exists. </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        _databaseModel = null;
        if (_connectionRequest is not null)
            await ConnectAsync(_connectionRequest);
    }

    private bool CanRefresh() => IsConnected;

    partial void OnIsConnectedChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();

    private Task<List<ColumnSummary>> LoadColumnSummariesAsync(TableSummary table, CancellationToken cancellationToken)
    {
        if (_connectionRequest is null)
            return Task.FromResult(new List<ColumnSummary>());

        var schemaProvider = SchemaProviderFactory.Create(_connectionRequest, _settings.SpecialLogicColumnsConfigPath);
        return schemaProvider.ListColumnSummariesAsync(table.SchemaName, table.TableName, cancellationToken);
    }

    /// <summary> Templates applicable to this table's shape, for building its right-click menu (section 8). </summary>
    public List<TemplateInfo> GetApplicableTemplates(TableNodeViewModel table) =>
        TemplateCatalog.Discover(_settings.TemplatesDirectory)
            .Where(t => t.AppliesTo(table.Summary.HasPrimaryKey, table.Summary.IsJunctionTable, table.Summary.HasChildForeignKeys, table.Summary.PrimaryKeyShape, table.Summary.IsNameActiveTable,
                _connectionRequest?.Provider switch { DatabaseProvider.PostgreSql => SqlDialect.PostgreSql, DatabaseProvider.MySql => SqlDialect.MySql, DatabaseProvider.Sqlite => SqlDialect.Sqlite, _ => SqlDialect.SqlServer },
                table.Summary.IsAuditTable))
            .ToList();

    /// <summary> The shapes the table looks like that the chosen project does not name: <see cref="TableShapeDetector"/> on the table, and with a project chosen also the shapes that need the other tables
    /// (<see cref="DatabaseShapeDetector"/>: polymorphic link, snapshot, archive and the like) that point at this table. Empty when nothing is connected or the read fails. </summary>
    public async Task<IReadOnlyList<ShapeSuggestion>> DetectShapesAsync(TableNodeViewModel table)
    {
        if (_connectionRequest is null)
            return [];
        try
        {
            var project = LoadActiveProject();
            if (HasActiveProject)
            {
                var database = await GetDatabaseModelAsync(table.SchemaName);
                var found = ShapeMenu.Suggestions(database, table.TableName, project);
                if (database.Tables.Any(t => t.TableName == table.TableName || t.DbTableName == table.TableName))
                    return found;
            }
            var schemaProvider = SchemaProviderFactory.Create(_connectionRequest, _settings.SpecialLogicColumnsConfigPath, project.Naming, project.Acronyms, project.IgnoredColumns, project.MoneyColumns, project.NotMoneyColumns, project.KeyColumns);
            var model = await schemaProvider.BuildTableModelAsync(table.SchemaName, table.TableName, false, false);
            return TableShapeDetector.Of(model, project.ForServer(model.ServerVersion));
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary> Whether a project is chosen, so a shape can be added to its file. </summary>
    public bool HasActiveProject => _settings.Current.LastProject.Length > 0 && File.Exists(ProjectSettings.FilePathFor(_settings.ProjectsDirectory, _settings.Current.LastProject));

    /// <summary> Adds the suggestion's item to its setting in the chosen project's file; every other line of the file stays as it is. </summary>
    public void ApplyShape(ShapeSuggestion suggestion)
    {
        string path = ProjectSettings.FilePathFor(_settings.ProjectsDirectory, _settings.Current.LastProject);
        File.WriteAllText(path, ShapeMenu.Apply(File.ReadAllText(path), suggestion));
        StatusMessage = $"Added {suggestion.Item} to {suggestion.Setting} in {Path.GetFileName(path)}. Regenerate to use it.";
    }

    /// <summary> Adds the shape to the chosen project's file (<see cref="ApplyShape"/>), then regenerates the project's Stacks for the table and the templates that cover the whole database (where a shape's routes
    /// and checks are written); the other tables' own files wait for a project run. Files that did not change are not touched, and files a person edited are kept. </summary>
    public async Task ApplyShapeAndGenerateAsync(TableNodeViewModel table, ShapeSuggestion suggestion)
    {
        ApplyShape(suggestion);
        if (_connectionRequest is null)
            return;
        IsBusy = true;
        try
        {
            var project = LoadActiveProject();
            if (project.Stacks.Length == 0)
            {
                StatusMessage += " The project has no Stacks setting, so no files were written.";
                return;
            }
            var provider = SchemaProviderFactory.Create(_connectionRequest, _settings.SpecialLogicColumnsConfigPath, project.Naming, project.Acronyms, project.IgnoredColumns, project.MoneyColumns, project.NotMoneyColumns, project.KeyColumns);
            var options = new GenerateOptions
            {
                Project = project, Stacks = project.Stacks, OutputDirectory = _settings.OutputDirectory, DatabaseName = _connectionRequest.DatabaseName, Schema = table.SchemaName,
                Tables = [table.TableName], WithDatabaseTemplates = true
            };
            StatusMessage = $"Generating {string.Join(", ", project.Stacks)} with {table.TableName} as {suggestion.Shape.ToLowerInvariant()}...";
            var report = await Task.Run(() => ProjectGenerator.RunAsync(provider, _settings.TemplatesDirectory, options));
            int written = report.Count(FileOutcomeKind.Created) + report.Count(FileOutcomeKind.Updated);
            int kept = report.Count(FileOutcomeKind.Edited) + report.Count(FileOutcomeKind.Unknown);
            StatusMessage = report.Success
                ? $"Added {suggestion.Item} to {suggestion.Setting} and wrote {written} files" + (kept > 0 ? $"; kept {kept} you edited" : "") + $" for {table.TableName} and the database-wide files; regenerate the project to refresh the other tables."
                : "Added the setting, but generation failed: " + string.Join(" | ", report.Errors);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Added the setting, but generation failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private DatabaseModel? _databaseModel;
    private string? _databaseModelProject;

    /// <summary> The templates <see cref="GetApplicableTemplates"/> offers, less those the chosen project would write nothing for (<see cref="TemplateOffer"/>): a screen of a table the Screens setting leaves out, an OData page
    /// for a table that is not served as OData. The database is read once and kept until the project changes or the tables are refreshed; when it cannot be read, every template of the table's shape is offered. </summary>
    public async Task<List<TemplateInfo>> GetOfferedTemplatesAsync(TableNodeViewModel table)
    {
        var templates = GetApplicableTemplates(table);
        if (_connectionRequest is null || !HasActiveProject)
            return templates;
        try
        {
            var database = await GetDatabaseModelAsync(table.SchemaName);
            var project = LoadActiveProject().ForServer(database.ServerVersion);
            var model = database.Tables.FirstOrDefault(t => t.TableName == table.TableName || t.DbTableName == table.TableName);
            return model is null ? templates : [.. TemplateOffer.Filter(templates, model, database, project)];
        }
        catch (Exception)
        {
            return templates;
        }
    }

    /// <summary> The whole database as the chosen project reads it, kept until the project's file changes or the tables are refreshed. </summary>
    private async Task<DatabaseModel> GetDatabaseModelAsync(string schema)
    {
        var project = LoadActiveProject();
        string projectKey = _settings.Current.LastProject + "|" + schema + "|" + project.ToFileText().GetHashCode();
        if (_databaseModel is null || _databaseModelProject != projectKey)
        {
            var schemaProvider = SchemaProviderFactory.Create(_connectionRequest!, _settings.SpecialLogicColumnsConfigPath, project.Naming, project.Acronyms, project.IgnoredColumns, project.MoneyColumns, project.NotMoneyColumns, project.KeyColumns);
            _databaseModel = await schemaProvider.BuildAsync(_connectionRequest!.DatabaseName, schema);
            _databaseModelProject = projectKey;
        }
        return _databaseModel;
    }

    /// <summary> The templates that write one file for the whole database (CS_DbContext, API_Registration), for the toolbar's database menu. </summary>
    public List<TemplateInfo> GetDatabaseTemplates() =>
        TemplateCatalog.Discover(_settings.TemplatesDirectory).Where(t => t.Config.DatabaseOnly).ToList();

    /// <summary> Runs a database-level template over every table of the connected database's main schema (the one most tables are in) and
    /// writes its file under the output folder. Returns the first file written, or null (StatusMessage says why). </summary>
    public async Task<string?> RunDatabaseTemplateAsync(TemplateInfo template)
    {
        if (_connectionRequest is null || MainSchema is not { } schema)
            return null;

        IsBusy = true;
        StatusMessage = $"Generating '{template.Name}' for every table of [{schema}]...";
        try
        {
            var project = LoadActiveProject();
            var schemaProvider = SchemaProviderFactory.Create(_connectionRequest, _settings.SpecialLogicColumnsConfigPath, project.Naming, project.Acronyms, project.IgnoredColumns, project.MoneyColumns, project.NotMoneyColumns, project.KeyColumns);
            var database = await schemaProvider.BuildAsync(_connectionRequest.DatabaseName, schema);

            var result = await TemplateRunner.RunAsync(template.FilePath, database, project.ForServer(database.ServerVersion));
            if (!result.Success)
            {
                StatusMessage = "Template generation failed: " + string.Join(" | ", result.Errors);
                return null;
            }

            LastOutputFiles = await GeneratedFiles.WriteAsync(_settings.OutputDirectory, template, project.ContextName ?? database.DatabaseName + "Context", result.GeneratedText!);
            StatusMessage = LastOutputFiles.Count == 1
                ? $"Done. Wrote {LastOutputFiles[0]}"
                : $"Done. Wrote {LastOutputFiles.Count} files under {_settings.OutputDirectory}";
            return LastOutputFiles[0];
        }
        catch (Exception ex)
        {
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary> The view model of the Generate All dialog for the connected database (its main schema: the one most tables are in), or null when nothing is connected. </summary>
    public GenerateAllDialogViewModel? CreateGenerateAllViewModel(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        if (_connectionRequest is not { } request || MainSchema is not { } schema)
            return null;
        var dialect = request.Provider switch { DatabaseProvider.PostgreSql => SqlDialect.PostgreSql, DatabaseProvider.MySql => SqlDialect.MySql, DatabaseProvider.Sqlite => SqlDialect.Sqlite, _ => SqlDialect.SqlServer };
        bool isFile = request.Provider == DatabaseProvider.Sqlite;
        return new GenerateAllDialogViewModel(_settings.ProjectsDirectory, _settings.TemplatesDirectory, _settings.OutputDirectory,
            project => SchemaProviderFactory.Create(request, _settings.SpecialLogicColumnsConfigPath, project.Naming, project.Acronyms, project.IgnoredColumns, project.MoneyColumns, project.NotMoneyColumns, project.KeyColumns),
            schema, request.DatabaseName, dialect, isFile ? null : request.ServerName, isFile || request.AuthMode == AuthMode.WindowsAuth ? null : request.UserName, dispatcher);
    }

    /// <summary> Every file the last successful RunTemplateAsync wrote (most templates write one; the TS_ templates several). </summary>
    public IReadOnlyList<string> LastOutputFiles { get; private set; } = [];

    /// <summary> The project chosen on the Project Settings screen, or ProjectSettings.None (each template's own values)
    /// when none is chosen or its file has since gone. </summary>
    private ProjectSettings LoadActiveProject()
    {
        string name = _settings.Current.LastProject;
        if (name.Length == 0)
            return ProjectSettings.None;
        try
        {
            return ProjectSettings.LoadNamed(_settings.ProjectsDirectory, name);
        }
        catch (FileNotFoundException)
        {
            return ProjectSettings.None;
        }
    }

    public async Task<string?> RunTemplateAsync(TableNodeViewModel table, TemplateInfo template)
    {
        if (_connectionRequest is null)
            return null;

        IsBusy = true;
        StatusMessage = $"Generating '{template.Name}' for [{table.SchemaName}].[{table.TableName}]...";
        try
        {
            // The project's NamingStyle decides the names the schema reader gives tables and columns (customer_item -> CustomerItem).
            var project = LoadActiveProject();
            var schemaProvider = SchemaProviderFactory.Create(_connectionRequest, _settings.SpecialLogicColumnsConfigPath, project.Naming, project.Acronyms, project.IgnoredColumns, project.MoneyColumns, project.NotMoneyColumns, project.KeyColumns);
            var model = await schemaProvider.BuildTableModelAsync(
                table.SchemaName, table.TableName, template.Config.NeedsRowData, template.Config.NeedsReferencedDisplayColumns);

            // a table that looks like a log is listed in LogTables for this run when the person chose the log page or its index script
            var result = await TemplateRunner.RunAsync(template.FilePath, model, LogTablePlan.ForMenuRun(project.ForServer(model.ServerVersion), template.Name, model));
            if (!result.Success)
            {
                StatusMessage = "Template generation failed: " + string.Join(" | ", result.Errors);
                return null;
            }

            LastOutputFiles = await GeneratedFiles.WriteAsync(_settings.OutputDirectory, template, model.TableName, result.GeneratedText!);

            StatusMessage = LastOutputFiles.Count == 1
                ? $"Done. Wrote {LastOutputFiles[0]}"
                : $"Done. Wrote {LastOutputFiles.Count} files under {_settings.OutputDirectory}";
            return LastOutputFiles[0];
        }
        catch (Exception ex)
        {
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
