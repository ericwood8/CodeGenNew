using CodeGenNew.Core.Stock;

namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/stock_routes.rs</c> of the Rust API (RS_TestStock): the same cases and words as the Python tests (<see cref="PythonStockTests"/>, <see cref="StockWords"/>), played in process over a scratch database. Each test adds an item of its own. </summary>
public static class RustStockTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && StockTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = StockTestPlan.For(database, project)!;
        var names = new (string, string)[]
        {
            ("§ITEMROUTE", RustNames.Str(plan.ItemRoute)), ("§LEDGERROUTE", RustNames.Str(plan.LedgerRoute)), ("§ITEMBODY", plan.ItemBody), ("§ITEMKEY", plan.ItemKeyJson),
            ("§T_SHORT", StockWords.ShortType), ("§T_CLOSED", StockWords.ClosedType), ("§T_BELOW", StockWords.BelowReservedType),
        };
        return TemplateText.FillAll(Text, names) + "\n";
    }

    private const string Text = """"
// Stock through the routes of the ledger: a receipt puts stock on hand, a reservation takes it out of what is available with one guarded update, a commit turns a hold into a sale, a release gives it back and a stock-take writes
// an adjustment row. Written by RS_TestStock; regenerated with the tables. Each test adds an item of its own.
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const ITEMS: &str = §ITEMROUTE;
const LEDGER: &str = §LEDGERROUTE;

async fn item(api: &Client) -> Value {
    let made = api.post(ITEMS, &json!(§ITEMBODY)).await;
    assert_eq!(made.status, StatusCode::CREATED, "{}", made.body);
    made.json()["§ITEMKEY"].clone()
}

async fn post(api: &Client, route: &str, body: Value) -> Reply {
    api.post(&format!("{LEDGER}/{route}"), &body).await
}

async fn level(api: &Client, item: &Value) -> (i64, i64, i64) {
    let answer = api.get(&format!("{LEDGER}/available?item={item}")).await;
    assert_eq!(answer.status, StatusCode::OK, "{}", answer.body);
    let body = answer.json();
    (body["onHand"].as_i64().unwrap_or(-1), body["reserved"].as_i64().unwrap_or(-1), body["available"].as_i64().unwrap_or(-1))
}

#[tokio::test]
async fn a_receipt_a_reservation_and_a_commit_move_the_level_and_write_the_ledger() {
    let Some(api) = Client::start().await else { return };
    let item = item(&api).await;
    assert_eq!(post(&api, "receive", json!({ "item": item, "quantity": 10 })).await.status, StatusCode::CREATED);
    let hold = post(&api, "reserve", json!({ "item": item, "quantity": 4 })).await;
    assert_eq!(hold.status, StatusCode::CREATED, "{}", hold.body);
    assert_eq!(level(&api, &item).await, (10, 4, 6));
    let id = hold.json()["reservation"].clone();
    let sold = post(&api, &format!("commit/{id}"), json!({})).await;
    assert_eq!(sold.status, StatusCode::OK, "{}", sold.body);
    assert_eq!(level(&api, &item).await, (6, 0, 6), "a sale takes the quantity from on hand and from reserved");
    assert_eq!(post(&api, &format!("commit/{id}"), json!({})).await.status, StatusCode::CONFLICT, "a hold is committed once");
}

#[tokio::test]
async fn the_last_item_goes_to_one_buyer_and_a_release_gives_a_hold_back() {
    let Some(api) = Client::start().await else { return };
    let item = item(&api).await;
    post(&api, "receive", json!({ "item": item, "quantity": 3 })).await;
    let first = post(&api, "reserve", json!({ "item": item, "quantity": 3 })).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    let refused = post(&api, "reserve", json!({ "item": item, "quantity": 1 })).await;
    assert!(refused.status == StatusCode::CONFLICT && refused.body.contains("§T_SHORT"), "{}", refused.body);
    let id = first.json()["reservation"].clone();
    assert_eq!(post(&api, &format!("release/{id}"), json!({})).await.status, StatusCode::OK);
    assert_eq!(level(&api, &item).await, (3, 0, 3));
    let closed = post(&api, &format!("commit/{id}"), json!({})).await;
    assert!(closed.status == StatusCode::CONFLICT && closed.body.contains("§T_CLOSED"), "{}", closed.body);
    assert_eq!(post(&api, "commit/987654", json!({})).await.status, StatusCode::NOT_FOUND);
}

#[tokio::test]
async fn a_stock_take_writes_an_adjustment_and_a_count_below_what_is_reserved_is_refused() {
    let Some(api) = Client::start().await else { return };
    let item = item(&api).await;
    post(&api, "receive", json!({ "item": item, "quantity": 10 })).await;
    post(&api, "reserve", json!({ "item": item, "quantity": 4 })).await;
    let below = post(&api, "adjust", json!({ "item": item, "counted": 3 })).await;
    assert!(below.status == StatusCode::CONFLICT && below.body.contains("§T_BELOW"), "{}", below.body);
    let counted = post(&api, "adjust", json!({ "item": item, "counted": 9 })).await;
    assert_eq!(counted.status, StatusCode::OK, "{}", counted.body);
    assert_eq!(counted.json()["difference"], json!(-1));
    assert_eq!(level(&api, &item).await, (9, 4, 5));
    let checked = api.get(&format!("{LEDGER}/check")).await.json();
    let differences = checked["differences"].as_array().cloned().unwrap_or_default();
    assert!(differences.iter().all(|d| d["item"] != item), "the level agrees with the ledger");
}
"""";
}
