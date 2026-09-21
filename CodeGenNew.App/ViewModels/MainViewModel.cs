using System.Collections.ObjectModel;
using CodeGenNew.App.Services;
using CodeGenNew.Connections;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CodeGenNew.App.ViewModels;

/// <summary> The main screen's ViewModel (Docs/specs.md section 9.4): the TreeView's data, the current
/// connection, and running a template against the selected table. CodeGenNew never writes to the target
/// database itself (section 1.1) -- SqlServerSchemaProvider only ever reads schema metadata here. </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppSettingsService _settings;
    private ConnectionRequest? _connectionRequest;

    public IconProvider Icons { get; }
    public ObservableCollection<TableNodeViewModel> Tables { get; } = [];

    [ObservableProperty]
    private string _databaseLabel = "(not connected)";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Connect to a database to get started.";

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
            var schemaProvider = new SqlServerSchemaProvider(request, _settings.SpecialLogicColumnsConfigPath);
            var summaries = await schemaProvider.ListTablesAsync();

            _connectionRequest = request;
            DatabaseLabel = $"{request.ServerName} \\ {request.DatabaseName}";
            IsConnected = true;

            Tables.Clear();
            foreach (var summary in summaries)
                Tables.Add(new TableNodeViewModel(summary, Icons, LoadColumnSummariesAsync));

            StatusMessage = $"Loaded {Tables.Count} table(s).";

            await using var probeConnection = SqlServerConnectionFactory.CreateConnection(request);
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
        if (_connectionRequest is not null)
            await ConnectAsync(_connectionRequest);
    }

    private bool CanRefresh() => IsConnected;

    partial void OnIsConnectedChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();

    private Task<List<ColumnSummary>> LoadColumnSummariesAsync(TableSummary table, CancellationToken cancellationToken)
    {
        if (_connectionRequest is null)
            return Task.FromResult(new List<ColumnSummary>());

        var schemaProvider = new SqlServerSchemaProvider(_connectionRequest, _settings.SpecialLogicColumnsConfigPath);
        return schemaProvider.ListColumnSummariesAsync(table.SchemaName, table.TableName, cancellationToken);
    }

    /// <summary> Templates applicable to this table's shape, for building its right-click menu (section 8). </summary>
    public List<TemplateInfo> GetApplicableTemplates(TableNodeViewModel table) =>
        TemplateCatalog.Discover(_settings.TemplatesDirectory)
            .Where(t => t.AppliesTo(table.Summary.HasPrimaryKey, isView: false))
            .ToList();

    /// <summary> Every file the last successful RunTemplateAsync wrote (most templates write one; the TS_ templates several). </summary>
    public IReadOnlyList<string> LastOutputFiles { get; private set; } = [];

    public async Task<string?> RunTemplateAsync(TableNodeViewModel table, TemplateInfo template)
    {
        if (_connectionRequest is null)
            return null;

        IsBusy = true;
        StatusMessage = $"Generating '{template.Name}' for [{table.SchemaName}].[{table.TableName}]...";
        try
        {
            var schemaProvider = new SqlServerSchemaProvider(_connectionRequest, _settings.SpecialLogicColumnsConfigPath);
            var model = await schemaProvider.BuildTableModelAsync(
                table.SchemaName, table.TableName, template.Config.NeedsRowData, template.Config.NeedsReferencedDisplayColumns);

            var result = await TemplateRunner.RunAsync(template.FilePath, model);
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
