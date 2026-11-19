namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/rate_document_routes.rs</c> of the Rust API (RS_TestRates): the same cases and words as <see cref="PythonRateDocumentTests"/>, played in process through the router. </summary>
public static class RustRateDocumentTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && RateDocumentTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = RateDocumentTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string? d1 = plan.DocumentDay("2024-01-01"), d2 = plan.DocumentDay("2024-03-01");
        string rate = Json(s.Rate), baseAmount = Json(s.BaseAmount);
        string setDay = s.Day is null ? "    let _ = day;\n" : $"    if let Some(day) = day {{\n        body[\"{Json(s.Day)}\"] = json!(day);\n    }}\n";
        string[] lines = s.Day is null
            ? [$"    assert!(close(number(&written[\"{rate}\"]) * 100.0, number(&written[\"{baseAmount}\"])), \"the amount times the rate: {{}}\", first.body);"]
            : [$"    assert!(close(number(&written[\"{rate}\"]), 1.1), \"the rate in force on the date: {{}}\", first.body);",
               $"    assert!(close(number(&written[\"{baseAmount}\"]), 110.0), \"the amount in the base currency: {{}}\", first.body);",
               "    assert!(moved.status.is_success(), \"{}\", moved.body);",
               $"    assert!(close(number(&again.json()[\"{rate}\"]), 1.2), \"a new date takes the rate of that date: {{}}\", again.body);",
               $"    assert!(close(number(&again.json()[\"{baseAmount}\"]), 120.0), \"{{}}\", again.body);"];
        string move = s.Day is null ? "" : string.Join('\n',
            "    let mut moving = written.clone();",
            $"    moving[\"{Json(s.Day)}\"] = json!(\"{d2}\");",
            "    let moved = api.put(&route, &moving).await;",
            "    let again = api.get(&route).await;") + "\n";
        return TemplateText.FillAll(Text, new (string, string)[]
        {
            ("§SETDAY", setDay), ("§DATED", string.Join('\n', lines) + "\n"), ("§MOVE", move),
            ("§ROUTE", RustNames.Str(RustTable.Of(s.Table, project).Route)), ("§RATES", RustNames.Str(RustTable.Of(s.Rates.Table, project).Route)),
            ("§RATEBODY", plan.RateBody), ("§BODY", plan.Body),
            ("§KEY", Json(s.Key)), ("§CUR", Json(s.Currency)), ("§RATE", rate), ("§AMOUNT", Json(s.Amount)), ("§BASEAMOUNT", baseAmount),
            ("§RKEY", Json(s.Rates.Key)), ("§RFROM", Json(s.Rates.From)), ("§RTO", Json(s.Rates.To)), ("§RRATE", Json(s.Rates.Rate)), ("§RDAY", Json(s.Rates.Day)),
            ("§BASE", s.BaseCurrency), ("§DAY1", d1 is null ? "None" : "Some(\"" + d1 + "\")"),
            ("§RD1", plan.RateDay("2024-01-01")), ("§RD2", plan.RateDay("2024-03-01")),
            ("§T_NEEDED", RateWords.NeededType)
        }) + "\n";
    }

    private const string Text = """"
// A document of a rate table through the router: written with the rate in force on its date, kept while its currency and date stay, the amount times the rate as its converted amount, and refused when its
// currency has no rate. Written by RS_TestRates; regenerated with the tables. Each test adds rates of a currency no other run uses and documents in it, and removes them again.
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const ROUTE: &str = §ROUTE;
const RATES: &str = §RATES;
const BASE: &str = "§BASE";
const NEEDED: &str = "§T_NEEDED";

fn currency() -> String {
    let tag = uuid::Uuid::new_v4().simple().to_string().to_uppercase();
    format!("T{}", &tag[..2])
}

fn number(value: &Value) -> f64 {
    value.as_f64().or_else(|| value.as_str().and_then(|text| text.parse().ok())).unwrap_or(f64::NAN)
}

fn close(left: f64, right: f64) -> bool {
    (left - right).abs() < 0.005
}

async fn rate(api: &Client, source: &str, value: f64, day: &str) -> Reply {
    let mut body = json!(§RATEBODY);
    body["§RFROM"] = json!(source);
    body["§RTO"] = json!(BASE);
    body["§RRATE"] = json!(value);
    body["§RDAY"] = json!(day);
    api.post(RATES, &body).await
}

async fn document(api: &Client, source: &str, amount: f64, day: Option<&str>) -> Reply {
    let mut body = json!(§BODY);
    body["§CUR"] = json!(source);
    body["§AMOUNT"] = json!(amount);
    body["§RATE"] = json!(99);
    body["§BASEAMOUNT"] = json!(1);
§SETDAY    api.post(ROUTE, &body).await
}

async fn remove(api: &Client, route: &str, key: &str, saved: &[&Reply]) {
    for made in saved {
        if made.status == StatusCode::CREATED {
            api.delete(&format!("{route}/{}", made.json()[key])).await;
        }
    }
}

#[tokio::test]
async fn a_document_is_written_with_the_rate_in_force_and_keeps_it() {
    let Some(api) = Client::start().await else { return };
    let code = currency();
    let low = rate(&api, &code, 1.1, "§RD1").await;
    let high = rate(&api, &code, 1.2, "§RD2").await;
    let first = document(&api, &format!(" {} ", code.to_lowercase()), 100.0, §DAY1).await;
    let written = first.json();
    let route = format!("{ROUTE}/{}", written["§KEY"]);
§MOVE    let mut edit = written.clone();
    edit["§AMOUNT"] = json!(200);
    edit["§RATE"] = json!(5);
    let changed = api.put(&route, &edit).await;
    let kept = api.get(&route).await;
    let home = document(&api, &BASE.to_lowercase(), 50.0, §DAY1).await;
    remove(&api, ROUTE, "§KEY", &[&first, &home]).await;
    remove(&api, RATES, "§RKEY", &[&low, &high]).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    assert_eq!(written["§CUR"], code.as_str(), "the currency is trimmed and upper case: {}", first.body);
§DATED    assert!(changed.status.is_success(), "{}", changed.body);
    assert!(number(&kept.json()["§RATE"]) != 5.0, "the rate is not the caller's: {}", kept.body);
    assert!(close(number(&kept.json()["§RATE"]) * 200.0, number(&kept.json()["§BASEAMOUNT"])), "the amount times the rate: {}", kept.body);
    assert_eq!(home.status, StatusCode::CREATED, "{}", home.body);
    assert!(close(number(&home.json()["§RATE"]), 1.0), "the base currency has rate 1: {}", home.body);
    assert!(close(number(&home.json()["§BASEAMOUNT"]), 50.0), "{}", home.body);
}

#[tokio::test]
async fn a_document_in_a_currency_with_no_rate_is_refused() {
    let Some(api) = Client::start().await else { return };
    let none = document(&api, &currency(), 100.0, §DAY1).await;
    let blank = document(&api, "  ", 100.0, §DAY1).await;
    remove(&api, ROUTE, "§KEY", &[&none, &blank]).await;
    assert!(none.status == StatusCode::BAD_REQUEST && none.body.contains(NEEDED), "{}", none.body);
    assert_eq!(blank.status, StatusCode::BAD_REQUEST, "{}", blank.body);
}
"""";
}
