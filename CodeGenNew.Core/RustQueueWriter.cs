using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum and sqlx) counterpart of <see cref="QueueWriter"/>: <c>src/routes/queue.rs</c>, the <c>stats</c>, <c>failed</c>, <c>claim</c>, <c>{id}/renew</c>, <c>{id}/complete</c>, <c>{id}/fail</c> and <c>{id}/retry</c> routes of every
/// table in <c>QueueTables</c>, with the same sentences (<see cref="QueueWords"/>), problem type and status codes. The SQL is written for the project's database with <see cref="DialectInfo"/>; every change of status is one
/// UPDATE of the row in the status it expects. The shared types are written once and each table adds its handlers. </summary>
public static class RustQueueWriter
{
    public static string File(IReadOnlyList<QueueTable> tables, ProjectSettings project, SqlDialect dialect)
    {
        var di = DialectInfo.For(dialect);
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Tbl(TableModel t) => di.QuoteTable(t.SchemaName, t.DbTableName);
        string P(int n) => di.Placeholder(n);
        int max = project.QueueMaxRetries;

        var imports = new StringBuilder();
        var routes = new StringBuilder();
        foreach (var q in tables)
        {
            var main = RustTable.Of(q.Table, project);
            var key = Q(q.Key);
            string status = Q(q.Status), retry = Q(q.Retry), created = Q(q.Created), claimed = Q(q.Claimed), processed = Q(q.Processed);
            string table = Tbl(q.Table);
            string clearBy = q.ClaimedBy is null ? "" : $", {Q(q.ClaimedBy)} = NULL";
            string pending = $"'{QueueStatus.Pending}'", claimedLiteral = $"'{QueueStatus.Claimed}'", done = $"'{QueueStatus.Done}'", failed = $"'{QueueStatus.Failed}'";
            var createdField = main.FieldOf(q.Created);

            string release = $"UPDATE {table} SET {status} = {pending}, {claimed} = NULL{clearBy} WHERE {status} = {claimedLiteral} AND {claimed} < {P(1)}";
            string counts = $"SELECT {status}, COUNT(*) FROM {table} GROUP BY {status}";
            string oldest = $"SELECT {key}, {created} FROM {table} WHERE {status} = {pending} ORDER BY {created}, {key} LIMIT 1";
            string next = $"SELECT {key} FROM {table} WHERE {status} = {pending} ORDER BY {created}, {key} LIMIT 1";
            string claim = q.ClaimedBy is null
                ? $"UPDATE {table} SET {status} = {claimedLiteral}, {claimed} = {P(1)} WHERE {key} = {P(2)} AND {status} = {pending}"
                : $"UPDATE {table} SET {status} = {claimedLiteral}, {claimed} = {P(1)}, {Q(q.ClaimedBy)} = {P(2)} WHERE {key} = {P(3)} AND {status} = {pending}";
            string renew = $"UPDATE {table} SET {claimed} = {P(1)} WHERE {key} = {P(2)} AND {status} = {claimedLiteral}";
            string complete = $"UPDATE {table} SET {status} = {done}, {processed} = {P(1)} WHERE {key} = {P(2)} AND {status} = {claimedLiteral}";
            string exhausted = $"{retry} + 1 >= {max}";
            // the retry count is written last: MySQL reads a column that an earlier assignment of the same UPDATE already changed
            string failSets = $"{status} = CASE WHEN {exhausted} THEN {failed} ELSE {pending} END, {processed} = CASE WHEN {exhausted} THEN {P(1)} ELSE NULL END, {claimed} = NULL{clearBy}";
            if (q.LastError is not null)
                failSets += $", {Q(q.LastError)} = {P(2)}";
            failSets += $", {retry} = {retry} + 1";
            string fail = $"UPDATE {table} SET {failSets} WHERE {key} = {P(q.LastError is null ? 2 : 3)} AND {status} = {claimedLiteral}";
            string retrySql = $"UPDATE {table} SET {status} = {pending}, {retry} = 0, {processed} = NULL, {claimed} = NULL{clearBy} WHERE {key} = {P(1)} AND {status} = {failed}";
            string failedRows = $"SELECT {main.SelectList} FROM {table} WHERE {status} = {failed} ORDER BY {processed} DESC, {key} DESC LIMIT {QueueWords.FailedRows}";

            string errorLine = q.LastError is null ? "" : $"    let message = fit(failure.error,{Length(q.LastError)});\n";
            string errorBind = q.LastError is null ? "" : ".bind(message)";
            string claimBy = q.ClaimedBy is null ? "" : $".bind(fit(query.worker.clone(), {Length(q.ClaimedBy)}))";

            imports.Append($"use crate::models::{main.Module}::{main.Struct};\nuse crate::repos::{main.Module} as repo_{main.Module};\n");
            routes.Append(Fill(TableText,
                ("§M", main.Module), ("§S", main.Struct), ("§ROUTE", main.Route), ("§KT", main.Key.BaseType), ("§CT", createdField.Type), ("§ON", createdField.IsOption ? "*on" : "Some(*on)"),
                ("§NOWCLAIMED", RustTable.Now(main.FieldOf(q.Claimed))), ("§NOWPROCESSED", RustTable.Now(main.FieldOf(q.Processed))),
                ("§SQL_RELEASE", RustTable.Raw(release)), ("§SQL_COUNTS", RustTable.Raw(counts)), ("§SQL_OLDEST", RustTable.Raw(oldest)), ("§SQL_NEXT", RustTable.Raw(next)), ("§SQL_CLAIM", RustTable.Raw(claim)),
                ("§SQL_COMPLETE", RustTable.Raw(complete)), ("§SQL_RENEW", RustTable.Raw(renew)), ("§SQL_FAILED_ROWS", RustTable.Raw(failedRows)), ("§SQL_FAIL", RustTable.Raw(fail)), ("§SQL_RETRY", RustTable.Raw(retrySql)),
                ("§ERRORLINE", errorLine), ("§ERRORBIND", errorBind), ("§CLAIMBY", claimBy),
                ("§PENDING", QueueStatus.Pending), ("§CLAIMED", QueueStatus.Claimed), ("§DONE", QueueStatus.Done), ("§FAILED", QueueStatus.Failed)));
        }
        var o = new StringBuilder();
        o.Append(Fill(Head, ("§IMPORTS", imports.ToString().TrimEnd('\n')), ("§T_STATE", QueueWords.WrongStateType), ("§W_NOTCLAIMED", Str(QueueWords.NotClaimed)), ("§W_NOTFAILED", Str(QueueWords.NotFailed)),
            ("§MIN", project.QueueClaimMinutes.ToString()), ("§ATTEMPTS", QueueWords.ClaimAttempts.ToString())));
        o.Append("\npub fn router() -> Router<Pool> {\n    Router::new()\n");
        foreach (var q in tables)
            o.Append($"        .merge(router_{RustTable.Of(q.Table, project).Module}())\n");
        o.Append("}\n");
        o.Append(routes);
        return o.ToString();
    }

