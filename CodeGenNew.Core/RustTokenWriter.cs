using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (axum and sqlx) counterpart of <see cref="TokenWriter"/>: <c>src/routes/tokens.rs</c>, the list without the hash, <c>{id}/revoke</c>, <c>revoke-person/{person_id}</c> and <c>spend</c> of every table in <c>TokenTables</c>, with the same
/// sentences (<see cref="TokenWords"/>), problem type and status codes. A token table has no struct or repository, so each answer is built from the row by position. Every change is one UPDATE of the row that is still active. </summary>
public static class RustTokenWriter
{
    public static string File(IReadOnlyList<TokenShape> tables, ProjectSettings project, SqlDialect dialect)
    {
        var di = DialectInfo.For(dialect);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string P(int n) => di.Placeholder(n);

        var routes = new StringBuilder();
        foreach (var t in tables)
        {
            var main = RustTable.Of(t.Table, project);
            string table = di.QuoteTable(t.Table.SchemaName, t.Table.DbTableName), key = Q(t.Key), hash = Q(t.Hash), expiry = Q(t.Expiry);
            var shown = t.Shown.Where(c => main.Fields.Any(f => f.Column.Name == c.Name)).Select(c => main.FieldOf(c)).ToList();
            string select = string.Join(", ", shown.Select(f => f.Select));
            var keyField = main.FieldOf(t.Key);
            var personField = t.Person is null ? null : main.FieldOf(t.Person);

            // the test that a row is still active, and what ends it; a statement whose SET needs "now" binds it first
            string EndsTest() => string.Concat(t.Ends.Select(c => c.IsBooleanColumn ? $" AND {Q(c)} = {di.BooleanLiteral(false)}" : $" AND {Q(c)} IS NULL"));
            string SetClause(ColumnModel? end, out int used, out string bind)
            {
                bool needsNow = end is null || !end.IsBooleanColumn;
                used = needsNow ? 1 : 0;
                bind = needsNow ? ".bind(now)" : "";
                string column = end is null ? expiry : Q(end);
                return needsNow ? $"{column} = {P(1)}" : $"{column} = {di.BooleanLiteral(true)}";
            }

            string revokeSet = SetClause(t.Revoked, out int ru, out string revokeBind), spendSet = SetClause(t.Spent, out int su, out string spendBind);
            string spendSql = $"UPDATE {table} SET {spendSet} WHERE {key} = {P(su + 1)} AND {expiry} > {P(su + 2)}{EndsTest()}";
            string revokeSql = $"UPDATE {table} SET {revokeSet} WHERE {key} = {P(ru + 1)} AND {expiry} > {P(ru + 2)}{EndsTest()}";
            string personSql = $"UPDATE {table} SET {revokeSet} WHERE {(t.Person is null ? "" : Q(t.Person))} = {P(ru + 1)} AND {expiry} > {P(ru + 2)}{EndsTest()}";
            string familySql = t.Family is null ? "" : $"UPDATE {table} SET {revokeSet} WHERE {Q(t.Family)} = {P(ru + 1)} AND {expiry} > {P(ru + 2)}{EndsTest()}";
            string foundSql = $"SELECT {key}{(t.Person is null ? "" : ", " + Q(t.Person))}{(t.Family is null ? "" : ", " + Q(t.Family) + ", " + expiry)} FROM {table} WHERE {hash} = {P(1)} OR {hash} = {P(2)}";
            string listAll = $"SELECT {select} FROM {table} ORDER BY {key} DESC LIMIT {TokenWords.ListRows}";
            string listPerson = t.Person is null ? "" : $"SELECT {select} FROM {table} WHERE {Q(t.Person)} = {P(1)} ORDER BY {key} DESC LIMIT {TokenWords.ListRows}";
            string one = $"SELECT {select} FROM {table} WHERE {key} = {P(1)}";

            // the answer of a row, read by position
            var describe = new StringBuilder();
            for (int i = 0; i < shown.Count; i++)
                describe.Append($"    let v{i}: {shown[i].Type} = row.try_get({i})?;\n    object.insert(\"{shown[i].Json}\".to_string(), {Json(shown[i], "v" + i)});\n");
            int expiryIndex = shown.FindIndex(f => f.Column.Name == t.Expiry.Name);
            var endTests = new List<string>();
            foreach (var end in t.Ends)
            {
                int index = shown.FindIndex(f => f.Column.Name == end.Name);
                endTests.Add(end.IsBooleanColumn ? (shown[index].IsOption ? $"v{index} == Some(true)" : $"v{index}") : $"v{index}.is_some()");
            }
            string ended = endTests.Count == 0 ? "false" : string.Join(" || ", endTests);
            string expired = shown[expiryIndex].IsOption ? $"v{expiryIndex}.is_none_or(|e| e <= now)" : $"v{expiryIndex} <= now";

            var familyField = t.Family is null ? null : main.FieldOf(t.Family);
            int next = 1;
            string spentPerson = personField is null ? "" : $"    let person: {personField.Type} = found.try_get({next++})?;\n";
            string spentFamily = familyField is null ? "" : $"    let family: {familyField.Type} = found.try_get({next++})?;\n    let expiry: NaiveDateTime = found.try_get({next++})?;\n";
            var spentParts = new List<string> { "\"id\": id" };
            if (personField is not null)
                spentParts.Add("\"personId\": person");
            if (familyField is not null)
                spentParts.Add($"\"family\": {Json(familyField, "family")}");
            string spentJson = $"json!({{ {string.Join(", ", spentParts)} }})";
            string reuse = familyField is null ? "" :
                $"        if expiry > now {{\n{(familyField.IsOption ? "            if let Some(family) = family {\n" : "            {\n")}" +
                $"                sqlx::query({RustTable.Raw(familySql)}){revokeBind}.bind(family).bind(now).execute(&pool).await?;\n            }}\n        }}\n";
            string personRoutes = t.Person is null ? "" : $"        .route(\"{main.Route}/revoke-person/{{person_id}}\", post(revoke_person_{main.Module}))\n";
            string personHandlers = t.Person is null ? "" : FillAll(PersonText, ("§M", main.Module), ("§PT", personField!.BaseType), ("§SQL_PERSON", RustTable.Raw(personSql)), ("§ENDBIND", revokeBind));
            string listQuery = t.Person is null ? "" : $"\n#[derive(Deserialize)]\nstruct ListQuery{main.Struct} {{\n    #[serde(rename = \"personId\")]\n    person_id: Option<{personField!.BaseType}>,\n}}\n";
            string listArgs = t.Person is null ? "" : $", Query(query): Query<ListQuery{main.Struct}>";
            string listBody = t.Person is null
                ? $"    let rows = sqlx::query({RustTable.Raw(listAll)}).fetch_all(&pool).await?;"
                : $"    let rows = match query.person_id {{\n        Some(person) => sqlx::query({RustTable.Raw(listPerson)}).bind(person).fetch_all(&pool).await?,\n        None => sqlx::query({RustTable.Raw(listAll)}).fetch_all(&pool).await?,\n    }};";

            routes.Append(FillAll(TableText,
                ("§M", main.Module), ("§S", main.Struct), ("§ROUTE", main.Route), ("§KT", keyField.BaseType), ("§SQL_SPEND", RustTable.Raw(spendSql)), ("§SQL_REVOKE", RustTable.Raw(revokeSql)), ("§SQL_FOUND", RustTable.Raw(foundSql)),
                ("§SQL_ONE", RustTable.Raw(one)), ("§ENDBIND", revokeBind), ("§SPENDBIND", spendBind), ("§REUSE", reuse), ("§SPENDROUTE", t.Anonymous ? "" : $"        .route(\"{main.Route}/spend\", post(spend_{main.Module}))\n"), ("§PUBLICSPEND", t.Anonymous ? $"\n        .route(\"{main.Route}/spend\", post(spend_{main.Module}))" : ""), ("§SPENTFAMILY", spentFamily), ("§DESCRIBE", describe.ToString()), ("§ENDED", ended), ("§EXPIRED", expired), ("§SPENTPERSON", spentPerson), ("§SPENTJSON", spentJson),
                ("§PERSONROUTES", personRoutes), ("§PERSONHANDLERS", personHandlers), ("§LISTQUERY", listQuery), ("§LISTARGS", listArgs), ("§LISTBODY", listBody)));
        }
        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§T_SPEND", TokenWords.SpendType), ("§W_SPENDTITLE", Str(TokenWords.SpendTitle)), ("§W_SPENDDETAIL", Str(TokenWords.SpendDetail)),
            ("§W_ADMINTITLE", Str(TokenWords.AdminsTitle)), ("§W_ADMINDETAIL", Str(TokenWords.AdminsDetail)), ("§FORBIDDEN", HttpStatus.Forbidden.ToString()), ("§BADREQUEST", HttpStatus.BadRequest.ToString())));
        o.Append("\npub fn router() -> Router<Pool> {\n    Router::new()\n");
        foreach (var t in tables)
            o.Append($"        .merge(router_{RustTable.Of(t.Table, project).Module}())\n");
        o.Append("}\n");
        o.Append("\n/// The spend routes of the tables in TokenAnonymousSpend, which need no sign-in: merged into the sign-in's own router.\npub fn public_router() -> Router<Pool> {\n    Router::new()\n");
        foreach (var t in tables)
            o.Append($"        .merge(public_router_{RustTable.Of(t.Table, project).Module}())\n");
        o.Append("}\n");
        o.Append(routes);
        return o.ToString();
    }

