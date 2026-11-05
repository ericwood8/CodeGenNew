using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/api/feature_routes.rs</c> of the Rust API (RS_TestFeatures): tests of the feature flag routes. Each test makes tenants with <c>make_tenant</c> of the tenant tests and flags of its own with SQL, and takes
/// both out again (every Rust test works in a database of its own). The same cases and words as the ASP.NET tests (<see cref="ApiFeatureTests"/>) and the Python tests (<see cref="PythonFeatureTests"/>). </summary>
public static class RustFeatureTests
{
    private const long FirstKey = 90011;

    /// <summary> Whether the tests are written: <c>RustTests</c> over a Rust API with feature flags that have a tenant column on the override table, and a database the flags can be put in with SQL (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        database.Dialect != SqlDialect.SqlServer && RustTenantTests.Applies(database, project) && RustFeatureWriter.PlanOf(database, project) is { OverrideTenant: not null } plan
        && FeatureFlagTables.FlagRowsAreSimple(plan) && plan.FlagKey.IsIntegerColumn;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = RustFeatureWriter.PlanOf(database, project)!;
        var di = DialectInfo.For(database.Dialect);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Raw(string statement) => RustTable.Raw(statement);
        string flagTable = di.QuoteTable(plan.Flag.SchemaName, plan.Flag.DbTableName), overrideTable = di.QuoteTable(plan.Override.SchemaName, plan.Override.DbTableName);
        string Insert(long key, string name, bool on) =>
            $"INSERT INTO {flagTable} ({Q(plan.FlagKey)}, {Q(plan.FlagName)}, {Q(plan.FlagDefault)}) VALUES ({key}, '{name}', {di.BooleanLiteral(on)})";
        string keys = $"{FirstKey}, {FirstKey + 1}";
        string clearTenant = $"DELETE FROM {overrideTable} WHERE {Q(plan.OverrideTenant!)} = {di.Placeholder(1)}";
        string person = plan.OverridePerson is null ? ""
            : "    let somebody = inside.put(\"" + FeatureFlagTables.Path + "/test-feature-off\", &json!({ \"enabled\": true, \"personId\": 1 })).await;\n    assert_eq!(somebody.status, StatusCode::FORBIDDEN, \"{}\", somebody.body);\n";

