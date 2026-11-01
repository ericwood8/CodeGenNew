using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/api/subscription_routes.rs</c> of the Rust API (RS_TestSubscriptions): tests of the pause, the plan limits and the subscription routes. Each test makes a tenant with <c>make_tenant</c> of the tenant
/// tests and a plan of its own with SQL, and takes both out again (every Rust test works in a database of its own). The same cases and words as the ASP.NET tests (<see cref="ApiSubscriptionTests"/>) and the Python tests
/// (<see cref="PythonSubscriptionTests"/>). </summary>
public static class RustSubscriptionTests
{
    /// <summary> Whether the tests are written: <c>RustTests</c> over a Rust API with subscriptions and a database the plan can be put in with SQL (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        RustTenantTests.Applies(database, project) && RustSubscriptionWriter.Applies(project) && SubscriptionPlan.Problems(database, project).Count == 0
        && SubscriptionTestSql.For(database, project, "", out _) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var di = DialectInfo.For(database.Dialect);
        var sql = SubscriptionTestSql.For(database, project, di.Placeholder(1), out _)!;
        var subscription = SubscriptionPlan.SubscriptionOf(database, project)!;
        string Str(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Raw(string statement) => RustTable.Raw(statement);
        string List(IEnumerable<string> statements)
        {
            var items = statements.Select(Raw).ToList();
            return $"[{string.Join(", ", items)}]";
        }
        bool trialing = SubscriptionPlan.ActiveStatuses(project).Contains("trialing", StringComparer.OrdinalIgnoreCase);
        var write = sql.WriteTable;
        string clearRows = sql.ClearRows is null ? "" : $"    sqlx::query(CLEAR_ROWS).bind(tenant).execute(&api.pool()).await.expect(\"the rows are removed\");\n";

        var o = new StringBuilder();
        o.Append(Fill(Head, ("§CRATE", project.RustCrateName), ("§PLAN", sql.PlanKey.ToString()), ("§ADDPLAN", Raw(sql.AddPlan)),
            ("§REMOVEPLAN_COUNT", sql.RemovePlan.Count.ToString()), ("§REMOVEPLAN", List(sql.RemovePlan)), ("§CLEARSUBSCRIPTION", Raw(sql.ClearSubscription)),
            ("§CLEARROWSCONST", sql.ClearRows is null ? "" : $"const CLEAR_ROWS: &str = {Raw(sql.ClearRows)};\n"), ("§CLEARROWS", clearRows),
            ("§WRITECONST", write is null ? "" : $"const WRITE: &str = {Str(RustTestBodies.Route(write.TableName))};\n")));
        if (write is not null)
        {
            o.Append("\n#[allow(dead_code)]\n").Append(RustTenantTests.BodyFunction("write_body", write, project, "")).Append('\n');
            o.Append(Fill(WriteCases, ("§INACTIVE_TYPE", SubscriptionWords.InactiveType),
                ("§NOSUBSCRIPTION", project.SubscriptionRequired
                    ? $"    assert_eq!(refused.status, StatusCode::PAYMENT_REQUIRED, \"{{}}\", refused.body);\n    assert_eq!(refused.json()[\"type\"], \"{SubscriptionWords.InactiveType}\");\n"
                    : "    assert_eq!(refused.status, StatusCode::CREATED, \"{}\", refused.body);\n"),
                ("§NOSUBSCRIPTION_NAME", project.SubscriptionRequired ? "cannot_write_when_a_subscription_is_required" : "is_on_no_plan_and_may_write")));
            if (sql.LimitKey is { } key)
                o.Append(Fill(LimitCase, ("§LIMIT_TYPE", SubscriptionWords.LimitType), ("§KEY", Str(key)), ("§ONE", List(sql.SetLimit(key, 1))), ("§TWO", List(sql.SetLimit(key, 2)))));
            if (subscription.PeriodEnd is not null && trialing)
                o.Append(Fill(TrialCase, ("§INACTIVE_TYPE", SubscriptionWords.InactiveType)));
        }
        o.Append(Fill(RouteCases, ("§PLATFORM_TYPE", SubscriptionWords.OnlyPlatformType)));
        o.Append(Fill(PlansCase, ("§PRICELINE", subscription.PlanPrice is null ? "" : "    assert_eq!(info[\"planPrice\"], mine[\"price\"], \"the view shows the price of the tenant's plan\");\n"),
            ("§LIMITCOUNT", SubscriptionPlan.LimitedTables(database, project).Count.ToString())));
        if (SubscriptionPlan.HasWebhook(database, project))
        {
            o.Append(Fill(WebhookCase, ("§SECRETNAME", project.SubscriptionWebhookSecret!), ("§WEBHOOKPATH", SubscriptionWords.WebhookPath), ("§HEADER", SubscriptionWords.WebhookHeader),
                ("§EXPIRED_TYPE", SubscriptionWords.WebhookExpiredType),
                ("§PAUSEDLINES", write is null ? "" : "    let paused = inside.post(WRITE, &write_body()).await;\n    assert_eq!(paused.status, StatusCode::PAYMENT_REQUIRED, \"{}\", paused.body);\n    assert_eq!(paused.json()[\"type\"], \"" + SubscriptionWords.InactiveType + "\");\n")));
        }
        return o.ToString().TrimEnd('\n') + "\n";
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Head = """"
// Plans, subscriptions and limits through the router. Written by RS_TestSubscriptions; regenerated with the tables. Each test makes a tenant (make_tenant of the tenant tests) and a plan of its own with SQL, and takes both out again.
#![allow(dead_code)]
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::tenant_routes::{make_tenant, remove_tenant};
use crate::test_support::*;

const PLAN: i64 = §PLAN;
const ADD_PLAN: &str = §ADDPLAN;
const REMOVE_PLAN: [&str; §REMOVEPLAN_COUNT] = §REMOVEPLAN;
const CLEAR_SUBSCRIPTION: &str = §CLEARSUBSCRIPTION;
§CLEARROWSCONST§WRITECONST
async fn run(api: &Client, statements: &[&'static str]) {
    for statement in statements {
        sqlx::query(*statement).execute(&api.pool()).await.expect("the statement runs");
    }
}

/// A tenant made by a platform administrator and a plan of the test's own.
async fn start(api: &Client) -> i32 {
    // every test works in a database of its own but the process shares what it remembers of a tenant's subscription, and the tests run side by side: nothing is remembered
    unsafe { std::env::set_var("SUBSCRIPTION_STATE_SECONDS", "0") };
    run(api, &REMOVE_PLAN).await;
    run(api, &[ADD_PLAN]).await;
    let tenant = make_tenant(api).await;
    §CRATE::forget_state(tenant);
    tenant
}

/// Takes out the subscription, the rows the test made, the plan and the tenant.
async fn finish(api: &Client, tenant: i32) {
    §CRATE::forget_state(tenant);
§CLEARROWS    sqlx::query(CLEAR_SUBSCRIPTION).bind(tenant).execute(&api.pool()).await.expect("the subscription is removed");
    run(api, &REMOVE_PLAN).await;
    remove_tenant(api, tenant).await;
}

async fn set(api: &Client, tenant: i32, change: Value) {
    let reply = api.platform().put(&format!("/api/subscription/{tenant}"), &change).await;
    assert_eq!(reply.status, StatusCode::OK, "{}", reply.body);
}
"""";

    private const string WriteCases = """"

#[tokio::test]
async fn writes_are_paused_while_the_subscription_is_not_active_and_reads_go_on() {
    let Some(api) = Client::start().await else { return };
    let tenant = start(&api).await;
    set(&api, tenant, json!({ "planId": PLAN, "status": "cancelled" })).await;
    let inside = api.in_tenant(tenant, ADMIN_ROLE);
    let refused = inside.post(WRITE, &write_body()).await;
    assert_eq!(refused.status, StatusCode::PAYMENT_REQUIRED, "{}", refused.body);
    assert_eq!(refused.json()["type"], "§INACTIVE_TYPE");
    assert_eq!(inside.get(WRITE).await.status, StatusCode::OK, "reads are never paused");
    set(&api, tenant, json!({ "status": "active" })).await;
    let created = inside.post(WRITE, &write_body()).await;
    assert_eq!(created.status, StatusCode::CREATED, "{}", created.body);
    finish(&api, tenant).await;
}

#[tokio::test]
async fn a_tenant_with_no_subscription_§NOSUBSCRIPTION_NAME() {
    let Some(api) = Client::start().await else { return };
    let tenant = start(&api).await;
    let refused = api.in_tenant(tenant, ADMIN_ROLE).post(WRITE, &write_body()).await;
§NOSUBSCRIPTION    finish(&api, tenant).await;
}
"""";

    private const string LimitCase = """"

#[tokio::test]
async fn a_create_past_the_plan_limit_is_refused_until_the_limit_is_raised() {
    let Some(api) = Client::start().await else { return };
    let tenant = start(&api).await;
    run(&api, &§ONE).await;
    set(&api, tenant, json!({ "planId": PLAN, "status": "active" })).await;
    let inside = api.in_tenant(tenant, ADMIN_ROLE);
    let first = inside.post(WRITE, &write_body()).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    let second = inside.post(WRITE, &write_body()).await;
    assert_eq!(second.status, StatusCode::PAYMENT_REQUIRED, "{}", second.body);
    assert_eq!(second.json()["type"], "§LIMIT_TYPE");
    let shown = inside.get("/api/subscription").await;
    assert_eq!(shown.status, StatusCode::OK, "{}", shown.body);
    let info = shown.json();
    let row = info["limits"].as_array().and_then(|rows| rows.iter().find(|r| r["key"] == §KEY)).expect("the limit is listed").clone();
    assert_eq!(row["allowed"], json!(1));
    assert_eq!(row["used"], json!(1));
    run(&api, &§TWO).await;
    let third = inside.post(WRITE, &write_body()).await;
    assert_eq!(third.status, StatusCode::CREATED, "{}", third.body);
    finish(&api, tenant).await;
}
"""";

    private const string TrialCase = """"

#[tokio::test]
async fn a_trial_ends_at_its_period_end() {
    let Some(api) = Client::start().await else { return };
    let tenant = start(&api).await;
    let day = chrono::Duration::days(1);
    set(&api, tenant, json!({ "planId": PLAN, "status": "trialing", "periodEnd": (chrono::Utc::now() - day).to_rfc3339() })).await;
    let inside = api.in_tenant(tenant, ADMIN_ROLE);
    let ended = inside.post(WRITE, &write_body()).await;
    assert_eq!(ended.status, StatusCode::PAYMENT_REQUIRED, "{}", ended.body);
    assert_eq!(ended.json()["type"], "§INACTIVE_TYPE");
    set(&api, tenant, json!({ "periodEnd": (chrono::Utc::now() + day * 14).to_rfc3339() })).await;
    let running = inside.post(WRITE, &write_body()).await;
    assert_eq!(running.status, StatusCode::CREATED, "{}", running.body);
    finish(&api, tenant).await;
}
"""";

    private const string PlansCase = """"

#[tokio::test]
async fn the_view_lists_every_plan_with_what_it_allows_and_marks_the_tenants_own() {
    let Some(api) = Client::start().await else { return };
    let tenant = start(&api).await;
    set(&api, tenant, json!({ "planId": PLAN, "status": "active" })).await;
    let info = api.in_tenant(tenant, ADMIN_ROLE).get("/api/subscription").await.json();
    let plans = info["plans"].as_array().expect("a list of plans");
    let mine = plans.iter().find(|plan| plan["planId"] == json!(PLAN)).expect("the tenant's plan is listed");
    assert_eq!(mine["current"], json!(true));
    assert_eq!(plans.iter().filter(|plan| plan["current"] == json!(true)).count(), 1, "only the tenant's own plan is marked");
    assert_eq!(info["planName"], mine["name"]);
§PRICELINE    assert_eq!(mine["limits"].as_array().map(|limits| limits.len()), Some(§LIMITCOUNT), "each plan lists every limit");
    finish(&api, tenant).await;
}
"""";

    private const string WebhookCase = """"

fn signed(body: &str, secret: &str, at: Option<i64>) -> String {
    use hmac::{Hmac, Mac};
    let stamp = at.unwrap_or_else(|| chrono::Utc::now().timestamp());
    let mut mac = Hmac::<sha2::Sha256>::new_from_slice(secret.as_bytes()).expect("a key");
    mac.update(format!("{stamp}.").as_bytes());
    mac.update(body.as_bytes());
    let digest: String = mac.finalize().into_bytes().iter().map(|byte| format!("{byte:02x}")).collect();
    format!("t={stamp},v1={digest}")
}

fn message(gateway: &str, status: Option<&str>, event: Option<&str>) -> String {
    let mut body = json!({ "gatewayId": gateway });
    if let Some(status) = status {
        body["status"] = json!(status);
    }
    if let Some(event) = event {
        body["id"] = json!(event);
    }
    body.to_string()
}

async fn hook(api: &Client, body: &str, signature: Option<String>) -> Reply {
    let mut client = api.anonymous();
    if let Some(signature) = signature {
        client = client.with_header("§HEADER", &signature);
    }
    client.send_text(axum::http::Method::POST, "§WEBHOOKPATH", Some(("application/json", body.to_string()))).await
}

async fn status_of(client: &Client) -> String {
    client.get("/api/subscription").await.json()["status"].as_str().unwrap_or("").to_string()
}

#[tokio::test]
async fn the_webhook_sets_the_status_of_the_subscription_the_gateway_names_and_refuses_what_is_not_signed() {
    let Some(api) = Client::start().await else { return };
    let secret = "test-secret";
    // the secret is read from the environment at each request; every test sets the same value, so the tests do not disturb each other
    unsafe { std::env::set_var("§SECRETNAME", secret) };
    let tenant = start(&api).await;
    let gateway = format!("gw-{}", new_code(12));
    set(&api, tenant, json!({ "planId": PLAN, "status": "active", "gatewayId": gateway })).await;
    let inside = api.in_tenant(tenant, ADMIN_ROLE);

    let body = message(&gateway, Some("cancelled"), None);
    assert_eq!(hook(&api, &body, None).await.status, StatusCode::UNAUTHORIZED, "a message with no signature is refused");
    assert_eq!(hook(&api, &body, Some(signed(&body, "not-the-secret", None))).await.status, StatusCode::UNAUTHORIZED, "a message signed with another secret is refused");
    let old = hook(&api, &body, Some(signed(&body, secret, Some(chrono::Utc::now().timestamp() - 3600)))).await;
    assert_eq!(old.status, StatusCode::BAD_REQUEST, "a signed message an hour old is a replay: {}", old.body);
    assert_eq!(old.json()["type"], "§EXPIRED_TYPE");
    assert_eq!(status_of(&inside).await, "active", "the refused messages changed nothing");

    let past_due = message(&gateway, Some("past_due"), Some(&format!("evt-{}", new_code(12))));
    let applied = hook(&api, &past_due, Some(signed(&past_due, secret, None))).await;
    assert_eq!(applied.status, StatusCode::OK, "{}", applied.body);
    assert_eq!(applied.json()["applied"], json!(true));
    assert_eq!(status_of(&inside).await, "past_due");
§PAUSEDLINES    let repeated = past_due.replace("past_due", "active");
    assert_eq!(hook(&api, &repeated, Some(signed(&repeated, secret, None))).await.json()["applied"], json!(false), "an event id is applied once");
    assert_eq!(status_of(&inside).await, "past_due");
    let resumed = message(&gateway, Some("active"), Some(&format!("evt-{}", new_code(12))));
    assert_eq!(hook(&api, &resumed, Some(signed(&resumed, secret, None))).await.status, StatusCode::OK);
    assert_eq!(status_of(&inside).await, "active");
    let unknown = message("gw-nobody", Some("active"), None);
    assert_eq!(hook(&api, &unknown, Some(signed(&unknown, secret, None))).await.status, StatusCode::NOT_FOUND);
    let incomplete = message(&gateway, None, None);
    assert_eq!(hook(&api, &incomplete, Some(signed(&incomplete, secret, None))).await.status, StatusCode::BAD_REQUEST);
    finish(&api, tenant).await;
}
"""";

    private const string RouteCases = """"

#[tokio::test]
async fn only_a_platform_administrator_changes_a_subscription() {
    let Some(api) = Client::start().await else { return };
    let tenant = start(&api).await;
    let inside = api.in_tenant(tenant, ADMIN_ROLE);
    let refused = inside.put(&format!("/api/subscription/{tenant}"), &json!({ "planId": PLAN, "status": "active" })).await;
    assert_eq!(refused.status, StatusCode::FORBIDDEN, "an administrator inside a tenant cannot give themselves a plan: {}", refused.body);
    assert_eq!(refused.json()["type"], "§PLATFORM_TYPE");
    let platform = api.platform();
    assert_eq!(platform.put("/api/subscription/2147483000", &json!({ "planId": PLAN, "status": "active" })).await.status, StatusCode::NOT_FOUND);
    assert_eq!(platform.put(&format!("/api/subscription/{tenant}"), &json!({ "status": "active" })).await.status, StatusCode::BAD_REQUEST, "a tenant with no subscription needs a plan and a status");
    assert_eq!(platform.put(&format!("/api/subscription/{tenant}"), &json!({ "planId": 2147483000_i64, "status": "active" })).await.status, StatusCode::BAD_REQUEST, "an unknown plan is refused");
    finish(&api, tenant).await;
}

#[tokio::test]
async fn the_administrator_sees_the_subscription_and_a_member_does_not() {
    let Some(api) = Client::start().await else { return };
    let tenant = start(&api).await;
    set(&api, tenant, json!({ "planId": PLAN, "status": "active" })).await;
    let shown = api.in_tenant(tenant, ADMIN_ROLE).get("/api/subscription").await;
    assert_eq!(shown.status, StatusCode::OK, "{}", shown.body);
    let info = shown.json();
    assert_eq!(info["status"], "active");
    assert_eq!(info["planId"], json!(PLAN));
    assert_eq!(info["writable"], json!(true));
    assert_eq!(api.in_tenant(tenant, "Member").get("/api/subscription").await.status, StatusCode::FORBIDDEN);
    finish(&api, tenant).await;
}
"""";
}
