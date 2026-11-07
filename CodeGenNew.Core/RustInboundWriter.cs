using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum and sqlx) counterpart of <see cref="InboundWriter"/>: <c>src/routes/inbound.rs</c>, the <c>receive</c> and <c>receive/{source}</c> routes (open to anyone, a signature instead of a login) and the
/// <c>unprocessed</c> and <c>{id}/processed</c> routes of every table in <c>InboundTables</c>, with the same sentences (<see cref="InboundWords"/>), problem types and status codes. The SQL is written for the project's database
/// with <see cref="DialectInfo"/>. The receive routes are in <c>public_router</c>, which main.rs merges after the sign-in check. </summary>
public static class RustInboundWriter
{
    public static string File(IReadOnlyList<InboundShape> tables, ProjectSettings project, SqlDialect dialect)
    {
        var di = DialectInfo.For(dialect);
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Tbl(TableModel t) => di.QuoteTable(t.SchemaName, t.DbTableName);
        string P(int n) => di.Placeholder(n);

        var imports = new StringBuilder();
        var routes = new StringBuilder();
        foreach (var q in tables)
        {
            var main = RustTable.Of(q.Table, project);
            string table = Tbl(q.Table), key = Q(q.Key), processed = Q(q.Processed), received = Q(q.Received);
            string unprocessed = q.ProcessedIsFlag ? $"({processed} IS NULL OR {processed} = {di.BooleanLiteral(false)})" : $"{processed} IS NULL";

            var columns = new List<string> { Q(q.Body), received };
            if (q.EventId is not null)
            {
                columns.Add(Q(q.EventId));
                if (q.Source is not null)
                    columns.Add(Q(q.Source));
            }
            else if (q.Source is not null)
                columns.Add(Q(q.Source));
            if (q.ProcessedIsFlag)
                columns.Add(processed);
            var placeholders = columns.Select((_, i) => q.ProcessedIsFlag && i == columns.Count - 1 ? di.BooleanLiteral(false) : P(i + 1)).ToList();
            string insert = $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})" + (di.SupportsReturning ? $" RETURNING {key}" : "");
            string keyType = main.Key.BaseType;

            var binds = new StringBuilder();
            binds.Append(".bind(&text).bind(" + RustTable.Now(main.FieldOf(q.Received)) + ")");
            if (q.EventId is not null)
                binds.Append(".bind(&event)");
            if (q.Source is not null)
                binds.Append(".bind(&source_text)");
            string insertRun = di.SupportsReturning
                ? $"sqlx::query_scalar::<_, {keyType}>(SQL_INSERT_§M){binds}.fetch_one(&pool).await"
                : $"sqlx::query(SQL_INSERT_§M){binds}.execute(&pool).await.map(|result| result.last_insert_id() as {keyType})";

            string findSource = q.EventId is null ? "" : $"SELECT {key} FROM {table} WHERE {Q(q.EventId)} = {P(1)}" + (q.Source is null ? "" : $" AND {Q(q.Source)} = {P(2)}") + " LIMIT 1";
            string findNone = q.EventId is null || q.Source is null ? "" : $"SELECT {key} FROM {table} WHERE {Q(q.EventId)} = {P(1)} AND {Q(q.Source)} IS NULL LIMIT 1";

