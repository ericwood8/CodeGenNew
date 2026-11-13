using System.Text;

namespace CodeGenNew.Core;

/// <summary> What the edit dialog of an asset table (<c>AssetTables</c>) adds in the desktop front ends, WinUI3 and Uno: an Upload button on a new row (the file makes the row and the dialog closes, so the list shows it),
/// a Download button on a saved row and the picture of a saved row whose file is an image. A WinUI3 dialog reads the file through the table's <c>&lt;Table&gt;Assets</c> class, a Uno dialog through the API client. </summary>
public static class AssetDialogCode
{
    /// <summary> The buttons and the picture, written after the error bar (<paramref name="indent"/> spaces in), each line after a line end so a template can append it to a line. </summary>
    public static string Xaml(string indent)
    {
        var o = new StringBuilder();
        o.Append('\n').Append(indent).Append("<StackPanel Orientation=\"Horizontal\" Spacing=\"8\">");
        o.Append('\n').Append(indent).Append($"    <Button Content=\"{AssetScreens.UploadCaption}\" Click=\"OnDialogUploadClick\" Visibility=\"{{x:Bind ViewModel.UploadVisibility, Mode=OneWay}}\" />");
        o.Append('\n').Append(indent).Append($"    <Button Content=\"{AssetScreens.DownloadCaption}\" Click=\"OnDialogDownloadClick\" Visibility=\"{{x:Bind ViewModel.DownloadVisibility, Mode=OneWay}}\" />");
        o.Append('\n').Append(indent).Append($"    <Image Source=\"{{x:Bind ViewModel.Picture, Mode=OneWay}}\" MaxWidth=\"{AssetScreens.PictureEdge}\" MaxHeight=\"{AssetScreens.PictureEdge}\" />");
        o.Append('\n').Append(indent).Append("</StackPanel>");
        return o.ToString();
    }

    /// <summary> The click handlers of the dialog's code-behind (four spaces in), after a blank line and the last without a line end. A WinUI3 dialog takes the window of the picker from the active window, a Uno dialog from <c>App.MainWindow</c>. </summary>
    public static string Handlers(bool uno)
    {
        var o = new StringBuilder();
        string Owner(string indent) => uno
            ? $"#if WINDOWS\n{indent}if (App.MainWindow is {{ }} window)\n{indent}    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));\n#endif\n"
            : $"{indent}WinRT.Interop.InitializeWithWindow.Initialize(picker, GetActiveWindow());\n";
        o.Append("\n\n    private async void OnDialogUploadClick(object sender, RoutedEventArgs e)\n    {\n");
        o.Append("        var picker = new Windows.Storage.Pickers.FileOpenPicker();\n").Append(Owner("        "));
        o.Append("        picker.FileTypeFilter.Add(\"*\");\n        var file = await picker.PickSingleFileAsync();\n        if (file is null)\n            return;\n");
        if (uno)
            o.Append("        await using var content = (await file.OpenReadAsync()).AsStreamForRead();\n        if (await ViewModel.UploadAsync(content, file.Name, file.ContentType))\n            Hide();\n");
        else
            o.Append("        if (await ViewModel.UploadAsync(file.Path, file.ContentType))\n            Hide();\n");
        o.Append("    }\n\n    private async void OnDialogDownloadClick(object sender, RoutedEventArgs e)\n    {\n");
        if (uno)
        {
            o.Append("        if (await ViewModel.DownloadAsync() is not { } downloaded)\n            return;\n");
            o.Append("        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(downloaded.FileName) };\n").Append(Owner("        "));
            o.Append("        string extension = Path.GetExtension(downloaded.FileName);\n        picker.FileTypeChoices.Add(\"File\", [extension.Length > 1 ? extension : \".bin\"]);\n");
            o.Append("        var target = await picker.PickSaveFileAsync();\n        if (target is not null)\n            await Windows.Storage.FileIO.WriteBytesAsync(target, downloaded.Content);\n");
        }
        else
        {
            o.Append("        var picker = new Windows.Storage.Pickers.FileSavePicker();\n").Append(Owner("        "));
            o.Append("        picker.FileTypeChoices.Add(\"File\", new List<string> { \".\" });\n        var target = await picker.PickSaveFileAsync();\n        if (target is not null)\n            await ViewModel.ExportAsync(target.Path);\n");
        }
        o.Append("    }");
        if (!uno)
            o.Append("\n\n    [System.Runtime.InteropServices.DllImport(\"user32.dll\")]\n    private static extern IntPtr GetActiveWindow();");
        return o.ToString();
    }

