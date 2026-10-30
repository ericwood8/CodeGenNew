using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum and sqlx) counterpart of <see cref="ApprovalWriter"/>: <c>src/routes/approvals.rs</c>, the request, approve, reject and withdraw routes, <c>GET toapprove</c> and <c>GET {id}/approvals</c> of
/// every table in <c>RowApprovals</c>, over the same side table, with the same sentences (<see cref="ApprovalWords"/>), problem types and status codes. The SQL is written for the project's database with
/// <see cref="DialectInfo"/>; the shared helpers are written once and each table adds its handlers. </summary>
public static class RustApprovalWriter
{
    public static string File(ApprovalPlan plan, ProjectSettings project, SqlDialect dialect)
    {
        var di = DialectInfo.For(dialect);
        var user = plan.User!;
        string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string P(int n) => di.Placeholder(n);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Tbl(TableModel t) => di.QuoteTable(t.SchemaName, t.DbTableName);

        var o = new StringBuilder();
        var imports = new StringBuilder();
        foreach (var t in plan.Tables)
        {
            var main = RustTable.Of(t.Table, project);
            var side = RustTable.Of(t.Side, project);
            _ = main;
            _ = side;
        }

        string userKeyType = RustTable.Of(user.Table, project).Key.BaseType;
        var approvers = plan.Approvers;
        var columns = new List<string> { Q(user.UserName), Q(user.Table.PrimaryKeyColumns[0]) };
        var types = new List<string> { "String", userKeyType };
        var names = new List<string> { "name", "key" };
        var parts = new List<string>();
        if (approvers is null)
        {
            columns.Add(Q(user.Role!));
            types.Add("Option<bool>");
            names.Add("flag");
            parts.Add("flag.unwrap_or(false)");
        }
        if (user.Admin is { } adminColumn)
        {
            columns.Add(Q(adminColumn));
            types.Add("Option<bool>");
            names.Add("admin");
            parts.Add("admin.unwrap_or(false)");
        }
        parts.Add("user.is_admin()");
        string personSql = $"SELECT {string.Join(", ", columns)} FROM {Tbl(user.Table)} WHERE {Q(user.UserName)} = {P(1)}";
        string personType = "(" + string.Join(", ", types) + ")";
        string personMap = $"|({string.Join(", ", names)})| Person {{ name, key, may_decide: {string.Join(" || ", parts)} }}";
        string mayDecide = MayDecideFlag;
        if (approvers is not null)
        {
            string forTable = approvers.ForTable is { } named ? $" AND ({Q(named)} IS NULL OR {Q(named)} = '' OR {Q(named)} = {P(2)})" : "";
            string bindTable = approvers.ForTable is null ? "" : ".bind(table)";
            string countSql = $"SELECT COUNT(*) FROM {Tbl(approvers.Table)} WHERE {Q(approvers.Person)} = {P(1)}{forTable}";
            mayDecide = MayDecideRows.Replace("§SQL_APPROVER", RustTable.Raw(countSql)).Replace("§BIND_TABLE", bindTable).Replace("§TABLE_ARG", approvers.ForTable is null ? "_table" : "table");
        }

        o.Append(Fill(Head, ("§IMPORTS", imports.ToString().TrimEnd('\n')), ("§PERSON_SQL", RustTable.Raw(personSql)), ("§PERSON_TYPE", personType), ("§PERSON_MAP", personMap), ("§KEY_TYPE", userKeyType), ("§MAYDECIDE", mayDecide),
            ("§T_UNKNOWN", ApprovalWriter.RequesterUnknownType), ("§T_NOTAPPROVER", ApprovalWriter.NotApproverType), ("§T_FOUREYES", ApprovalWriter.FourEyesType),
            ("§T_STATE", ApprovalWriter.WrongStateType), ("§T_REFUSED", ApprovalWriter.RefusedType),
            ("§W_UNKNOWN", Str(ApprovalWords.RequesterUnknown)), ("§W_NOTAPPROVER", Str(ApprovalWords.NotApprover)), ("§W_NOTREQUESTER", Str(ApprovalWords.NotRequester)),
            ("§W_FOUREYES", Str(ApprovalWords.FourEyes)), ("§W_ALREADY", Str(ApprovalWords.AlreadyWaiting)), ("§W_NOTHING", Str(ApprovalWords.NothingWaiting)),
            ("§W_MEANWHILE", Str(ApprovalWords.DecidedMeanwhile)), ("§W_JUSTNOW", Str(ApprovalWords.DecidedJustNow)),
            ("§PENDING", ApprovalStatus.Pending.ToString()), ("§APPROVED", ApprovalStatus.Approved.ToString()), ("§REJECTED", ApprovalStatus.Rejected.ToString()), ("§WITHDRAWN", ApprovalStatus.Withdrawn.ToString())));

        var routes = new StringBuilder();
        foreach (var t in plan.Tables)
        {
            var main = RustTable.Of(t.Table, project);
            var side = RustTable.Of(t.Side, project);
            RustField F(ColumnModel c) => side.FieldOf(c);
            string select = side.SelectList;
            string sideTable = Tbl(t.Side);
            string link = Q(t.Link), status = Q(t.Status), revision = Q(t.Revision), requestedBy = Q(t.RequestedBy), requestedOn = Q(t.RequestedOn);
            string now = RustTable.Now(side.FieldOf(t.RequestedOn));
            string nowOption = RustTable.Now(side.FieldOf(t.DecidedOn));
            string keyType = main.Key.BaseType;

            string latestSql = $"SELECT {select} FROM {sideTable} WHERE {link} = {P(1)} ORDER BY {revision} DESC LIMIT 1";
            // a tenant-owned row is reached only by a caller of its tenant (the side table has no tenant column of its own, so it is judged by the row it points at)
            var owned = TenantPlan.HasApiTenancy(project) ? TenantPlan.OwnedBy(t.Table, project) : null;
            string existsSql = $"SELECT {Q(t.Key)} FROM {Tbl(t.Table)} WHERE {Q(t.Key)} = {P(1)}" + (owned is null ? "" : $" AND {Q(owned.Column)} = {P(2)}");
            string insertSql = $"INSERT INTO {sideTable} ({link}, {status}, {Q(t.Reason)}, {requestedBy}, {requestedOn}, {revision}) VALUES ({P(1)}, {P(2)}, {P(3)}, {P(4)}, {P(5)}, {P(6)})";
            string decideSql = $"UPDATE {sideTable} SET {status} = {P(1)}, {Q(t.DecidedBy)} = {P(2)}, {Q(t.DecidedOn)} = {P(3)}, {Q(t.DecisionNote)} = {P(4)} WHERE {Q(t.SideKey)} = {P(5)} AND {status} = {P(6)}";
            string withdrawSql = $"UPDATE {sideTable} SET {status} = {P(1)}, {Q(t.DecidedBy)} = {P(2)}, {Q(t.DecidedOn)} = {P(3)} WHERE {Q(t.SideKey)} = {P(4)} AND {status} = {P(5)}";
            string waitingSql = $"SELECT {select} FROM {sideTable} WHERE {status} = {P(1)} AND {requestedBy} <> {P(2)}"
                + (owned is null ? "" : $" AND {link} IN (SELECT {Q(t.Key)} FROM {Tbl(t.Table)} WHERE {Q(owned.Column)} = {P(3)})") + $" ORDER BY {requestedOn}";
            string historySql = $"SELECT {select} FROM {sideTable} WHERE {link} = {P(1)} ORDER BY {revision} DESC";

            // the side table has no model of its own (it has no CRUD API), so its row is read into a struct written here
            var row = new StringBuilder($"\n#[derive(sqlx::FromRow)]\nstruct {side.Struct} {{\n");
            foreach (var f in side.Fields)
                row.Append($"    #[sqlx(rename = \"{f.Column.DbName}\")]\n    {f.Name}: {f.Type},\n");
            row.Append("}\n");
            routes.Append(row);
            routes.Append(Fill(TableText,
                ("§M", main.Module), ("§S", side.Struct), ("§TABLE", Str(t.Table.TableName)), ("§ROUTE", main.Route), ("§KT", keyType),
                ("§ST", F(t.Status).Name), ("§RV", F(t.Revision).Name), ("§ID", F(t.SideKey).Name), ("§LINK", F(t.Link).Name), ("§RS", F(t.Reason).Name),
                ("§RB", F(t.RequestedBy).Name), ("§RO", F(t.RequestedOn).Name), ("§DB", F(t.DecidedBy).Name), ("§DO", F(t.DecidedOn).Name), ("§DN", F(t.DecisionNote).Name),
                ("§NOWOPT", nowOption), ("§NOW", now),
                ("§BIND_TENANT", owned is null ? "" : ".bind(user.tenant_id)"), ("§ROWFN", owned is null ? "" : Fill(RowFunction, ("§M", main.Module), ("§KT", keyType), ("§SQL_EXISTS", RustTable.Raw(existsSql)))),
                ("§REQUIRE_ROW_OWN", owned is null ? "" : $"    require_row_{main.Module}(&pool, &user, id).await?;\n"), ("§REQUIRE_ROW", owned is null ? "" : $"    require_row_{main.Module}(pool, user, id).await?;\n"),
                ("§SQL_LATEST", RustTable.Raw(latestSql)), ("§SQL_EXISTS", RustTable.Raw(existsSql)), ("§SQL_INSERT", RustTable.Raw(insertSql)), ("§SQL_DECIDE", RustTable.Raw(decideSql)),
                ("§SQL_WITHDRAW", RustTable.Raw(withdrawSql)), ("§SQL_WAITING", RustTable.Raw(waitingSql)), ("§SQL_HISTORY", RustTable.Raw(historySql)),
                ("§W_NOTHING", Str(ApprovalWords.NothingWaiting)), ("§W_ALREADY", Str(ApprovalWords.AlreadyWaiting)), ("§W_FOUREYES", Str(ApprovalWords.FourEyes)),
                ("§W_NOTREQUESTER", Str(ApprovalWords.NotRequester)), ("§W_MEANWHILE", Str(ApprovalWords.DecidedMeanwhile)), ("§W_JUSTNOW", Str(ApprovalWords.DecidedJustNow)),
                ("§W_NOROW", Str(ApprovalWords.NoSuchRow)),
                ("§PENDING", ApprovalStatus.Pending.ToString()), ("§APPROVED", ApprovalStatus.Approved.ToString()), ("§REJECTED", ApprovalStatus.Rejected.ToString()), ("§WITHDRAWN", ApprovalStatus.Withdrawn.ToString())));
        }
        o.Append("\npub fn router() -> Router<Pool> {\n    Router::new()\n");
        foreach (var t in plan.Tables)
        {
            var main = RustTable.Of(t.Table, project);
            o.Append($"        .merge(router_{main.Module}())\n");
        }
        o.Append("}\n");
        o.Append(routes);
        return o.ToString();
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string MayDecideFlag = """"
async fn may_decide(_pool: &Pool, person: &Person, _table: &str) -> ApiResult<bool> {
    Ok(person.may_decide)
}
"""";

    private const string MayDecideRows = """"
/// An administrator, or a person with a row in the approvers table for this table.
async fn may_decide(pool: &Pool, person: &Person, §TABLE_ARG: &str) -> ApiResult<bool> {
    if person.may_decide {
        return Ok(true);
    }
    let (rows,): (i64,) = sqlx::query_as(§SQL_APPROVER).bind(person.key)§BIND_TABLE.fetch_one(pool).await?;
    Ok(rows > 0)
}
"""";

    private const string Head = """"
// The row approvals (RowApprovals): for each table a request, an approval or rejection by someone else, a withdrawal, the requests waiting for the signed-in approver and the history of a row, over the
// <Table>Approval side table, one row per request cycle. The same routes, sentences and problem types as the ASP.NET API (API_Approvals). Generated by RS_Approvals.
use std::future::Future;
use std::pin::Pin;
use std::sync::RwLock;

use axum::{
    Extension, Json, Router,
    extract::{Path, State},
    routing::{get, post},
};
use chrono::NaiveDateTime;
use serde::{Deserialize, Serialize};

use crate::auth::CurrentUser;
use crate::support::{ApiError, ApiResult, Db, Pool};
§IMPORTS

const PROBLEM_UNKNOWN: &str = "§T_UNKNOWN";
const PROBLEM_NOT_APPROVER: &str = "§T_NOTAPPROVER";
const PROBLEM_FOUR_EYES: &str = "§T_FOUREYES";
const PROBLEM_WRONG_STATE: &str = "§T_STATE";
const PROBLEM_REFUSED: &str = "§T_REFUSED";

const PENDING: i32 = §PENDING;
const APPROVED: i32 = §APPROVED;
const REJECTED: i32 = §REJECTED;
const WITHDRAWN: i32 = §WITHDRAWN;

fn status_name(status: i32) -> &'static str {
    match status {
        PENDING => "Pending",
        APPROVED => "Approved",
        REJECTED => "Rejected",
        WITHDRAWN => "Withdrawn",
        _ => "Unknown",
    }
}

/// What a caller sends with a request (reason) or a decision (note).
#[derive(Deserialize, Default)]
pub struct ApprovalNote {
    #[serde(default)]
    reason: Option<String>,
    #[serde(default)]
    note: Option<String>,
}

/// One request cycle of a row, as the routes answer it.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ApprovalView {
    id: i64,
    row_id: i64,
    revision: i32,
    status: &'static str,
    reason: Option<String>,
    requested_by: String,
    #[serde(with = "crate::support::json_date_time")]
    requested_on: NaiveDateTime,
    decided_by: Option<String>,
    #[serde(with = "crate::support::json_date_time_option")]
    decided_on: Option<NaiveDateTime>,
    decision_note: Option<String>,
}

/// What happened, handed to the project's hooks.
pub struct ApprovalEvent {
    pub table: String,
    pub row_id: i64,
    pub action: String,
    pub person: String,
    pub revision: i32,
}

pub type ValidateHook = fn(&ApprovalEvent) -> Option<&'static str>;
pub type DecidedFuture<'a> = Pin<Box<dyn Future<Output = Result<(), sqlx::Error>> + Send + 'a>>;
pub type DecidedHook = for<'a> fn(&'a mut sqlx::Transaction<'static, Db>, &'a ApprovalEvent) -> DecidedFuture<'a>;

/// The two places a project adds its own rules. validate runs before a request, an approval, a rejection or a withdrawal and must have no side effects: a sentence refuses it (409).
/// on_decided runs after an approval or rejection is written and before it is committed, with the transaction, so what it changes is saved with the decision.
pub struct ApprovalHooks {
    pub validate: Option<ValidateHook>,
    pub on_decided: Option<DecidedHook>,
}

/// Set the hooks at start-up: `approvals::HOOKS.write().unwrap().validate = Some(my_validate);`
pub static HOOKS: RwLock<ApprovalHooks> = RwLock::new(ApprovalHooks { validate: None, on_decided: None });

fn validate_hook(table: &str, row_id: i64, action: &str, person: &str, revision: i32) -> ApiResult<()> {
    let hook = HOOKS.read().unwrap().validate;
    if let Some(validate) = hook {
        let event = ApprovalEvent { table: table.into(), row_id, action: action.into(), person: person.into(), revision };
        if let Some(refusal) = validate(&event) {
            return Err(ApiError::Problem(409, PROBLEM_REFUSED, "Refused", Some(refusal)));
        }
    }
    Ok(())
}

async fn decided_hook(tx: &mut sqlx::Transaction<'static, Db>, event: &ApprovalEvent) -> Result<(), sqlx::Error> {
    let hook = HOOKS.read().unwrap().on_decided;
    match hook {
        Some(decided) => decided(tx, event).await,
        None => Ok(()),
    }
}

/// The signed-in person as the user table knows them: may_decide is an administrator or a person whose role flag is set; key is their key in the user table.
struct Person {
    name: String,
    key: §KEY_TYPE,
    may_decide: bool,
}

async fn find_person(pool: &Pool, user: &CurrentUser) -> ApiResult<Option<Person>> {
    if user.name.is_empty() {
        return Ok(None);
    }
    let row: Option<§PERSON_TYPE> = sqlx::query_as(§PERSON_SQL).bind(&user.name).fetch_optional(pool).await?;
    Ok(row.map(§PERSON_MAP))
}

async fn require_person(pool: &Pool, user: &CurrentUser) -> ApiResult<Person> {
    find_person(pool, user).await?.ok_or(ApiError::Problem(403, PROBLEM_UNKNOWN, "Not allowed", Some(§W_UNKNOWN)))
}

§MAYDECIDE

async fn require_approver(pool: &Pool, user: &CurrentUser, table: &str) -> ApiResult<Person> {
    let person = require_person(pool, user).await?;
    if may_decide(pool, &person, table).await? {
        Ok(person)
    } else {
        Err(ApiError::Problem(403, PROBLEM_NOT_APPROVER, "Not allowed", Some(§W_NOTAPPROVER)))
    }
}

fn wrong_state(detail: &'static str) -> ApiError {
    ApiError::Problem(409, PROBLEM_WRONG_STATE, "Wrong state", Some(detail))
}
"""";

    private const string RowFunction = """"

/// A tenant-owned row is reached only by a caller of its tenant, for the side table has no tenant column of its own.
async fn require_row_§M(pool: &Pool, user: &CurrentUser, id: §KT) -> ApiResult<()> {
    let exists: Option<§KT> = sqlx::query_scalar(§SQL_EXISTS).bind(id).bind(user.tenant_id).fetch_optional(pool).await?;
    if exists.is_none() {
        return Err(ApiError::NotFound);
    }
    Ok(())
}
"""";

    private const string TableText = """"

// ===== §TABLE =====

fn router_§M() -> Router<Pool> {
    Router::new()
        .route("§ROUTE/toapprove", get(to_approve_§M))
        .route("§ROUTE/{id}/approvals", get(history_§M))
        .route("§ROUTE/{id}/request", post(request_§M))
        .route("§ROUTE/{id}/approve", post(approve_§M))
        .route("§ROUTE/{id}/reject", post(reject_§M))
        .route("§ROUTE/{id}/withdraw", post(withdraw_§M))
}

fn view_§M(cycle: §S) -> ApprovalView {
    ApprovalView {
        id: cycle.§ID as i64,
        row_id: cycle.§LINK as i64,
        revision: cycle.§RV,
        status: status_name(cycle.§ST),
        reason: cycle.§RS,
        requested_by: cycle.§RB,
        requested_on: cycle.§RO,
        decided_by: cycle.§DB,
        decided_on: cycle.§DO,
        decision_note: cycle.§DN,
    }
}

§ROWFN
async fn latest_§M(pool: &Pool, id: §KT) -> Result<Option<§S>, sqlx::Error> {
    sqlx::query_as::<_, §S>(§SQL_LATEST).bind(id).fetch_optional(pool).await
}

async fn request_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(id): Path<§KT>, body: Option<Json<ApprovalNote>>) -> ApiResult<Json<ApprovalView>> {
    let person = require_person(&pool, &user).await?;
    let exists: Option<§KT> = sqlx::query_scalar(§SQL_EXISTS).bind(id)§BIND_TENANT.fetch_optional(&pool).await?;
    if exists.is_none() {
        return Err(ApiError::NotFound);
    }
    let current = latest_§M(&pool, id).await?;
    if current.as_ref().is_some_and(|c| c.§ST == PENDING) {
        return Err(wrong_state(§W_ALREADY));
    }
    let revision = current.as_ref().map_or(0, |c| c.§RV) + 1;
    validate_hook(§TABLE, id as i64, "request", &person.name, revision)?;
    let reason = body.and_then(|Json(b)| b.reason).filter(|r| !r.trim().is_empty());
    sqlx::query(§SQL_INSERT).bind(id).bind(PENDING).bind(&reason).bind(&person.name).bind(§NOW).bind(revision).execute(&pool).await?;
    let cycle = latest_§M(&pool, id).await?.ok_or(ApiError::NotFound)?;
    Ok(Json(view_§M(cycle)))
}

async fn decide_§M(pool: &Pool, user: &CurrentUser, id: §KT, body: Option<Json<ApprovalNote>>, approve: bool) -> ApiResult<Json<ApprovalView>> {
    let person = require_approver(pool, user, §TABLE).await?;
§REQUIRE_ROW    let Some(current) = latest_§M(pool, id).await?.filter(|c| c.§ST == PENDING) else {
        return Err(wrong_state(§W_NOTHING));
    };
    if current.§RB.eq_ignore_ascii_case(&person.name) {
        return Err(ApiError::Problem(403, PROBLEM_FOUR_EYES, "Not allowed", Some(§W_FOUREYES)));
    }
    let action = if approve { "approve" } else { "reject" };
    validate_hook(§TABLE, id as i64, action, &person.name, current.§RV)?;
    let note = body.and_then(|Json(b)| b.note).filter(|n| !n.trim().is_empty());
    let mut tx = pool.begin().await?;
    let changed = sqlx::query(§SQL_DECIDE)
        .bind(if approve { APPROVED } else { REJECTED }).bind(&person.name).bind(§NOWOPT).bind(&note).bind(current.§ID).bind(PENDING)
        .execute(&mut *tx).await?.rows_affected();
    if changed == 0 {
        tx.rollback().await?;
        return Err(wrong_state(§W_MEANWHILE));
    }
    decided_hook(&mut tx, &ApprovalEvent { table: §TABLE.into(), row_id: id as i64, action: action.into(), person: person.name.clone(), revision: current.§RV }).await?;
    tx.commit().await?;
    let cycle = latest_§M(pool, id).await?.ok_or(ApiError::NotFound)?;
    Ok(Json(view_§M(cycle)))
}

async fn approve_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(id): Path<§KT>, body: Option<Json<ApprovalNote>>) -> ApiResult<Json<ApprovalView>> {
    decide_§M(&pool, &user, id, body, true).await
}

async fn reject_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(id): Path<§KT>, body: Option<Json<ApprovalNote>>) -> ApiResult<Json<ApprovalView>> {
    decide_§M(&pool, &user, id, body, false).await
}

async fn withdraw_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(id): Path<§KT>) -> ApiResult<Json<ApprovalView>> {
    let person = require_person(&pool, &user).await?;
§REQUIRE_ROW_OWN    let Some(current) = latest_§M(&pool, id).await?.filter(|c| c.§ST == PENDING) else {
        return Err(wrong_state(§W_NOTHING));
    };
    if !current.§RB.eq_ignore_ascii_case(&person.name) {
        return Err(ApiError::Problem(403, PROBLEM_NOT_APPROVER, "Not allowed", Some(§W_NOTREQUESTER)));
    }
    validate_hook(§TABLE, id as i64, "withdraw", &person.name, current.§RV)?;
    let changed = sqlx::query(§SQL_WITHDRAW).bind(WITHDRAWN).bind(&person.name).bind(§NOWOPT).bind(current.§ID).bind(PENDING).execute(&pool).await?.rows_affected();
    if changed == 0 {
        return Err(wrong_state(§W_JUSTNOW));
    }
    let cycle = latest_§M(&pool, id).await?.ok_or(ApiError::NotFound)?;
    Ok(Json(view_§M(cycle)))
}

async fn to_approve_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>) -> ApiResult<Json<Vec<ApprovalView>>> {
    let person = require_approver(&pool, &user, §TABLE).await?;
    let waiting = sqlx::query_as::<_, §S>(§SQL_WAITING).bind(PENDING).bind(&person.name)§BIND_TENANT.fetch_all(&pool).await?;
    Ok(Json(waiting.into_iter().map(view_§M).collect()))
}

async fn history_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(id): Path<§KT>) -> ApiResult<Json<Vec<ApprovalView>>> {
§REQUIRE_ROW_OWN    let cycles = sqlx::query_as::<_, §S>(§SQL_HISTORY).bind(id).fetch_all(&pool).await?;
    Ok(Json(cycles.into_iter().map(view_§M).collect()))
}
"""";
}