            string eventPieces = q.EventId is null ? "" : FillAll(EventText,
                ("§FIND", q.Source is null
                    ? "sqlx::query_scalar::<_, §KT>(SQL_FIND_§M).bind(event).fetch_optional(pool).await"
                    : "match source { Some(source) => sqlx::query_scalar::<_, §KT>(SQL_FIND_§M).bind(event).bind(source).fetch_optional(pool).await, None => sqlx::query_scalar::<_, §KT>(SQL_FIND_NONE_§M).bind(event).fetch_optional(pool).await }"),
                ("§KT", keyType), ("§M", main.Module));
            string eventLines = q.EventId is null ? "" :
                "    let event: Option<String> = headers.get(EVENT_HEADER).and_then(|value| value.to_str().ok()).filter(|value| !value.is_empty()).map(str::to_string).or_else(|| json_event_id(&text));\n" +
                $"    let event = event.map(|e| if e.chars().count() <= {Length(q.EventId)} {{ e }} else {{ e.chars().take({Length(q.EventId)}).collect() }});\n" +
                "    if let Some(event) = event.as_deref().filter(|event| !event.is_empty()) {\n        if let Some(existing) = find_event_§M(&pool, event, source_text.as_deref()).await? {\n            return Ok(Json(serde_json::json!({ \"received\": false, \"duplicate\": true, \"id\": existing })));\n        }\n    }\n";
            string raceLines = q.EventId is null ? "" :
                "        Err(_) => {\n            if let Some(event) = event.as_deref().filter(|event| !event.is_empty()) {\n                if let Some(winner) = find_event_§M(&pool, event, source_text.as_deref()).await? {\n                    return Ok(Json(serde_json::json!({ \"received\": false, \"duplicate\": true, \"id\": winner })));\n                }\n            }\n            Err(InboundError::Api(ApiError::BadRequest(NOT_SAVED.into())))\n        }\n";
            if (q.EventId is null)
                raceLines = "        Err(_) => Err(InboundError::Api(ApiError::BadRequest(NOT_SAVED.into()))),\n";
            string sourceLine = q.Source is null ? "" : $"    let source_text = fit(source, {Length(q.Source)});\n";
            string sourceUnused = q.Source is null ? "    let _ = source;\n    #[allow(unused_variables)]\n    let source_text: Option<String> = None;\n" : "";
            string stamp = q.ProcessedIsFlag ? "" : "";
            string markSql = q.ProcessedIsFlag
                ? $"UPDATE {table} SET {processed} = {di.BooleanLiteral(true)} WHERE {key} = {P(1)} AND {unprocessed}"
                : $"UPDATE {table} SET {processed} = {P(1)} WHERE {key} = {P(2)} AND {unprocessed}";
            string markRun = q.ProcessedIsFlag
                ? "sqlx::query(SQL_MARK_§M).bind(id).execute(&pool).await?.rows_affected()"
                : $"sqlx::query(SQL_MARK_§M).bind({RustTable.Now(main.FieldOf(q.Processed))}).bind(id).execute(&pool).await?.rows_affected()";
            string totalSql = $"SELECT COUNT(*) FROM {table}", waitingSql = $"SELECT COUNT(*) FROM {table} WHERE {unprocessed}";
            string oldestSql = $"SELECT {key} FROM {table} WHERE {unprocessed} ORDER BY {received}, {key} LIMIT 1";
            string listSql = $"SELECT {main.SelectList} FROM {table} WHERE {unprocessed} ORDER BY {received}, {key} LIMIT {InboundWords.UnprocessedRows}";

