using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (Axum, sqlx) counterpart of <see cref="SubscriptionWriter"/> and <see cref="PythonSubscriptionWriter"/>: <c>src/subscriptions.rs</c>, the check that pauses a tenant's writes while its subscription is not
/// active (<c>require_user</c> calls it), the plan limits on creates, <c>GET /api/subscription</c> and <c>PUT /api/subscription/{tenant_id}</c>. The same rules, sentences and answers as the ASP.NET API. Every statement is fixed
/// text for the database that was read. </summary>
public static class RustSubscriptionWriter
{
    /// <summary> Whether the project asks for subscriptions over a Rust API: <c>SubscriptionTable</c> with the tenants of the Rust API. </summary>
    public static bool Applies(ProjectSettings project) => SubscriptionPlan.IsEnabled(project) && RustTenantWriter.Applies(project);

    private static string IntType(ColumnModel column) => column.SqlType switch
    {
        System.Data.SqlDbType.BigInt => "i64",
        System.Data.SqlDbType.SmallInt => "i16",
        System.Data.SqlDbType.TinyInt => "u8",
        _ => "i32"
    };

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var subscription = SubscriptionPlan.SubscriptionOf(database, project)!;
        var limit = SubscriptionPlan.LimitTableOf(database, project);
        var limited = limit is null ? [] : SubscriptionPlan.LimitedTables(database, project);
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var tenantKey = TenantPlan.KeyOf(tenantTable)!;
        var di = DialectInfo.For(tenantTable.Dialect);
        string Table(TableModel table) => di.QuoteTable(table.SchemaName, table.DbTableName);
        string Q(ColumnModel column) => di.Quote(column.DbName);
        string Raw(string sql) => RustTable.Raw(sql);
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // the columns the subscription read returns, in order: the plan and the status, then the period end and the gateway id when the table has them
        var select = new List<string> { Q(subscription.Plan), Q(subscription.Status) };
        var rowTypes = new List<string> { "i32", "String" };
        int endIndex = -1, gatewayIndex = -1;
        if (subscription.PeriodEnd is not null)
        {
            endIndex = select.Count;
            select.Add(Q(subscription.PeriodEnd));
            rowTypes.Add("Option<chrono::NaiveDateTime>");
        }
        if (subscription.Gateway is not null)
        {
            gatewayIndex = select.Count;
            select.Add(Q(subscription.Gateway));
            rowTypes.Add("Option<String>");
        }
        string readSql = $"SELECT {string.Join(", ", select)} FROM {Table(subscription.Table)} WHERE {Q(subscription.Tenant)} = {di.Placeholder(1)} ORDER BY {Q(subscription.Key)} DESC";
        string existsSql = $"SELECT {Q(subscription.Key)} FROM {Table(subscription.Table)} WHERE {Q(subscription.Tenant)} = {di.Placeholder(1)} ORDER BY {Q(subscription.Key)} DESC";
        string planNameSql = $"SELECT {(subscription.PlanName is null ? di.ToText(Q(subscription.PlanKey)) : Q(subscription.PlanName))} FROM {Table(subscription.PlanTable)} WHERE {Q(subscription.PlanKey)} = {di.Placeholder(1)}";
        string planExistsSql = $"SELECT COUNT(*) FROM {Table(subscription.PlanTable)} WHERE {Q(subscription.PlanKey)} = {di.Placeholder(1)}";
        string priceSelect = subscription.PlanPrice is null ? "NULL" : di.ToDouble(Q(subscription.PlanPrice));
        string periodSelect = subscription.PlanPeriod is null ? "NULL" : Q(subscription.PlanPeriod);
        string nameSelect = subscription.PlanName is null ? di.ToText(Q(subscription.PlanKey)) : Q(subscription.PlanName);
        string plansSql = $"SELECT {Q(subscription.PlanKey)}, {nameSelect}, {priceSelect}, {periodSelect} FROM {Table(subscription.PlanTable)}";
        string tenantExistsSql = $"SELECT COUNT(*) FROM {Table(tenantTable)} WHERE {Q(tenantKey)} = {di.Placeholder(1)}";

