using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/api/token_routes.rs</c> of the Rust API (RS_TestTokens): the cases of the generated ASP.NET tests (<see cref="ApiTokenTests"/>) and of the Python tests (<see cref="PythonTokenTests"/>) played
/// through the router over the scratch database of RS_Test. Each test inserts rows of its own with plain SQL (the hash of a token it makes up) and removes them before it asserts. Written for each token table
/// whose other columns the generator can fill (<see cref="TokenPlan.TestColumns"/>). </summary>
public static class RustTokenTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => project.RustTests && project.ApiAuth && Tables(database, project).Count > 0;

    public static IReadOnlyList<TokenShape> Tables(DatabaseModel database, ProjectSettings project) =>
        database.Tables.Select(t => TableShapes.For(t, project).Token).OfType<TokenShape>().Where(t => TokenPlan.TestColumns(t) is not null).OrderBy(t => t.Table.TableName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary> The module (and file) name of a table's tests. </summary>
    public static string Module(TokenShape t, ProjectSettings project) => $"token_{RustTable.Of(t.Table, project).Module}_routes";

    public static string File(TokenShape t, DatabaseModel database, ProjectSettings project)
    {
        var di = DialectInfo.For(database.Dialect);
        var main = RustTable.Of(t.Table, project);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Json(ColumnModel c) => JsonNames.Camel(c.Name);
        string table = di.QuoteTable(t.Table.SchemaName, t.Table.DbTableName);
        var fill = TokenPlan.TestColumns(t)!;

        var columns = new List<string> { Q(t.Hash), Q(t.Expiry) };
        var binds = new StringBuilder(".bind(digest.clone())\n        .bind(chrono::Local::now().naive_local() + chrono::Duration::hours(hours))");
        foreach (var end in t.Ends.Where(c => c.IsBooleanColumn))
        {
            columns.Add(Q(end));
            binds.Append("\n        .bind(false)");
        }
        if (t.Person is not null)
        {
            columns.Add(Q(t.Person));
            binds.Append("\n        .bind(person)");
        }
        if (t.Family is not null)
        {
            columns.Add(Q(t.Family));
            binds.Append("\n        .bind(family.unwrap_or_else(fresh_family))");
        }
        int plain = columns.Count;
        foreach (var (column, kind) in fill)
        {
            columns.Add(Q(column));
            binds.Append("\n        .bind(" + kind switch
            {
                TokenPlan.FillKind.Time => "chrono::Local::now().naive_local()",
                TokenPlan.FillKind.Flag => "false",
                TokenPlan.FillKind.Number => "0" + main.FieldOf(column).BaseType,
                TokenPlan.FillKind.Guid => "uuid::Uuid::new_v4()",
                _ => "\"test\""
            } + ")");
        }
        string insert = $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select((c, i) => i < plain ? di.Placeholder(i + 1) : $"COALESCE((SELECT copied.c FROM (SELECT {c} AS c FROM {table} WHERE {Q(t.Key)} = (SELECT MIN({Q(t.Key)}) FROM {table})) copied), {di.Placeholder(i + 1)})"))})";
        string find = $"SELECT {Q(t.Key)} FROM {table} WHERE {Q(t.Hash)} = {di.Placeholder(1)}";
        string delete = $"DELETE FROM {table} WHERE {Q(t.Key)} = {di.Placeholder(1)}";
        string personType = "i32", personSql = "", familyType = "String";
        if (t.Person is not null)
        {
            var userTable = database.Tables.First(x => x.TableName.EqualsIgnoreCase(t.PersonTable!));
            var userKey = userTable.Columns.First(c => c.Name.EqualsIgnoreCase(t.PersonKey!));
            personType = main.FieldOf(t.Person).BaseType;
            personSql = $"SELECT MIN({Q(userKey)}) FROM {di.QuoteTable(userTable.SchemaName, userTable.DbTableName)}";
        }
        if (t.Family is not null)
            familyType = main.FieldOf(t.Family).BaseType;
        string fresh = t.Family is null ? "String::new()" : t.Family.IsIntegerColumn ? $"(std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().subsec_nanos() as {familyType}) + 1_000_000" : t.Family.IsGuidColumn ? "uuid::Uuid::new_v4()" : "new_token()";
        string familyJson = t.Family is not null && t.Family.IsGuidColumn ? "json!(family.to_string())" : "json!(family)";
        string key = Json(t.Key), hashJson = Json(t.Hash), person = t.Person is null ? "" : Json(t.Person), familyName = t.Family is null ? "" : Json(t.Family);
        string personNeeded = t.Person is not null && !t.Person.IsNullable ? "true" : "false";

        string text = TemplateText.FillAll(Head,
            ("§ROUTE", Str(main.Route)), ("§ADMINROLE", Str(project.AuthAdminRole)), ("§INSERT", RustTable.Raw(insert)), ("§FIND", RustTable.Raw(find)), ("§DELETE", RustTable.Raw(delete)), ("§KT", main.FieldOf(t.Key).BaseType),
            ("§PERSONSQL", t.Person is null ? "\"\"" : RustTable.Raw(personSql)), ("§PT", personType), ("§FT", familyType), ("§PERSONNEEDED", personNeeded), ("§FRESH", fresh),
            ("§HASHBIND", binds.ToString()));
        var o = new StringBuilder(text);
        o.Append(TemplateText.FillAll(Cases, ("§KEY", key), ("§HASH", hashJson), ("§T_SPEND", TokenWords.SpendType), ("§T_ROLE", ProblemTypes.RoleNotAllowed),
            ("§SPENDUNSIGNED", t.Anonymous ? "" : "    let spend = nobody.post(&format!(\"{ROUTE}/spend\"), &json!({ \"token\": new_token() })).await;\n    assert_eq!(spend.status, StatusCode::UNAUTHORIZED);\n")));
        if (t.Anonymous)
            o.Append(AnonymousCase);
        if (t.Ends.Count >= 2)
            o.Append(TemplateText.FillAll(TwoEndsCase, ("§KEY", key), ("§USED", Json(t.Spent!)), ("§REVOKED", Json(t.Revoked!))));
        if (t.Family is not null)
            o.Append(TemplateText.FillAll(FamilyCase, ("§FAMILYJSON", familyJson), ("§FAMILY", familyName)));
        if (t.Person is not null)
            o.Append(TemplateText.FillAll(PersonCase, ("§PERSON", person)));
        return o.ToString() + "\n";
    }

    private static string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private const string Head = """"
// The session and token routes through the router: a call without a sign-in, the administrator's list and revoke, a token spent once, an expired or unknown or blank one, and the cases of a family, an
// anonymous spend and a person's tokens. Written by RS_TestTokens; regenerated with the tables. Each test inserts rows of its own and removes them before it asserts.
#![allow(dead_code, unused_variables)]
use axum::http::StatusCode;
use serde_json::{Value, json};
use sha2::{Digest, Sha256};
use std::sync::atomic::{AtomicU64, Ordering};

use crate::test_support::*;

const ROUTE: &str = §ROUTE;
const ADMIN: &str = §ADMINROLE;
const PERSON_NEEDED: bool = §PERSONNEEDED;
type PersonKey = §PT;
type FamilyKey = §FT;

static COUNTER: AtomicU64 = AtomicU64::new(0);

fn new_token() -> String {
    let nanos = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos();
    format!("test-{nanos}-{}", COUNTER.fetch_add(1, Ordering::SeqCst))
}

fn digest_of(token: &str) -> String {
    Sha256::digest(token.as_bytes()).iter().map(|byte| format!("{byte:02x}")).collect()
}

fn fresh_family() -> FamilyKey {
    §FRESH
}

async fn start() -> Option<Client> {
    Client::start_signed_out().await
}

fn admin(api: &Client) -> Client {
    api.in_role(ADMIN)
}

/// The key of a person of the user table to hold a token: None when the table has no row; the outer None says the test cannot run.
async fn person_of(api: &Client) -> Option<Option<PersonKey>> {
    let sql = §PERSONSQL;
    if sql.is_empty() {
        return Some(None);
    }
    let found: Option<PersonKey> = sqlx::query_scalar(sql).fetch_one(&api.pool()).await.unwrap_or(None);
    if found.is_none() && PERSON_NEEDED {
        skip("the user table has no row to hold a token for");
        return None;
    }
    Some(found)
}

/// Adds a token row of the test's own; returns its key.
async fn add(api: &Client, token: &str, hours: i64, person: Option<PersonKey>, family: Option<FamilyKey>, upper: bool) -> §KT {
    let pool = api.pool();
    let digest = if upper { digest_of(token).to_uppercase() } else { digest_of(token) };
    sqlx::query(§INSERT)
        §HASHBIND
        .execute(&pool)
        .await
        .unwrap_or_else(|error| panic!("adding a token row: {error}"));
    sqlx::query_scalar(§FIND).bind(digest).fetch_one(&pool).await.unwrap()
}

async fn remove(api: &Client, ids: &[§KT]) {
    let pool = api.pool();
    for id in ids {
        let _ = sqlx::query(§DELETE).bind(*id).execute(&pool).await;
    }
}
"""";

    private const string Cases = """"

#[tokio::test]
async fn without_a_sign_in_the_routes_answer_401() {
    let Some(api) = start().await else { return };
    let nobody = api.anonymous();
    let list = nobody.get(ROUTE).await;
    let revoke = nobody.post_empty(&format!("{ROUTE}/1/revoke")).await;
    assert_eq!(list.status, StatusCode::UNAUTHORIZED);
    assert_eq!(revoke.status, StatusCode::UNAUTHORIZED);
§SPENDUNSIGNED}

#[tokio::test]
async fn a_role_that_is_not_the_administrators_can_neither_list_nor_revoke() {
    let Some(api) = start().await else { return };
    let other = api.in_role("NoSuchRole");
    let list = other.get(ROUTE).await;
    let revoke = other.post_empty(&format!("{ROUTE}/1/revoke")).await;
    assert_eq!(list.status, StatusCode::FORBIDDEN);
    assert_eq!(revoke.status, StatusCode::FORBIDDEN);
    assert!(revoke.json()["type"].as_str().is_some_and(|text| text.ends_with("§T_ROLE")), "{}", revoke.body);
}

#[tokio::test]
async fn a_token_is_spent_once() {
    let Some(api) = start().await else { return };
    let Some(person) = person_of(&api).await else { return };
    let token = new_token();
    let id = add(&api, &token, 1, person, None, false).await;
    let client = admin(&api);
    let first = client.post(&format!("{ROUTE}/spend"), &json!({ "token": token })).await;
    let second = client.post(&format!("{ROUTE}/spend"), &json!({ "token": token })).await;
    remove(&api, &[id]).await;
    assert_eq!(first.status, StatusCode::OK, "{}", first.body);
    assert_eq!(first.json()["id"], json!(id), "{}", first.body);
    assert_eq!(second.status, StatusCode::BAD_REQUEST, "{}", second.body);
    assert!(second.json()["type"].as_str().is_some_and(|text| text.ends_with("§T_SPEND")), "{}", second.body);
}

#[tokio::test]
async fn an_expired_token_and_one_that_is_not_known_cannot_be_spent() {
    let Some(api) = start().await else { return };
    let Some(person) = person_of(&api).await else { return };
    let token = new_token();
    let id = add(&api, &token, -1, person, None, false).await;
    let client = admin(&api);
    let expired = client.post(&format!("{ROUTE}/spend"), &json!({ "token": token })).await;
    let unknown = client.post(&format!("{ROUTE}/spend"), &json!({ "token": new_token() })).await;
    let blank = client.post(&format!("{ROUTE}/spend"), &json!({ "token": "" })).await;
    let missing = client.post(&format!("{ROUTE}/spend"), &json!({})).await;
    remove(&api, &[id]).await;
    for reply in [expired, unknown, blank, missing] {
        assert_eq!(reply.status, StatusCode::BAD_REQUEST, "{}", reply.body);
    }
}

#[tokio::test]
async fn a_hash_stored_in_capitals_is_found() {
    let Some(api) = start().await else { return };
    let Some(person) = person_of(&api).await else { return };
    let token = new_token();
    let id = add(&api, &token, 1, person, None, true).await;
    let spent = admin(&api).post(&format!("{ROUTE}/spend"), &json!({ "token": token })).await;
    remove(&api, &[id]).await;
    assert_eq!(spent.status, StatusCode::OK, "{}", spent.body);
    assert_eq!(spent.json()["id"], json!(id), "{}", spent.body);
}

#[tokio::test]
async fn a_revoked_token_cannot_be_spent_and_the_list_never_shows_the_hash() {
    let Some(api) = start().await else { return };
    let Some(person) = person_of(&api).await else { return };
    let token = new_token();
    let id = add(&api, &token, 1, person, None, false).await;
    let client = admin(&api);
    let revoked = client.post_empty(&format!("{ROUTE}/{id}/revoke")).await;
    let again = client.post_empty(&format!("{ROUTE}/{id}/revoke")).await;
    let spent = client.post(&format!("{ROUTE}/spend"), &json!({ "token": token })).await;
    let list = client.get(ROUTE).await;
    let missing = client.post_empty(&format!("{ROUTE}/2000000000/revoke")).await;
    remove(&api, &[id]).await;
    assert_eq!(revoked.status, StatusCode::OK, "{}", revoked.body);
    assert_eq!(revoked.json()["active"], json!(false), "{}", revoked.body);
    assert_eq!(again.status, StatusCode::OK, "revoking twice changes nothing more");
    assert_eq!(spent.status, StatusCode::BAD_REQUEST, "{}", spent.body);
    assert_eq!(list.status, StatusCode::OK, "{}", list.body);
    let rows = list.json();
    let rows = rows.as_array().expect("the list is an array");
    assert!(rows.iter().any(|row| row["§KEY"] == json!(id)), "the row is listed");
    assert!(!rows.iter().any(|row| row.get("§HASH").is_some()), "the hash is never answered");
    assert_eq!(missing.status, StatusCode::NOT_FOUND);
}
"""";

    private const string TwoEndsCase = """"

fn is_set(value: &Value) -> bool {
    !value.is_null() && value != &json!(false)
}

#[tokio::test]
async fn a_revoke_sets_the_revoked_column_and_a_spend_the_used_one() {
    let Some(api) = start().await else { return };
    let Some(person) = person_of(&api).await else { return };
    let (revoke_token, spend_token) = (new_token(), new_token());
    let revoke_id = add(&api, &revoke_token, 1, person, None, false).await;
    let spend_id = add(&api, &spend_token, 1, person, None, false).await;
    let client = admin(&api);
    let revoked = client.post_empty(&format!("{ROUTE}/{revoke_id}/revoke")).await;
    let spent = client.post(&format!("{ROUTE}/spend"), &json!({ "token": spend_token })).await;
    let list = client.get(ROUTE).await;
    remove(&api, &[revoke_id, spend_id]).await;
    assert_eq!(revoked.status, StatusCode::OK, "{}", revoked.body);
    assert_eq!(spent.status, StatusCode::OK, "{}", spent.body);
    let rows = list.json();
    let row_of = |id| rows.as_array().and_then(|rows| rows.iter().find(|row| row["§KEY"] == json!(id))).cloned().unwrap_or(Value::Null);
    let (revoked_row, spent_row) = (row_of(revoke_id), row_of(spend_id));
    assert!(is_set(&revoked_row["§REVOKED"]) && !is_set(&revoked_row["§USED"]), "a revoke sets the revoked column only: {revoked_row}");
    assert!(is_set(&spent_row["§USED"]) && !is_set(&spent_row["§REVOKED"]), "a spend sets the used column only: {spent_row}");
}
"""";

    private const string AnonymousCase = """"

#[tokio::test]
async fn a_token_is_spent_without_signing_in_and_a_wrong_one_is_refused_the_same_way() {
    let Some(api) = start().await else { return };
    let Some(person) = person_of(&api).await else { return };
    let token = new_token();
    let id = add(&api, &token, 1, person, None, false).await;
    let nobody = api.anonymous();
    let wrong = nobody.post(&format!("{ROUTE}/spend"), &json!({ "token": new_token() })).await;
    let first = nobody.post(&format!("{ROUTE}/spend"), &json!({ "token": token })).await;
    let second = nobody.post(&format!("{ROUTE}/spend"), &json!({ "token": token })).await;
    remove(&api, &[id]).await;
    assert_eq!(wrong.status, StatusCode::BAD_REQUEST, "{}", wrong.body);
    assert_eq!(first.status, StatusCode::OK, "{}", first.body);
    assert_eq!(second.status, StatusCode::BAD_REQUEST, "{}", second.body);
}
"""";

    private const string FamilyCase = """"

#[tokio::test]
async fn using_a_token_twice_ends_every_token_of_its_family() {
    let Some(api) = start().await else { return };
    let Some(person) = person_of(&api).await else { return };
    let family = fresh_family();
    let (first, second) = (new_token(), new_token());
    let one = add(&api, &first, 1, person, Some(family.clone()), false).await;
    let two = add(&api, &second, 1, person, Some(family.clone()), false).await;
    let client = admin(&api);
    let spent = client.post(&format!("{ROUTE}/spend"), &json!({ "token": first })).await;
    let reused = client.post(&format!("{ROUTE}/spend"), &json!({ "token": first })).await;
    let sibling = client.post(&format!("{ROUTE}/spend"), &json!({ "token": second })).await;
    remove(&api, &[one, two]).await;
    assert_eq!(spent.status, StatusCode::OK, "{}", spent.body);
    assert_eq!(spent.json()["family"], §FAMILYJSON, "{}", spent.body);
    assert_eq!(reused.status, StatusCode::BAD_REQUEST, "{}", reused.body);
    assert_eq!(sibling.status, StatusCode::BAD_REQUEST, "the reuse ended the family: {}", sibling.body);
}
"""";

    private const string PersonCase = """"

#[tokio::test]
async fn revoking_a_person_ends_every_active_token_of_that_person() {
    let Some(api) = start().await else { return };
    let Some(Some(person)) = person_of(&api).await else { return };
    let (first, second) = (new_token(), new_token());
    let one = add(&api, &first, 1, Some(person), None, false).await;
    let two = add(&api, &second, 1, Some(person), None, false).await;
    let client = admin(&api);
    let revoked = client.post_empty(&format!("{ROUTE}/revoke-person/{person}")).await;
    let spent_first = client.post(&format!("{ROUTE}/spend"), &json!({ "token": first })).await;
    let spent_second = client.post(&format!("{ROUTE}/spend"), &json!({ "token": second })).await;
    let mine = client.get(&format!("{ROUTE}?personId={person}")).await;
    remove(&api, &[one, two]).await;
    assert_eq!(revoked.status, StatusCode::OK, "{}", revoked.body);
    assert!(revoked.json()["revoked"].as_i64() >= Some(2), "both tokens ended: {}", revoked.body);
    assert_eq!(spent_first.status, StatusCode::BAD_REQUEST, "{}", spent_first.body);
    assert_eq!(spent_second.status, StatusCode::BAD_REQUEST, "{}", spent_second.body);
    assert_eq!(mine.status, StatusCode::OK, "{}", mine.body);
    assert!(mine.json().as_array().is_some_and(|rows| rows.iter().all(|row| row["§PERSON"] == json!(person))), "the list holds the person's rows only");
}
"""";
}
