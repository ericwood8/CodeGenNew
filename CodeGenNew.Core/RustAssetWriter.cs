using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum and sqlx) counterpart of the asset part of <see cref="RowShapeWriter"/>: <c>src/routes/asset_files.rs</c>, the upload and download routes of every table in <c>AssetTables</c> and the store
/// (<c>AssetStore</c>, a local folder by default). The rules that keep the storage key out of a request's hands are lines in the table's own create, update and delete (RS_Routes, <see cref="CreateLine"/>,
/// <see cref="UpdateLines"/>, <see cref="DeleteLines"/>). The words are <see cref="AssetWords"/>. </summary>
public static class RustAssetWriter
{
    public static IReadOnlyList<AssetShape> Shapes(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => TableShapes.For(t, project).Asset).OfType<AssetShape>().OrderBy(a => a.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The line of the create: a row made by the ordinary create holds an unattached key. </summary>
    public static string CreateLine(RustTable t, AssetShape asset) =>
        $"    row.{t.FieldOf(asset.StorageKey).Name} = crate::routes::asset_files::unattached();\n";

    /// <summary> The lines of the update, after the stored row is read: the key, type and size stay as stored. </summary>
    public static string UpdateLines(RustTable t, AssetShape asset) =>
        $"    row.{t.FieldOf(asset.StorageKey).Name} = stored.{t.FieldOf(asset.StorageKey).Name}.clone();\n    row.{t.FieldOf(asset.MimeType).Name} = stored.{t.FieldOf(asset.MimeType).Name}.clone();\n    row.{t.FieldOf(asset.Size).Name} = stored.{t.FieldOf(asset.Size).Name}.clone();\n";

    /// <summary> The lines before the delete (the stored key is read) and the text of the arm that follows a delete that removed a row (the file is removed). </summary>
    public static string DeleteLines(RustTable t, AssetShape asset) =>
        $"    let file_key = repo::get_by_id(&pool, id).await?.map(|stored| stored.{t.FieldOf(asset.StorageKey).Name});\n";

    public static string File(IReadOnlyList<AssetShape> tables, ProjectSettings project, SqlDialect dialect)
    {
        var di = DialectInfo.For(dialect);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var routes = new StringBuilder();
        var code = new StringBuilder();
        foreach (var a in tables)
        {
            var t = RustTable.Of(a.Table, project);
            string table = di.QuoteTable(a.Table.SchemaName, a.Table.DbTableName);
            string module = t.Module, upper = module.ToUpperInvariant();
            var name = t.FieldOf(a.FileName);
            var mime = t.FieldOf(a.MimeType);
            var size = t.FieldOf(a.Size);
            string keyType = t.Key.Type;
            int Room(ColumnModel c) => c.CharacterLength > 0 ? c.CharacterLength : 1000000;
            string insert = $"INSERT INTO {table} ({Q(a.StorageKey)}, {Q(a.FileName)}, {Q(a.MimeType)}, {Q(a.Size)}) VALUES ({di.Placeholder(1)}, {di.Placeholder(2)}, {di.Placeholder(3)}, {di.Placeholder(4)})"
                + (di.SupportsReturning ? $" RETURNING {Q(a.Key)}" : "");
            string insertCall = di.SupportsReturning
                ? $"    let id: {keyType} = sqlx::query_scalar(INSERT_{upper}).bind(&key).bind(&file_name).bind({(mime.IsOption ? "Some(mime.clone())" : "mime.clone()")}).bind(bytes.len() as {size.BaseType}).fetch_one(&pool).await.inspect_err(|_| forget(&key))?;\n"
                : $"    let id = sqlx::query(INSERT_{upper}).bind(&key).bind(&file_name).bind({(mime.IsOption ? "Some(mime.clone())" : "mime.clone()")}).bind(bytes.len() as {size.BaseType}).execute(&pool).await.inspect_err(|_| forget(&key))?.last_insert_id() as {keyType};\n";
            string urlSet = a.Url is null ? "" : $"    sqlx::query(SET_URL_{upper}).bind({(t.FieldOf(a.Url).IsOption ? "Some(" : "")}truncate(&format!(\"{{}}/{{id}}/download\", {Str(t.Route)}), {Room(a.Url)}){(t.FieldOf(a.Url).IsOption ? ")" : "")}).bind(id).execute(&pool).await?;\n";
            string setUrl = a.Url is null ? "" : $"const SET_URL_{upper}: &str = {RustTable.Raw($"UPDATE {table} SET {Q(a.Url)} = {di.Placeholder(1)} WHERE {Q(a.Key)} = {di.Placeholder(2)}")};\n";
            code.Append(TemplateText.FillAll(PerTable,
                ("§UPPER", upper), ("§MODULE", module), ("§STRUCT", t.Struct), ("§KT", keyType), ("§ROUTE", Str(t.Route)),
                ("§INSERTSQL", RustTable.Raw(insert)), ("§INSERTCALL", insertCall), ("§SETURLCONST", setUrl), ("§URLSET", urlSet),
                ("§FILESQL", RustTable.Raw($"SELECT {Q(a.StorageKey)}, {Q(a.FileName)}, {Q(a.MimeType)} FROM {table} WHERE {Q(a.Key)} = {di.Placeholder(1)}")),
                ("§KEYSQL", RustTable.Raw($"SELECT {Q(a.StorageKey)} FROM {table} WHERE {Q(a.Key)} = {di.Placeholder(1)}")),
                ("§NAMEMAX", Room(a.FileName).ToString()), ("§MIMEMAX", Room(a.MimeType).ToString())));
            routes.Append($"        .route({Str(t.Route + "/upload")}, post(upload_{module}).layer(DefaultBodyLimit::max(MAX_BYTES + BODY_SLACK)))\n        .route({Str(t.Route + "/{id}/download")}, get(download_{module}))\n");
        }
        string allowed = string.Join(", ", AssetPlan.AllowedTypes(project).Select(Str));
        return TemplateText.FillAll(Template,
            ("§TABLES", code.ToString()), ("§ROUTES", routes.ToString().TrimEnd('\n')), ("§FOLDER", Str(project.AssetStoreFolder)), ("§MAXBYTES", project.AssetMaxBytes.ToString()), ("§ALLOWED", allowed),
            ("§UNATTACHED", AssetWords.UnattachedPrefix), ("§OCTET", AssetWords.OctetStream),
            ("§NOFILE_TYPE", AssetWords.NoFileType), ("§NOFILE", Str(AssetWords.NoFile)), ("§TOOLARGE_TYPE", AssetWords.TooLargeType), ("§TOOLARGE", Str(AssetWords.TooLarge(project.AssetMaxBytes))),
            ("§NOTALLOWED_TYPE", AssetWords.TypeNotAllowedType), ("§NOTALLOWED", Str(AssetWords.TypeNotAllowed(AssetPlan.AllowedTypes(project)))),
            ("§MISSING_TYPE", AssetWords.FileMissingType), ("§MISSING", Str(AssetWords.FileMissing)),
            ("§BADREQUEST", HttpStatus.BadRequest.ToString()), ("§TOOLARGE_STATUS", "413"), ("§UNSUPPORTED", "415"), ("§NOTFOUND", HttpStatus.NotFound.ToString())) + "\n";
    }

    private const string PerTable = """"
const INSERT_§UPPER: &str = §INSERTSQL;
const FILE_§UPPER: &str = §FILESQL;
const KEY_§UPPER: &str = §KEYSQL;
§SETURLCONST
/// The storage key of a row, or None when there is no such row; the delete of the table reads it first and removes the file when the row is gone.
pub async fn key_of_§MODULE(pool: &Pool, id: §KT) -> Result<Option<String>, sqlx::Error> {
    sqlx::query_scalar(KEY_§UPPER).bind(id).fetch_optional(pool).await
}

async fn upload_§MODULE(State(pool): State<Pool>, mut multipart: Multipart) -> ApiResult<Response> {
    let upload = read_upload(&mut multipart).await?;
    let UploadedFile { name, mime, bytes } = upload;
    let key = make_key(&name);
    let file_name = clean_name(&name, §NAMEMAX);
    let mime = truncate(&mime, §MIMEMAX);
    store().save(&key, &bytes).map_err(|error| ApiError::BadRequest(error.to_string()))?;
§INSERTCALL§URLSET    let row = crate::repos::§MODULE::get_by_id(&pool, id).await?.ok_or(ApiError::NotFound)?;
    Ok(created(format!("{}/{}", §ROUTE, id), row))
}

async fn download_§MODULE(State(pool): State<Pool>, Path(id): Path<§KT>) -> ApiResult<Response> {
    let Some((key, name, mime)) = sqlx::query_as::<_, (String, String, Option<String>)>(FILE_§UPPER).bind(id).fetch_optional(&pool).await? else {
        return Err(ApiError::NotFound);
    };
    file_response(&key, &name, mime)
}

"""";

    private const string Template = """"
// The asset tables (AssetTables): rows that point at a stored file. POST <route>/upload takes a multipart form with a field named file, GET <route>/{id}/download streams it and a delete removes the file.
// The same routes, sentences and problem types as the ASP.NET API (RowShapes). The row never holds the bytes; the storage key is made here and cannot be named by an ordinary save. Generated by RS_Assets.
use std::path::PathBuf;
use std::sync::OnceLock;

use axum::{
    Router,
    extract::{DefaultBodyLimit, Multipart, Path, State},
    http::{HeaderValue, header},
    response::{IntoResponse, Response},
    routing::{get, post},
};

use crate::support::{ApiError, ApiResult, Pool, created, path_segment};

pub const MAX_BYTES: usize = §MAXBYTES;
const ALLOWED_TYPES: &[&str] = &[§ALLOWED];
const UNATTACHED: &str = "§UNATTACHED";
const OCTET_STREAM: &str = "§OCTET";
/// The room the request body gets besides the file itself (the form's own lines); the file is judged by MAX_BYTES.
const BODY_SLACK: usize = 65536;

/// Where the bytes of an uploaded file are kept; the row holds only the key. Call use_store() once, before the first request, to keep files elsewhere (S3, Azure Blob).
pub trait AssetStore: Send + Sync {
    fn save(&self, key: &str, bytes: &[u8]) -> std::io::Result<()>;
    /// The file, or None when the store has none under that key.
    fn open(&self, key: &str) -> std::io::Result<Option<Vec<u8>>>;
    /// Removes the file; a key with no file is not an error.
    fn delete(&self, key: &str) -> std::io::Result<()>;
}

/// The default store: one file per key in a folder (ASSET_STORE_FOLDER, else AssetStoreFolder). A key that could leave the folder is refused.
pub struct LocalFolderAssetStore {
    folder: PathBuf,
}

impl LocalFolderAssetStore {
    pub fn new(folder: impl Into<PathBuf>) -> Self {
        Self { folder: folder.into() }
    }

    fn path(&self, key: &str) -> std::io::Result<PathBuf> {
        if is_safe_key(key) {
            Ok(self.folder.join(key))
        } else {
            Err(std::io::Error::new(std::io::ErrorKind::InvalidInput, "Not a storage key."))
        }
    }
}

impl AssetStore for LocalFolderAssetStore {
    fn save(&self, key: &str, bytes: &[u8]) -> std::io::Result<()> {
        std::fs::create_dir_all(&self.folder)?;
        std::fs::write(self.path(key)?, bytes)
    }

    fn open(&self, key: &str) -> std::io::Result<Option<Vec<u8>>> {
        match std::fs::read(self.path(key)?) {
            Ok(bytes) => Ok(Some(bytes)),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
            Err(error) => Err(error),
        }
    }

    fn delete(&self, key: &str) -> std::io::Result<()> {
        match std::fs::remove_file(self.path(key)?) {
            Err(error) if error.kind() != std::io::ErrorKind::NotFound => Err(error),
            _ => Ok(()),
        }
    }
}

static STORE: OnceLock<Box<dyn AssetStore>> = OnceLock::new();

/// Keeps the files in another store; it takes effect only if called before the first upload or download.
pub fn use_store(store: Box<dyn AssetStore>) {
    let _ = STORE.set(store);
}

fn store() -> &'static dyn AssetStore {
    STORE.get_or_init(|| Box::new(LocalFolderAssetStore::new(std::env::var("ASSET_STORE_FOLDER").unwrap_or_else(|_| §FOLDER.to_string())))).as_ref()
}

/// The key of a row made without an upload: no file is behind it.
pub fn unattached() -> String {
    format!("{UNATTACHED}{}", uuid::Uuid::new_v4().simple())
}

/// Removes the file of a deleted row (the key read before the delete); a row with no file behind it is fine.
pub fn remove_file(key: Option<String>) {
    if let Some(key) = key {
        if !key.starts_with(UNATTACHED) && is_safe_key(&key) {
            let _ = store().delete(&key);
        }
    }
}

fn forget(key: &str) {
    let _ = store().delete(key);
}

fn is_safe_key(key: &str) -> bool {
    !key.is_empty() && key.len() <= 200 && !key.starts_with('.') && !key.contains("..") && key.chars().all(|c| c.is_ascii_alphanumeric() || matches!(c, '-' | '_' | '.'))
}

fn make_key(file_name: &str) -> String {
    let extension = std::path::Path::new(&file_name.replace('\\', "/")).extension().map(|e| e.to_string_lossy().to_lowercase()).unwrap_or_default();
    let extension = if (1..=8).contains(&extension.len()) && extension.chars().all(|c| c.is_ascii_alphanumeric()) { format!(".{extension}") } else { String::new() };
    format!("{}{extension}", uuid::Uuid::new_v4().simple())
}

fn clean_type(content_type: Option<&str>) -> String {
    let text = content_type.unwrap_or("").split(';').next().unwrap_or("").trim().to_lowercase();
    if text.contains('/') && text.len() <= 100 { text } else { OCTET_STREAM.to_string() }
}

fn clean_name(name: &str, limit: usize) -> String {
    let last = name.replace('\\', "/").rsplit('/').next().unwrap_or("").to_string();
    let text: String = last.chars().filter(|c| !c.is_control()).collect();
    let text = text.trim();
    truncate(if text.is_empty() { "file" } else { text }, limit)
}

fn truncate(text: &str, limit: usize) -> String {
    text.chars().take(limit).collect()
}

fn accepts(mime: &str) -> bool {
    ALLOWED_TYPES.is_empty() || ALLOWED_TYPES.iter().any(|allowed| match allowed.strip_suffix('*') {
        Some(family) => mime.starts_with(family),
        None => *allowed == mime,
    })
}

struct UploadedFile {
    name: String,
    mime: String,
    bytes: Vec<u8>,
}

fn rejected(status: u16, kind: &'static str, title: &'static str, detail: &str) -> ApiError {
    ApiError::Rejected(status, kind, title, detail.to_string())
}

/// The file of a multipart form (the field named file, else the first field that has a file name), checked for presence, size and type.
async fn read_upload(multipart: &mut Multipart) -> Result<UploadedFile, ApiError> {
    let mut found: Option<UploadedFile> = None;
    while let Some(field) = multipart.next_field().await.map_err(|error| ApiError::BadRequest(error.body_text()))? {
        if field.name() == Some("file") || field.file_name().is_some() {
            let name = field.file_name().unwrap_or("").to_string();
            let mime = clean_type(field.content_type());
            let bytes = field.bytes().await.map_err(|error| ApiError::BadRequest(error.body_text()))?.to_vec();
            found = Some(UploadedFile { name, mime, bytes });
            break;
        }
    }
    let Some(file) = found.filter(|file| !file.bytes.is_empty()) else {
        return Err(rejected(§BADREQUEST, "§NOFILE_TYPE", "No file", §NOFILE));
    };
    if file.bytes.len() > MAX_BYTES {
        return Err(rejected(§TOOLARGE_STATUS, "§TOOLARGE_TYPE", "File too large", §TOOLARGE));
    }
    if !accepts(&file.mime) {
        return Err(rejected(§UNSUPPORTED, "§NOTALLOWED_TYPE", "File type not allowed", §NOTALLOWED));
    }
    Ok(file)
}

/// The file as a download, or the problem when the store has none for the row.
fn file_response(key: &str, name: &str, mime: Option<String>) -> ApiResult<Response> {
    let bytes = if key.starts_with(UNATTACHED) || !is_safe_key(key) { None } else { store().open(key).ok().flatten() };
    let Some(bytes) = bytes else {
        return Err(rejected(§NOTFOUND, "§MISSING_TYPE", "File missing", §MISSING));
    };
    let fallback: String = name.chars().map(|c| if c.is_ascii() && !c.is_control() && c != '"' && c != '\\' { c } else { '_' }).collect();
    let disposition = format!("attachment; filename=\"{fallback}\"; filename*=UTF-8''{}", path_segment(name));
    let mut response = bytes.into_response();
    let headers = response.headers_mut();
    if let Ok(value) = HeaderValue::from_str(mime.as_deref().unwrap_or(OCTET_STREAM)) {
        headers.insert(header::CONTENT_TYPE, value);
    }
    if let Ok(value) = HeaderValue::from_str(&disposition) {
        headers.insert(header::CONTENT_DISPOSITION, value);
    }
    headers.insert(header::X_CONTENT_TYPE_OPTIONS, HeaderValue::from_static("nosniff"));
    Ok(response)
}

§TABLES
pub fn router() -> Router<Pool> {
    Router::new()
§ROUTES
}
"""";
}