        var o = new StringBuilder();
        o.Append(Fill(Head, ("§INACTIVE_TYPE", SubscriptionWords.InactiveType), ("§LIMIT_TYPE", SubscriptionWords.LimitType), ("§PLATFORM_TYPE", SubscriptionWords.OnlyPlatformType),
            ("§ACTIVE_STATUSES", string.Join(", ", SubscriptionPlan.ActiveStatuses(project).Select(s => Str(s.ToLowerInvariant())))), ("§REQUIRED", project.SubscriptionRequired ? "true" : "false"),
            ("§ROWTYPE", "(" + string.Join(", ", rowTypes) + ",)"), ("§READ_SQL", Raw(readSql)), ("§EXISTS_SQL", Raw(existsSql)), ("§PLANNAME_SQL", Raw(planNameSql)), ("§PLANS_SQL", Raw(plansSql)), ("§PLANEXISTS_SQL", Raw(planExistsSql)),
            ("§TENANTEXISTS_SQL", Raw(tenantExistsSql)),
            ("§ENDVALUE", endIndex < 0 ? "None" : $"row.{endIndex}"), ("§GATEWAYVALUE", gatewayIndex < 0 ? "None" : $"row.{gatewayIndex}"),
            ("§INACTIVE_TEXT", Str(SubscriptionWords.InactiveText).Trim('"')), ("§TRIAL_TEXT", Str(SubscriptionWords.TrialEndedText).Trim('"')), ("§NONE_TEXT", Str(SubscriptionWords.NoSubscriptionText).Trim('"')),
            ("§LIMIT_TEXT", Str(SubscriptionWords.LimitText).Trim('"')), ("§PLATFORM_TEXT", Str(SubscriptionWords.OnlyPlatform).Trim('"')), ("§ADMIN_TEXT", Str(SubscriptionWords.OnlyAdministrator).Trim('"')),
            ("§NEEDS_TEXT", Str(SubscriptionWords.NeedsPlanAndStatus).Trim('"')), ("§UNKNOWNPLAN_TEXT", Str(SubscriptionWords.UnknownPlan).Trim('"')), ("§UNKNOWNTENANT_TEXT", Str(SubscriptionWords.UnknownTenant).Trim('"'))));

        // the limits
        o.Append("\n/// The names of the limits the project counts.\npub const LIMIT_KEYS: &[&str] = &[" + string.Join(", ", limited.Select(l => Str(l.LimitKey))) + "];\n");
        if (limit is not null)
        {
            string allowedSql = $"SELECT {Q(limit.Value)} FROM {Table(limit.Table)} WHERE {Q(limit.Plan)} = {di.Placeholder(1)} AND {Q(limit.Key)} = {di.Placeholder(2)}";
            string valueType = IntType(limit.Value);
            string allLimitsSql = $"SELECT {Q(limit.Plan)}, {Q(limit.Key)}, {Q(limit.Value)} FROM {Table(limit.Table)}";
            o.Append(Fill(LimitHelpers, ("§ALLOWED_SQL", Raw(allowedSql)), ("§ALL_LIMITS_SQL", Raw(allLimitsSql)), ("§VALUETYPE", valueType)));
            o.Append("\nasync fn used(pool: &Pool, tenant: i32, key: &str) -> ApiResult<i64> {\n    let sql = match key {\n");
            foreach (var l in limited)
            {
                string count = $"SELECT COUNT(*) FROM {Table(l.Table)} WHERE {Q(l.Tenant)} = {di.Placeholder(1)}" + (l.Deleted is null ? "" : $" AND {Q(l.Deleted)} = {di.BooleanLiteral(false)}");
                o.Append($"        {Str(l.LimitKey)} => {Raw(count)},\n");
            }
            o.Append("        _ => return Ok(0),\n    };\n    Ok(sqlx::query_scalar::<_, i64>(sql).bind(tenant).fetch_one(pool).await?)\n}\n");
            o.Append("\n/// The limit a create route is counted against, or None when the path adds no row to a limited table.\nfn limit_key(path: &str) -> Option<&'static str> {\n");
            foreach (var l in limited)
            {
                string route = Str(l.CreatePath.ToLowerInvariant());
                o.Append(l.IsMembership
                    ? $"    if path == {route} {{\n        return Some({Str(l.LimitKey)});\n    }}\n"
                    : $"    if path == {route} || path == format!(\"{{}}/import\", {route}) || (path.starts_with(&format!(\"{{}}/\", {route})) && path.ends_with(\"/clone\")) {{\n        return Some({Str(l.LimitKey)});\n    }}\n");
            }
            o.Append("    None\n}\n");
        }
        else
        {
            o.Append("\nasync fn allowed(_pool: &Pool, _plan: i32, _key: &str) -> ApiResult<Option<i64>> {\n    Ok(None)\n}\n");
            o.Append("\nasync fn allowed_by_plan(_pool: &Pool) -> ApiResult<HashMap<(i32, String), Option<i64>>> {\n    Ok(HashMap::new())\n}\n");
            o.Append("\nasync fn used(_pool: &Pool, _tenant: i32, _key: &str) -> ApiResult<i64> {\n    Ok(0)\n}\n");
            o.Append("\nfn limit_key(_path: &str) -> Option<&'static str> {\n    None\n}\n");
        }

