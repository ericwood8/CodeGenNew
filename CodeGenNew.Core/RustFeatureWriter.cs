using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (Axum, sqlx) counterpart of <see cref="FeatureFlagWriter"/> and <see cref="PythonFeatureWriter"/>: <c>src/features.rs</c> with <c>all_flags</c>, <c>is_enabled</c>, <c>GET /api/features</c> and
/// <c>PUT /api/features/{name}</c>. The same rules, sentences and answers as the ASP.NET API. Every statement is written for the database that was read. </summary>
public static class RustFeatureWriter
{
    /// <summary> The plan as the Rust API serves it: the tenant column counts only when the Rust API has tenants, the person column only with a sign-in that carries the owner id. </summary>
    public static FeatureFlagPlan? PlanOf(DatabaseModel database, ProjectSettings project)
    {
        if (FeatureFlagTables.Resolve(database, project).Plan is not { } plan)
            return null;
        var tenant = RustTenantWriter.Applies(project) ? plan.OverrideTenant : null;
        return tenant is null && plan.OverridePerson is null ? null : plan with { OverrideTenant = tenant, PlanLink = tenant is not null && RustSubscriptionWriter.Applies(project) ? plan.PlanLink : null };
    }

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = RustFeatureWriter.PlanOf(database, project)!;
        var di = DialectInfo.For(plan.Flag.Dialect);
        string Table(TableModel table) => di.QuoteTable(table.SchemaName, table.DbTableName);
        string Q(ColumnModel column) => di.Quote(column.DbName);
        string Raw(string sql) => RustTable.Raw(sql);
        string Text(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string P(int n) => di.Placeholder(n);
        string over = Table(plan.Override);
        var tenantTable = plan.OverrideTenant is null ? null : TenantPlan.TenantTableOf(database, project);
        var user = plan.OverridePerson is null ? null : UserTable.Resolve(database, project).Plan!;
        var (tenant, person) = (plan.OverrideTenant, plan.OverridePerson);

        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§FLAGS_SQL", Raw($"SELECT {Q(plan.FlagKey)}, {Q(plan.FlagName)}, {Q(plan.FlagDefault)} FROM {Table(plan.Flag)}")),
            ("§FLAG_SQL", Raw($"SELECT {Q(plan.FlagKey)} FROM {Table(plan.Flag)} WHERE {Q(plan.FlagName)} = {P(1)}")), ("§PATH", FeatureFlagTables.Path)));

        // the rows of a kind of target: a tenant's rows have no person, a person's rows have no tenant (when the table has both columns)
        string TenantWhere(int first) => $"{Q(tenant!)} = {P(first)}" + (person is null ? "" : $" AND {Q(person)} IS NULL");
        string PersonWhere(int first) => $"{Q(person!)} = {P(first)}" + (tenant is null ? "" : $" AND {Q(tenant)} IS NULL");

        var readBlocks = new StringBuilder();
        if (plan.PlanLink is { } link)
        {
            o.Append($"const PLAN_FLAGS_SQL: &str = {Raw($"SELECT {Q(link.Flag)} FROM {Table(link.Table)} WHERE {Q(link.Plan)} = {P(1)}")};\n");
            readBlocks.Append("    if let Some(tenant) = tenant {\n        if let Some(plan) = crate::subscriptions::read_state(pool, tenant).await?.plan {\n            for flag in sqlx::query_scalar::<_, i32>(PLAN_FLAGS_SQL).bind(plan).fetch_all(pool).await? {\n                if let Some(entry) = flags.get_mut(&flag) {\n                    entry.1 = true;\n                }\n            }\n        }\n    }\n");
        }
        if (tenant is not null)
        {
            o.Append($"const TENANT_ROWS_SQL: &str = {Raw($"SELECT {Q(plan.OverrideFlag)}, {Q(plan.OverrideEnabled)} FROM {over} WHERE {TenantWhere(1)}")};\n");
            readBlocks.Append("    if let Some(tenant) = tenant {\n        for (flag, on) in sqlx::query_as::<_, (i32, bool)>(TENANT_ROWS_SQL).bind(tenant).fetch_all(pool).await? {\n            if let Some(entry) = flags.get_mut(&flag) {\n                entry.1 = on;\n            }\n        }\n    }\n");
        }
        if (person is not null)
        {
            o.Append($"const PERSON_ROWS_SQL: &str = {Raw($"SELECT {Q(plan.OverrideFlag)}, {Q(plan.OverrideEnabled)} FROM {over} WHERE {PersonWhere(1)}")};\n");
            readBlocks.Append("    if let Some(person) = person {\n        for (flag, on) in sqlx::query_as::<_, (i32, bool)>(PERSON_ROWS_SQL).bind(person).fetch_all(pool).await? {\n            if let Some(entry) = flags.get_mut(&flag) {\n                entry.1 = on;\n            }\n        }\n    }\n");
        }

        // the statements of a write to one target
        var writes = new StringBuilder();
        string unknownTenant = "", unknownPerson = "";
        if (tenant is not null)
        {
            writes.Append($"const TENANT_EXISTS_ROW_SQL: &str = {Raw($"SELECT COUNT(*) FROM {over} WHERE {Q(plan.OverrideFlag)} = {P(1)} AND {TenantWhere(2)}")};\n");
            writes.Append($"const TENANT_DELETE_SQL: &str = {Raw($"DELETE FROM {over} WHERE {Q(plan.OverrideFlag)} = {P(1)} AND {TenantWhere(2)}")};\n");
            writes.Append($"const TENANT_UPDATE_SQL: &str = {Raw($"UPDATE {over} SET {Q(plan.OverrideEnabled)} = {P(1)} WHERE {Q(plan.OverrideFlag)} = {P(2)} AND {TenantWhere(3)}")};\n");
            writes.Append($"const TENANT_INSERT_SQL: &str = {Raw($"INSERT INTO {over} ({Q(plan.OverrideFlag)}, {Q(plan.OverrideEnabled)}, {Q(tenant)}) VALUES ({P(1)}, {P(2)}, {P(3)})")};\n");
            if (tenantTable is not null)
                writes.Append($"const TENANT_EXISTS_SQL: &str = {Raw($"SELECT COUNT(*) FROM {Table(tenantTable)} WHERE {Q(TenantPlan.KeyOf(tenantTable)!)} = {P(1)}")};\n");
            unknownTenant = "        if sqlx::query_scalar::<_, i64>(TENANT_EXISTS_SQL).bind(tenant).fetch_one(&pool).await? == 0 {\n            return Err(ApiError::Problem(404, \"about:blank\", \"Not found\", Some(\"" + Text(SubscriptionWords.UnknownTenant) + "\")));\n        }\n";
        }
        if (person is not null)
        {
            writes.Append($"const PERSON_EXISTS_ROW_SQL: &str = {Raw($"SELECT COUNT(*) FROM {over} WHERE {Q(plan.OverrideFlag)} = {P(1)} AND {PersonWhere(2)}")};\n");
            writes.Append($"const PERSON_DELETE_SQL: &str = {Raw($"DELETE FROM {over} WHERE {Q(plan.OverrideFlag)} = {P(1)} AND {PersonWhere(2)}")};\n");
            writes.Append($"const PERSON_UPDATE_SQL: &str = {Raw($"UPDATE {over} SET {Q(plan.OverrideEnabled)} = {P(1)} WHERE {Q(plan.OverrideFlag)} = {P(2)} AND {PersonWhere(3)}")};\n");
            writes.Append($"const PERSON_INSERT_SQL: &str = {Raw($"INSERT INTO {over} ({Q(plan.OverrideFlag)}, {Q(plan.OverrideEnabled)}, {Q(person)}) VALUES ({P(1)}, {P(2)}, {P(3)})")};\n");
            writes.Append($"const PERSON_EXISTS_SQL: &str = {Raw($"SELECT COUNT(*) FROM {Table(user!.Table)} WHERE {Q(user.Table.PrimaryKeyColumns[0])} = {P(1)}")};\n");
            unknownPerson = "        if sqlx::query_scalar::<_, i64>(PERSON_EXISTS_SQL).bind(person).fetch_one(&pool).await? == 0 {\n            return Err(ApiError::Problem(404, \"about:blank\", \"Not found\", Some(\"" + Text(FeatureFlagTables.UnknownPersonText) + "\")));\n        }\n";
        }

        string Apply(string kind, string target) =>
            $"        let found = sqlx::query_scalar::<_, i64>({kind}_EXISTS_ROW_SQL).bind(flag).bind({target}).fetch_one(&pool).await? > 0;\n"
            + $"        match body.enabled {{\n            None => {{ sqlx::query({kind}_DELETE_SQL).bind(flag).bind({target}).execute(&pool).await?; }}\n"
            + $"            Some(on) if found => {{ sqlx::query({kind}_UPDATE_SQL).bind(on).bind(flag).bind({target}).execute(&pool).await?; }}\n"
            + $"            Some(on) => {{ sqlx::query({kind}_INSERT_SQL).bind(flag).bind(on).bind({target}).execute(&pool).await?; }}\n        }}\n";
        string applyTenant = tenant is null ? "" : "    if let Some(tenant) = tenant_target {\n" + unknownTenant + Apply("TENANT", "tenant") + "    }\n";
        string applyPerson = person is null ? "" : "    if let Some(person) = person_target {\n" + unknownPerson + Apply("PERSON", "person") + "    }\n";

        bool tenantsInApi = RustTenantWriter.Applies(project);
        o.Append(writes);
        o.Append(FillAll(Body, ("§READBLOCKS", readBlocks.ToString()), ("§APPLYTENANT", applyTenant), ("§APPLYPERSON", applyPerson),
            ("§SECONDS", project.FeatureCacheSeconds.ToString()), ("§WORKING", tenantsInApi ? "user.tenant_id" : "None"), ("§OWNER", person is null ? "None" : "user.owner_id.map(|id| id as i32)"),
            ("§HASTENANT", tenant is null ? "false" : "true"), ("§HASPERSON", person is null ? "false" : "true"),
            ("§UNKNOWN_TEXT", Text(FeatureFlagTables.UnknownText)), ("§ADMIN_TEXT", Text(FeatureFlagTables.OnlyAdministratorText)), ("§SCOPE_TEXT", Text(FeatureFlagTables.NeedsScopeText)),
            ("§PLATFORM_TEXT", Text(FeatureFlagTables.OnlyPlatformText)), ("§NOTARGET_TEXT", Text(FeatureFlagTables.NoTargetText)), ("§PATH", FeatureFlagTables.Path)));
        return o.ToString();
    }

    private const string Head = """"
// Feature flags kept in two tables (FeatureTables): a flag is on or off by default, a tenant's override replaces the default and a person's override replaces both. GET §PATH and PUT §PATH/{name}.
// The same rules and sentences as the ASP.NET API (API_Features). Written by RS_Features; regenerate it instead of editing it.
#![allow(dead_code, unused_imports, unused_variables)] // a project with one kind of override does not call every helper
use std::collections::{BTreeMap, HashMap};
use std::sync::Mutex;
use std::time::Instant;

use axum::{
    Extension, Json, Router,
    extract::{Path, State},
    routing::{get, put},
};
use serde::Deserialize;

use crate::auth::CurrentUser;
use crate::support::{ApiError, ApiJson, ApiResult, Pool};

const FLAGS_SQL: &str = §FLAGS_SQL;
const FLAG_SQL: &str = §FLAG_SQL;
"""";

    private const string Body = """"

const CACHE_SECONDS: u64 = §SECONDS;

type Cache = HashMap<(String, Option<i32>, Option<i32>), (Instant, BTreeMap<String, bool>)>;

/// The database a copy was read from, so two databases in one process (the tests make one each) never share a copy.
fn database_of(pool: &Pool) -> String {
    format!("{:?}", pool.connect_options())
}

static CACHE: Mutex<Option<Cache>> = Mutex::new(None);

/// Forgets what was read, for a change made to the tables by other means than the routes.
pub fn clear_cache() {
    if let Ok(mut cache) = CACHE.lock() {
        *cache = None;
    }
}

/// Every flag by name with its value for the tenant and the person (either may be None): the default, then the plan's, then the tenant's override, then the person's.
/// What was read is kept for FeatureCacheSeconds (zero reads the tables on every call); a change through the feature routes forgets it at once.
pub async fn all_flags(pool: &Pool, tenant: Option<i32>, person: Option<i32>) -> ApiResult<BTreeMap<String, bool>> {
    if CACHE_SECONDS > 0 {
        if let Ok(cache) = CACHE.lock() {
            if let Some((at, flags)) = cache.as_ref().and_then(|c| c.get(&(database_of(pool), tenant, person))) {
                if at.elapsed().as_secs() < CACHE_SECONDS {
                    return Ok(flags.clone());
                }
            }
        }
    }
    let read = read_flags(pool, tenant, person).await?;
    if CACHE_SECONDS > 0 {
        if let Ok(mut cache) = CACHE.lock() {
            cache.get_or_insert_with(HashMap::new).insert((database_of(pool), tenant, person), (Instant::now(), read.clone()));
        }
    }
    Ok(read)
}

async fn read_flags(pool: &Pool, tenant: Option<i32>, person: Option<i32>) -> ApiResult<BTreeMap<String, bool>> {
    let mut flags: HashMap<i32, (String, bool)> = sqlx::query_as::<_, (i32, String, bool)>(FLAGS_SQL).fetch_all(pool).await?.into_iter().map(|(key, name, on)| (key, (name, on))).collect();
§READBLOCKS    Ok(flags.into_values().collect())
}

/// Whether the named flag is on for the caller of the request; a name that is not a flag is off.
pub async fn is_enabled(pool: &Pool, name: &str, user: &CurrentUser) -> ApiResult<bool> {
    Ok(all_flags(pool, working(user), own_person(user)).await?.get(name).copied().unwrap_or(false))
}

fn working(user: &CurrentUser) -> Option<i32> {
    §WORKING
}

fn own_person(user: &CurrentUser) -> Option<i32> {
    §OWNER
}

pub fn router() -> Router<Pool> {
    Router::new().route("§PATH", get(get_features)).route("§PATH/{name}", put(put_feature))
}

/// Every flag by name as the caller sees it (their tenant's and their own overrides applied).
async fn get_features(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>) -> ApiResult<Json<BTreeMap<String, bool>>> {
    Ok(Json(all_flags(&pool, working(&user), own_person(&user)).await?))
}

/// The body of PUT §PATH/{name}: on or off (null removes the override) for a tenant or a person.
#[derive(Deserialize)]
struct FeatureChange {
    #[serde(default)]
    enabled: Option<bool>,
    #[serde(rename = "tenantId", default)]
    tenant_id: Option<i32>,
    #[serde(rename = "personId", default)]
    person_id: Option<i32>,
}

/// Switches a flag for the caller's tenant (an administrator of the tenant), or for the tenant or person named in the body (a platform administrator).
async fn put_feature(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(name): Path<String>, ApiJson(body): ApiJson<FeatureChange>) -> ApiResult<Json<BTreeMap<String, bool>>> {
    if !user.is_admin() {
        return Err(ApiError::Problem(403, "about:blank", "Administrators only", Some("§ADMIN_TEXT")));
    }
    let Some(flag) = sqlx::query_scalar::<_, i32>(FLAG_SQL).bind(&name).fetch_optional(&pool).await? else {
        return Err(ApiError::Problem(404, "about:blank", "Not found", Some("§UNKNOWN_TEXT")));
    };
    let (tenant_target, person_target): (Option<i32>, Option<i32>);
    if let Some(working) = working(&user) {
        if !§HASTENANT || body.person_id.is_some() || body.tenant_id.is_some_and(|id| id != working) {
            return Err(ApiError::Problem(403, "about:blank", "Platform administrators only", Some("§PLATFORM_TEXT")));
        }
        (tenant_target, person_target) = (Some(working), None);
    } else {
        if (!§HASTENANT && body.tenant_id.is_some()) || (!§HASPERSON && body.person_id.is_some()) {
            return Err(ApiError::BadRequest("§NOTARGET_TEXT".into()));
        }
        if (!§HASTENANT && !§HASPERSON) || body.tenant_id.is_none() == body.person_id.is_none() {
            return Err(ApiError::BadRequest("§SCOPE_TEXT".into()));
        }
        (tenant_target, person_target) = (body.tenant_id, body.person_id);
    }
§APPLYTENANT§APPLYPERSON    clear_cache();
    Ok(Json(all_flags(&pool, tenant_target.or(working(&user)), person_target.or(own_person(&user))).await?))
}
"""";
}
