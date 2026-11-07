namespace CodeGenNew.Core;

/// <summary> <c>tests/api/inbound_&lt;table&gt;_routes.rs</c> of the Rust API (RS_TestInbound): the cases of the generated ASP.NET tests (<see cref="ApiInboundTests"/>) played through the router of <c>RS_Inbound</c> over the scratch
/// database, with the same words as the Python tests (<see cref="PythonInboundTests"/>). The tests set the shared secret in the environment once and remove the events they wrote (their bodies hold a marker). </summary>
public static class RustInboundTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && Tables(database, project).Count > 0;

    public static IReadOnlyList<InboundShape> Tables(DatabaseModel database, ProjectSettings project) => PythonInboundTests.Tables(database, project);

    public static string Module(InboundShape t, ProjectSettings project) => $"inbound_{RustTable.Of(t.Table, project).Module}_routes";

    public static string File(InboundShape t, DatabaseModel database, ProjectSettings project)
    {
        var main = RustTable.Of(t.Table, project);
        var di = DialectInfo.For(database.Dialect);
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string body = JsonNames.Camel(t.Body.Name), key = JsonNames.Camel(t.Key.Name);
        int limit = ApiInboundTests.Limit(t, project);
        string cleanup = $"DELETE FROM {di.QuoteTable(t.Table.SchemaName, t.Table.DbTableName)} WHERE {di.Quote(t.Body.DbName)} LIKE '%{PythonInboundTests.Marker}%'";
        string testSource = InboundPlan.TestSource(project);
        string path = t.Source is null ? "/receive" : "/receive/" + testSource;
        string source = t.Source is null ? "" : $"    assert_eq!(row[\"{JsonNames.Camel(t.Source.Name)}\"], \"{testSource}\", \"{{}}\", listed.body);\n";
        var own = InboundPlan.SourceSecrets(project).FirstOrDefault();
        string ownConstants = own.Source is null ? "" : $"const OWN_SOURCE: &str = {Str(own.Source)};\nconst OWN_SECRET_NAME: &str = {Str(own.Secret)};\nconst OWN_SECRET: &str = \"test-own-secret-zz\";\n";
        string ownSet = own.Source is null ? "" : "            std::env::set_var(OWN_SECRET_NAME, OWN_SECRET);\n";
        string ownTests = own.Source is null ? "" : OwnTestsText.Replace("§OWNSOURCEASSERT", t.Source is null ? "" : $"    assert_eq!(rows[0][\"{JsonNames.Camel(t.Source.Name)}\"], OWN_SOURCE, \"{{}}\", listed.body);\n");
        string eventTest = t.EventId is null ? "" : """"

#[tokio::test]
async fn an_event_id_is_kept_once() {
    let Some(api) = ready().await else { return };
    let event = format!("zz-evt-{MARKER}");
    let first = send(&api, "/receive", &message(Some(&event)), SECRET, now(), None).await;
    let second = send(&api, "/receive", &message(Some(&event)), SECRET, now(), None).await;
    let header = send(&api, "/receive", &message(None), SECRET, now(), Some(&event)).await;
    let listed = api.get(&format!("{ROUTE}/unprocessed")).await;
    clear(&api).await;
    assert_eq!(first.json()["duplicate"], false, "{}", first.body);
    assert!(second.json()["duplicate"] == true && second.json()["id"] == first.json()["id"], "{}", second.body);
    assert_eq!(header.json()["duplicate"], true, "the id in the header counts the same: {}", header.body);
    assert_eq!(ours(&listed).len(), 1, "{}", listed.body);
}
"""";
        string authTest = project.ApiAuth ? """"

#[tokio::test]
async fn without_a_sign_in_the_worker_routes_answer_401() {
    let Some(api) = ready().await else { return };
    let list = api.anonymous().get(&format!("{ROUTE}/unprocessed")).await;
    let done = api.anonymous().post_empty(&format!("{ROUTE}/1/processed")).await;
    assert_eq!(list.status, StatusCode::UNAUTHORIZED, "{}", list.body);
    assert_eq!(done.status, StatusCode::UNAUTHORIZED, "{}", done.body);
}
"""" : "";
        int clientLimit = Math.Min(t.BodyLength > 0 ? t.BodyLength : int.MaxValue, project.InboundClientMaxBytes);
        string clientSource = t.Source is null ? "" : $"    assert_eq!(rows[0][\"{JsonNames.Camel(t.Source.Name)}\"], \"{InboundWords.ClientSource}\", \"{{}}\", listed.body);\n";
        string clientTests = !t.Client ? "" : ClientTestsText.Replace("§CLIENTSOURCEASSERT", clientSource).Replace("§CLIENTLIMIT", (clientLimit + 1).ToString())
            .Replace("§CLIENTRATE", project.InboundClientPerMinute.ToString()).Replace("§T_RATE", InboundWords.RateLimitedType.Split('/').Last());
        return TemplateText.FillAll(Text, new (string, string)[]
        {
            ("§CLIENTENABLED", t.Client ? "true" : "false"), ("§UNPROCLABEL", InboundScreens.UnprocessedLabel), ("§OWNCONSTANTS\n", ownConstants), ("§OWNSET", ownSet), ("§OWNTESTS", ownTests), ("§CLIENTTESTS", clientTests), ("§EVENTTEST", eventTest), ("§AUTHTEST", authTest), ("§SOURCEASSERT", source), ("§PATH", Str(path)), ("§BODY", body), ("§KEYFIELD", key), ("§LIMIT", (limit + 1).ToString()),
            ("§ROUTE", Str(main.Route)), ("§CLEANUP", RustTable.Raw(cleanup)), ("§SECRETNAME", Str(project.InboundSecret)), ("§HEADER", Str(InboundWords.SignatureHeader)), ("§EVENTHEADER", Str(InboundWords.EventHeader)),
            ("§MARKER", Str(PythonInboundTests.Marker)), ("§T_EXPIRED", InboundWords.ExpiredType.Split('/').Last()), ("§T_LARGE", InboundWords.TooLargeType.Split('/').Last())
        }) + "\n";
    }

    private const string OwnTestsText = """"

#[tokio::test]
async fn a_sender_with_its_own_secret_is_checked_against_that_secret_only() {
    let Some(api) = ready().await else { return };
    let path = format!("/receive/{}", OWN_SOURCE.to_uppercase());
    let shared = send(&api, &path, &message(None), SECRET, now(), None).await;
    let own = send(&api, &path, &message(None), OWN_SECRET, now(), None).await;
    let listed = api.get(&format!("{ROUTE}/unprocessed")).await;
    clear(&api).await;
    assert_eq!(shared.status, StatusCode::UNAUTHORIZED, "the shared secret does not work for this sender: {}", shared.body);
    assert!(own.status == StatusCode::OK && own.json()["received"] == true, "{}", own.body);
    let rows = ours(&listed);
    assert_eq!(rows.len(), 1, "{}", listed.body);
§OWNSOURCEASSERT}
"""";

    private const string ClientTestsText = """"

#[tokio::test]
async fn a_client_message_is_kept_with_no_signature_and_the_answer_holds_nothing() {
    let Some(api) = ready().await else { return };
    let path = format!("{ROUTE}/collect");
    let sent = api.anonymous().post_text(&path, "application/json", message(None)).await;
    let listed = api.get(&format!("{ROUTE}/unprocessed")).await;
    let empty = api.anonymous().post_text(&path, "application/json", String::new()).await;
    let large = api.anonymous().post_text(&path, "application/json", "x".repeat(§CLIENTLIMIT)).await;
    clear(&api).await;
    assert!(sent.status == StatusCode::ACCEPTED && sent.body.is_empty(), "a write-only route tells the browser nothing: {}", sent.body);
    let rows = ours(&listed);
    assert_eq!(rows.len(), 1, "the message is listed as unprocessed: {}", listed.body);
§CLIENTSOURCEASSERT    assert_eq!(empty.status, StatusCode::BAD_REQUEST, "{}", empty.body);
    assert_eq!(large.status, StatusCode::PAYLOAD_TOO_LARGE, "{}", large.body);
}

#[tokio::test]
async fn a_caller_over_the_limit_of_requests_a_minute_is_refused() {
    if now() % 60 > 52 {
        tokio::time::sleep(std::time::Duration::from_secs(8)).await;
    }
    let Some(api) = ready().await else { return };
    let path = format!("{ROUTE}/collect");
    let client = api.anonymous();
    for number in 0..§CLIENTRATE {
        let allowed = client.post_text(&path, "application/json", message(None)).await;
        assert_eq!(allowed.status, StatusCode::ACCEPTED, "request {} is inside the limit: {}", number + 1, allowed.body);
    }
    let refused = client.post_text(&path, "application/json", message(None)).await;
    clear(&api).await;
    assert!(refused.status == StatusCode::TOO_MANY_REQUESTS && refused.json()["type"].as_str().is_some_and(|text| text.ends_with("§T_RATE")), "{}", refused.body);
}
"""";

    private const string Text = """"
// The inbound routes of one table: a bad, missing or old signature is refused and stores nothing, a signed message is kept and listed as unprocessed, marking it processed takes it out of the list, a body over the
// limit is refused. Written by RS_TestInbound; regenerated with the tables.
use axum::http::StatusCode;
use hmac::{Hmac, Mac};
use serde_json::{Value, json};
use sha2::Sha256;
use std::sync::{Mutex, MutexGuard, OnceLock};

use crate::test_support::*;

const ROUTE: &str = §ROUTE;
const CLEANUP: &str = §CLEANUP;
const SECRET_NAME: &str = §SECRETNAME;
const SIGNATURE_HEADER: &str = §HEADER;
const EVENT_HEADER: &str = §EVENTHEADER;
const MARKER: &str = §MARKER;
const SECRET: &str = "test-secret-zz";
§OWNCONSTANTS

/// The shared secret goes into the environment once, before the application under test reads it.
fn secret_set() {
    static ONCE: OnceLock<()> = OnceLock::new();
    ONCE.get_or_init(|| {
        // SAFETY: this runs once, before a request reads the environment, and nothing else here writes this variable
        unsafe {
            std::env::set_var(SECRET_NAME, SECRET);
§OWNSET        }
    });
}

/// The tests of one table count and remove the same rows, so they take turns even when cargo runs them side by side over one database.
static ROWS: Mutex<()> = Mutex::new(());

struct Ready {
    api: Client,
    _rows: MutexGuard<'static, ()>,
}

impl std::ops::Deref for Ready {
    type Target = Client;

    fn deref(&self) -> &Client {
        &self.api
    }
}

async fn ready() -> Option<Ready> {
    secret_set();
    let rows = ROWS.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let api = Client::start().await?;
    let _ = sqlx::query(CLEANUP).execute(&api.pool()).await;
    Some(Ready { api, _rows: rows })
}

async fn clear(api: &Client) {
    let _ = sqlx::query(CLEANUP).execute(&api.pool()).await;
}

fn now() -> i64 {
    chrono::Utc::now().timestamp()
}

fn message(event: Option<&str>) -> String {
    json!({ "id": event, "marker": MARKER }).to_string()
}

fn sign(text: &str, key: &str, at: i64) -> String {
    let mut mac = Hmac::<Sha256>::new_from_slice(key.as_bytes()).expect("a key");
    mac.update(format!("{at}.").as_bytes());
    mac.update(text.as_bytes());
    let hex: String = mac.finalize().into_bytes().iter().map(|byte| format!("{byte:02x}")).collect();
    format!("t={at},v1={hex}")
}

async fn send(api: &Client, path: &str, text: &str, key: &str, at: i64, event: Option<&str>) -> Reply {
    let mut client = api.anonymous().with_header(SIGNATURE_HEADER, &sign(text, key, at));
    if let Some(event) = event {
        client = client.with_header(EVENT_HEADER, event);
    }
    client.post_text(&format!("{ROUTE}{path}"), "application/json", text.to_string()).await
}

fn ours(listed: &Reply) -> Vec<Value> {
    listed.json().as_array().cloned().unwrap_or_default().into_iter().filter(|row| row["§BODY"].as_str().is_some_and(|text| text.contains(MARKER))).collect()
}

#[tokio::test]
async fn a_message_with_no_signature_a_wrong_one_or_an_old_one_is_refused_and_stores_nothing() {
    let Some(api) = ready().await else { return };
    let unsigned = api.anonymous().post_text(&format!("{ROUTE}/receive"), "application/json", message(None)).await;
    let wrong = send(&api, "/receive", &message(None), "not-the-secret", now(), None).await;
    let old = send(&api, "/receive", &message(None), SECRET, now() - 3600, None).await;
    let listed = api.get(&format!("{ROUTE}/unprocessed")).await;
    clear(&api).await;
    assert_eq!(unsigned.status, StatusCode::UNAUTHORIZED, "{}", unsigned.body);
    assert_eq!(wrong.status, StatusCode::UNAUTHORIZED, "{}", wrong.body);
    assert!(old.status == StatusCode::BAD_REQUEST && old.json()["type"].as_str().is_some_and(|text| text.ends_with("§T_EXPIRED")), "{}", old.body);
    assert!(ours(&listed).is_empty(), "{}", listed.body);
}

#[tokio::test]
async fn a_signed_message_is_kept_listed_as_unprocessed_and_marked_processed() {
    let Some(api) = ready().await else { return };
    let text = message(None);
    let received = send(&api, §PATH, &text, SECRET, now(), None).await;
    let listed = api.get(&format!("{ROUTE}/unprocessed")).await;
    let rows = ours(&listed);
    let id = received.json()["id"].clone();
    let done = api.post_empty(&format!("{ROUTE}/{id}/processed")).await;
    let after = api.get(&format!("{ROUTE}/unprocessed")).await;
    let again = api.post_empty(&format!("{ROUTE}/{id}/processed")).await;
    let missing = api.post_empty(&format!("{ROUTE}/2147483647/processed")).await;
    clear(&api).await;
    assert!(received.status == StatusCode::OK && received.json()["received"] == true, "{}", received.body);
    assert_eq!(rows.len(), 1, "the event is listed as unprocessed: {}", listed.body);
    let row = &rows[0];
    assert_eq!(row["§BODY"], text.as_str(), "the raw body is kept as it came");
§SOURCEASSERT    assert_eq!(row["§KEYFIELD"], id, "{}", listed.body);
    assert!(done.status == StatusCode::OK && done.json()["processed"] == true, "{}", done.body);
    assert!(ours(&after).is_empty(), "a processed event is no longer listed: {}", after.body);
    assert!(again.status == StatusCode::OK && again.json()["alreadyProcessed"] == true, "marking it again changes nothing: {}", again.body);
    assert_eq!(missing.status, StatusCode::NOT_FOUND, "{}", missing.body);
}
§EVENTTEST
#[tokio::test]
async fn the_status_counts_the_events_that_wait() {
    let Some(api) = ready().await else { return };
    let before = api.get(&format!("{ROUTE}/status")).await;
    let received = send(&api, "/receive", &message(None), SECRET, now(), None).await;
    let after = api.get(&format!("{ROUTE}/status")).await;
    let id = received.json()["id"].clone();
    let done = api.post_empty(&format!("{ROUTE}/{id}/processed")).await;
    let end = api.get(&format!("{ROUTE}/status")).await;
    clear(&api).await;
    assert_eq!(after.json()["unprocessed"].as_i64().unwrap_or(-1), before.json()["unprocessed"].as_i64().unwrap_or(-2) + 1, "the new event waits: {}", after.body);
    assert_eq!(after.json()["total"].as_i64().unwrap_or(-1), before.json()["total"].as_i64().unwrap_or(-2) + 1, "{}", after.body);
    assert!(after.json()["oldestUnprocessedId"].is_number(), "{}", after.body);
    assert!(after.json()["summary"].as_str().is_some_and(|text| text.starts_with("§UNPROCLABEL")), "{}", after.body);
    assert_eq!(after.json()["collect"]["enabled"], §CLIENTENABLED, "{}", after.body);
    assert_eq!(done.status, StatusCode::OK, "{}", done.body);
    assert_eq!(end.json()["unprocessed"], before.json()["unprocessed"], "a processed event no longer waits: {}", end.body);
}
§CLIENTTESTS§OWNTESTS
#[tokio::test]
async fn a_body_over_the_limit_is_refused() {
    let Some(api) = ready().await else { return };
    let text = "x".repeat(§LIMIT);
    let response = send(&api, "/receive", &text, SECRET, now(), None).await;
    assert!(response.status == StatusCode::PAYLOAD_TOO_LARGE && response.json()["type"].as_str().is_some_and(|text| text.ends_with("§T_LARGE")), "{}", response.body);
}
§AUTHTEST
"""";
}
