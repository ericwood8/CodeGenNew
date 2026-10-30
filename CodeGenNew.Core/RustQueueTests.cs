namespace CodeGenNew.Core;

/// <summary> <c>tests/api/queue_routes.rs</c> of the Rust API (RS_TestQueue): three pending rows (<see cref="QueueTestSeed"/>) are added to the scratch database before each test and removed after it, and the cases play
/// the routes through the router: the claim takes the oldest rows in turn, a row is completed once, a failed row goes back to pending until the retries are used, Retry, a row that is not there and a stale claim.
/// The same cases and words as the Python tests (<see cref="QueueWords"/>). </summary>
public static class RustQueueTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && Seeds(database, project).Count > 0;

    public static IReadOnlyList<QueueTestSeed> Seeds(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => QueuePlan.For(t, project)).OfType<QueueTable>().OrderBy(q => q.Table.TableName, StringComparer.OrdinalIgnoreCase)
            .Select(q => QueueTestSeed.For(q, database, project, out _)).OfType<QueueTestSeed>().ToList();

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var seed = Seeds(database, project)[0];
        var q = seed.Queue;
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string List(IReadOnlyList<string> statements) => string.Join("\n", statements.Select(s => "    " + RustTable.Raw(s) + ","));
        string by = q.ClaimedBy is null ? "" : $"    assert!(first.json()[\"{Json(q.ClaimedBy)}\"] == \"w1\" && second.json()[\"{Json(q.ClaimedBy)}\"] == \"w2\", \"{{}}\", first.body);\n";
        string error = q.LastError is null ? "" : $"        assert_eq!(failed.json()[\"{Json(q.LastError)}\"], \"boom\");\n";
        // what a producer sends: the columns that must have a value, and junk for the ones the queue sets itself
        var payload = q.Payload.Select(c => $"{Str(Json(c))}: {ApiSampleValues.Value(c, project, q.Table.TableName)}");
        var junk = new[] { (q.Status, "\"done\""), (q.Retry, "7"), (q.Claimed, "\"2001-01-01T00:00:00\""), (q.ClaimedBy, "\"someone\""), (q.LastError, "\"old\"") }
            .Where(j => j.Item1 is not null).Select(j => $"{Str(Json(j.Item1!))}: {j.Item2}");
        var di = DialectInfo.For(database.Dialect);
        string deleteOne = $"DELETE FROM {di.QuoteTable(q.Table.SchemaName, q.Table.DbTableName)} WHERE {di.Quote(q.Key.DbName)} = {di.Placeholder(1)}";
        string required = q.Payload.Count == 0 ? "" : """"

#[tokio::test]
async fn a_row_without_a_value_the_table_needs_is_not_enqueued() {
    let Some(api) = seeded().await else { return };
    let refused = api.post(ROUTE, &json!({})).await;
    clear(&api).await;
    assert_eq!(refused.status, StatusCode::BAD_REQUEST, "{}", refused.body);
}
"""";
        string text = Text;
        foreach (var (token, value) in new (string, string)[]
        {
            ("§REQUIRED", required), ("§PAYLOAD", "{ " + string.Join(", ", payload) + " }"), ("§JUNK", "{ " + string.Join(", ", junk) + " }"), ("§DELETEONE", Str(deleteOne)), ("§CREATEDON", Json(q.Created)), ("§BYASSERT", by), ("§ERRORASSERT", error),
            ("§SETUP", List(seed.Setup)), ("§CLEANUP", List(seed.Cleanup)), ("§STALESQL", RustTable.Raw(seed.StaleSql)), ("§ROUTE", Str(RustTable.Of(q.Table, project).Route)),
            ("§K1", seed.Keys[0].ToString()), ("§K2", seed.Keys[1].ToString()), ("§K3", seed.Keys[2].ToString()), ("§MAX", project.QueueMaxRetries.ToString()),
            ("§ID", Json(q.Key)), ("§STATUS", Json(q.Status)), ("§RETRY", Json(q.Retry)), ("§PROCESSED", Json(q.Processed)), ("§CLAIMEDON", Json(q.Claimed)),
            ("§T_STATE", QueueWords.WrongStateType), ("§W_NOTCLAIMED", Str(QueueWords.NotClaimed)), ("§W_NOTFAILED", Str(QueueWords.NotFailed)),
            ("§PENDING", QueueStatus.Pending), ("§CLAIMED", QueueStatus.Claimed), ("§DONE", QueueStatus.Done), ("§FAILED", QueueStatus.Failed)
        }.OrderByDescending(v => v.Item1.Length))
            text = text.Replace(token, value);
        return text + "\n";
    }

    private const string Text = """"
// The work queue through the router: the claim, complete, fail and retry, a row that is not there and a stale claim. Written by RS_TestQueue; regenerated with the tables.
// Three pending rows, the oldest of the table, are added to the scratch database before each test and removed after it.
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const ROUTE: &str = §ROUTE;
const K1: i64 = §K1;
const K2: i64 = §K2;
const K3: i64 = §K3;
const MAX_RETRIES: i64 = §MAX;
const NOT_CLAIMED: &str = §W_NOTCLAIMED;
const NOT_FAILED: &str = §W_NOTFAILED;

const SETUP: &[&str] = &[
§SETUP
];

const CLEANUP: &[&str] = &[
§CLEANUP
];

const STALE: &str = §STALESQL;
const DELETE_ONE: &str = §DELETEONE;

async fn seeded() -> Option<Client> {
    let api = Client::start().await?;
    let pool = api.pool();
    for &sql in CLEANUP {
        let _ = sqlx::query(sql).execute(&pool).await;
    }
    for &sql in SETUP {
        sqlx::query(sql).execute(&pool).await.unwrap_or_else(|error| panic!("{sql}: {error}"));
    }
    Some(api)
}

async fn clear(api: &Client) {
    let pool = api.pool();
    for &sql in CLEANUP {
        let _ = sqlx::query(sql).execute(&pool).await;
    }
}

#[tokio::test]
async fn a_claim_takes_the_oldest_pending_rows_in_turn() {
    let Some(api) = seeded().await else { return };
    let first = api.post_empty(&format!("{ROUTE}/claim?worker=w1")).await;
    let second = api.post_empty(&format!("{ROUTE}/claim?worker=w2")).await;
    let stats = api.get(&format!("{ROUTE}/stats")).await;
    clear(&api).await;
    assert_eq!(first.status, StatusCode::OK, "{}", first.body);
    assert!(first.json()["§ID"] == K1 && first.json()["§STATUS"] == "§CLAIMED" && !first.json()["§CLAIMEDON"].is_null(), "{}", first.body);
    assert!(second.status == StatusCode::OK && second.json()["§ID"] == K2, "{}", second.body);
§BYASSERT    assert!(stats.json()["claimed"].as_i64() >= Some(2) && stats.json()["pending"].as_i64() >= Some(1) && stats.json()["oldestPendingId"] == K3, "{}", stats.body);
}

#[tokio::test]
async fn a_row_is_completed_once() {
    let Some(api) = seeded().await else { return };
    api.post_empty(&format!("{ROUTE}/claim")).await;
    let done = api.post_empty(&format!("{ROUTE}/{K1}/complete")).await;
    let again = api.post_empty(&format!("{ROUTE}/{K1}/complete")).await;
    let unclaimed = api.post_empty(&format!("{ROUTE}/{K2}/complete")).await;
    clear(&api).await;
    assert_eq!(done.status, StatusCode::OK, "{}", done.body);
    assert!(done.json()["§STATUS"] == "§DONE" && !done.json()["§PROCESSED"].is_null(), "{}", done.body);
    assert_eq!(again.status, StatusCode::CONFLICT, "{}", again.body);
    assert!(again.json()["type"].as_str().is_some_and(|text| text.ends_with("§T_STATE")) && again.json()["detail"] == NOT_CLAIMED, "{}", again.body);
    assert_eq!(unclaimed.status, StatusCode::CONFLICT, "a row that nobody claimed cannot be completed: {}", unclaimed.body);
}

#[tokio::test]
async fn a_failed_row_goes_back_to_pending_until_the_retries_are_used_and_retry_puts_it_back() {
    let Some(api) = seeded().await else { return };
    let mut failed = None;
    for attempt in 1..=MAX_RETRIES {
        let claimed = api.post_empty(&format!("{ROUTE}/claim?worker=w1")).await;
        assert_eq!(claimed.json()["§ID"], K1, "{}", claimed.body);
        let reply = api.post(&format!("{ROUTE}/{K1}/fail"), &json!({ "error": "boom" })).await;
        assert_eq!(reply.status, StatusCode::OK, "{}", reply.body);
        assert_eq!(reply.json()["§RETRY"], attempt, "{}", reply.body);
        assert_eq!(reply.json()["§STATUS"], if attempt == MAX_RETRIES { "§FAILED" } else { "§PENDING" }, "{}", reply.body);
        failed = Some(reply);
    }
    let failed = failed.expect("at least one attempt");
    let listed = api.get(&format!("{ROUTE}/failed")).await;
    let retried = api.post_empty(&format!("{ROUTE}/{K1}/retry")).await;
    let again = api.post_empty(&format!("{ROUTE}/{K1}/retry")).await;
    let unclaimed = api.post_empty(&format!("{ROUTE}/{K1}/fail")).await;
    clear(&api).await;
§ERRORASSERT    assert!(!failed.json()["§PROCESSED"].is_null() && failed.json()["§CLAIMEDON"].is_null(), "{}", failed.body);
    let ids: Vec<Value> = listed.json().as_array().cloned().unwrap_or_default().iter().map(|row| row["§ID"].clone()).collect();
    assert!(ids.contains(&json!(K1)), "{}", listed.body);
    assert!(retried.status == StatusCode::OK && retried.json()["§STATUS"] == "§PENDING" && retried.json()["§RETRY"] == 0, "{}", retried.body);
    assert!(again.status == StatusCode::CONFLICT && again.json()["detail"] == NOT_FAILED, "{}", again.body);
    assert_eq!(unclaimed.status, StatusCode::CONFLICT, "a row that is not claimed cannot be failed: {}", unclaimed.body);
}

#[tokio::test]
async fn an_enqueued_row_is_pending_and_the_queues_own_columns_are_not_the_producers() {
    let Some(api) = seeded().await else { return };
    let mut sent = json!(§PAYLOAD);
    for (name, value) in json!(§JUNK).as_object().expect("an object") {
        sent[name] = value.clone();
    }
    let created = api.post(ROUTE, &sent).await;
    let row = created.json();
    if let Some(id) = row["§ID"].as_i64() {
        let _ = sqlx::query(DELETE_ONE).bind(id).execute(&api.pool()).await;
    }
    clear(&api).await;
    assert_eq!(created.status, StatusCode::CREATED, "{}", created.body);
    assert!(row["§STATUS"] == "§PENDING" && row["§RETRY"] == 0, "{}", created.body);
    assert!(row["§CLAIMEDON"].is_null() && row["§PROCESSED"].is_null(), "{}", created.body);
    assert!(!row["§CREATEDON"].as_str().unwrap_or("").starts_with("2001"), "the created stamp is the queue's: {}", created.body);
}
§REQUIRED
#[tokio::test]
async fn a_row_that_is_not_there_is_not_found() {
    let Some(api) = seeded().await else { return };
    let mut statuses = Vec::new();
    for action in ["complete", "fail", "retry", "renew"] {
        statuses.push(api.post_empty(&format!("{ROUTE}/{}/{action}", K3 + 500000)).await.status);
    }
    clear(&api).await;
    assert_eq!(statuses, [StatusCode::NOT_FOUND; 4]);
}

#[tokio::test]
async fn a_renewed_claim_is_not_released_and_only_a_claimed_row_can_be_renewed() {
    let Some(api) = seeded().await else { return };
    let first = api.post_empty(&format!("{ROUTE}/claim")).await;
    let _ = sqlx::query(STALE).execute(&api.pool()).await;
    let renewed = api.post_empty(&format!("{ROUTE}/{K1}/renew")).await;
    let next = api.post_empty(&format!("{ROUTE}/claim")).await;
    let done = api.post_empty(&format!("{ROUTE}/{K1}/complete")).await;
    let late = api.post_empty(&format!("{ROUTE}/{K1}/renew")).await;
    let pending = api.post_empty(&format!("{ROUTE}/{K3}/renew")).await;
    clear(&api).await;
    assert_eq!(first.json()["§ID"], K1, "{}", first.body);
    assert!(renewed.status == StatusCode::OK && renewed.json()["§STATUS"] == "§CLAIMED", "{}", renewed.body);
    assert_eq!(next.json()["§ID"], K2, "the renewed claim stays with its worker: {}", next.body);
    assert_eq!(done.status, StatusCode::OK, "{}", done.body);
    assert!(late.status == StatusCode::CONFLICT && late.json()["detail"] == NOT_CLAIMED, "a finished row has no claim to renew: {}", late.body);
    assert_eq!(pending.status, StatusCode::CONFLICT, "a pending row has no claim to renew: {}", pending.body);
}

#[tokio::test]
async fn a_claim_nobody_completed_goes_back_to_pending() {
    let Some(api) = seeded().await else { return };
    let first = api.post_empty(&format!("{ROUTE}/claim")).await;
    let _ = sqlx::query(STALE).execute(&api.pool()).await;
    api.get(&format!("{ROUTE}/stats")).await;
    let again = api.post_empty(&format!("{ROUTE}/claim")).await;
    clear(&api).await;
    assert_eq!(first.json()["§ID"], K1, "{}", first.body);
    assert_eq!(again.json()["§ID"], K1, "{}", again.body);
}
"""";
}