    private static string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Json(RustField f, string name) => f.BaseType switch
    {
        "NaiveDateTime" => $"date_json({(f.IsOption ? name : $"Some({name})")})",
        "String" or "bool" or "i16" or "i32" or "i64" or "f32" or "f64" => $"json!({name})",
        _ => f.IsOption ? $"json!({name}.map(|x| x.to_string()))" : $"json!({name}.to_string())"
    };

    private const string Head = """"
//! The session and token tables (TokenTables): an administrator's list without the hash, revoke, sign a person out everywhere, and spend a token once. The same routes, sentences and problem type as the ASP.NET API (API_Token).
//! The row holds the SHA-256 of the token in hex, never the token. Every change is one UPDATE of the row that is still active, so a token is spent once. Generated by RS_Token.
use axum::{
    Extension, Json, Router,
    extract::{Path, Query, State},
    routing::{get, post},
};
use chrono::NaiveDateTime;
use serde::Deserialize;
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use sqlx::Row;

use crate::auth::{CurrentUser, PROBLEM_ROLE_NOT_ALLOWED};
use crate::support::{ApiError, ApiResult, Pool};

const PROBLEM_NOT_VALID: &str = "§T_SPEND";

/// What a caller sends to spend a token.
#[derive(Deserialize, Default)]
struct TokenSpend {
    #[serde(default)]
    token: Option<String>,
}

fn token_hash(token: &str) -> String {
    Sha256::digest(token.as_bytes()).iter().map(|byte| format!("{byte:02x}")).collect()
}

fn administrator_only(user: &CurrentUser) -> ApiResult<()> {
    if user.is_admin() {
        Ok(())
    } else {
        Err(ApiError::Problem(§FORBIDDEN, PROBLEM_ROLE_NOT_ALLOWED, §W_ADMINTITLE, Some(§W_ADMINDETAIL)))
    }
}

fn not_valid() -> ApiError {
    ApiError::Problem(§BADREQUEST, PROBLEM_NOT_VALID, §W_SPENDTITLE, Some(§W_SPENDDETAIL))
}

fn date_json(value: Option<NaiveDateTime>) -> Value {
    match value {
        Some(date) => json!(date.format("%Y-%m-%dT%H:%M:%S%.f").to_string()),
        None => Value::Null,
    }
}

fn parse_spend(body: &axum::body::Bytes) -> Result<TokenSpend, ApiError> {
    if body.is_empty() {
        return Ok(TokenSpend::default());
    }
    serde_json::from_slice(body).map_err(|error| ApiError::BadRequest(error.to_string()))
}
"""";