    private static string Length(ColumnModel column) => (column.CharacterLength > 0 ? column.CharacterLength : int.MaxValue).ToString();

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Head = """"
//! The work queues (QueueTables): a worker claims the oldest pending row, then completes or fails it; a failed row can be retried. The same routes, sentences and problem type as the ASP.NET API (API_Queue).
//! Every change of status is one UPDATE of the row in the status it expects, so two workers never get the same row. Generated by RS_Queue.
use axum::{
    Json, Router,
    body::Bytes,
    extract::{Path, Query, State},
    http::{HeaderValue, StatusCode, header},
    response::{IntoResponse, Response},
    routing::{get, post},
};
use chrono::NaiveDateTime;
use serde::{Deserialize, Serialize};

use crate::support::{ApiError, Pool};
§IMPORTS

const PROBLEM_WRONG_STATE: &str = "§T_STATE";
const NOT_CLAIMED: &str = §W_NOTCLAIMED;
const NOT_FAILED: &str = §W_NOTFAILED;

const CLAIM_MINUTES: i64 = §MIN;
const CLAIM_ATTEMPTS: usize = §ATTEMPTS;

/// What a worker sends when it fails a row.
#[derive(Deserialize, Default)]
pub struct QueueFailure {
    #[serde(default)]
    error: Option<String>,
}

/// The body of a fail call is optional: an empty one is no error text.
fn parse_failure(body: &Bytes) -> Result<QueueFailure, ApiError> {
    if body.is_empty() {
        return Ok(QueueFailure::default());
    }
    serde_json::from_slice(body).map_err(|error| ApiError::BadRequest(error.to_string()))
}

/// The worker that claims a row.
#[derive(Deserialize)]
#[allow(dead_code)]
struct ClaimQuery {
    worker: Option<String>,
}

/// What a queue handler can fail with: the project's ApiError, or a row that is not in the status the call expects (409, in the problem-details form of the other stacks, whether or not the project has sign-in).
enum QueueError {
    Api(ApiError),
    WrongState(&'static str),
}

type QueueResult<T> = Result<T, QueueError>;

impl From<ApiError> for QueueError {
    fn from(error: ApiError) -> Self {
        QueueError::Api(error)
    }
}

impl From<sqlx::Error> for QueueError {
    fn from(error: sqlx::Error) -> Self {
        QueueError::Api(error.into())
    }
}

impl IntoResponse for QueueError {
    fn into_response(self) -> Response {
        match self {
            QueueError::Api(error) => error.into_response(),
            QueueError::WrongState(detail) => {
                let body = serde_json::json!({ "type": PROBLEM_WRONG_STATE, "title": "Wrong state", "status": 409, "detail": detail });
                let mut response = (StatusCode::CONFLICT, Json(body)).into_response();
                response.headers_mut().insert(header::CONTENT_TYPE, HeaderValue::from_static("application/problem+json"));
                response
            }
        }
    }
}

/// A text cut to the length of its column.
#[allow(dead_code)]
fn fit(text: Option<String>, length: usize) -> Option<String> {
    text.map(|t| if t.chars().count() <= length { t } else { t.chars().take(length).collect() })
}
"""";

