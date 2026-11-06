using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum and sqlx) counterpart of <see cref="UsageWriter"/>: <c>src/routes/usage.rs</c>, the usage ledger of every table in <c>UsageTables</c> (the first keeps the plain function names, each other has the module name as a suffix) with the same routes
/// (<c>GET</c> the summary per month and kind, <c>POST</c> for a platform administrator), the same sentences (<see cref="UsageWords"/>) and answers and the same plan-limit reading (<c>UsageLimits</c>).
/// <c>record_usage</c> is the only writer of an event. A ledger table has no struct, so every statement is written out. </summary>
public static class RustUsageWriter
{
    public static string File(IReadOnlyList<UsageShape> tables, DatabaseModel database, ProjectSettings project, SqlDialect dialect)
    {
        var di = DialectInfo.For(dialect);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string P(int n) => di.Placeholder(n);
        bool limits = project.UsageLimits.Length > 0;
        var o = new StringBuilder();
        (string, string)[] words =
        [
            ("§ADMINS_TITLE", Str(UsageWords.AdminsTitle)), ("§ADMINS_TEXT", Str(UsageWords.AdminsDetail)), ("§PLATFORM_TYPE", UsageWords.PlatformType), ("§PLATFORM_TITLE", Str(UsageWords.PlatformTitle)),
            ("§PLATFORM_TEXT", Str(UsageWords.PlatformDetail)), ("§NAMETENANT_TEXT", Str(UsageWords.NameTenant)), ("§NEEDSTENANT_TEXT", Str(UsageWords.NeedsTenant)), ("§NEEDSKIND_TEXT", Str(UsageWords.NeedsKind)),
            ("§KINDLONG_TEXT", UsageWords.KindTooLong), ("§QUANTITY_TEXT", Str(UsageWords.QuantityPositive)), ("§QUANTITYWHOLE_TEXT", Str(UsageWords.QuantityWhole)), ("§UNKNOWNTENANT_TEXT", Str(UsageWords.UnknownTenant))
        ];
        o.Append(TemplateText.FillAll(Head, words));
        if (project.UsageRefuse && limits)
            o.Append(TemplateText.Fill(GateStatic, ("§PERMITS", dialect == SqlDialect.Sqlite ? "1" : "2")));
        string cast = dialect switch { SqlDialect.PostgreSql => "BIGINT", SqlDialect.MySql => "SIGNED", _ => "INTEGER" };
        string Year(string c) => dialect switch { SqlDialect.Sqlite => $"CAST(strftime('%Y', {c}) AS INTEGER)", SqlDialect.PostgreSql => $"CAST(EXTRACT(YEAR FROM {c}) AS BIGINT)", _ => $"CAST(YEAR({c}) AS SIGNED)" };
        string Month(string c) => dialect switch { SqlDialect.Sqlite => $"CAST(strftime('%m', {c}) AS INTEGER)", SqlDialect.PostgreSql => $"CAST(EXTRACT(MONTH FROM {c}) AS BIGINT)", _ => $"CAST(MONTH({c}) AS SIGNED)" };

        var scheduled = new List<string>();
        foreach (var t in tables)
        {
            var keys = UsagePlan.KeysOf(database, project, t);
            bool refuse = project.UsageRefuse && keys.Count > 0;
            var rollup = UsagePlan.RollupFor(database, project, t);
            var main = RustTable.Of(t.Table, project);
            string suffix = UsagePlan.RustSuffix(database, project, t);   // the first table keeps the names other code calls (record_usage, used_this_month)
            var tenantTable = database.Tables.First(x => x.TableName.EqualsIgnoreCase(t.TenantTable));
            var tenantKey = TenantPlan.KeyOf(tenantTable)!;
            string table = di.QuoteTable(t.Table.SchemaName, t.Table.DbTableName), occurred = Q(t.Occurred), kind = Q(t.Kind), tenant = Q(t.Tenant);
            string floatCast = dialect switch { SqlDialect.PostgreSql => "DOUBLE PRECISION", SqlDialect.MySql => "DOUBLE", _ => "REAL" };
            string units = t.Quantity is null ? "COUNT(*)" : t.QuantityIsDecimal ? $"CAST(COALESCE(SUM({Q(t.Quantity)}), 0) AS {floatCast})" : $"CAST(COALESCE(SUM({Q(t.Quantity)}), 0) AS {cast})";
            string unitType = t.QuantityIsDecimal ? "f64" : "i64";
            string keyType = main.FieldOf(t.Key).BaseType;
            var fill = UsagePlan.FillColumns(t);

            string usedSql = $"SELECT {units} FROM {table} WHERE {tenant} = {P(1)} AND {kind} = {P(2)} AND {occurred} >= {P(3)} AND {occurred} < {P(4)}";
            string summarySql = $"SELECT {Year(occurred)}, {Month(occurred)}, {kind}, {units}, COUNT(*) FROM {table} WHERE {tenant} = {P(1)} AND ({P(2)} IS NULL OR {kind} = {P(3)}) AND ({P(4)} IS NULL OR {occurred} >= {P(5)}) " +
                $"AND ({P(6)} IS NULL OR {occurred} < {P(7)}) GROUP BY {Year(occurred)}, {Month(occurred)}, {kind}";
            string tenantExistsSql = $"SELECT COUNT(*) FROM {di.QuoteTable(tenantTable.SchemaName, tenantTable.DbTableName)} WHERE {di.Quote(tenantKey.DbName)} = {P(1)}";

            var columns = new List<string> { tenant, kind, occurred };
            var binds = new StringBuilder(".bind(tenant)\n        .bind(kind.clone())\n        .bind(occurred_on)");
            if (t.Quantity is not null)
            {
                columns.Add(Q(t.Quantity));
                string quantityType = main.FieldOf(t.Quantity).BaseType;
                binds.Append(quantityType switch
                {
                    "Decimal" => "\n        .bind(rust_decimal::Decimal::try_from(quantity).unwrap_or_default())",
                    "f64" => "\n        .bind(quantity)",
                    "f32" => "\n        .bind(quantity as f32)",
                    _ => $"\n        .bind(quantity as {quantityType})"
                });
            }
            foreach (var (column, fillKind) in fill ?? [])
            {
                columns.Add(Q(column));
                binds.Append("\n        .bind(" + fillKind switch
                {
                    TokenPlan.FillKind.Time => "chrono::Local::now().naive_local()",
                    TokenPlan.FillKind.Flag => "false",
                    TokenPlan.FillKind.Number => "0" + main.FieldOf(column).BaseType,
                    TokenPlan.FillKind.Guid => "uuid::Uuid::new_v4()",
                    _ => "\"test\""
                } + ")");
            }
            string insertSql = $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select((_, i) => P(i + 1)))})";
            bool returning = dialect != SqlDialect.MySql;
            string insertRun = returning
                ? $"    let id: {keyType} = sqlx::query_scalar({RustTable.Raw(insertSql + " RETURNING " + Q(t.Key))})\n        {binds}\n        .fetch_one(pool)\n        .await?;\n"
                : $"    let id = sqlx::query({RustTable.Raw(insertSql)})\n        {binds}\n        .execute(pool)\n        .await?\n        .last_insert_id() as {keyType};\n";

            (string, string)[] values =
            [
                ("§S", suffix), ("§M", main.Module), ("§ROUTE", Str(main.Route)), ("§REFUSE", refuse ? TemplateText.Fill(GateText(dialect) + RefuseText, ("§S", suffix), ("§M", main.Module)) + "\n" : ""),
                ("§ALLOWED", TemplateText.Fill(keys.Count > 0 ? AllowedLimited : AllowedNone, ("§S", suffix), ("§LIMITEDKINDS", string.Join(", ", keys.Select(Str))))),
                ("§REFUSEROUTE", refuse ? TemplateText.Fill(RefuseRouteText, ("§S", suffix)) + "\n" : ""), ("§KT", keyType), ("§USED_SQL", RustTable.Raw(usedSql)), ("§SUMMARY_SQL", RustTable.Raw(summarySql)), ("§TENANTEXISTS_SQL", RustTable.Raw(tenantExistsSql)),
                ("§INSERTRUN", insertRun), ("§UNITTYPE", unitType), ("§WHOLE", t.QuantityIsDecimal || t.Quantity is null ? "false" : "true"), ("§WHOLECHECK", t.QuantityIsDecimal || t.Quantity is null ? "" : TemplateText.Fill(WholeCheck, ("§QUANTITYWHOLE_TEXT", Str(UsageWords.QuantityWhole)))), ("§KINDLENGTH", t.Kind.CharacterLength > 0 ? t.Kind.CharacterLength.ToString() : "usize::MAX"), .. words
            ];
            string handler = fill is null ? "" : TemplateText.FillAll(RecordHandler, values);
            string rollBlock = "", rollMerge = "", rollRoute = "", rollHandler = "";
            if (rollup is not null)
            {
                scheduled.Add("roll_up" + suffix);
                var monthly = RustTable.Of(rollup.Table, project);
                string mtable = di.QuoteTable(rollup.Table.SchemaName, rollup.Table.DbTableName), mkey = Q(rollup.Key), mtenant = Q(rollup.Tenant), mkind = Q(rollup.Kind), mmonth = Q(rollup.Month), mq = Q(rollup.Quantity);
                string unitCast = t.QuantityIsDecimal ? floatCast : cast;
                string batchUnits = t.Quantity is null ? $"CAST(1 AS {cast})" : $"CAST({Q(t.Quantity)} AS {unitCast})";
                string batchSql = $"SELECT {Q(t.Key)}, {tenant}, {kind}, {occurred}, {batchUnits} FROM {table} WHERE {occurred} < {P(1)} ORDER BY {Q(t.Key)} LIMIT 1000";
                string findSql = $"SELECT {mkey}, CAST({mq} AS {unitCast}) FROM {mtable} WHERE {mtenant} = {P(1)} AND {mkind} = {P(2)} AND {mmonth} = {P(3)}";
                string insertMonth = $"INSERT INTO {mtable} ({mtenant}, {mkind}, {mmonth}, {mq}) VALUES ({P(1)}, {P(2)}, {P(3)}, {P(4)})";
                string updateMonth = $"UPDATE {mtable} SET {mq} = {P(1)} WHERE {mkey} = {P(2)}";
                string deleteEvent = $"DELETE FROM {table} WHERE {Q(t.Key)} = {P(1)}";
                string monthsSql = $"SELECT {Year(mmonth)}, {Month(mmonth)}, {mkind}, CAST({mq} AS {unitCast}) FROM {mtable} WHERE {mtenant} = {P(1)}";
                string mqType = monthly.FieldOf(rollup.Quantity).BaseType;
                string QBind(string v) => mqType switch
                {
                    "Decimal" => $".bind(rust_decimal::Decimal::try_from({v}).unwrap_or_default())",
                    "f64" => $".bind({v})",
                    "f32" => $".bind({v} as f32)",
                    _ => $".bind({v}.round() as {mqType})"
                };
                (string, string)[] rv =
                [
                    ("§DAYS", project.UsageRollupDays.ToString()), ("§BATCH_SQL", RustTable.Raw(batchSql)), ("§MONTHFIND_SQL", RustTable.Raw(findSql)), ("§MONTHINSERT_SQL", RustTable.Raw(insertMonth)),
                    ("§MONTHUPDATE_SQL", RustTable.Raw(updateMonth)), ("§DELETEEVENT_SQL", RustTable.Raw(deleteEvent)), ("§MONTHS_SQL", RustTable.Raw(monthsSql)), ("§MKT", monthly.FieldOf(rollup.Key).BaseType),
                    ("§MONTHVALUE", rollup.MonthIsDateOnly ? "NaiveDate::from_ymd_opt(year, month, 1).unwrap()" : "NaiveDate::from_ymd_opt(year, month, 1).unwrap().and_hms_opt(0, 0, 0).unwrap()"),
                    ("§QBINDTOTAL", QBind("total")), ("§QBINDADDED", QBind("added"))
                ];
                rollBlock = TemplateText.FillAll(RollupBlock, [.. values, .. rv]);
                rollMerge = TemplateText.FillAll(RollupMerge, [.. values, .. rv]);
                rollRoute = $"        .route({Str(main.Route + "/roll-up")}, post(roll_up_route_{main.Module}))\n";
                rollHandler = TemplateText.FillAll(RollupHandler, [.. values, .. rv]);
            }
            o.Append(TemplateText.FillAll(TableText,
                [.. values, ("§RECORDROUTE", fill is null ? "" : $"        .route({Str(main.Route)}, post(record_route_{main.Module}))\n"), ("§RECORDHANDLER", handler), ("§ROLLUPBLOCK", rollBlock), ("§ROLLUPMERGE", rollMerge),
                    ("§ROLLUPROUTE", rollRoute), ("§ROLLUPHANDLER", rollHandler)]));
        }
        if (scheduled.Count > 0 && project.UsageRollupHours > 0)
            o.Append(TemplateText.FillAll(ScheduleText, ("§HOURS", project.UsageRollupHours.ToString()), ("§CALLS", string.Concat(scheduled.Select(f => $"            if {f}(&pool, None).await.is_err() {{\n                eprintln!(\"The scheduled usage roll-up failed.\");\n            }}\n")))));
        o.Append("\npub fn router() -> Router<Pool> {\n    Router::new()\n");
        foreach (var t in tables)
            o.Append($"        .merge(router_{RustTable.Of(t.Table, project).Module}())\n");
        o.Append("}\n");
        return o.ToString();
    }

    private static string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private const string AllowedNone = """"
const LIMITED_KINDS§S: &[&str] = &[];

/// What the tenant's plan allows of a kind this month; None here, since this table counts no limit.
async fn allowed§S(_pool: &Pool, _tenant: i32, _kind: &str) -> ApiResult<Option<i64>> {
    Ok(None)
}
"""";

    private const string AllowedLimited = """"
const LIMITED_KINDS§S: &[&str] = &[§LIMITEDKINDS];

/// What the tenant's plan allows of a kind this month, or None when it is unlimited (a kind this table does not count for a limit, a tenant with no plan, no limit row).
async fn allowed§S(pool: &Pool, tenant: i32, kind: &str) -> ApiResult<Option<i64>> {
    if !LIMITED_KINDS§S.contains(&kind) {
        return Ok(None);
    }
    match crate::subscriptions::current_state(pool, tenant).await?.plan {
        Some(plan) => crate::subscriptions::allowed(pool, plan, kind).await,
        None => Ok(None),
    }
}
"""";

    private const string Head = """"
//! The usage ledger tables (UsageTables): an append-only table of billable events written only by record_usage, summed per month and kind. The same routes, sentences and answers as the ASP.NET API (API_Usage).
//! GET reads the events of the caller's tenant (an administrator), POST records an event for a tenant (a platform administrator). Nothing edits or deletes an event. Generated by RS_Usage.
#![allow(dead_code, unused_imports)] // a project without a limited kind does not call every helper
use axum::{
    Extension, Json, Router,
    extract::{Query, State},
    http::StatusCode,
    routing::{get, post},
};
use chrono::{Datelike, NaiveDate, NaiveDateTime};
use serde::Deserialize;
use serde_json::{Value, json};
use sqlx::Row;

use crate::auth::CurrentUser;
use crate::support::{ApiError, ApiJson, ApiResult, Pool};

const PROBLEM_PLATFORM: &str = "§PLATFORM_TYPE";

fn now() -> NaiveDateTime {
    chrono::Utc::now().naive_utc()
}

/// The first moment of the month (UTC) that contains the moment, and of the month after it.
fn month_bounds(at: NaiveDateTime) -> (NaiveDateTime, NaiveDateTime) {
    let start = NaiveDate::from_ymd_opt(at.year(), at.month(), 1).unwrap().and_hms_opt(0, 0, 0).unwrap();
    let (year, month) = if at.month() == 12 { (at.year() + 1, 1) } else { (at.year(), at.month() + 1) };
    (start, NaiveDate::from_ymd_opt(year, month, 1).unwrap().and_hms_opt(0, 0, 0).unwrap())
}

fn parse_moment(text: &str) -> Option<NaiveDateTime> {
    let text = text.trim().trim_end_matches('Z');
    NaiveDateTime::parse_from_str(text, "%Y-%m-%dT%H:%M:%S%.f")
        .or_else(|_| NaiveDateTime::parse_from_str(text, "%Y-%m-%d %H:%M:%S%.f"))
        .ok()
        .or_else(|| NaiveDate::parse_from_str(text, "%Y-%m-%d").ok().map(|day| day.and_hms_opt(0, 0, 0).unwrap()))
}

/// What a platform administrator sends to record usage for a tenant.
#[derive(Deserialize, Default)]
struct UsageBody {
    #[serde(rename = "tenantId")]
    tenant_id: Option<i32>,
    kind: Option<String>,
    quantity: Option<f64>,
    #[serde(rename = "occurredOn")]
    occurred_on: Option<String>,
}

#[derive(Deserialize)]
struct RollUpQuery {
    before: Option<String>,
}

#[derive(Deserialize)]
struct SummaryQuery {
    #[serde(rename = "tenantId")]
    tenant_id: Option<i32>,
    kind: Option<String>,
    from: Option<String>,
    to: Option<String>,
}
"""";

    private const string TableText = """"

// ===== §M =====
§ALLOWED
const USED_SQL§S: &str = §USED_SQL;
const SUMMARY_SQL§S: &str = §SUMMARY_SQL;
const TENANT_EXISTS_SQL§S: &str = §TENANTEXISTS_SQL;
/// Whether the ledger counts whole units (an answer then shows 6, not 6.0).
const WHOLE§S: bool = §WHOLE;

/// A number of units as the answers show it.
fn units§S(value: f64) -> Value {
    if WHOLE§S { json!(value as i64) } else { json!(value) }
}

/// The units of a kind the tenant used this month (UTC). The plan limit of a limited kind reads this.
pub async fn used_this_month§S(pool: &Pool, tenant: i32, kind: &str) -> ApiResult<f64> {
    let (start, end) = month_bounds(now());
    let used: §UNITTYPE = sqlx::query_scalar(USED_SQL§S).bind(tenant).bind(kind).bind(start).bind(end).fetch_one(pool).await?;
    Ok(used as f64)
}

/// Records an event: the only way a row gets into the ledger. The event is always written (the ledger is what happened); the answer says whether the tenant is now over its plan.
pub async fn record_usage§S(pool: &Pool, tenant: i32, kind: &str, quantity: f64, occurred_on: Option<NaiveDateTime>) -> ApiResult<Value> {
§REFUSE    let occurred_on = occurred_on.unwrap_or_else(now);
    let kind = kind.to_string();
    let _ = quantity;
§INSERTRUN    let used = used_this_month§S(pool, tenant, &kind).await?;
    let most = allowed§S(pool, tenant, &kind).await?;
    Ok(json!({ "id": id, "used": units§S(used), "allowed": most, "over": most.is_some_and(|m| used > m as f64) }))
}

/// How many more units of a kind the tenant may use this month before its plan is passed; None when it is unlimited. Call it before the work a unit pays for.
pub async fn remaining§S(pool: &Pool, tenant: i32, kind: &str) -> ApiResult<Option<f64>> {
    match allowed§S(pool, tenant, kind).await? {
        Some(most) => Ok(Some((most as f64 - used_this_month§S(pool, tenant, kind).await?).max(0.0))),
        None => Ok(None),
    }
}

fn router_§M() -> Router<Pool> {
    Router::new()
        .route(§ROUTE, get(summary_route_§M))
§RECORDROUTE§ROLLUPROUTE}

/// An administrator reads the usage of their tenant (a platform administrator names one with ?tenantId=).
async fn summary_route_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Query(query): Query<SummaryQuery>) -> ApiResult<Json<Vec<Value>>> {
    if !user.is_admin() {
        return Err(ApiError::Problem(403, "about:blank", §ADMINS_TITLE, Some(§ADMINS_TEXT)));
    }
    let Some(tenant) = user.tenant_id.or(query.tenant_id) else {
        return Err(ApiError::BadRequest(§NAMETENANT_TEXT.to_string()));
    };
    let since = match query.from.as_deref().filter(|t| !t.trim().is_empty()) {
        Some(text) => Some(parse_moment(text).ok_or_else(|| ApiError::BadRequest("The from date is not a date.".to_string()))?),
        None => None,
    };
    let until = match query.to.as_deref().filter(|t| !t.trim().is_empty()) {
        Some(text) => Some(parse_moment(text).ok_or_else(|| ApiError::BadRequest("The to date is not a date.".to_string()))?),
        None => None,
    };
    let kind = query.kind.as_deref().map(str::trim).filter(|k| !k.is_empty()).map(str::to_string);
    let mut rows = sqlx::query_as::<_, (i64, i64, String, §UNITTYPE, i64)>(SUMMARY_SQL§S)
        .bind(tenant)
        .bind(kind.clone())
        .bind(kind.clone())
        .bind(since)
        .bind(since)
        .bind(until)
        .bind(until)
        .fetch_all(&pool)
        .await?;
§ROLLUPMERGE    rows.sort_by(|a, b| b.0.cmp(&a.0).then(b.1.cmp(&a.1)).then(a.2.cmp(&b.2)));
    Ok(Json(rows.into_iter().map(|(year, month, kind, quantity, events)| json!({ "month": format!("{year:04}-{month:02}"), "kind": kind, "quantity": units§S(quantity as f64), "events": events })).collect()))
}
§RECORDHANDLER§ROLLUPBLOCK§ROLLUPHANDLER
"""";

    private const string RollupMerge = """"
    let totals = sqlx::query_as::<_, (i64, i64, String, §UNITTYPE)>(MONTHS_SQL§S).bind(tenant).fetch_all(&pool).await?;
    for (year, month, total_kind, total) in totals {
        let start = NaiveDate::from_ymd_opt(year as i32, month as u32, 1).unwrap().and_hms_opt(0, 0, 0).unwrap();
        if kind.as_deref().is_some_and(|k| k != total_kind) || since.is_some_and(|s| start < s) || until.is_some_and(|u| start.checked_add_months(chrono::Months::new(1)).unwrap() > u) {
            continue;
        }
        if let Some(row) = rows.iter_mut().find(|r| r.0 == year && r.1 == month && r.2 == total_kind) {
            row.3 += total;
        } else {
            rows.push((year, month, total_kind, total, 0));
        }
    }
"""";

    private const string RollupBlock = """"


const ROLLUP_DAYS§S: i64 = §DAYS;
const BATCH_SQL§S: &str = §BATCH_SQL;
const MONTH_FIND_SQL§S: &str = §MONTHFIND_SQL;
const MONTH_INSERT_SQL§S: &str = §MONTHINSERT_SQL;
const MONTH_UPDATE_SQL§S: &str = §MONTHUPDATE_SQL;
const DELETE_EVENT_SQL§S: &str = §DELETEEVENT_SQL;
const MONTHS_SQL§S: &str = §MONTHS_SQL;

/// Adds the events before the day (default: the first day of the month that contains today minus ROLLUP_DAYS§S, never later than this month) into the monthly totals and removes them, a thousand at a time,
/// each batch in one transaction: an event is removed only when it was counted. The owner's scheduler calls this.
pub async fn roll_up§S(pool: &Pool, before: Option<NaiveDateTime>) -> ApiResult<Value> {
    let (this_month, _) = month_bounds(now());
    let cutoff = match before {
        Some(day) if day < this_month => day,
        _ => month_bounds(now() - chrono::Duration::days(ROLLUP_DAYS§S)).0,
    };
    let (mut events, mut months) = (0i64, 0i64);
    loop {
        let batch = sqlx::query_as::<_, (§KT, i32, String, NaiveDateTime, §UNITTYPE)>(BATCH_SQL§S).bind(cutoff).fetch_all(pool).await?;
        if batch.is_empty() {
            break;
        }
        let mut totals: std::collections::HashMap<(i32, String, i32, u32), f64> = std::collections::HashMap::new();
        for (_, tenant, kind, at, units) in &batch {
            *totals.entry((*tenant, kind.clone(), at.year(), at.month())).or_insert(0.0) += *units as f64;
        }
        let mut transaction = pool.begin().await?;
        for ((tenant, kind, year, month), added) in totals {
            let first = §MONTHVALUE;
            let found = sqlx::query_as::<_, (§MKT, §UNITTYPE)>(MONTH_FIND_SQL§S).bind(tenant).bind(kind.as_str()).bind(first).fetch_optional(&mut *transaction).await?;
            match found {
                Some((id, have)) => {
                    let total = have as f64 + added;
                    sqlx::query(MONTH_UPDATE_SQL§S)§QBINDTOTAL.bind(id).execute(&mut *transaction).await?;
                }
                None => {
                    sqlx::query(MONTH_INSERT_SQL§S).bind(tenant).bind(kind.as_str()).bind(first)§QBINDADDED.execute(&mut *transaction).await?;
                }
            }
            months += 1;
        }
        for (id, _, _, _, _) in &batch {
            sqlx::query(DELETE_EVENT_SQL§S).bind(*id).execute(&mut *transaction).await?;
        }
        transaction.commit().await?;
        events += batch.len() as i64;
    }
    Ok(json!({ "events": events, "months": months }))
}
"""";

    private const string RollupHandler = """"


/// A platform administrator rolls the old events up into the monthly totals (?before= names a day: only events before it, and never this month's). 200 with what moved.
async fn roll_up_route_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Query(query): Query<RollUpQuery>) -> ApiResult<Json<Value>> {
    if !user.is_platform() {
        return Err(ApiError::Problem(403, PROBLEM_PLATFORM, §PLATFORM_TITLE, Some(§PLATFORM_TEXT)));
    }
    let before = match query.before.as_deref().filter(|t| !t.trim().is_empty()) {
        Some(text) => Some(parse_moment(text).ok_or_else(|| ApiError::BadRequest("The before date is not a date.".to_string()))?),
        None => None,
    };
    Ok(Json(roll_up§S(&pool, before).await?))
}
"""";

    private const string ScheduleText = """"

const ROLLUP_EVERY_HOURS: u64 = §HOURS;

/// Rolls the old events up every ROLLUP_EVERY_HOURS hours (UsageRollupHours); the first run is one interval after the start. Run it in one instance of the API only.
pub fn start_roll_up(pool: Pool) {
    tokio::spawn(async move {
        let every = std::time::Duration::from_secs(ROLLUP_EVERY_HOURS * 3600);
        let mut timer = tokio::time::interval_at(tokio::time::Instant::now() + every, every);
        loop {
            timer.tick().await;
§CALLS        }
    });
}
"""";

    private const string GateStatic = """"

/// Bounds how many requests of this process check a plan limit and write the event at once, so they do not use up the pool waiting for the database lock; the lock keeps other processes out.
static USAGE_GATE: tokio::sync::Semaphore = tokio::sync::Semaphore::const_new(§PERMITS);
"""";

    /// <summary> The start of <c>record_usage</c> when the project refuses past the limit: it takes the gate (a few requests at a time per process) and the database's own lock of the tenant, runs the rest of the function as
    /// <c>record_usage_locked</c> and gives the lock back, so two requests at the same moment cannot both read the month before either writes. SQLite has one process and needs no lock of its own. </summary>
    private static string GateText(SqlDialect dialect)
    {
        const string head = "    let _permit = USAGE_GATE.acquire().await.expect(\"the usage gate is never closed\");\n";
        const string call = "    let outcome = record_usage_locked§S(pool, tenant, kind, quantity, occurred_on).await;\n";
        const string tail = "    outcome\n}\n\nasync fn record_usage_locked§S(pool: &Pool, tenant: i32, kind: &str, quantity: f64, occurred_on: Option<NaiveDateTime>) -> ApiResult<Value> {\n";
        return dialect switch
        {
            SqlDialect.Sqlite => head + call + tail,
            SqlDialect.PostgreSql => head + "    let mut gate = pool.acquire().await?;\n    sqlx::query(\"SELECT pg_advisory_lock(5821, $1)\").bind(tenant).execute(&mut *gate).await?;\n" + call
                + "    if sqlx::query(\"SELECT pg_advisory_unlock(5821, $1)\").bind(tenant).execute(&mut *gate).await.is_err() {\n        let _ = gate.detach();\n    }\n" + tail,
            _ => head + "    let mut gate = pool.acquire().await?;\n    let name = format!(\"usage-§M-{tenant}\");\n"
                + "    let got: Option<i64> = sqlx::query_scalar(\"SELECT GET_LOCK(?, 60)\").bind(name.as_str()).fetch_one(&mut *gate).await?;\n"
                + "    if got != Some(1) {\n        return Err(ApiError::Problem(503, \"about:blank\", \"Try again\", Some(\"Usage is being recorded for this tenant; try again.\")));\n    }\n" + call
                + "    if sqlx::query_scalar::<_, Option<i64>>(\"SELECT RELEASE_LOCK(?)\").bind(name.as_str()).fetch_one(&mut *gate).await.is_err() {\n        let _ = gate.detach();\n    }\n" + tail
        };
    }

    private const string RefuseText = """"
    if occurred_on.map_or(true, |at| month_bounds(at).0 == month_bounds(now()).0) {
        if let Some(most) = allowed§S(pool, tenant, kind).await? {
            let had = used_this_month§S(pool, tenant, kind).await?;
            if had + quantity > most as f64 {
                return Ok(json!({ "id": null, "used": units§S(had), "allowed": most, "over": true, "refused": true }));
            }
        }
    }
"""";

    private static readonly string RefuseRouteText = """"
    if recorded["refused"] == json!(true) {
        let text = "§LIMIT_TEXT".replace("{0}", &kind).replace("{1}", &recorded["allowed"].to_string()).replace("{2}", &recorded["used"].to_string());
        return Err(ApiError::Problem(402, crate::subscriptions::PROBLEM_LIMIT, "Plan limit reached", Some(crate::subscriptions::leak(&text))));
    }
"""".Replace("§LIMIT_TEXT", SubscriptionWords.LimitText.Replace("\\", "\\\\").Replace("\"", "\\\""));

    private const string WholeCheck = """"
    if quantity.fract() != 0.0 {
        return Err(ApiError::BadRequest(§QUANTITYWHOLE_TEXT.to_string()));
    }
"""";

    private const string RecordHandler = """"

/// A platform administrator records usage for a tenant. 201 with what the month holds now and whether the tenant is over its plan.
async fn record_route_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, ApiJson(body): ApiJson<UsageBody>) -> ApiResult<(StatusCode, Json<Value>)> {
    if !user.is_platform() {
        return Err(ApiError::Problem(403, PROBLEM_PLATFORM, §PLATFORM_TITLE, Some(§PLATFORM_TEXT)));
    }
    let Some(tenant) = body.tenant_id else {
        return Err(ApiError::BadRequest(§NEEDSTENANT_TEXT.to_string()));
    };
    let kind = body.kind.as_deref().unwrap_or("").trim().to_string();
    if kind.is_empty() {
        return Err(ApiError::BadRequest(§NEEDSKIND_TEXT.to_string()));
    }
    if kind.chars().count() > §KINDLENGTH {
        return Err(ApiError::BadRequest(format!("§KINDLONG_TEXT", §KINDLENGTH)));
    }
    let quantity = body.quantity.unwrap_or(1.0);
    if quantity <= 0.0 {
        return Err(ApiError::BadRequest(§QUANTITY_TEXT.to_string()));
    }
§WHOLECHECK
    if sqlx::query_scalar::<_, i64>(TENANT_EXISTS_SQL§S).bind(tenant).fetch_one(&pool).await? == 0 {
        return Err(ApiError::NotFound);
    }
    let occurred = match body.occurred_on.as_deref().filter(|t| !t.trim().is_empty()) {
        Some(text) => Some(parse_moment(text).ok_or_else(|| ApiError::BadRequest("The occurredOn date is not a date.".to_string()))?),
        None => None,
    };
    let recorded = record_usage§S(&pool, tenant, &kind, quantity, occurred).await?;
§REFUSEROUTE    Ok((StatusCode::CREATED, Json(recorded)))
}
"""";
}
