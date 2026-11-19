using CodeGenNew.Core.Naming;
using CodeGenNew.Core.Rate;

namespace CodeGenNew.Core.Rust;

/// <summary> <c>tests/api/rate_routes.rs</c> of the Rust API (RS_TestRates): the cases add rates through the router and remove them again. The same cases and words as the Python tests
/// (<see cref="PythonRateTests"/>, <see cref="RateWords"/>). </summary>
public static class RustRateTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && RateTestPlan.For(database, project) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = RateTestPlan.For(database, project)!;
        var s = plan.Shape;
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        var names = new (string, string)[]
        {
            ("§ROUTE", RustNames.Str(RustTable.Of(s.Table, project).Route)), ("§BODY", plan.Body),
            ("§KEY", Json(s.Key)), ("§FROM", Json(s.From)), ("§TO", Json(s.To)), ("§RATE", Json(s.Rate)), ("§DAY", Json(s.Day)),
            ("§D1", plan.Day("2024-01-01")), ("§D2", plan.Day("2024-03-01")),
            ("§T_TAKEN", RateWords.TakenType), ("§T_INVALID", RateWords.InvalidType), ("§T_MISSING", RateWords.MissingType), ("§D3", plan.Day("2024-03-15"))
        };
        string text = TemplateText.FillAll(Text, names);
        if (project.RateInverse && RatePlan.InvertibleRate(s.Rate))
            text += "\n" + TemplateText.FillAll(InverseText, names);
        return text + "\n";
    }

    private const string Text = """"
// The rate table through the router: currencies are trimmed and upper case and differ, a rate is above zero, a pair has one rate per day, and the rate in force on a day is the latest one on or before it.
// Written by RS_TestRates; regenerated with the tables. Each test adds rates of a pair of currencies no other run uses and removes them again.
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const ROUTE: &str = §ROUTE;
const TAKEN: &str = "§T_TAKEN";
const INVALID: &str = "§T_INVALID";
const MISSING: &str = "§T_MISSING";

fn pair() -> (String, String) {
    let tag = uuid::Uuid::new_v4().simple().to_string().to_uppercase();
    (format!("T{}", &tag[..2]), format!("Y{}", &tag[2..4]))
}

async fn save(api: &Client, source: &str, target: &str, rate: f64, day: &str) -> Reply {
    let mut body = json!(§BODY);
    body["§FROM"] = json!(source);
    body["§TO"] = json!(target);
    body["§RATE"] = json!(rate);
    body["§DAY"] = json!(day);
    api.post(ROUTE, &body).await
}

async fn on(api: &Client, source: &str, target: &str, day: Option<&str>) -> Reply {
    let when = day.map(|d| format!("&on={d}")).unwrap_or_default();
    api.get(&format!("{ROUTE}/rate?from={source}&to={target}{when}")).await
}

fn number(value: &Value) -> f64 {
    value.as_f64().or_else(|| value.as_str().and_then(|text| text.parse().ok())).unwrap_or(f64::NAN)
}

async fn remove(api: &Client, saved: &[&Reply]) {
    for made in saved {
        if made.status == StatusCode::CREATED {
            api.delete(&format!("{ROUTE}/{}", made.json()["§KEY"])).await;
        }
    }
}

#[tokio::test]
async fn a_rate_is_kept_trimmed_in_upper_case_and_once_per_pair_and_day() {
    let Some(api) = Client::start().await else { return };
    let (source, target) = pair();
    let first = save(&api, &format!("  {} ", source.to_lowercase()), &target, 1.5, "§D1").await;
    let same = save(&api, &source.to_lowercase(), &target, 1.6, "§D1").await;
    let opposite = save(&api, &target, &source, 0.66, "§D1").await;
    let later = save(&api, &source, &target, 1.7, "§D2").await;
    let mut row = later.json();
    let route = format!("{ROUTE}/{}", row["§KEY"]);
    row["§RATE"] = json!(1.8);
    let changed = api.put(&route, &row).await;
    row["§DAY"] = json!("§D1");
    let clash = api.put(&route, &row).await;
    remove(&api, &[&first, &same, &opposite, &later]).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    assert!(first.json()["§FROM"] == source.as_str() && first.json()["§TO"] == target.as_str(), "both are trimmed and upper case: {}", first.body);
    assert!(same.status == StatusCode::CONFLICT && same.body.contains(TAKEN), "{}", same.body);
    assert_eq!(opposite.status, StatusCode::CREATED, "the opposite pair is another rate: {}", opposite.body);
    assert_eq!(later.status, StatusCode::CREATED, "{}", later.body);
    assert!(changed.status.is_success(), "a row keeps its own pair and day: {}", changed.body);
    assert!(clash.status == StatusCode::CONFLICT && clash.body.contains(TAKEN), "{}", clash.body);
}

#[tokio::test]
async fn a_rate_that_is_not_valid_is_refused() {
    let Some(api) = Client::start().await else { return };
    let (source, target) = pair();
    let same = save(&api, &source, &source, 1.0, "§D1").await;
    let zero = save(&api, &source, &target, 0.0, "§D1").await;
    let negative = save(&api, &source, &target, -2.0, "§D1").await;
    let blank = save(&api, "  ", &target, 1.0, "§D1").await;
    remove(&api, &[&same, &zero, &negative, &blank]).await;
    assert!(same.status == StatusCode::BAD_REQUEST && same.body.contains(INVALID), "{}", same.body);
    assert!(zero.status == StatusCode::BAD_REQUEST && zero.body.contains(INVALID), "{}", zero.body);
    assert_eq!(negative.status, StatusCode::BAD_REQUEST, "{}", negative.body);
    assert!(blank.status == StatusCode::BAD_REQUEST && blank.body.contains(INVALID), "{}", blank.body);
}

#[tokio::test]
async fn the_rate_in_force_is_the_latest_one_on_or_before_the_day() {
    let Some(api) = Client::start().await else { return };
    let (source, target) = pair();
    let first = save(&api, &source, &target, 1.5, "§D1").await;
    let second = save(&api, &source, &target, 1.7, "§D2").await;
    let opposite = save(&api, &target, &source, 0.66, "§D2").await;
    let early = on(&api, &source.to_lowercase(), &target, Some("2023-12-31")).await;
    let between = on(&api, &source, &target, Some("2024-02-15")).await;
    let start = on(&api, &source, &target, Some("2024-03-01")).await;
    let today = on(&api, &source, &target, None).await;
    let back = on(&api, &target, &source, Some("2024-06-01")).await;
    let unknown = on(&api, &source, "XXQ", Some("2024-06-01")).await;
    let twice = on(&api, &source, &source, None).await;
    let lonely = api.get(&format!("{ROUTE}/rate?from={source}")).await;
    remove(&api, &[&first, &second, &opposite]).await;
    assert!(early.status == StatusCode::NOT_FOUND && early.body.contains(MISSING), "{}", early.body);
    assert_eq!(number(&between.json()["§RATE"]), 1.5, "a day between two rates gets the earlier one: {}", between.body);
    assert_eq!(number(&start.json()["§RATE"]), 1.7, "the day a rate starts gets that rate: {}", start.body);
    assert_eq!(number(&today.json()["§RATE"]), 1.7, "no day means today: {}", today.body);
    assert_eq!(number(&back.json()["§RATE"]), 0.66, "the opposite pair has its own rates: {}", back.body);
    assert_eq!(unknown.status, StatusCode::NOT_FOUND, "{}", unknown.body);
    assert_eq!(twice.status, StatusCode::BAD_REQUEST, "{}", twice.body);
    assert_eq!(lonely.status, StatusCode::BAD_REQUEST, "{}", lonely.body);
}
"""";

    private const string InverseText = """"

#[tokio::test]
async fn a_newer_rate_of_the_opposite_pair_answers_as_one_divided_by_it() {
    let Some(api) = Client::start().await else { return };
    let (source, target) = pair();
    let own = save(&api, &source, &target, 1.1, "§D1").await;
    let back = save(&api, &target, &source, 0.8, "§D1").await;
    let direct = on(&api, &source, &target, Some("2024-02-01")).await;
    let newer = save(&api, &target, &source, 0.5, "§D3").await;
    let inverse = on(&api, &source, &target, Some("2024-04-01")).await;
    remove(&api, &[&own, &back, &newer]).await;
    assert_eq!(number(&direct.json()["§RATE"]), 1.1, "on the same day the pair's own row wins: {}", direct.body);
    assert_eq!(number(&inverse.json()["§RATE"]), 2.0, "one divided by the newer opposite rate: {}", inverse.body);
    assert!(inverse.json()["§FROM"] == source.as_str() && inverse.json()["§TO"] == target.as_str(), "{}", inverse.body);
}
"""";
}