    private const string TableText = """"

// ===== §S =====

/// The rows in each status and the oldest pending row.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Counts§S {
    pending: i64,
    claimed: i64,
    done: i64,
    failed: i64,
    oldest_pending_id: Option<§KT>,
    #[serde(with = "crate::support::json_date_time_option")]
    oldest_pending_on: Option<NaiveDateTime>,
}

fn router_§M() -> Router<Pool> {
    Router::new()
        .route("§ROUTE/stats", get(stats_§M))
        .route("§ROUTE/failed", get(failed_§M))
        .route("§ROUTE/claim", post(claim_§M))
        .route("§ROUTE/{id}/renew", post(renew_§M))
        .route("§ROUTE/{id}/complete", post(complete_§M))
        .route("§ROUTE/{id}/fail", post(fail_§M))
        .route("§ROUTE/{id}/retry", post(retry_§M))
}

/// A claim nobody completed within CLAIM_MINUTES goes back to pending.
async fn release_stale_§M(pool: &Pool) -> Result<(), sqlx::Error> {
    sqlx::query(§SQL_RELEASE).bind(chrono::Local::now().naive_local() - chrono::Duration::minutes(CLAIM_MINUTES)).execute(pool).await?;
    Ok(())
}

async fn answer_§M(pool: &Pool, id: §KT, changed: u64, detail: &'static str) -> QueueResult<Json<§S>> {
    let row = repo_§M::get_by_id(pool, id).await?.ok_or(ApiError::NotFound)?;
    if changed == 1 { Ok(Json(row)) } else { Err(QueueError::WrongState(detail)) }
}

async fn stats_§M(State(pool): State<Pool>) -> QueueResult<Json<Counts§S>> {
    release_stale_§M(&pool).await?;
    let rows: Vec<(String, i64)> = sqlx::query_as(§SQL_COUNTS).fetch_all(&pool).await?;
    let of = |status: &str| rows.iter().filter(|(s, _)| s == status).map(|(_, n)| *n).sum::<i64>();
    let oldest: Option<(§KT, §CT)> = sqlx::query_as(§SQL_OLDEST).fetch_optional(&pool).await?;
    Ok(Json(Counts§S {
        pending: of("§PENDING"),
        claimed: of("§CLAIMED"),
        done: of("§DONE"),
        failed: of("§FAILED"),
        oldest_pending_id: oldest.as_ref().map(|(id, _)| *id),
        oldest_pending_on: oldest.as_ref().and_then(|(_, on)| §ON),
    }))
}

async fn failed_§M(State(pool): State<Pool>) -> QueueResult<Json<Vec<§S>>> {
    Ok(Json(sqlx::query_as::<_, §S>(§SQL_FAILED_ROWS).fetch_all(&pool).await?))
}

/// The oldest pending row, taken with one UPDATE that still tests that it is pending; 204 when there is none.
async fn claim_§M(State(pool): State<Pool>, Query(query): Query<ClaimQuery>) -> QueueResult<Response> {
    release_stale_§M(&pool).await?;
    for _ in 0..CLAIM_ATTEMPTS {
        let next: Option<§KT> = sqlx::query_scalar(§SQL_NEXT).fetch_optional(&pool).await?;
        let Some(id) = next else {
            return Ok(StatusCode::NO_CONTENT.into_response());
        };
        let changed = sqlx::query(§SQL_CLAIM).bind(§NOWCLAIMED)§CLAIMBY.bind(id).execute(&pool).await?.rows_affected();
        if changed == 1 {
            let row = repo_§M::get_by_id(&pool, id).await?.ok_or(ApiError::NotFound)?;
            return Ok(Json(row).into_response());
        }
    }
    Ok(StatusCode::NO_CONTENT.into_response())
}

/// The worker that holds a claim says it is still working: the claim starts again, so it is not released after CLAIM_MINUTES.
async fn renew_§M(State(pool): State<Pool>, Path(id): Path<§KT>) -> QueueResult<Json<§S>> {
    let changed = sqlx::query(§SQL_RENEW).bind(§NOWCLAIMED).bind(id).execute(&pool).await?.rows_affected();
    answer_§M(&pool, id, changed, NOT_CLAIMED).await
}

async fn complete_§M(State(pool): State<Pool>, Path(id): Path<§KT>) -> QueueResult<Json<§S>> {
    let changed = sqlx::query(§SQL_COMPLETE).bind(§NOWPROCESSED).bind(id).execute(&pool).await?.rows_affected();
    answer_§M(&pool, id, changed, NOT_CLAIMED).await
}

async fn fail_§M(State(pool): State<Pool>, Path(id): Path<§KT>, body: Bytes) -> QueueResult<Json<§S>> {
    let failure = parse_failure(&body)?;
§ERRORLINE    let changed = sqlx::query(§SQL_FAIL).bind(§NOWPROCESSED)§ERRORBIND.bind(id).execute(&pool).await?.rows_affected();
    answer_§M(&pool, id, changed, NOT_CLAIMED).await
}

async fn retry_§M(State(pool): State<Pool>, Path(id): Path<§KT>) -> QueueResult<Json<§S>> {
    let changed = sqlx::query(§SQL_RETRY).bind(id).execute(&pool).await?.rows_affected();
    answer_§M(&pool, id, changed, NOT_FAILED).await
}
"""";
}