        // the plan link: a plan of its own (the statements of the subscription tests), the link row, and the removal of the link before the flags
        var link = plan.PlanLink;
        var sql = link is null ? null : SubscriptionTestSql.For(database, project, di.Placeholder(1), out _);
        bool planCase = link is not null && sql is not null && PythonFeatureTests.PlanLinkIsInsertable(link);
        string linkTable = link is null ? "" : di.QuoteTable(link.Table.SchemaName, link.Table.DbTableName);
        string unlink = planCase ? $"DELETE FROM {linkTable} WHERE {Q(link!.Flag)} IN ({keys})" : "";
        var removeFlags = new List<string>();
        if (planCase)
            removeFlags.Add(Raw(unlink));
        removeFlags.Add(Raw($"DELETE FROM {overrideTable} WHERE {Q(plan.OverrideFlag)} IN ({keys})"));
        removeFlags.Add(Raw($"DELETE FROM {flagTable} WHERE {Q(plan.FlagKey)} IN ({keys})"));

        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§ADDFLAGS", $"[{Raw(Insert(FirstKey, "test-feature-on", true))}, {Raw(Insert(FirstKey + 1, "test-feature-off", false))}]"),
            ("§REMOVEFLAGS_COUNT", removeFlags.Count.ToString()), ("§REMOVEFLAGS", $"[{string.Join(", ", removeFlags)}]"),
            ("§CLEARTENANT", Raw(clearTenant)), ("§PATH", FeatureFlagTables.Path)));
        o.Append(FillAll(Cases, ("§PATH", FeatureFlagTables.Path), ("§PERSON", person)));
        if (planCase)
        {
            string insertLink = $"INSERT INTO {linkTable} ({Q(link!.Plan)}, {Q(link.Flag)}) VALUES ({sql!.PlanKey}, {FirstKey + 1})";
            o.Append(FillAll(PlanCase, ("§PATH", FeatureFlagTables.Path), ("§PLAN", sql.PlanKey.ToString()), ("§ADDPLAN", Raw(sql.AddPlan)),
                ("§REMOVEPLAN_COUNT", sql.RemovePlan.Count.ToString()), ("§REMOVEPLAN", "[" + string.Join(", ", sql.RemovePlan.Select(Raw)) + "]"),
                ("§CLEARSUBSCRIPTION", Raw(sql.ClearSubscription)), ("§LINK", Raw(insertLink)), ("§UNLINK", Raw(unlink))));
        }
        return o.ToString().TrimEnd('\n') + "\n";
    }

    private const string Head = """"
// Feature flags through the router. Written by RS_TestFeatures; regenerated with the tables. Each test makes tenants (make_tenant of the tenant tests) and flags of its own with SQL, and takes both out again.
#![allow(dead_code)]
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::tenant_routes::{make_tenant, remove_tenant};
use crate::test_support::*;

const ADD_FLAGS: [&str; 2] = §ADDFLAGS;
const REMOVE_FLAGS: [&str; §REMOVEFLAGS_COUNT] = §REMOVEFLAGS;
const CLEAR_TENANT: &str = §CLEARTENANT;
const ON: &str = "test-feature-on";
const OFF: &str = "test-feature-off";
const NOBODY: &str = "Nobody";

async fn run(api: &Client, statements: &[&'static str]) {
    for statement in statements {
        sqlx::query(*statement).execute(&api.pool()).await.expect("the statement runs");
    }
}

/// The two flags of the tests: ON is on by default and OFF is off.
async fn start(api: &Client) {
    run(api, &REMOVE_FLAGS).await;
    run(api, &ADD_FLAGS).await;
}

/// Takes out the flags (and with them every override), then the tenants.
async fn finish(api: &Client, tenants: &[i32]) {
    run(api, &REMOVE_FLAGS).await;
    for tenant in tenants {
        sqlx::query(CLEAR_TENANT).bind(*tenant).execute(&api.pool()).await.expect("the overrides are removed");
        remove_tenant(api, *tenant).await;
    }
}

async fn seen(api: &Client, tenant: i32, name: &str) -> Option<bool> {
    let reply = api.in_tenant(tenant, NOBODY).get("§PATH").await;
    assert_eq!(reply.status, StatusCode::OK, "{}", reply.body);
    reply.json()[name].as_bool()
}
"""";

    private const string PlanCase = """"

const PLAN: i64 = §PLAN;
const ADD_PLAN: &str = §ADDPLAN;
const REMOVE_PLAN: [&str; §REMOVEPLAN_COUNT] = §REMOVEPLAN;
const CLEAR_SUBSCRIPTION: &str = §CLEARSUBSCRIPTION;
const LINK_PLAN: &str = §LINK;

#[tokio::test]
async fn a_flag_the_plan_lists_is_on_for_a_tenant_on_that_plan_and_the_tenants_override_wins() {
    let Some(api) = Client::start().await else { return };
    start(&api).await;
    let tenant = make_tenant(&api).await;
    run(&api, &REMOVE_PLAN).await;
    run(&api, &[ADD_PLAN, LINK_PLAN]).await;
    let subscribed = api.platform().put(&format!("/api/subscription/{tenant}"), &json!({ "planId": PLAN, "status": "active" })).await;
    assert_eq!(subscribed.status, StatusCode::OK, "{}", subscribed.body);
    assert_eq!(seen(&api, tenant, OFF).await, Some(true), "the tenant's plan lists the flag");
    let admin = api.in_tenant(tenant, ADMIN_ROLE);
    let off = admin.put("§PATH/test-feature-off", &json!({ "enabled": false })).await;
    assert_eq!(off.status, StatusCode::OK, "{}", off.body);
    assert_eq!(seen(&api, tenant, OFF).await, Some(false), "the tenant's override wins over its plan");
    let back = admin.put("§PATH/test-feature-off", &json!({ "enabled": null })).await;
    assert_eq!(back.status, StatusCode::OK, "{}", back.body);
    assert_eq!(seen(&api, tenant, OFF).await, Some(true), "without the override the plan decides again");
    sqlx::query(CLEAR_SUBSCRIPTION).bind(tenant).execute(&api.pool()).await.expect("the subscription is removed");
    finish(&api, &[tenant]).await;
}
"""";

    private const string Cases = """"

#[tokio::test]
async fn a_flag_is_read_with_its_default() {
    let Some(api) = Client::start().await else { return };
    start(&api).await;
    let tenant = make_tenant(&api).await;
    assert_eq!(seen(&api, tenant, ON).await, Some(true));
    assert_eq!(seen(&api, tenant, OFF).await, Some(false));
    assert_eq!(seen(&api, tenant, "test-feature-nothing").await, None, "a name that is not a flag is not listed");
    finish(&api, &[tenant]).await;
}

#[tokio::test]
async fn a_tenants_override_reaches_that_tenant_only_and_a_null_removes_it() {
    let Some(api) = Client::start().await else { return };
    start(&api).await;
    let (first, second) = (make_tenant(&api).await, make_tenant(&api).await);
    let admin = api.in_tenant(first, ADMIN_ROLE);
    let on = admin.put("§PATH/test-feature-off", &json!({ "enabled": true })).await;
    assert_eq!(on.status, StatusCode::OK, "{}", on.body);
    assert_eq!(seen(&api, first, OFF).await, Some(true));
    assert_eq!(seen(&api, second, OFF).await, Some(false));
    let again = admin.put("§PATH/test-feature-off", &json!({ "enabled": true })).await;
    assert_eq!(again.status, StatusCode::OK, "{}", again.body);
    let off = admin.put("§PATH/test-feature-off", &json!({ "enabled": null })).await;
    assert_eq!(off.status, StatusCode::OK, "{}", off.body);
    assert_eq!(seen(&api, first, OFF).await, Some(false));
    finish(&api, &[first, second]).await;
}

#[tokio::test]
async fn only_an_administrator_changes_a_flag_and_only_for_their_own_tenant() {
    let Some(api) = Client::start().await else { return };
    start(&api).await;
    let (tenant, other) = (make_tenant(&api).await, make_tenant(&api).await);
    let employee = api.in_tenant(tenant, NOBODY);
    assert_eq!(employee.put("§PATH/test-feature-off", &json!({ "enabled": true })).await.status, StatusCode::FORBIDDEN);
    let inside = api.in_tenant(tenant, ADMIN_ROLE);
    assert_eq!(inside.put("§PATH/test-feature-off", &json!({ "enabled": true, "tenantId": other })).await.status, StatusCode::FORBIDDEN);
    assert_eq!(inside.put("§PATH/test-feature-nothing", &json!({ "enabled": true })).await.status, StatusCode::NOT_FOUND);
§PERSON    assert_eq!(seen(&api, tenant, OFF).await, Some(false), "nothing was changed");
    finish(&api, &[tenant, other]).await;
}

#[tokio::test]
async fn a_platform_administrator_names_the_tenant_and_exactly_one_target() {
    let Some(api) = Client::start().await else { return };
    start(&api).await;
    let tenant = make_tenant(&api).await;
    let platform = api.platform();
    assert_eq!(platform.put("§PATH/test-feature-off", &json!({ "enabled": true })).await.status, StatusCode::BAD_REQUEST);
    assert_eq!(platform.put("§PATH/test-feature-off", &json!({ "enabled": true, "tenantId": 2147483000 })).await.status, StatusCode::NOT_FOUND);
    let named = platform.put("§PATH/test-feature-off", &json!({ "enabled": true, "tenantId": tenant })).await;
    assert_eq!(named.status, StatusCode::OK, "{}", named.body);
    assert_eq!(seen(&api, tenant, OFF).await, Some(true));
    finish(&api, &[tenant]).await;
}
"""";
}
