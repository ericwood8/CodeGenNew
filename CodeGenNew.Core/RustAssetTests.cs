namespace CodeGenNew.Core;

/// <summary> <c>tests/api/asset_routes.rs</c> of the Rust API (RS_TestAssets): an upload, a download and a delete through the router, the refusals (no file, too large, a type that is not allowed) and the storage key
/// that an ordinary save cannot name. The files go to a temporary folder (ASSET_STORE_FOLDER, set once before the first request). The same cases and words as the Python tests (<see cref="AssetWords"/>). </summary>
public static class RustAssetTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && RustAssetWriter.Shapes(database, project).Count > 0;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var asset = RustAssetWriter.Shapes(database, project)[0];
        var main = RustTable.Of(asset.Table, project);
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Json(ColumnModel c) => main.FieldOf(c).Json;
        var allowed = AssetPlan.AllowedTypes(project);
        string sampleType = allowed.Length == 0 ? "text/plain" : allowed[0].Replace("/*", "/x-sample");
        var fixedColumns = new[] { asset.Key, asset.StorageKey, asset.FileName, asset.MimeType, asset.Size }.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string others = string.Concat(asset.Table.Columns.Where(c => !fixedColumns.Contains(c.Name) && !c.IsNullable && !c.IsIdentity && !c.IsComputed)
            .Select(c => $", {Str(main.FieldOf(c).Json)}: {c.ValidExample(project, asset.Table.TableName)}"));
        string tooLarge = project.AssetMaxBytes > 2_000_000 ? "" : """"

#[tokio::test]
async fn a_file_over_the_limit_is_refused() {
    let Some(api) = start().await else { return };
    let big = api.post_text(&format!("{ROUTE}/upload"), &content_type(), form("big.bin", "§TYPE", &"x".repeat(§MAX + 1))).await;
    assert_eq!(big.status.as_u16(), 413, "{}", big.body);
    assert_eq!(big.json()["type"], "§T_LARGE");
}
"""".Replace("§MAX", project.AssetMaxBytes.ToString()).Replace("§TYPE", sampleType).Replace("§T_LARGE", AssetWords.TooLargeType);
        string notAllowed = allowed.Length == 0 ? "" : """"

#[tokio::test]
async fn a_type_that_is_not_allowed_is_refused() {
    let Some(api) = start().await else { return };
    let wrong = api.post_text(&format!("{ROUTE}/upload"), &content_type(), form("x.bin", "application/x-never-allowed", "abc")).await;
    assert_eq!(wrong.status.as_u16(), 415, "{}", wrong.body);
    assert_eq!(wrong.json()["type"], "§T_TYPE");
}
"""".Replace("§T_TYPE", AssetWords.TypeNotAllowedType);
        return TemplateText.FillAll(Text,
            ("§ROUTE", Str(main.Route)), ("§TYPE", sampleType), ("§ID", Json(asset.Key)), ("§NAME", Json(asset.FileName)), ("§MIME", Json(asset.MimeType)), ("§SIZE", Json(asset.Size)), ("§KEY", Json(asset.StorageKey)),
            ("§OTHERS", others), ("§UNATTACHED", AssetWords.UnattachedPrefix), ("§T_NOFILE", AssetWords.NoFileType), ("§T_MISSING", AssetWords.FileMissingType),
            ("§TOOLARGE", tooLarge), ("§NOTALLOWED", notAllowed)) + "\n";
    }

    private const string Text = """"
// The asset routes through the router: an upload, a download and a delete, the refusals, and a storage key an ordinary save cannot name. Written by RS_TestAssets; regenerated with the tables.
use std::sync::Once;

use serde_json::json;

use crate::test_support::*;

const ROUTE: &str = §ROUTE;
const BOUNDARY: &str = "----assettestboundary";

static FOLDER: Once = Once::new();

/// The files of these tests go to a folder of their own (the store reads ASSET_STORE_FOLDER the first time it is used).
fn store_folder() -> std::path::PathBuf {
    let folder = std::env::temp_dir().join(format!("assets_test_{}", std::process::id()));
    FOLDER.call_once(|| {
        let _ = std::fs::create_dir_all(&folder);
        // SAFETY: set once, before any request of these tests reads it, and nothing else in this test program changes the environment
        unsafe { std::env::set_var("ASSET_STORE_FOLDER", &folder) };
    });
    folder
}

async fn start() -> Option<Client> {
    store_folder();
    Client::start().await
}

fn content_type() -> String {
    format!("multipart/form-data; boundary={BOUNDARY}")
}

fn form(file_name: &str, mime: &str, content: &str) -> String {
    format!("--{BOUNDARY}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{file_name}\"\r\nContent-Type: {mime}\r\n\r\n{content}\r\n--{BOUNDARY}--\r\n")
}

#[tokio::test]
async fn an_uploaded_file_can_be_downloaded_and_a_delete_removes_it() {
    let Some(api) = start().await else { return };
    let uploaded = api.post_text(&format!("{ROUTE}/upload"), &content_type(), form("hello.txt", "§TYPE", "asset sample")).await;
    assert_eq!(uploaded.status.as_u16(), 201, "{}", uploaded.body);
    let row = uploaded.json();
    assert_eq!(row["§NAME"], "hello.txt");
    assert_eq!(row["§SIZE"], 12);
    assert_eq!(row["§MIME"], "§TYPE");
    let key = row["§KEY"].as_str().expect("a key").to_string();
    assert!(!key.starts_with("§UNATTACHED") && !key.contains('/') && !key.contains('\\'), "the server made the key");
    let address = format!("{ROUTE}/{}", row["§ID"]);
    let download = api.get(&format!("{address}/download")).await;
    assert_eq!(download.status.as_u16(), 200, "{}", download.body);
    assert_eq!(download.body, "asset sample");
    assert!(store_folder().join(&key).is_file(), "the file is in the store");
    let deleted = api.delete(&address).await;
    assert!(deleted.status.is_success(), "{}", deleted.body);
    assert_eq!(api.get(&format!("{address}/download")).await.status.as_u16(), 404);
    assert!(!store_folder().join(&key).exists(), "the delete removed the file");
}

#[tokio::test]
async fn an_upload_without_a_file_is_refused() {
    let Some(api) = start().await else { return };
    let none = api.post_text(&format!("{ROUTE}/upload"), &content_type(), format!("--{BOUNDARY}\r\nContent-Disposition: form-data; name=\"note\"\r\n\r\nx\r\n--{BOUNDARY}--\r\n")).await;
    assert_eq!(none.status.as_u16(), 400, "{}", none.body);
    assert_eq!(none.json()["type"], "§T_NOFILE");
    let empty = api.post_text(&format!("{ROUTE}/upload"), &content_type(), form("empty.txt", "§TYPE", "")).await;
    assert_eq!(empty.status.as_u16(), 400, "{}", empty.body);
}
§TOOLARGE§NOTALLOWED

#[tokio::test]
async fn the_storage_key_cannot_be_named_by_a_save_and_a_row_with_no_upload_has_no_file() {
    let Some(api) = start().await else { return };
    let uploaded = api.post_text(&format!("{ROUTE}/upload"), &content_type(), form("k.txt", "§TYPE", "abc")).await;
    assert_eq!(uploaded.status.as_u16(), 201, "{}", uploaded.body);
    let row = uploaded.json();
    let address = format!("{ROUTE}/{}", row["§ID"]);
    let mut body = row.clone();
    body["§KEY"] = json!("other");
    body["§SIZE"] = json!(99);
    body["§MIME"] = json!("x/y");
    body["§NAME"] = json!("renamed.txt");
    let edited = api.put(&address, &body).await;
    assert_eq!(edited.status.as_u16(), 200, "{}", edited.body);
    let stored = api.get(&address).await.json();
    assert_eq!(stored["§KEY"], row["§KEY"], "a save keeps the stored key");
    assert_eq!(stored["§SIZE"], 3);
    assert_eq!(stored["§MIME"], "§TYPE");
    assert_eq!(stored["§NAME"], "renamed.txt", "the name may be changed");
    api.delete(&address).await;

    let bare = api.post(ROUTE, &json!({ "§NAME": "bare.txt", "§MIME": "text/plain", "§SIZE": 1, "§KEY": "../../escape"§OTHERS })).await;
    assert_eq!(bare.status.as_u16(), 201, "{}", bare.body);
    let made = bare.json();
    assert!(made["§KEY"].as_str().expect("a key").starts_with("§UNATTACHED"), "a row made without an upload holds an unattached key");
    let missing = api.get(&format!("{ROUTE}/{}/download", made["§ID"])).await;
    assert_eq!(missing.status.as_u16(), 404);
    assert_eq!(missing.json()["type"], "§T_MISSING");
    api.delete(&format!("{ROUTE}/{}", made["§ID"])).await;
}
"""";
}
