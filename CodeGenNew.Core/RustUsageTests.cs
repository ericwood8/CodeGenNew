using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/api/usage_&lt;table&gt;.rs</c> of the Rust API (RS_TestUsage): tests of a usage ledger through its routes, the same cases and sentences as the ASP.NET tests (<see cref="ApiUsageTests"/>) and the
/// Python tests (<see cref="PythonUsageTests"/>). Every Rust test works in a database of its own, so the events a test records need no clean-up; a second tenant is the one the test client already knows (<c>other_tenant</c>). </summary>
public static class RustUsageTests
{
    /// <summary> Whether the tests are written: <c>RustTests</c> over a tenant API with a usage ledger and a database the tests can copy (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        RustTenantTests.Applies(database, project) && UsageTestPlan.Ledgers(database, project).Count > 0;

    public static string Module(UsageShape t) => "usage_" + RustNames.Snake(t.Table.TableName);

    public static string File(UsageShape t, DatabaseModel database, ProjectSettings project, UsageRollupShape? rollup)
    {
        var (q1, q2, q3, sum) = UsageTestPlan.Units(t);
        bool units = t.Quantity is not null;
        string Str(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Json(string value) => units ? $"json!({value})" : "Value::Null";
        string sumText = sum.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§TABLE", t.Table.TableName), ("§ROUTE", Str(RustTable.Of(t.Table, project).Route)), ("§LENGTH", UsageTestPlan.KindLength(t).ToString())));
        o.Append(FillAll(SummaryCases, ("§Q1", Json(q1)), ("§Q2", Json(q2)), ("§Q3", Json(q3)),
            ("§SUMCHECK", units
                ? $"    assert!((number(&line(&everything, &one, &this_month()).expect(\"this month\")[\"quantity\"]) - {sumText}).abs() < 1e-9, \"the units of the two events of this month\");\n"
                + $"    assert!((number(&line(&everything, &two, \"2020-01\").expect(\"2020-01\")[\"quantity\"]) - {q3}).abs() < 1e-9);\n"
                : "    assert_eq!(number(&line(&everything, &one, &this_month()).expect(\"this month\")[\"quantity\"]), 2.0, \"each event is one unit\");\n    assert_eq!(number(&line(&everything, &two, \"2020-01\").expect(\"2020-01\")[\"quantity\"]), 1.0);\n")));
        o.Append(FillAll(RecordCases, ("§QUANTITY0", units ? ", \"quantity\": 0" : ""),
            ("§FRACTION", units && !t.QuantityIsDecimal
                ? "    let fraction = platform.post(ROUTE, &json!({ \"tenantId\": tenant, \"kind\": kind, \"quantity\": 1.5 })).await;\n    assert_eq!(fraction.status, StatusCode::BAD_REQUEST, \"a whole-number ledger refuses a fraction\");\n" : ""),
            ("§QUANTITY6", Json("6")),
            ("§USEDLINE", units ? "    assert_eq!(number(&recorded[\"used\"]), 6.0);\n" : ""),
            ("§HALF", t.QuantityIsDecimal ? "    let half = platform.post(ROUTE, &json!({ \"tenantId\": tenant, \"kind\": kind, \"quantity\": 1.5 })).await;\n    assert_eq!(half.status, StatusCode::CREATED, \"{}\", half.body);\n" : ""),
            ("§LINECHECK", units ? $"    assert!((number(&line(&read(&inside).await, &kind, &this_month()).expect(\"the recorded kind\")[\"quantity\"]) - {(t.QuantityIsDecimal ? "7.5" : "6.0")}).abs() < 1e-9);\n"
                : "    assert!(line(&read(&inside).await, &kind, &this_month()).is_some());\n")));
        if (rollup is not null && units)
            o.Append(Rollup);
        var di = DialectInfo.For(database.Dialect);
        if (UsageTestPlan.LimitCaseOf(database, project, t, RustSubscriptionWriter.Applies(project) && RustSubscriptionTests.Applies(database, project), di.Placeholder(1)) is { } limit)
        {
            var sql = limit.Sql;
            string List(IEnumerable<string> statements) => "[" + string.Join(", ", statements.Select(RustTable.Raw)) + "]";
            o.Append(FillAll(LimitHead, ("§PLAN", sql.PlanKey.ToString()), ("§ADDPLAN", RustTable.Raw(sql.AddPlan)), ("§REMOVEPLAN_COUNT", sql.RemovePlan.Count.ToString()), ("§REMOVEPLAN", List(sql.RemovePlan)),
                ("§KEY", Str(limit.Key)), ("§CRATE", project.RustCrateName)));
            o.Append(FillAll(project.UsageRefuse ? RefuseCases : OverCases, ("§LIMIT5", List(sql.SetLimit(limit.Key, 5))), ("§LIMIT100", List(sql.SetLimit(limit.Key, 100))), ("§LIMIT_TYPE", SubscriptionWords.LimitType)));
        }
        o.Append(NoEdit);
        return o.ToString().TrimEnd('\n') + "\n";
    }

    private const string Head = """"
// The usage ledger §TABLE through the router. Written by RS_TestUsage; regenerated with the tables. Every test works in a database of its own, so the events it records need no clean-up.
#![allow(dead_code)]
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const ROUTE: &str = §ROUTE;

fn new_kind() -> String {
    format!("t{}", &uuid::Uuid::new_v4().simple().to_string()[..§LENGTH - 1])
}

fn this_month() -> String {
    chrono::Utc::now().format("%Y-%m").to_string()
}

/// A number the answer holds as a JSON number or as text.
fn number(value: &Value) -> f64 {
    value.as_f64().or_else(|| value.as_str().and_then(|text| text.parse().ok())).unwrap_or(f64::NAN)
}

fn line(lines: &Value, kind: &str, month: &str) -> Option<Value> {
    lines.as_array()?.iter().find(|l| l["kind"] == kind && l["month"] == month).cloned()
}

async fn read(client: &Client) -> Value {
    let reply = client.get(ROUTE).await;
    assert_eq!(reply.status, StatusCode::OK, "{}", reply.body);
    reply.json()
}

async fn read_with(client: &Client, query: &str) -> Value {
    let reply = client.get(&format!("{ROUTE}{query}")).await;
    assert_eq!(reply.status, StatusCode::OK, "{}", reply.body);
    reply.json()
}

/// Records an event as a platform administrator; the answer says what the month holds.
async fn add(api: &Client, tenant: i32, kind: &str, quantity: Value, occurred: Option<&str>) -> Value {
    let mut body = json!({ "tenantId": tenant, "kind": kind });
    if !quantity.is_null() {
        body["quantity"] = quantity;
    }
    if let Some(day) = occurred {
        body["occurredOn"] = json!(day);
    }
    let reply = api.platform().post(ROUTE, &body).await;
    assert_eq!(reply.status, StatusCode::CREATED, "{}", reply.body);
    reply.json()
}

#[tokio::test]
async fn without_a_token_the_routes_answer_401() {
    let Some(api) = Client::start().await else { return };
    let nobody = api.anonymous();
    assert_eq!(nobody.get(ROUTE).await.status, StatusCode::UNAUTHORIZED);
    assert_eq!(nobody.post(ROUTE, &json!({ "tenantId": 1, "kind": new_kind() })).await.status, StatusCode::UNAUTHORIZED);
}

#[tokio::test]
async fn a_role_that_is_not_the_administrators_cannot_read_the_usage() {
    let Some(api) = Client::start().await else { return };
    let Some(tenant) = api.tenant else { return skip("the database needs an active tenant") };
    assert_eq!(api.in_tenant(tenant, "NoSuchRole").get(ROUTE).await.status, StatusCode::FORBIDDEN);
}
"""";

    private const string SummaryCases = """"

#[tokio::test]
async fn the_summary_adds_the_events_per_month_and_kind() {
    let Some(api) = Client::start().await else { return };
    let Some(tenant) = api.tenant else { return skip("the database needs an active tenant") };
    let (one, two) = (new_kind(), new_kind());
    add(&api, tenant, &one, §Q1, None).await;
    add(&api, tenant, &one, §Q2, None).await;
    add(&api, tenant, &two, §Q3, Some("2020-01-15T12:00:00")).await;
    let inside = api.in_tenant(tenant, ADMIN_ROLE);
    let everything = read(&inside).await;
§SUMCHECK    assert_eq!(line(&everything, &one, &this_month()).expect("this month")["events"], json!(2));
    let by_kind = read_with(&inside, &format!("?kind={one}")).await;
    assert!(!by_kind.as_array().unwrap().is_empty() && by_kind.as_array().unwrap().iter().all(|l| l["kind"] == one.as_str()), "the kind filter");
    let early = read_with(&inside, "?from=2019-01-01&to=2020-06-01").await;
    assert!(line(&early, &two, "2020-01").is_some(), "the date filter keeps the old event");
    assert!(line(&early, &one, &this_month()).is_none(), "and leaves this month out");
    let months: Vec<String> = everything.as_array().unwrap().iter().map(|l| l["month"].as_str().unwrap_or_default().to_string()).collect();
    let mut newest_first = months.clone();
    newest_first.sort_by(|a, b| b.cmp(a));
    assert_eq!(months, newest_first, "newest month first");
}

#[tokio::test]
async fn another_tenants_events_are_not_in_the_summary() {
    let Some(api) = Client::start().await else { return };
    let Some(tenant) = api.tenant else { return skip("the database needs an active tenant") };
    let Some(other) = api.other_tenant else { return skip("the database has only one tenant") };
    let kind = new_kind();
    add(&api, other, &kind, json!(2), None).await;
    assert!(line(&read(&api.in_tenant(tenant, ADMIN_ROLE)).await, &kind, &this_month()).is_none(), "a tenant never reads another tenant's usage");
    assert!(line(&read(&api.in_tenant(other, ADMIN_ROLE)).await, &kind, &this_month()).is_some());
}

#[tokio::test]
async fn a_platform_administrator_names_the_tenant_to_read() {
    let Some(api) = Client::start().await else { return };
    let Some(tenant) = api.tenant else { return skip("the database needs an active tenant") };
    let kind = new_kind();
    add(&api, tenant, &kind, json!(1), None).await;
    let platform = api.platform();
    assert_eq!(platform.get(ROUTE).await.status, StatusCode::BAD_REQUEST);
    assert!(line(&read_with(&platform, &format!("?tenantId={tenant}")).await, &kind, &this_month()).is_some());
}
"""";

    private const string RecordCases = """"

#[tokio::test]
async fn only_a_platform_administrator_records_usage_and_it_reaches_the_summary() {
    let Some(api) = Client::start().await else { return };
    let Some(tenant) = api.tenant else { return skip("the database needs an active tenant") };
    let kind = new_kind();
    let inside = api.in_tenant(tenant, ADMIN_ROLE);
    let platform = api.platform();
    assert_eq!(inside.post(ROUTE, &json!({ "tenantId": tenant, "kind": kind })).await.status, StatusCode::FORBIDDEN);
    assert_eq!(platform.post(ROUTE, &json!({ "tenantId": tenant, "kind": " " })).await.status, StatusCode::BAD_REQUEST);
    assert_eq!(platform.post(ROUTE, &json!({ "kind": kind })).await.status, StatusCode::BAD_REQUEST);
    assert_eq!(platform.post(ROUTE, &json!({ "tenantId": tenant, "kind": kind§QUANTITY0 })).await.status, StatusCode::BAD_REQUEST);
    assert_eq!(platform.post(ROUTE, &json!({ "tenantId": 2000000000, "kind": kind })).await.status, StatusCode::NOT_FOUND);
§FRACTION    let recorded = add(&api, tenant, &kind, §QUANTITY6, None).await;
    assert_eq!(recorded["over"], json!(false), "a kind no plan limits is never over");
§USEDLINE§HALF§LINECHECK}
"""";

    private const string Rollup = """"

#[tokio::test]
async fn rolling_up_totals_old_events_into_the_months_and_the_summary_still_adds_them() {
    let Some(api) = Client::start().await else { return };
    let Some(tenant) = api.tenant else { return skip("the database needs an active tenant") };
    let kind = new_kind();
    add(&api, tenant, &kind, json!(2), Some("2019-03-10T12:00:00")).await;
    add(&api, tenant, &kind, json!(3), Some("2019-03-20T12:00:00")).await;
    add(&api, tenant, &kind, json!(4), None).await;
    let inside = api.in_tenant(tenant, ADMIN_ROLE);
    let platform = api.platform();
    let roll = format!("{ROUTE}/roll-up?before=2019-04-01");
    assert_eq!(inside.post_empty(&roll).await.status, StatusCode::FORBIDDEN, "only a platform administrator rolls up");
    let rolled = platform.post_empty(&roll).await;
    assert_eq!(rolled.status, StatusCode::OK, "{}", rolled.body);
    assert!(rolled.json()["events"].as_i64().unwrap_or(0) >= 2, "the two old events were totalled");
    let lines = read(&inside).await;
    let march = line(&lines, &kind, "2019-03").expect("the rolled month is still in the summary");
    assert_eq!(number(&march["quantity"]), 5.0, "2 and 3 units");
    assert_eq!(march["events"], json!(0), "no event of that month is left");
    assert_eq!(number(&line(&lines, &kind, &this_month()).expect("this month")["quantity"]), 4.0, "this month was not rolled");
    assert!(line(&read_with(&inside, "?from=2019-03-10").await, &kind, "2019-03").is_none(), "a rolled month is left out when From cuts into it");
    assert!(line(&read_with(&inside, "?to=2019-03-20").await, &kind, "2019-03").is_none(), "a rolled month is left out when Before cuts into it");
    assert!(line(&read_with(&inside, "?from=2019-03-01&to=2019-04-01").await, &kind, "2019-03").is_some(), "a rolled month is kept when the whole month is inside");
    add(&api, tenant, &kind, json!(6), Some("2019-03-05T12:00:00")).await;
    assert_eq!(platform.post_empty(&roll).await.status, StatusCode::OK);
    assert_eq!(number(&line(&read(&inside).await, &kind, "2019-03").expect("march")["quantity"]), 11.0, "a second roll-up adds to the month");
}
"""";

    private const string LimitHead = """"

use crate::tenant_routes::make_tenant;

const PLAN: i64 = §PLAN;
const ADD_PLAN: &str = §ADDPLAN;
const REMOVE_PLAN: [&str; §REMOVEPLAN_COUNT] = §REMOVEPLAN;
const KEY: &str = §KEY;

async fn run(api: &Client, statements: &[&'static str]) {
    for statement in statements {
        sqlx::query(*statement).execute(&api.pool()).await.expect("the statement runs");
    }
}

/// A tenant on a plan of the test's own (the test sets its limits).
async fn billed(api: &Client) -> i32 {
    // every test works in a database of its own but the process shares what it remembers of a tenant's subscription, and the tests run side by side: nothing is remembered
    unsafe { std::env::set_var("SUBSCRIPTION_STATE_SECONDS", "0") };
    run(api, &REMOVE_PLAN).await;
    run(api, &[ADD_PLAN]).await;
    let tenant = make_tenant(api).await;
    §CRATE::forget_state(tenant);
    let chosen = api.platform().put(&format!("/api/subscription/{tenant}"), &json!({ "planId": PLAN, "status": "active" })).await;
    assert_eq!(chosen.status, StatusCode::OK, "{}", chosen.body);
    tenant
}

async fn post_units(api: &Client, tenant: i32, quantity: i64, occurred: Option<&str>) -> Reply {
    let mut body = json!({ "tenantId": tenant, "kind": KEY, "quantity": quantity });
    if let Some(day) = occurred {
        body["occurredOn"] = json!(day);
    }
    api.platform().post(ROUTE, &body).await
}

async fn used(api: &Client, tenant: i32) -> f64 {
    line(&read(&api.in_tenant(tenant, ADMIN_ROLE)).await, KEY, &this_month()).map(|l| number(&l["quantity"])).unwrap_or(0.0)
}

async fn limit_row(api: &Client, tenant: i32) -> Value {
    let info = api.in_tenant(tenant, ADMIN_ROLE).get("/api/subscription").await.json();
    info["limits"].as_array().and_then(|rows| rows.iter().find(|r| r["key"] == KEY)).expect("the limit is listed").clone()
}
"""";

    private const string OverCases = """"

#[tokio::test]
async fn the_answer_says_when_a_tenant_passes_its_plan_and_the_subscription_shows_it() {
    let Some(api) = Client::start().await else { return };
    let tenant = billed(&api).await;
    run(&api, &§LIMIT5).await;
    let first = add(&api, tenant, KEY, json!(3), None).await;
    assert_eq!(number(&first["allowed"]), 5.0);
    assert_eq!(number(&first["used"]), 3.0);
    assert_eq!(first["over"], json!(false));
    let second = add(&api, tenant, KEY, json!(3), None).await;
    assert_eq!(number(&second["used"]), 6.0);
    assert_eq!(second["over"], json!(true), "the event is written and the answer says the tenant is over");
    let row = limit_row(&api, tenant).await;
    assert_eq!(number(&row["allowed"]), 5.0);
    assert_eq!(number(&row["used"]), 6.0);
    run(&api, &§LIMIT100).await;
    assert_eq!(add(&api, tenant, KEY, json!(1), None).await["over"], json!(false), "raising the limit lifts the flag");
}
"""";

    private const string RefuseCases = """"

#[tokio::test]
async fn a_request_past_the_plan_is_refused_and_writes_nothing() {
    let Some(api) = Client::start().await else { return };
    let tenant = billed(&api).await;
    run(&api, &§LIMIT5).await;
    assert_eq!(add(&api, tenant, KEY, json!(3), None).await["over"], json!(false));
    let refused = post_units(&api, tenant, 3, None).await;
    assert_eq!(refused.status, StatusCode::PAYMENT_REQUIRED, "{}", refused.body);
    assert_eq!(refused.json()["type"], "§LIMIT_TYPE");
    assert_eq!(used(&api, tenant).await, 3.0, "a refused request writes nothing");
    let reached = add(&api, tenant, KEY, json!(2), None).await;
    assert_eq!(number(&reached["used"]), 5.0);
    assert_eq!(reached["over"], json!(false), "reaching the limit exactly is allowed");
    assert_eq!(post_units(&api, tenant, 1, None).await.status, StatusCode::PAYMENT_REQUIRED, "one more is refused");
    assert_eq!(post_units(&api, tenant, 1000, Some("2020-01-15T12:00:00")).await.status, StatusCode::CREATED, "an event of an earlier month is judged against nothing");
    let row = limit_row(&api, tenant).await;
    assert_eq!(number(&row["allowed"]), 5.0);
    assert_eq!(number(&row["used"]), 5.0);
    run(&api, &§LIMIT100).await;
    assert_eq!(post_units(&api, tenant, 3, None).await.status, StatusCode::CREATED, "raising the limit lifts the refusal");
}

#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn requests_at_the_same_moment_cannot_both_pass_the_limit() {
    let Some(api) = Client::start().await else { return };
    let tenant = billed(&api).await;
    run(&api, &§LIMIT100).await;
    let tasks: Vec<_> = (0..30)
        .map(|_| {
            let client = api.clone();
            tokio::spawn(async move { post_units(&client, tenant, 10, None).await.status })
        })
        .collect();
    let (mut created, mut refused, mut other) = (0, 0, 0);
    for task in tasks {
        match task.await.expect("the request finishes") {
            StatusCode::CREATED => created += 1,
            StatusCode::PAYMENT_REQUIRED => refused += 1,
            _ => other += 1,
        }
    }
    assert_eq!((created, refused, other), (10, 20, 0), "exactly the limit fits");
    assert_eq!(used(&api, tenant).await, 100.0, "the ledger holds exactly the limit");
}
"""";

    private const string NoEdit = """"

#[tokio::test]
async fn nothing_edits_or_deletes_an_event() {
    let Some(api) = Client::start().await else { return };
    let platform = api.platform();
    for reply in [platform.put(&format!("{ROUTE}/1"), &json!({})).await, platform.delete(&format!("{ROUTE}/1")).await] {
        assert!(reply.status == StatusCode::NOT_FOUND || reply.status == StatusCode::METHOD_NOT_ALLOWED, "the ledger has no edit or delete route: {}", reply.status);
    }
}
"""";
}
