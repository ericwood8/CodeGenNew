using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The screens of an asset table (AssetTables): an Upload button in the bar and a Download button on a row, with the client calls behind them, in every front end. </summary>
[TestClass]
public class AssetScreenTests
{
    private static string CreateDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_asset_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        string[] statements =
        [
            "CREATE TABLE Asset (AssetId INTEGER PRIMARY KEY AUTOINCREMENT, FileName VARCHAR(200) NOT NULL, MimeType VARCHAR(100) NOT NULL, SizeBytes BIGINT NOT NULL, StorageKey VARCHAR(300) NOT NULL, PublicUrl VARCHAR(500) NULL, UploadedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP, Caption VARCHAR(200) NULL)",
            "CREATE UNIQUE INDEX UX_Asset_StorageKey ON Asset (StorageKey)"
        ];
        foreach (string statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static async Task<(GenerateReport Report, Dictionary<string, string> Files)> GenerateAsync(string stacks, params (string Key, string Value)[] extra)
    {
        string database = CreateDatabase();
        string output = Path.Combine(Path.GetTempPath(), "codegen_asset_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stacks, ["Dashboard"] = "false", ["AccessMode"] = "Ef",
                ["AssetTables"] = "Asset:StorageKey:FileName:MimeType:SizeBytes:PublicUrl", ["AssetAllowedTypes"] = "text/*,image/png", ["Screens"] = "Asset", ["AngularVersion"] = "22"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = stacks.Split(","), OutputDirectory = output, DatabaseName = database, Schema = "main"
            });
            var files = Directory.Exists(output)
                ? Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"))
                : [];
            return (report, files);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    private static string FileEnding(Dictionary<string, string> files, string ending) => files.Single(f => f.Key.EndsWith(ending)).Value;

    [TestMethod]
    public async Task React_has_an_upload_button_a_download_button_and_the_calls_behind_them()
    {
        var (report, files) = await GenerateAsync("Api,React");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string page = FileEnding(files, "AssetPage.tsx");
        Expect.Contains(page, "<input ref={uploadInput} type=\"file\" accept=\"text/*,image/png\" hidden");
        Expect.Contains(page, "<button type=\"button\" onClick={() => uploadInput.current?.click()}>Upload</button>");
        Expect.Contains(page, "await assetApi.upload(file);");
        Expect.Contains(page, "setError('Problem while uploading!');");
        Expect.Contains(page, "<button type=\"button\" onClick={() => void download(asset.assetId!, asset.fileName)}>Download</button>");
        Expect.Contains(page, "setError('Problem while downloading!');");
        string client = FileEnding(files, "assetApi.ts");
        Expect.Contains(client, "import { downloadFile, lookupQuery, request, uploadFile } from './client';");
        Expect.Contains(client, "upload: (file: File) => uploadFile<Asset>(`${apiUrl}/upload`, file),");
        Expect.Contains(client, "download: (id: number, fileName: string) => downloadFile(`${apiUrl}/${id}/download`, fileName),");
    }

    [TestMethod]
    public async Task Angular_has_an_upload_button_a_download_button_and_the_service_calls_behind_them()
    {
        var (report, files) = await GenerateAsync("Api,Angular");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string html = FileEnding(files, "asset.component.html");
        Expect.Contains(html, "<input #uploadInput type=\"file\" accept=\"text/*,image/png\" hidden (change)=\"upload($event)\" />");
        Expect.Contains(html, "(click)=\"uploadInput.click()\" class=\"btn\">Upload</button>");
        Expect.Contains(html, "<button (click)=\"download(asset.assetId!, asset.fileName)\" class=\"btn-action\">Download</button>");
        string code = FileEnding(files, "asset.component.ts");
        Expect.Contains(code, "this.service.upload(file).subscribe({ next: () => this.load(), error: (error) => this.showError('uploading', error) });");
        Expect.Contains(code, "this.service.download(id, fileName).subscribe({ error: (error) => this.showError('downloading', error) });");
        string service = FileEnding(files, "asset.service.ts");
        Expect.Contains(service, "import { Observable, map } from 'rxjs';");
        Expect.Contains(service, "upload(file: File): Observable<Asset> {");
        Expect.Contains(service, "return this.http.post<Asset>(`${this.apiUrl}/upload`, form);");
        Expect.Contains(service, "download(id: number, fallbackName: string): Observable<void> {");
    }

    [TestMethod]
    public async Task Blazor_has_an_upload_box_a_download_button_and_the_script_that_saves_the_file()
    {
        var (report, files) = await GenerateAsync("Api,Blazor");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string page = FileEnding(files, "AssetPage.razor");
        Expect.Contains(page, "<label>Upload <InputFile OnChange=\"UploadAsync\" accept=\"text/*,image/png\" /></label>");
        Expect.Contains(page, "<button type=\"button\" @onclick=\"() => DownloadAsync(item)\">Download</button>");
        Expect.Contains(page, "await using var content = e.File.OpenReadStream(10485760);");
        Expect.Contains(page, "await Client.UploadAsync(content, e.File.Name, e.File.ContentType);");
        Expect.Contains(page, "await Js.InvokeVoidAsync(\"codegenSaveFile\", name, type, reference);");
        Expect.Contains(page, "message = \"Problem while uploading!\";");
        string client = FileEnding(files, "AssetClient.cs");
        Expect.Contains(client, "public async Task<Asset> UploadAsync(Stream content, string fileName, string? contentType)");
        Expect.Contains(client, "public async Task<(byte[] Content, string FileName, string ContentType)> DownloadAsync(int id)");
    }

    [TestMethod]
    public async Task Uno_has_an_upload_button_a_download_button_and_the_pickers_behind_them()
    {
        var (report, files) = await GenerateAsync("Api,Uno");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string xaml = FileEnding(files, "AssetListPage.xaml");
        Expect.Contains(xaml, "<Button Content=\"Upload\" FontWeight=\"Bold\"");
        Expect.Contains(xaml, "Click=\"OnUploadClick\"");
        Expect.Contains(xaml, "<Button Content=\"Download\" FontWeight=\"Bold\"");
        Expect.Contains(xaml, "Click=\"OnDownloadClick\"");
        string code = FileEnding(files, "AssetListPage.xaml.cs");
        Expect.Contains(code, "private async void OnUploadClick(object sender, RoutedEventArgs e)");
        Expect.Contains(code, "new Windows.Storage.Pickers.FileOpenPicker()");
        Expect.Contains(code, "await ViewModel.UploadAsync(content, file.Name, file.ContentType);");
        Expect.Contains(code, "new Windows.Storage.Pickers.FileSavePicker");
        Expect.Contains(code, "await Windows.Storage.FileIO.WriteBytesAsync(target, downloaded.Content);");
        string model = FileEnding(files, "AssetListViewModel.cs");
        Expect.Contains(model, "public async Task UploadAsync(Stream content, string fileName, string? contentType)");
        Expect.Contains(model, "public async Task<(byte[] Content, string FileName, string ContentType)?> DownloadAsync(int id)");
    }

    [TestMethod]
    public async Task WinUI3_has_an_upload_button_a_download_button_and_the_rules_the_dialogs_call()
    {
        var (report, files) = await GenerateAsync("WinUI3");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string xaml = FileEnding(files, "AssetListPage.xaml");
        Expect.Contains(xaml, "Click=\"OnUploadClick\"");
        Expect.Contains(xaml, "Click=\"OnDownloadClick\"");
        string code = FileEnding(files, "AssetListPage.xaml.cs");
        Expect.Contains(code, "await ViewModel.UploadAsync(file.Path, file.ContentType);");
        Expect.Contains(code, "await ViewModel.ExportAsync(id, target.Path);");
        string model = FileEnding(files, "AssetListViewModel.cs");
        Expect.Contains(model, "await AssetAssets.RememberFileAsync(_context, id);");
        Expect.Contains(model, "await AssetAssets.AfterDeleteAsync(_context);");
        Expect.Contains(model, "string? refused = await AssetAssets.UploadAsync(_context, path, contentType);");
        string dialog = FileEnding(files, "AssetDetailViewModel.cs");
        Expect.Contains(dialog, "await AssetAssets.PrepareAsync(_context, entity, _editing is null) is { } assetRefusal");
        string assets = FileEnding(files, "Assets.cs");
        Expect.Contains(assets, "public static class AssetAssets");
        Expect.DoesNotContain(assets, "IResult");
    }

    [TestMethod]
    public async Task A_table_that_is_not_an_asset_table_gets_none_of_it()
    {
        var (report, files) = await GenerateAsync("Api,React,Angular,Blazor", ("AssetTables", "Nothing"));
        Assert.IsFalse(report.Success && files.Values.Any(f => f.Contains("uploadInput") || f.Contains("codegenSaveFile")));
    }
}