        string startColumn = subscription.PeriodStart is null ? "" : $", {Q(subscription.PeriodStart)}";
        string startValue = subscription.PeriodStart is null ? "" : $", {di.CurrentTimestamp}";
        string insertSql = $"INSERT INTO {Table(subscription.Table)} ({Q(subscription.Tenant)}, {Q(subscription.Plan)}, {Q(subscription.Status)}{startColumn}) VALUES ({di.Placeholder(1)}, {di.Placeholder(2)}, {di.Placeholder(3)}{startValue})";
        string updatePlanSql = $"UPDATE {Table(subscription.Table)} SET {Q(subscription.Plan)} = {di.Placeholder(1)} WHERE {Q(subscription.Key)} = {di.Placeholder(2)}";
        string updateStatusSql = $"UPDATE {Table(subscription.Table)} SET {Q(subscription.Status)} = {di.Placeholder(1)} WHERE {Q(subscription.Key)} = {di.Placeholder(2)}";
        string endUpdate = "", gatewayUpdate = "";
        if (subscription.PeriodEnd is not null)
        {
            string stamp = tenantTable.Dialect == SqlDialect.PostgreSql ? $"CAST({di.Placeholder(1)} AS TIMESTAMP)" : di.Placeholder(1);
            endUpdate = Fill(EndUpdate, ("§UPDATE_END_SQL", Raw($"UPDATE {Table(subscription.Table)} SET {Q(subscription.PeriodEnd)} = {stamp} WHERE {Q(subscription.Key)} = {di.Placeholder(2)}")));
        }
        if (subscription.Gateway is not null)
            gatewayUpdate = Fill(GatewayUpdate, ("§UPDATE_GATEWAY_SQL", Raw($"UPDATE {Table(subscription.Table)} SET {Q(subscription.Gateway)} = {di.Placeholder(1)} WHERE {Q(subscription.Key)} = {di.Placeholder(2)}")));
        if (subscription.PeriodEnd is not null)
            o.Append(EndHelper);
        o.Append(Fill(Body, ("§INSERT_SQL", Raw(insertSql)), ("§UPDATE_PLAN_SQL", Raw(updatePlanSql)), ("§UPDATE_STATUS_SQL", Raw(updateStatusSql)), ("§ENDUPDATE", endUpdate), ("§GATEWAYUPDATE", gatewayUpdate),
            ("§PLATFORM_TYPE", SubscriptionWords.OnlyPlatformType), ("§PLATFORM_TEXT", Str(SubscriptionWords.OnlyPlatform).Trim('"')), ("§ADMIN_TEXT", Str(SubscriptionWords.OnlyAdministrator).Trim('"')),
            ("§NEEDS_TEXT", Str(SubscriptionWords.NeedsPlanAndStatus).Trim('"')), ("§UNKNOWNPLAN_TEXT", Str(SubscriptionWords.UnknownPlan).Trim('"')), ("§UNKNOWNTENANT_TEXT", Str(SubscriptionWords.UnknownTenant).Trim('"')),
            ("§WEBHOOKROUTE", SubscriptionPlan.HasWebhook(database, project) ? $".route(\"{SubscriptionWords.WebhookPath}\", axum::routing::post(webhook))" : "")));
        if (SubscriptionPlan.HasWebhook(database, project))
        {
            string findSql = $"SELECT {Q(subscription.Key)}, {Q(subscription.Tenant)} FROM {Table(subscription.Table)} WHERE {Q(subscription.Gateway!)} = {di.Placeholder(1)} ORDER BY {Q(subscription.Key)} DESC";
            string webhookEnd = "";
            if (subscription.PeriodEnd is not null)
            {
                string stamp = tenantTable.Dialect == SqlDialect.PostgreSql ? $"CAST({di.Placeholder(1)} AS TIMESTAMP)" : di.Placeholder(1);
                webhookEnd = Fill(WebhookEnd, ("§WEBHOOK_END_SQL", Raw($"UPDATE {Table(subscription.Table)} SET {Q(subscription.PeriodEnd)} = {stamp} WHERE {Q(subscription.Key)} = {di.Placeholder(2)}")));
            }
            o.Append(Fill(Webhook, ("§SECRETNAME", project.SubscriptionWebhookSecret!), ("§HEADER", SubscriptionWords.WebhookHeader), ("§TOLERANCE", SubscriptionWords.WebhookToleranceSeconds.ToString()),
                ("§SIGNATURE_TYPE", SubscriptionWords.WebhookSignatureType), ("§EXPIRED_TYPE", SubscriptionWords.WebhookExpiredType), ("§FIND_SQL", Raw(findSql)),
                ("§WEBHOOK_STATUS_SQL", Raw(updateStatusSql)), ("§WEBHOOKEND", webhookEnd),
                ("§SIGNATURE_TEXT", SubscriptionWords.WebhookSignatureText), ("§EXPIRED_TEXT", SubscriptionWords.WebhookExpiredText), ("§UNCONFIGURED_TEXT", SubscriptionWords.WebhookUnconfiguredText),
                ("§BODY_TEXT", SubscriptionWords.WebhookBodyText), ("§UNKNOWN_TEXT", SubscriptionWords.WebhookUnknownText)));
        }
        return o.ToString();
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Head = """"
// Plans, subscriptions and entitlements (SubscriptionTable): a tenant's writes pause while its subscription is not active, a create past a plan limit is refused, and the subscription is read and changed through
// GET /api/subscription and PUT /api/subscription/{tenant_id}. The same rules and sentences as the ASP.NET API (API_Subscriptions). Written by RS_Subscriptions; regenerate it instead of editing it.
#![allow(dead_code, unused_imports)] // a project without a limit table does not call every helper
use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

use axum::{
    Extension, Json, Router,
    extract::{Path, State},
    http::Method,
    routing::{get, put},
};
use serde::Deserialize;

use crate::auth::CurrentUser;
use crate::support::{ApiError, ApiJson, ApiResult, Pool};

pub const PROBLEM_INACTIVE: &str = "§INACTIVE_TYPE";
pub const PROBLEM_LIMIT: &str = "§LIMIT_TYPE";
pub const PROBLEM_PLATFORM: &str = "§PLATFORM_TYPE";
const STATE_SECONDS: u64 = 10;
const STATE_SECONDS_VARIABLE: &str = "SUBSCRIPTION_STATE_SECONDS";

/// How long a tenant's subscription is remembered: STATE_SECONDS, or the number in the environment variable SUBSCRIPTION_STATE_SECONDS (0 asks the database every time, which the tests set).
fn state_seconds() -> u64 {
    std::env::var(STATE_SECONDS_VARIABLE).ok().and_then(|text| text.trim().parse().ok()).unwrap_or(STATE_SECONDS)
}
const ACTIVE_STATUSES: &[&str] = &[§ACTIVE_STATUSES];
const REQUIRED: bool = §REQUIRED;

const READ_SQL: &str = §READ_SQL;
const EXISTS_SQL: &str = §EXISTS_SQL;
const PLAN_NAME_SQL: &str = §PLANNAME_SQL;
const PLANS_SQL: &str = §PLANS_SQL;
const PLAN_EXISTS_SQL: &str = §PLANEXISTS_SQL;
const TENANT_EXISTS_SQL: &str = §TENANTEXISTS_SQL;

/// What a tenant's subscription says now.
#[derive(Clone)]
pub struct Snapshot {
    pub exists: bool,
    pub plan: Option<i32>,
    pub status: Option<String>,
    pub end: Option<chrono::NaiveDateTime>,
    pub gateway: Option<String>,
    pub writable: bool,
    pub why: Option<String>,
}

fn known() -> &'static Mutex<HashMap<i32, (Snapshot, Instant)>> {
    static KNOWN: OnceLock<Mutex<HashMap<i32, (Snapshot, Instant)>>> = OnceLock::new();
    KNOWN.get_or_init(Default::default)
}

/// The rule: a status outside ACTIVE_STATUSES pauses writes, and a trialing subscription also stops at its period end.
pub fn evaluate(status: &str, end: Option<chrono::NaiveDateTime>, now: chrono::NaiveDateTime) -> (bool, Option<String>) {
    let lower = status.trim().to_lowercase();
    if !ACTIVE_STATUSES.contains(&lower.as_str()) {
        return (false, Some("§INACTIVE_TEXT".replace("{0}", status)));
    }
    if let Some(ends) = end {
        if ends < now && lower == "trialing" {
            return (false, Some("§TRIAL_TEXT".replace("{0}", &ends.format("%Y-%m-%d").to_string())));
        }
    }
    (true, None)
}

pub async fn read_state(pool: &Pool, tenant: i32) -> ApiResult<Snapshot> {
    let row = sqlx::query_as::<_, §ROWTYPE>(READ_SQL).bind(tenant).fetch_optional(pool).await?;
    Ok(match row {
        None => Snapshot { exists: false, plan: None, status: None, end: None, gateway: None, writable: !REQUIRED, why: if REQUIRED { Some("§NONE_TEXT".to_string()) } else { None } },
        Some(row) => {
            let end: Option<chrono::NaiveDateTime> = §ENDVALUE;
            let (writable, why) = evaluate(&row.1, end, chrono::Utc::now().naive_utc());
            Snapshot { exists: true, plan: Some(row.0), status: Some(row.1.clone()), end, gateway: §GATEWAYVALUE, writable, why }
        }
    })
}

/// The tenant's subscription (its newest row), remembered for STATE_SECONDS so a status change reaches a request in use after that long unless forget_state is called.
pub async fn current_state(pool: &Pool, tenant: i32) -> ApiResult<Snapshot> {
    let cached = known().lock().ok().and_then(|map| map.get(&tenant).cloned()).filter(|(_, at)| at.elapsed() < Duration::from_secs(state_seconds()));
    if let Some((state, _)) = cached {
        return Ok(state);
    }
    let state = read_state(pool, tenant).await?;
    if let Ok(mut map) = known().lock() {
        map.insert(tenant, (state.clone(), Instant::now()));
    }
    Ok(state)
}

/// Forgets what is known of a tenant's subscription, so the next call asks the database.
pub fn forget_state(tenant: i32) {
    if let Ok(mut map) = known().lock() {
        map.remove(&tenant);
    }
}

/// Called by require_user for a caller who works in a tenant: a paused subscription refuses a write (402), and a create that would pass a plan limit is refused (402). Reads, sign-in and the subscription routes are never paused.
pub async fn check_write(pool: &Pool, method: &Method, path: &str, tenant: i32) -> ApiResult<()> {
    if !(method == Method::POST || method == Method::PUT || method == Method::PATCH || method == Method::DELETE) {
        return Ok(());
    }
    let path = path.trim_end_matches('/').to_lowercase();
    if path.starts_with("/api/subscription") || (path.starts_with("/api/auth") && !path.starts_with("/api/auth/members")) {
        return Ok(());
    }
    let state = current_state(pool, tenant).await?;
    if !state.writable {
        return Err(ApiError::Problem(402, PROBLEM_INACTIVE, "Subscription not active", state.why.as_deref().map(leak)));
    }
    if method == Method::POST {
        if let (Some(key), Some(plan)) = (limit_key(&path), state.plan) {
            if let Some(most) = allowed(pool, plan, key).await? {
                let have = used(pool, tenant, key).await?;
                if have >= most {
                    let text = "§LIMIT_TEXT".replace("{0}", key).replace("{1}", &most.to_string()).replace("{2}", &have.to_string());
                    return Err(ApiError::Problem(402, PROBLEM_LIMIT, "Plan limit reached", Some(leak(&text))));
                }
            }
        }
    }
    Ok(())
}

/// The problem answers hold a static sentence, so a sentence made at run time is kept for the life of the process; the same sentence is kept once (there are few: one per status and per limit state).
fn leak(text: &str) -> &'static str {
    static SENTENCES: OnceLock<Mutex<HashMap<String, &'static str>>> = OnceLock::new();
    let mut map = SENTENCES.get_or_init(Default::default).lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    map.entry(text.to_string()).or_insert_with(|| Box::leak(text.to_string().into_boxed_str()))
}
"""";