    private const string TableText = """"

// ===== §S =====
§LISTQUERY
fn router_§M() -> Router<Pool> {
    Router::new()
        .route("§ROUTE", get(list_§M))
§SPENDROUTE        .route("§ROUTE/{id}/revoke", post(revoke_§M))
§PERSONROUTES}

fn public_router_§M() -> Router<Pool> {
    Router::new()§PUBLICSPEND
}

/// A row as the routes answer it: every column but the hash, and whether the token is still active.
fn describe_§M(row: &<crate::support::Db as sqlx::Database>::Row, now: NaiveDateTime) -> Result<Value, sqlx::Error> {
    let mut object = serde_json::Map::new();
§DESCRIBE    object.insert("active".to_string(), json!(!(§ENDED) && !(§EXPIRED)));
    Ok(Value::Object(object))
}

async fn list_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>§LISTARGS) -> ApiResult<Json<Vec<Value>>> {
    administrator_only(&user)?;
    let now = chrono::Local::now().naive_local();
§LISTBODY
    Ok(Json(rows.iter().map(|row| describe_§M(row, now)).collect::<Result<Vec<_>, _>>()?))
}

/// One UPDATE of the row that is still active: 400 when the token is not known, was used or revoked, or has expired. With a family column, a token that was used before ends its family.
async fn spend_§M(State(pool): State<Pool>, body: axum::body::Bytes) -> ApiResult<Json<Value>> {
    let Some(token) = parse_spend(&body)?.token.filter(|t| !t.is_empty()) else {
        return Err(not_valid());
    };
    let digest = token_hash(&token);
    let upper = digest.to_uppercase();
    let found = sqlx::query(§SQL_FOUND).bind(digest.as_str()).bind(upper.as_str()).fetch_optional(&pool).await?.ok_or_else(not_valid)?;
    let id: §KT = found.try_get(0)?;
§SPENTPERSON§SPENTFAMILY    let now = chrono::Local::now().naive_local();
    let changed = sqlx::query(§SQL_SPEND)§SPENDBIND.bind(id).bind(now).execute(&pool).await?.rows_affected();
    if changed != 1 {
§REUSE        return Err(not_valid());
    }
    Ok(Json(§SPENTJSON))
}

async fn revoke_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(id): Path<§KT>) -> ApiResult<Json<Value>> {
    administrator_only(&user)?;
    let now = chrono::Local::now().naive_local();
    sqlx::query(§SQL_REVOKE)§ENDBIND.bind(id).bind(now).execute(&pool).await?;
    let row = sqlx::query(§SQL_ONE).bind(id).fetch_optional(&pool).await?.ok_or(ApiError::NotFound)?;
    Ok(Json(describe_§M(&row, now)?))
}
§PERSONHANDLERS
"""";

    private const string PersonText = """"

/// Signs a person out everywhere: every token of the person that is still active ends.
async fn revoke_person_§M(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(person_id): Path<§PT>) -> ApiResult<Json<Value>> {
    administrator_only(&user)?;
    let now = chrono::Local::now().naive_local();
    let changed = sqlx::query(§SQL_PERSON)§ENDBIND.bind(person_id).bind(now).execute(&pool).await?.rows_affected();
    Ok(Json(json!({ "revoked": changed })))
}
"""";
}
