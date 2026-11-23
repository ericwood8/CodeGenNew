using CodeGenNew.Core.Payment;

namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/ledger_routes.rs</c> of the Rust API (RS_TestLedger): the same cases and words as the Python tests (<see cref="PythonLedgerTests"/>, <see cref="PaymentWords"/>), played in process over a scratch database.
/// A ledger keeps its rows, so each test adds a document of its own. </summary>
public static class RustLedgerTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && LedgerTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = LedgerTestPlan.For(database, project)!;
        var names = new (string, string)[]
        {
            ("§DOCROUTE", RustNames.Str(RustTable.Of(plan.Shape.DocumentTable, project).Route)), ("§ROWROUTE", RustNames.Str(RustTable.Of(plan.Shape.Table, project).Route)),
            ("§DOCBODY", plan.DocumentBody), ("§ROWBODY", plan.RowBody),
            ("§DOCKEY", plan.DocumentKeyJson), ("§TOTAL", plan.TotalJson), ("§ROWKEY", plan.KeyJson), ("§DOC", plan.DocumentJson), ("§KIND", plan.KindJson), ("§STATUS", plan.StatusJson),
            ("§AMOUNT", plan.AmountJson), ("§GATEWAY", plan.GatewayJson), ("§IDEM", plan.IdempotencyJson),
            ("§T_REFUND", PaymentWords.RefundOverType), ("§T_CAPTURE", PaymentWords.CaptureOverType), ("§T_DUP", PaymentWords.LedgerDuplicateType),
            ("§RECORD", LedgerPlan.RecordRoute), ("§PAID", LedgerPlan.PaidRoute),
        };
        return TemplateText.FillAll(Text, names) + "\n";
    }

    private const string Text = """"
// The ledger through the router: a row is recorded against a document and the paid amount is worked out from the succeeded rows, a refund is negative and never more than has been paid, a capture never more than the total,
// a retried call answers with the stored row and a key or a gateway id is recorded once. Written by RS_TestLedger; regenerated with the tables. A ledger keeps its rows, so each test adds a document of its own.
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const DOCUMENTS: &str = §DOCROUTE;
const ROWS: &str = §ROWROUTE;

async fn document(api: &Client, total: f64) -> Value {
    let mut body = json!(§DOCBODY);
    body["§TOTAL"] = json!(total);
    let made = api.post(DOCUMENTS, &body).await;
    assert_eq!(made.status, StatusCode::CREATED, "{}", made.body);
    made.json()["§DOCKEY"].clone()
}

async fn record(api: &Client, document: &Value, kind: &str, amount: f64, status: &str, gateway: Option<&str>, key: Option<&str>) -> Reply {
    let tag = uuid::Uuid::new_v4().simple().to_string();
    let mut body = json!(§ROWBODY);
    body["§DOC"] = document.clone();
    body["§KIND"] = json!(kind);
    body["§STATUS"] = json!(status);
    body["§AMOUNT"] = json!(amount);
    body["§GATEWAY"] = json!(gateway.map(String::from).unwrap_or(format!("g{tag}")));
    body["§IDEM"] = json!(key.map(String::from).unwrap_or(format!("k{tag}")));
    api.post(&format!("{ROWS}/§RECORD"), &body).await
}

async fn paid(api: &Client, document: &Value) -> Value {
    let answer = api.get(&format!("{ROWS}/§PAID?document={document}")).await;
    assert_eq!(answer.status, StatusCode::OK, "{}", answer.body);
    answer.json()
}

fn number(value: &Value) -> f64 {
    value.as_f64().or_else(|| value.as_str().and_then(|text| text.parse().ok())).unwrap_or(f64::NAN)
}

#[tokio::test]
async fn what_a_document_has_been_paid_is_worked_out_from_the_succeeded_rows() {
    let Some(api) = Client::start().await else { return };
    let doc = document(&api, 300.0).await;
    let first = record(&api, &doc, "Authorize", 300.0, "succeeded", None, None).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    assert_eq!(first.json()["§KIND"], json!("authorize"), "the kind is written in lower case");
    assert_eq!(record(&api, &doc, "capture", 200.0, "succeeded", None, None).await.status, StatusCode::CREATED);
    assert_eq!(record(&api, &doc, "capture", 40.0, "pending", None, None).await.status, StatusCode::CREATED);
    assert_eq!(record(&api, &doc, "refund", -50.0, "succeeded", None, None).await.status, StatusCode::CREATED);
    let answer = paid(&api, &doc).await;
    assert_eq!(number(&answer["captured"]), 200.0);
    assert_eq!(number(&answer["refunded"]), 50.0);
    assert_eq!(number(&answer["paid"]), 150.0);
    assert_eq!(number(&answer["remaining"]), 150.0);
    assert_eq!(answer["settled"], json!(false));
}

#[tokio::test]
async fn a_refund_is_negative_and_never_more_than_has_been_paid_and_a_capture_never_more_than_the_total() {
    let Some(api) = Client::start().await else { return };
    let doc = document(&api, 100.0).await;
    assert_eq!(record(&api, &doc, "refund", 10.0, "succeeded", None, None).await.status, StatusCode::BAD_REQUEST, "a refund is a negative row");
    assert_eq!(record(&api, &doc, "capture", -10.0, "succeeded", None, None).await.status, StatusCode::BAD_REQUEST);
    assert_eq!(record(&api, &doc, "capture", 60.0, "succeeded", None, None).await.status, StatusCode::CREATED);
    let too_much = record(&api, &doc, "refund", -61.0, "succeeded", None, None).await;
    assert!(too_much.status == StatusCode::BAD_REQUEST && too_much.body.contains("§T_REFUND"), "{}", too_much.body);
    let over = record(&api, &doc, "capture", 41.0, "succeeded", None, None).await;
    assert!(over.status == StatusCode::BAD_REQUEST && over.body.contains("§T_CAPTURE"), "{}", over.body);
    assert_eq!(record(&api, &doc, "capture", 40.0, "succeeded", None, None).await.status, StatusCode::CREATED);
    assert_eq!(paid(&api, &doc).await["settled"], json!(true));
}

#[tokio::test]
async fn a_retried_call_answers_with_the_stored_row_and_a_key_or_gateway_id_is_recorded_once() {
    let Some(api) = Client::start().await else { return };
    let doc = document(&api, 100.0).await;
    let key = format!("retry-{}", uuid::Uuid::new_v4().simple());
    let first = record(&api, &doc, "capture", 30.0, "succeeded", None, Some(&key)).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    let stored = first.json();
    let retry = record(&api, &doc, "capture", 30.0, "succeeded", None, Some(&key)).await;
    assert!(retry.status == StatusCode::OK && retry.json()["§ROWKEY"] == stored["§ROWKEY"], "{}", retry.body);
    assert_eq!(number(&paid(&api, &doc).await["captured"]), 30.0, "the retry wrote nothing");
    let other = record(&api, &doc, "capture", 5.0, "succeeded", None, Some(&key)).await;
    assert!(other.status == StatusCode::CONFLICT && other.body.contains("§T_DUP"), "{}", other.body);
    let gateway = stored["§GATEWAY"].as_str().unwrap_or_default().to_string();
    assert_eq!(record(&api, &doc, "capture", 5.0, "succeeded", Some(&gateway), None).await.status, StatusCode::CONFLICT);
}

#[tokio::test]
async fn a_row_is_never_changed_or_deleted() {
    let Some(api) = Client::start().await else { return };
    let doc = document(&api, 100.0).await;
    let row = record(&api, &doc, "capture", 10.0, "succeeded", None, None).await.json();
    assert_eq!(api.put(&format!("{ROWS}/{}", row["§ROWKEY"]), &row).await.status, StatusCode::METHOD_NOT_ALLOWED);
    assert_eq!(api.delete(&format!("{ROWS}/{}", row["§ROWKEY"])).await.status, StatusCode::METHOD_NOT_ALLOWED);
}
"""";
}