    private const string LimitHelpers = """"

const ALLOWED_SQL: &str = §ALLOWED_SQL;

/// What the plan allows for a limit, or None when it is unlimited (no row, or a NULL value).
async fn allowed(pool: &Pool, plan: i32, key: &str) -> ApiResult<Option<i64>> {
    let value = sqlx::query_scalar::<_, Option<§VALUETYPE>>(ALLOWED_SQL).bind(plan).bind(key).fetch_optional(pool).await?;
    Ok(value.flatten().map(|v| v as i64))
}

const ALL_LIMITS_SQL: &str = §ALL_LIMITS_SQL;

/// What each plan allows of each limit, keyed by plan and limit name.
async fn allowed_by_plan(pool: &Pool) -> ApiResult<HashMap<(i32, String), Option<i64>>> {
    let rows = sqlx::query_as::<_, (i32, String, Option<§VALUETYPE>)>(ALL_LIMITS_SQL).fetch_all(pool).await?;
    Ok(rows.into_iter().map(|(plan, key, value)| ((plan, key), value.map(|v| v as i64))).collect())
}
"""";

    private const string EndHelper = """"

/// A period end as the text the database stores (UTC, no zone): an RFC 3339 time with its offset, a date and time without one (taken as UTC), or a date.
fn parse_end(text: &str) -> Option<String> {
    let moment = chrono::DateTime::parse_from_rfc3339(text).map(|at| at.naive_utc()).ok()
        .or_else(|| chrono::NaiveDateTime::parse_from_str(text, "%Y-%m-%dT%H:%M:%S%.f").ok())
        .or_else(|| chrono::NaiveDateTime::parse_from_str(text, "%Y-%m-%d %H:%M:%S%.f").ok())
        .or_else(|| chrono::NaiveDate::parse_from_str(text, "%Y-%m-%d").ok().and_then(|day| day.and_hms_opt(0, 0, 0)))?;
    Some(moment.format("%Y-%m-%d %H:%M:%S%.f").to_string())
}
"""";