            string sourceClient = q.Source is null ? "" : $"    let source_text: Option<String> = fit(Some(\"{InboundWords.ClientSource}\".to_string()), {Length(q.Source)});\n";
            string eventNone = q.EventId is null ? "" : "    let event: Option<String> = None;\n";
            string collectFn = !q.Client ? "" : Fill(CollectText, ("§EVENTNONE", eventNone), ("§SOURCECLIENT", sourceClient), ("§INSERTRUN", insertRun));
            string collectRoute = !q.Client ? "" : "        .route(\"§ROUTE/collect\", post(collect_§M))\n        .layer(Extension(Arc::new(ClientLimit::default())))\n";
            imports.Append($"use crate::models::{main.Module}::{main.Struct};\nuse crate::repos::{main.Module} as repo_{main.Module};\n");
            string eventText = eventPieces;
            routes.Append(FillAll(TableText,
                ("§SECRETPICK", HasOwn(project) ? "    let (secret_name, source) = source_secret(source);\n" : ""), ("§SECRETARG", HasOwn(project) ? ", secret_name" : ""), ("§COLLECTFN", collectFn), ("§COLLECTROUTE", collectRoute), ("§EVENTHELPERS", eventText), ("§EVENTLINES", eventLines), ("§RACEARMS", raceLines), ("§SOURCELINE", sourceLine), ("§SOURCEUNUSED", sourceUnused), ("§INSERTRUN", insertRun),
                ("§KEYFIELD", main.Key.Name), ("§M", main.Module), ("§S", main.Struct), ("§ROUTE", main.Route), ("§KT", keyType), ("§BODYMAX", q.BodyLength.ToString()),
                ("§SQL_INSERT", RustTable.Raw(insert)), ("§SQL_FIND_NONE_CONST", q.EventId is not null && q.Source is not null ? $"const SQL_FIND_NONE_§M: &str = {RustTable.Raw(findNone)};\n" : ""),
                ("§SQL_FIND_CONST", q.EventId is null ? "" : $"const SQL_FIND_§M: &str = {RustTable.Raw(findSource)};\n"),
                ("§SQL_TOTAL", RustTable.Raw(totalSql)), ("§SQL_WAITING", RustTable.Raw(waitingSql)), ("§SQL_OLDEST", RustTable.Raw(oldestSql)),
                ("§CLIENTON", q.Client ? "true" : "false"), ("§CLIENTRATE", project.InboundClientPerMinute.ToString()), ("§CLIENTBYTES", project.InboundClientMaxBytes.ToString()),
                ("§PARTITIONED", InboundPlan.HasPartitionScript(q.Table.TableName, project) ? "true" : "false"), ("§UNPROCLABEL", InboundScreens.UnprocessedLabel), ("§OLDESTLABEL", InboundScreens.OldestLabel), ("§TAIL", InboundScreens.Tail(q, project)),
                ("§SQL_LIST", RustTable.Raw(listSql)), ("§SQL_MARK", RustTable.Raw(markSql)), ("§MARKRUN", markRun)).Replace("§M", main.Module).Replace("§KT", keyType));
            _ = stamp;
        }
        bool clients = tables.Any(q => q.Client);
        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§IMPORTS", imports.ToString().TrimEnd('\n')), ("§SENDERS", string.Join(", ", InboundPlan.SourceSecrets(project).Select(s => $"\"{s.Source}\""))), ("§SECRETNAME", project.InboundSecret), ("§SOURCESECRETS\n", SourceSecretsText(project)), ("§SECRETFN\n", HasOwn(project) ? SourceFn + "\n\n" : ""),
            ("§SECRETPARAM", HasOwn(project) ? ", secret_name: &str" : ""), ("§SECRETVAR", HasOwn(project) ? "secret_name" : "SECRET_NAME"), ("§HEADER", InboundWords.SignatureHeader), ("§EVENTHEADER", InboundWords.EventHeader),
            ("§TOLERANCE", project.InboundToleranceSeconds.ToString()), ("§MAXBYTES", project.InboundMaxBytes.ToString()),
            ("§CLIENTUSES", clients ? ClientUses : ""), ("§CLIENTCONSTS", clients ? ClientConsts : ""), ("§CLIENTVARIANT", clients ? ClientVariant : ""), ("§CLIENTARM", clients ? ClientArm : ""), ("§CLIENTLIMIT", clients ? ClientLimitText : ""),
            ("§CLIENTRATE", project.InboundClientPerMinute.ToString()), ("§CLIENTBYTES", project.InboundClientMaxBytes.ToString()), ("§T_RATE", InboundWords.RateLimitedType), ("§W_RATE", Str(InboundWords.RateLimitedText)),
            ("§T_SIGNATURE", InboundWords.SignatureType), ("§T_EXPIRED", InboundWords.ExpiredType), ("§T_LARGE", InboundWords.TooLargeType),
            ("§W_UNCONFIGURED", Str(InboundWords.UnconfiguredText)), ("§W_SIGNATURE", Str(InboundWords.SignatureText)), ("§W_EXPIRED", Str(InboundWords.ExpiredText)),
            ("§W_LARGE", Str(InboundWords.TooLargeText)), ("§W_EMPTY", Str(InboundWords.EmptyText)), ("§W_NOTSAVED", Str(InboundWords.NotSavedText))));
        o.Append("\n/// The routes a worker uses: the signed-in ones.\npub fn router() -> Router<Pool> {\n    Router::new()\n");
        foreach (var q in tables)
            o.Append($"        .merge(router_{RustTable.Of(q.Table, project).Module}())\n");
        o.Append("}\n\n/// The receive routes: open to anyone, since the signature is the check. main.rs merges them after the sign-in check.\npub fn public_router() -> Router<Pool> {\n    Router::new()\n");
        foreach (var q in tables)
            o.Append($"        .merge(public_router_{RustTable.Of(q.Table, project).Module}())\n");
        o.Append("}\n");
        o.Append(routes);
        return o.ToString();
    }

    private static string Length(ColumnModel column) => (column.CharacterLength > 0 ? column.CharacterLength : int.MaxValue).ToString();

    private const string Head = """"
//! The inbound event landing tables (InboundTables): a sender posts a signed message that is kept as text, a worker lists the unprocessed events and marks each processed. The same routes, sentences and problem types as the
//! ASP.NET API (API_Inbound). The receive routes are open to anyone, since the signature is the check. Generated by RS_Inbound.
use axum::{
    Json, Router,
    body::Bytes,
    extract::{Path, State},
    http::{HeaderMap, HeaderValue, StatusCode, header},
    response::{IntoResponse, Response},
    routing::{get, post},
};
use hmac::{Hmac, Mac};
use sha2::Sha256;
§CLIENTUSES
use crate::support::{ApiError, Pool};
§IMPORTS

const SECRET_NAME: &str = "§SECRETNAME";
const SENDERS: &[&str] = &[§SENDERS];
§SOURCESECRETS
const SIGNATURE_HEADER: &str = "§HEADER";
#[allow(dead_code)]
const EVENT_HEADER: &str = "§EVENTHEADER";
const TOLERANCE: i64 = §TOLERANCE;
const MAX_BYTES: usize = §MAXBYTES;
const PROBLEM_SIGNATURE: &str = "§T_SIGNATURE";
const PROBLEM_EXPIRED: &str = "§T_EXPIRED";
const PROBLEM_TOO_LARGE: &str = "§T_LARGE";
const UNCONFIGURED: &str = §W_UNCONFIGURED;
const BAD_SIGNATURE: &str = §W_SIGNATURE;
const EXPIRED: &str = §W_EXPIRED;
const TOO_LARGE: &str = §W_LARGE;
const EMPTY: &str = §W_EMPTY;
const NOT_SAVED: &str = §W_NOTSAVED;
§CLIENTCONSTS
/// What an inbound handler can fail with: the project's ApiError, or a refusal in the problem-details form of the other stacks (whether or not the project has sign-in).
enum InboundError {
    Api(ApiError),
    Problem(StatusCode, &'static str, &'static str, &'static str),
§CLIENTVARIANT}

type InboundResult<T> = Result<T, InboundError>;

impl From<ApiError> for InboundError {
    fn from(error: ApiError) -> Self {
        InboundError::Api(error)
    }
}

impl From<sqlx::Error> for InboundError {
    fn from(error: sqlx::Error) -> Self {
        InboundError::Api(error.into())
    }
}

impl IntoResponse for InboundError {
    fn into_response(self) -> Response {
        match self {
            InboundError::Api(error) => error.into_response(),
            InboundError::Problem(status, kind, title, detail) => {
                let body = serde_json::json!({ "type": kind, "title": title, "status": status.as_u16(), "detail": detail });
                let mut response = (status, Json(body)).into_response();
                response.headers_mut().insert(header::CONTENT_TYPE, HeaderValue::from_static("application/problem+json"));
                response
            }
§CLIENTARM        }
    }
}
§CLIENTLIMIT
fn hex_decode(text: &str) -> Option<Vec<u8>> {
    if text.len() % 2 != 0 {
        return None;
    }
    (0..text.len()).step_by(2).map(|at| u8::from_str_radix(text.get(at..at + 2)?, 16).ok()).collect()
}

/// A text cut to the length of its column.
#[allow(dead_code)]
fn fit(text: Option<String>, length: usize) -> Option<String> {
    text.map(|t| if t.chars().count() <= length { t } else { t.chars().take(length).collect() })
}

§SECRETFN
/// The body as text once the secret, the size, the signature and the signed time are right; the problem otherwise. The signature is X-Signature: t=unix seconds,v1=hex of the HMAC-SHA256 of "t.body".
fn verified_text(headers: &HeaderMap, body: &Bytes, body_max: usize§SECRETPARAM) -> InboundResult<String> {
    let secret = std::env::var(§SECRETVAR).unwrap_or_default();
    if secret.is_empty() {
        return Err(InboundError::Problem(StatusCode::SERVICE_UNAVAILABLE, "about:blank", "Not configured", UNCONFIGURED));
    }
    if body.len() > MAX_BYTES {
        return Err(InboundError::Problem(StatusCode::PAYLOAD_TOO_LARGE, PROBLEM_TOO_LARGE, "Too large", TOO_LARGE));
    }
    let header = headers.get(SIGNATURE_HEADER).and_then(|value| value.to_str().ok()).unwrap_or("");
    let mut signed_at: Option<i64> = None;
    let mut signature = String::new();
    for part in header.split(',') {
        if let Some((name, value)) = part.trim().split_once('=') {
            match name {
                "t" => signed_at = value.parse().ok(),
                "v1" => signature = value.to_string(),
                _ => {}
            }
        }
    }
    let valid = match (signed_at, hex_decode(&signature)) {
        (Some(at), Some(given)) => match Hmac::<Sha256>::new_from_slice(secret.as_bytes()) {
            Ok(mut mac) => {
                mac.update(format!("{at}.").as_bytes());
                mac.update(body);
                mac.verify_slice(&given).is_ok()
            }
            Err(_) => false,
        },
        _ => false,
    };
    if !valid {
        return Err(InboundError::Problem(StatusCode::UNAUTHORIZED, PROBLEM_SIGNATURE, "Bad signature", BAD_SIGNATURE));
    }
    if (chrono::Utc::now().timestamp() - signed_at.unwrap_or(0)).abs() > TOLERANCE {
        return Err(InboundError::Problem(StatusCode::BAD_REQUEST, PROBLEM_EXPIRED, "Message expired", EXPIRED));
    }
    let text = String::from_utf8_lossy(body).into_owned();
    if text.is_empty() {
        return Err(InboundError::Api(ApiError::BadRequest(EMPTY.into())));
    }
    if body_max > 0 && text.chars().count() > body_max {
        return Err(InboundError::Problem(StatusCode::PAYLOAD_TOO_LARGE, PROBLEM_TOO_LARGE, "Too large", TOO_LARGE));
    }
    Ok(text)
}
"""";

    private static bool HasOwn(ProjectSettings project) => InboundPlan.SourceSecrets(project).Count > 0;

    private static string SourceSecretsText(ProjectSettings project) =>
        HasOwn(project) ? "const SOURCE_SECRETS: &[(&str, &str)] = &[" + string.Join(", ", InboundPlan.SourceSecrets(project).Select(s => $"(\"{s.Source}\", \"{s.Secret}\")")) + "];\n" : "";

    private const string SourceFn = """
/// The environment variable that holds the secret of the sender and the sender's name as configured; the shared secret for any other sender.
fn source_secret(source: Option<String>) -> (&'static str, Option<String>) {
    if let Some(given) = source.as_deref() {
        if let Some(own) = SOURCE_SECRETS.iter().find(|own| own.0.eq_ignore_ascii_case(given)) {
            return (own.1, Some(own.0.to_string()));
        }
    }
    (SECRET_NAME, source)
}
""";

    private const string ClientUses = """
use axum::extract::{ConnectInfo, Extension};
use std::collections::HashMap;
use std::net::SocketAddr;
use std::sync::{Arc, Mutex};

""";

    private const string ClientConsts = """
const CLIENT_PER_MINUTE: u32 = §CLIENTRATE;
const CLIENT_MAX_BYTES: usize = §CLIENTBYTES;
const PROBLEM_RATE_LIMITED: &str = "§T_RATE";
const RATE_LIMITED: &str = §W_RATE;

""";

    private const string ClientVariant = """
    RateLimited(i64),

""";

    private const string ClientArm = """
            InboundError::RateLimited(seconds) => {
                let body = serde_json::json!({ "type": PROBLEM_RATE_LIMITED, "title": "Too many requests", "status": 429, "detail": RATE_LIMITED });
                let mut response = (StatusCode::TOO_MANY_REQUESTS, Json(body)).into_response();
                response.headers_mut().insert(header::CONTENT_TYPE, HeaderValue::from_static("application/problem+json"));
                response.headers_mut().insert(header::RETRY_AFTER, HeaderValue::from(seconds.max(1) as u64));
                response
            }

""";

    private const string ClientLimitText = """"

/// A fixed window of one minute for each caller address, kept in the memory of this process; each router holds its own.
#[derive(Default)]
struct ClientLimit(Mutex<HashMap<String, (i64, u32)>>);

impl ClientLimit {
    fn check(&self, caller: &str) -> InboundResult<()> {
        let now = chrono::Utc::now().timestamp();
        let minute = now / 60;
        let mut counts = self.0.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        if counts.len() > 10000 {
            counts.retain(|_, (window, _)| *window == minute);
        }
        let entry = counts.entry(caller.to_string()).or_insert((minute, 0));
        if entry.0 != minute {
            *entry = (minute, 0);
        }
        entry.1 += 1;
        if entry.1 > CLIENT_PER_MINUTE {
            return Err(InboundError::RateLimited(60 - now % 60));
        }
        Ok(())
    }
}
"""";

    private const string CollectText = """"

/// For a browser: open to anyone, no signature, write only (the answer is 202 with no body), a small body and a limit of requests a minute for each caller address.
async fn collect_§M(State(pool): State<Pool>, Extension(limit): Extension<Arc<ClientLimit>>, ConnectInfo(address): ConnectInfo<SocketAddr>, body: Bytes) -> InboundResult<StatusCode> {
    limit.check(&address.ip().to_string())?;
    if body.len() > CLIENT_MAX_BYTES {
        return Err(InboundError::Problem(StatusCode::PAYLOAD_TOO_LARGE, PROBLEM_TOO_LARGE, "Too large", TOO_LARGE));
    }
    let text = String::from_utf8_lossy(&body).into_owned();
    if text.is_empty() {
        return Err(InboundError::Api(ApiError::BadRequest(EMPTY.into())));
    }
    if BODY_MAX_§M > 0 && text.chars().count() > BODY_MAX_§M {
        return Err(InboundError::Problem(StatusCode::PAYLOAD_TOO_LARGE, PROBLEM_TOO_LARGE, "Too large", TOO_LARGE));
    }
§SOURCECLIENT§EVENTNONE    let run = §INSERTRUN;
    match run {
        Ok(_) => Ok(StatusCode::ACCEPTED),
        Err(_) => Err(InboundError::Api(ApiError::BadRequest(NOT_SAVED.into()))),
    }
}
"""";

    private const string EventText = """"

/// The id property of a JSON body, as text.
fn json_event_id(text: &str) -> Option<String> {
    let message: serde_json::Value = serde_json::from_str(text).ok()?;
    match message.as_object()?.get("id")? {
        serde_json::Value::Null => None,
        serde_json::Value::String(id) => Some(id.clone()),
        other => Some(other.to_string()),
    }
}

async fn find_event_§M(pool: &Pool, event: &str, source: Option<&str>) -> Result<Option<§KT>, sqlx::Error> {
    §FIND
}
"""";

    private const string TableText = """"

// ===== §S =====

const SQL_INSERT_§M: &str = §SQL_INSERT;
§SQL_FIND_CONST§SQL_FIND_NONE_CONST
const SQL_LIST_§M: &str = §SQL_LIST;
const SQL_TOTAL_§M: &str = §SQL_TOTAL;
const SQL_WAITING_§M: &str = §SQL_WAITING;
const SQL_OLDEST_§M: &str = §SQL_OLDEST;
const SQL_MARK_§M: &str = §SQL_MARK;
#[allow(dead_code)]
const BODY_MAX_§M: usize = §BODYMAX;
§EVENTHELPERS
fn router_§M() -> Router<Pool> {
    Router::new()
        .route("§ROUTE/unprocessed", get(unprocessed_§M))
        .route("§ROUTE/status", get(status_§M))
        .route("§ROUTE/{id}/processed", post(processed_§M))
}

fn public_router_§M() -> Router<Pool> {
    Router::new()
        .route("§ROUTE/receive", post(receive_§M))
        .route("§ROUTE/receive/{source}", post(receive_from_§M))
§COLLECTROUTE}

async fn receive_§M(State(pool): State<Pool>, headers: HeaderMap, body: Bytes) -> InboundResult<Json<serde_json::Value>> {
    receive_inner_§M(pool, headers, body, None).await
}

async fn receive_from_§M(State(pool): State<Pool>, Path(source): Path<String>, headers: HeaderMap, body: Bytes) -> InboundResult<Json<serde_json::Value>> {
    receive_inner_§M(pool, headers, body, Some(source)).await
}

async fn receive_inner_§M(pool: Pool, headers: HeaderMap, body: Bytes, source: Option<String>) -> InboundResult<Json<serde_json::Value>> {
§SECRETPICK    let text = verified_text(&headers, &body, BODY_MAX_§M§SECRETARG)?;
§SOURCELINE§SOURCEUNUSED§EVENTLINES    let run = §INSERTRUN;
    match run {
        Ok(id) => Ok(Json(serde_json::json!({ "received": true, "duplicate": false, "id": id }))),
§RACEARMS    }
}

§COLLECTFN
async fn unprocessed_§M(State(pool): State<Pool>) -> InboundResult<Json<Vec<§S>>> {
    Ok(Json(sqlx::query_as::<_, §S>(SQL_LIST_§M).fetch_all(&pool).await?))
}

/// How many events there are and how many wait, the oldest one that waits, and what the generator was told about this table.
async fn status_§M(State(pool): State<Pool>) -> InboundResult<Json<serde_json::Value>> {
    let total: i64 = sqlx::query_scalar(SQL_TOTAL_§M).fetch_one(&pool).await?;
    let waiting: i64 = sqlx::query_scalar(SQL_WAITING_§M).fetch_one(&pool).await?;
    let oldest: Option<§KT> = sqlx::query_scalar(SQL_OLDEST_§M).fetch_optional(&pool).await?;
    let summary = format!("§UNPROCLABEL{waiting} of {total}") + &oldest.map(|id| format!("§OLDESTLABEL{id}")).unwrap_or_default() + "§TAIL.";
    Ok(Json(serde_json::json!({
        "summary": summary,
        "total": total,
        "unprocessed": waiting,
        "oldestUnprocessedId": oldest,
        "senders": SENDERS,
        "collect": { "enabled": §CLIENTON, "perMinute": §CLIENTRATE, "maxBytes": §CLIENTBYTES },
        "partitionScript": §PARTITIONED,
    })))
}

/// One UPDATE that still tests that the event is unprocessed, so two workers never both succeed.
async fn processed_§M(State(pool): State<Pool>, Path(id): Path<§KT>) -> InboundResult<Json<serde_json::Value>> {
    let changed = §MARKRUN;
    let row = repo_§M::get_by_id(&pool, id).await?.ok_or(ApiError::NotFound)?;
    Ok(Json(serde_json::json!({ "processed": changed == 1, "alreadyProcessed": changed == 0, "row": row })))
}
"""";
}