    /// <summary> The view-model members of the dialog (four spaces in, a line end after the last). <paramref name="clientExpression"/> is the Uno dialog's API client, or null for a WinUI3 dialog, which uses <c>&lt;table&gt;Assets</c>. </summary>
    public static string Members(TableModel table, AssetShape asset, string? clientExpression)
    {
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s.Length == 0 ? "" : "    " + s).Append('\n');
        string key = asset.Key.CSharpName();
        string storageKey = asset.StorageKey.CSharpName();
        string mime = asset.MimeType.CSharpName();
        L("/// <summary> The Upload button shows on a new row, the Download button and the picture on a saved one. </summary>");
        L("public Microsoft.UI.Xaml.Visibility UploadVisibility => _editing is null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;");
        L();
        L("public Microsoft.UI.Xaml.Visibility DownloadVisibility => _editing is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;");
        L();
        L("[ObservableProperty]");
        L("private Microsoft.UI.Xaml.Media.Imaging.BitmapImage? _picture;   // the picture of the saved row, when its file is an image");
        L();
        if (clientExpression is null)
        {
            L("/// <summary> Copies the chosen file into the store and adds its row; true when it worked (the dialog then closes and the list shows the row). </summary>");
            L("public async Task<bool> UploadAsync(string path, string? contentType)");
            L("{");
            L("    ErrorMessage = null;");
            L($"    ErrorMessage = await {table.TableName}Assets.UploadAsync(_context, path, contentType);");
            L("    return ErrorMessage is null;");
            L("}");
            L();
            L("/// <summary> Writes the file of the saved row to the place the person chose. </summary>");
            L("public async Task ExportAsync(string destination)");
            L("{");
            L("    if (_editing is null)");
            L("        return;");
            L("    string? problem;");
            L("    await using (var target = System.IO.File.Create(destination))");
            L($"        problem = await {table.TableName}Assets.CopyFileAsync(_editing.{storageKey}, target);");
            L("    ErrorMessage = problem;");
            L("    if (problem is not null)");
            L("        System.IO.File.Delete(destination);");
            L("}");
            L();
            L("private async Task<byte[]?> ReadFileAsync()");
            L("{");
            L("    using var memory = new System.IO.MemoryStream();");
            L($"    return await {table.TableName}Assets.CopyFileAsync(_editing!.{storageKey}, memory) is null ? memory.ToArray() : null;");
            L("}");
        }
        else
        {
            L("/// <summary> The chosen file is sent to the upload route (the server makes the storage key and the row); true when it worked (the dialog then closes and the list shows the row). </summary>");
            L("public async Task<bool> UploadAsync(System.IO.Stream content, string fileName, string? contentType)");
            L("{");
            L("    ErrorMessage = null;");
            L("    try");
            L("    {");
            L($"        await {clientExpression}.UploadAsync(content, fileName, contentType);");
            L("        return true;");
            L("    }");
            L("    catch (ApiException ex)");
            L("    {");
            L("        ErrorMessage = ex.Explain(\"uploading\");");
            L("    }");
            L("    catch (System.Net.Http.HttpRequestException)");
            L("    {");
            L("        ErrorMessage = \"The server could not be reached.\";");
            L("    }");
            L("    return false;");
            L("}");
            L();
            L("/// <summary> The file of the saved row with the name and type the server gives it, or null (with the reason in ErrorMessage) when it cannot be fetched. </summary>");
            L("public async Task<(byte[] Content, string FileName, string ContentType)?> DownloadAsync()");
            L("{");
            L("    ErrorMessage = null;");
            L("    if (_editing is null)");
            L("        return null;");
            L("    try");
            L("    {");
            L($"        return await {clientExpression}.DownloadAsync(_editing.{key});");
            L("    }");
            L("    catch (ApiException ex)");
            L("    {");
            L("        ErrorMessage = ex.Explain(\"downloading\");");
            L("    }");
            L("    catch (System.Net.Http.HttpRequestException)");
            L("    {");
            L("        ErrorMessage = \"The server could not be reached.\";");
            L("    }");
            L("    return null;");
            L("}");
            L();
            L("private async Task<byte[]?> ReadFileAsync()");
            L("{");
            L("    try");
            L("    {");
            L($"        return (await {clientExpression}.DownloadAsync(_editing!.{key})).Content;");
            L("    }");
            L("    catch (ApiException)");
            L("    {");
            L("        return null;");
            L("    }");
            L("    catch (System.Net.Http.HttpRequestException)");
            L("    {");
            L("        return null;");
            L("    }");
            L("}");
        }
        L();
        L("/// <summary> Reads the file of a saved row that holds an image and shows it as the picture; any other row shows none. </summary>");
        L("private async Task LoadPictureAsync()");
        L("{");
        L($"    if (_editing is null || _editing.{mime} is not {{ }} type || !type.StartsWith(\"image/\", StringComparison.OrdinalIgnoreCase) || type.StartsWith(\"image/svg\", StringComparison.OrdinalIgnoreCase))");
        L("        return;");
        L("    if (await ReadFileAsync() is not { Length: > 0 } bytes)");
        L("        return;");
        L("    try");
        L("    {");
        L("        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();");
        L("        using (var writer = new Windows.Storage.Streams.DataWriter(stream))");
        L("        {");
        L("            writer.WriteBytes(bytes);");
        L("            await writer.StoreAsync();");
        L("            writer.DetachStream();");
        L("        }");
        L("        stream.Seek(0);");
        L("        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = " + AssetScreens.PictureEdge + " };");
        L("        await bitmap.SetSourceAsync(stream);");
        L("        Picture = bitmap;");
        L("    }");
        L("    catch (Exception)");
        L("    {");
        L("        Picture = null;   // a file that is not a picture the app can draw shows none");
        L("    }");
        L("}");
        L();
        return o.ToString();
    }

    /// <summary> The line the view-model's constructor runs for a saved row (twelve spaces in): the picture is read without holding the dialog up. </summary>
    public const string ConstructorLine = "            _ = LoadPictureAsync();\n";
}