    private const string EndUpdate = """"
    if let Some(end) = body.period_end.as_deref().map(str::trim).filter(|end| !end.is_empty()) {
        let Some(end) = parse_end(end) else { return Err(ApiError::BadRequest("The period end is not a date and time.".into())) };
        sqlx::query(§UPDATE_END_SQL).bind(end).bind(key).execute(&pool).await?;
    }
"""";

    private const string GatewayUpdate = """"
    if let Some(gateway) = body.gateway_id.as_deref() {
        sqlx::query(§UPDATE_GATEWAY_SQL).bind(gateway.trim().to_string()).bind(key).execute(&pool).await?;
    }
"""";

    private const string Body = """"

#[derive(serde::Serialize)]
struct LimitInfo {
    key: String,
    allowed: Option<i64>,
    used: i64,
}

/// What a plan allows of one limit (None: unlimited).
#[derive(serde::Serialize)]
struct PlanLimit {
    key: String,
    allowed: Option<i64>,
}

/// A plan the tenant could choose: its price and billing period when the plan table has them, and what it allows.
#[derive(serde::Serialize)]
struct PlanInfo {
    #[serde(rename = "planId")]
    plan_id: i32,
    name: String,
    price: Option<f64>,
    period: Option<String>,
    current: bool,
    limits: Vec<PlanLimit>,
}

/// Every plan, cheapest first (a plan with no price last), with what it allows of each limit; current marks the tenant's own.
async fn plans_of(pool: &Pool, current: Option<i32>) -> ApiResult<Vec<PlanInfo>> {
    let rows = sqlx::query_as::<_, (i32, Option<String>, Option<f64>, Option<String>)>(PLANS_SQL).fetch_all(pool).await?;
    let values = allowed_by_plan(pool).await?;
    let mut plans: Vec<PlanInfo> = rows
        .into_iter()
        .map(|(id, name, price, period)| PlanInfo {
            plan_id: id,
            name: name.unwrap_or_default(),
            price,
            period,
            current: Some(id) == current,
            limits: LIMIT_KEYS.iter().map(|key| PlanLimit { key: key.to_string(), allowed: values.get(&(id, key.to_string())).copied().flatten() }).collect(),
        })
        .collect();
    plans.sort_by(|a, b| {
        a.price.is_none().cmp(&b.price.is_none())
            .then(a.price.unwrap_or(0.0).partial_cmp(&b.price.unwrap_or(0.0)).unwrap_or(std::cmp::Ordering::Equal))
            .then(a.plan_id.cmp(&b.plan_id))
    });
    Ok(plans)
}

async fn view(pool: &Pool, tenant: i32) -> ApiResult<serde_json::Value> {
    let state = read_state(pool, tenant).await?;
    let plans = plans_of(pool, state.plan).await?;
    let current = plans.iter().find(|plan| plan.current);
    let (name, price, period) = (current.map(|plan| plan.name.clone()), current.and_then(|plan| plan.price), current.and_then(|plan| plan.period.clone()));
    let mut limits = Vec::new();
    for key in LIMIT_KEYS {
        let most = match state.plan {
            Some(plan) => allowed(pool, plan, key).await?,
            None => None,
        };
        limits.push(LimitInfo { key: key.to_string(), allowed: most, used: used(pool, tenant, key).await? });
    }
    Ok(serde_json::json!({
        "hasSubscription": state.exists, "planId": state.plan, "planName": name, "planPrice": price, "planPeriod": period, "status": state.status, "periodEnd": state.end,
        "gatewayId": state.gateway, "writable": state.writable, "reason": state.why, "limits": limits, "plans": plans,
    }))
}

pub fn router() -> Router<Pool> {
    Router::new().route("/api/subscription", get(get_subscription)).route("/api/subscription/{tenant_id}", put(put_subscription))
}

/// The routes that need no sign-in: the payment gateway's webhook, when the project names a secret for it.
pub fn public_router() -> Router<Pool> {
    Router::new()§WEBHOOKROUTE
}

/// The subscription of the caller's tenant for its administrator: the plan, the status and, for each limit, what the plan allows and what the tenant has.
async fn get_subscription(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>) -> ApiResult<Json<serde_json::Value>> {
    if !user.is_admin() {
        return Err(ApiError::Problem(403, "about:blank", "Administrators only", Some("§ADMIN_TEXT")));
    }
    let tenant = crate::tenancy::require_tenant(&user)?;
    Ok(Json(view(&pool, tenant).await?))
}

/// The body of PUT /api/subscription/{tenant_id}: what to change. A tenant with no subscription needs a planId and a status.
#[derive(Deserialize)]
struct SubscriptionChange {
    #[serde(rename = "planId", default)]
    plan_id: Option<i32>,
    #[serde(default)]
    status: Option<String>,
    #[serde(rename = "periodEnd", default)]
    period_end: Option<String>,
    #[serde(rename = "gatewayId", default)]
    gateway_id: Option<String>,
}

/// Sets the plan, status, period end or gateway id of a tenant's subscription, making the row when the tenant has none. Only a platform administrator may.
async fn put_subscription(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(tenant_id): Path<i32>, ApiJson(body): ApiJson<SubscriptionChange>) -> ApiResult<Json<serde_json::Value>> {
    if !user.is_platform() {
        return Err(ApiError::Problem(403, PROBLEM_PLATFORM, "Platform administrators only", Some("§PLATFORM_TEXT")));
    }
    if sqlx::query_scalar::<_, i64>(TENANT_EXISTS_SQL).bind(tenant_id).fetch_one(&pool).await? == 0 {
        return Err(ApiError::Problem(404, "about:blank", "Not found", Some("§UNKNOWNTENANT_TEXT")));
    }
    if let Some(plan) = body.plan_id {
        if sqlx::query_scalar::<_, i64>(PLAN_EXISTS_SQL).bind(plan).fetch_one(&pool).await? == 0 {
            return Err(ApiError::BadRequest("§UNKNOWNPLAN_TEXT".into()));
        }
    }
    let status = body.status.as_deref().map(str::trim).filter(|status| !status.is_empty());
    let existing = sqlx::query_scalar::<_, i32>(EXISTS_SQL).bind(tenant_id).fetch_optional(&pool).await?;
    let key = match existing {
        Some(key) => {
            if let Some(plan) = body.plan_id {
                sqlx::query(§UPDATE_PLAN_SQL).bind(plan).bind(key).execute(&pool).await?;
            }
            if let Some(status) = status {
                sqlx::query(§UPDATE_STATUS_SQL).bind(status.to_string()).bind(key).execute(&pool).await?;
            }
            key
        }
        None => {
            let (Some(plan), Some(status)) = (body.plan_id, status) else {
                return Err(ApiError::BadRequest("§NEEDS_TEXT".into()));
            };
            sqlx::query(§INSERT_SQL).bind(tenant_id).bind(plan).bind(status.to_string()).execute(&pool).await.map_err(|_| ApiError::BadRequest("The subscription could not be saved (a value is too long or missing).".into()))?;
            sqlx::query_scalar::<_, i32>(EXISTS_SQL).bind(tenant_id).fetch_one(&pool).await?
        }
    };
§ENDUPDATE§GATEWAYUPDATE    forget_state(tenant_id);
    Ok(Json(view(&pool, tenant_id).await?))
}
"""";

