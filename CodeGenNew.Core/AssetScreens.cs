namespace CodeGenNew.Core;

/// <summary> What the screens of an asset table (<c>AssetTables</c>) add to the ordinary list: an Upload button in the toolbar that sends a chosen file to <c>POST upload</c>, a Download button on a row that saves the file
/// of <c>GET {id}/download</c>, and a form that shows the storage key, the type and the download address as text because the upload route owns them. The words are here so every front end and its tests agree. </summary>
public static class AssetScreens
{
    public const string UploadCaption = "Upload";
    public const string DownloadCaption = "Download";
    public const string UploadFailed = "Problem while uploading!";
    public const string DownloadFailed = "Problem while downloading!";

    /// <summary> The name a downloaded file is saved under when the server names none. </summary>
    public const string FallbackFileName = "file";

    /// <summary> The method names a front end's client uses, <c>upload</c> (TypeScript) or <c>UploadAsync</c> (C#). </summary>
    public const string UploadRoute = "upload";
    public const string DownloadRoute = "download";

    /// <summary> The client method that reads the file of a row as bytes, for the picture a dialog shows. </summary>
    public const string PictureRoute = "picture";

    /// <summary> The largest edge, in pixels, of the picture a dialog shows. </summary>
    public const int PictureEdge = 160;

    /// <summary> The file types the browser's file box offers: the project's <c>AssetAllowedTypes</c> as an <c>accept</c> value (<c>image/*,application/pdf</c>), or empty for any file. </summary>
    public static string Accept(ProjectSettings project) => string.Join(",", AssetPlan.AllowedTypes(project));

    /// <summary> Whether the column is one the upload route owns (the storage key, the file type and the download address), which the form shows as text and a save cannot change. The size is a number the server also owns, but
    /// a number box has nothing to say "set by the upload", so it stays an ordinary box and the server ignores a change. </summary>
    public static bool IsUploadColumn(TableModel table, ProjectSettings project, ColumnModel column) =>
        TableShapes.For(table, project).Asset is { } asset
        && (asset.StorageKey.Name.EqualsIgnoreCase(column.Name) || asset.MimeType.Name.EqualsIgnoreCase(column.Name) || (asset.Url is not null && asset.Url.Name.EqualsIgnoreCase(column.Name)));
}