    private const string WebhookEnd = """"
    if let Some(end) = message["periodEnd"].as_str().map(str::trim).filter(|end| !end.is_empty()) {
        let Some(end) = parse_end(end) else { return Err(ApiError::BadRequest("§BODY_TEXT".into())) };
        sqlx::query(§WEBHOOK_END_SQL).bind(end).bind(key).execute(&pool).await?;
    }
"""";

    private const string Webhook = """"


use axum::body::Bytes;
use axum::http::HeaderMap;
use hmac::{Hmac, Mac};
use sha2::Sha256;

const WEBHOOK_SECRET_NAME: &str = "§SECRETNAME";
const WEBHOOK_HEADER: &str = "§HEADER";
const WEBHOOK_TOLERANCE: i64 = §TOLERANCE;
const PROBLEM_SIGNATURE: &str = "§SIGNATURE_TYPE";
const PROBLEM_EXPIRED: &str = "§EXPIRED_TYPE";
const WEBHOOK_FIND_SQL: &str = §FIND_SQL;
const WEBHOOK_STATUS_SQL: &str = §WEBHOOK_STATUS_SQL;

fn events() -> &'static Mutex<HashMap<String, Instant>> {
    static EVENTS: OnceLock<Mutex<HashMap<String, Instant>>> = OnceLock::new();
    EVENTS.get_or_init(|| Mutex::new(HashMap::new()))
}

/// True the first time an event id is seen in ten minutes.
fn remember_event(event: &str) -> bool {
    let Ok(mut seen) = events().lock() else { return true };
    seen.retain(|_, at| at.elapsed() < Duration::from_secs(600));
    seen.insert(event.to_string(), Instant::now()).is_none()
}

fn hex_decode(text: &str) -> Option<Vec<u8>> {
    if text.len() % 2 != 0 {
        return None;
    }
    (0..text.len()).step_by(2).map(|at| u8::from_str_radix(text.get(at..at + 2)?, 16).ok()).collect()
}

/// The payment gateway tells the API a subscription's new status. The message is signed (X-Gateway-Signature: t=unix seconds,v1=hex of the HMAC-SHA256 of "t.body" with the shared secret in the environment variable
/// named by WEBHOOK_SECRET_NAME), must be no older than WEBHOOK_TOLERANCE seconds, and is applied once per event id; the body is { id?, gatewayId, status, periodEnd? }. Open to anyone, since the signature is the check.
async fn webhook(State(pool): State<Pool>, headers: HeaderMap, body: Bytes) -> ApiResult<Json<serde_json::Value>> {
    let secret = std::env::var(WEBHOOK_SECRET_NAME).unwrap_or_default();
    if secret.is_empty() {
        return Err(ApiError::Problem(503, "about:blank", "Webhook not configured", Some("§UNCONFIGURED_TEXT")));
    }
    let header = headers.get(WEBHOOK_HEADER).and_then(|value| value.to_str().ok()).unwrap_or("");
    let mut signed_at: Option<i64> = None;
    let mut signature = String::new();
    for part in header.split(',') {
        if let Some((name, value)) = part.trim().split_once('=') {
            match name {
                "t" => signed_at = value.parse().ok(),
                "v1" => signature = value.to_string(),
                _ => {}
            }
        }
    }
    let valid = match (signed_at, hex_decode(&signature)) {
        (Some(at), Some(given)) => match Hmac::<Sha256>::new_from_slice(secret.as_bytes()) {
            Ok(mut mac) => {
                mac.update(format!("{at}.").as_bytes());
                mac.update(&body);
                mac.verify_slice(&given).is_ok()
            }
            Err(_) => false,
        },
        _ => false,
    };
    if !valid {
        return Err(ApiError::Problem(401, PROBLEM_SIGNATURE, "Bad signature", Some("§SIGNATURE_TEXT")));
    }
    if (chrono::Utc::now().timestamp() - signed_at.unwrap_or(0)).abs() > WEBHOOK_TOLERANCE {
        return Err(ApiError::Problem(400, PROBLEM_EXPIRED, "Message expired", Some("§EXPIRED_TEXT")));
    }
    let message: serde_json::Value = serde_json::from_slice(&body).unwrap_or(serde_json::Value::Null);
    let gateway = message["gatewayId"].as_str().unwrap_or("").trim().to_string();
    let status = message["status"].as_str().unwrap_or("").trim().to_string();
    if gateway.is_empty() || status.is_empty() {
        return Err(ApiError::BadRequest("§BODY_TEXT".into()));
    }
    if let Some(event) = message["id"].as_str().filter(|event| !event.is_empty()) {
        if !remember_event(event) {
            return Ok(Json(serde_json::json!({ "applied": false, "duplicate": true })));
        }
    }
    let Some((key, tenant)) = sqlx::query_as::<_, (i32, i32)>(WEBHOOK_FIND_SQL).bind(&gateway).fetch_optional(&pool).await? else {
        return Err(ApiError::Problem(404, "about:blank", "Not found", Some("§UNKNOWN_TEXT")));
    };
    sqlx::query(WEBHOOK_STATUS_SQL).bind(&status).bind(key).execute(&pool).await?;
§WEBHOOKEND    forget_state(tenant);
    Ok(Json(serde_json::json!({ "applied": true, "duplicate": false })))
}
"""";
}
