using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>src/people.rs</c> of the Rust (Axum, sqlx) API: <c>GET</c> and <c>POST /api/auth/first-person</c> (no sign-in), <c>PUT /api/auth/password</c> and, with a membership table, the members of the caller's tenant
/// under <c>/api/auth/members</c>. The same routes, answers and sentences as the ASP.NET and Python APIs (<see cref="PeoplePlan"/>, <see cref="PeopleWords"/>). Every statement is fixed text for the database that was read. </summary>
public static class RustPeopleWriter
{
    public static string File(DatabaseModel database, ProjectSettings project)
    {
        if (PeoplePlan.For(database, project) is not { } plan)
            return "";
        var table = plan.User.Table;
        var di = DialectInfo.For(table.Dialect);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string Raw(string sql) => RustTable.Raw(sql);
        string users = di.QuoteTable(table.SchemaName, table.DbTableName);
        bool members = plan.HasMembers;
        var membership = plan.Membership;
        var o = new StringBuilder();
        void Add(string text) => o.Append(text).Append((char)10);

        string countPeople = $"SELECT COUNT(*) FROM {users}";
        string setPassword = $"UPDATE {users} SET {Q(plan.User.PasswordHash)} = {di.Placeholder(1)} WHERE {Q(plan.User.UserName)} = {di.Placeholder(2)}";
        Add(Fill(Head, ("§COUNT_SQL", Raw(countPeople)), ("§SETPASSWORD_SQL", Raw(setPassword)), ("§TENANTUSE", members ? "use crate::tenancy::no_tenant;\n" : "")));
        Add(Fill(Routes, ("§FIRSTPATH", PeopleWords.FirstPersonPath), ("§PASSWORDPATH", PeopleWords.PasswordPath), ("§PASSWORD_TYPE", ProblemTypes.PasswordInvalid), ("§PASSWORD_TITLE", PeopleWords.PasswordInvalidTitle),
            ("§NOTYOURS_TYPE", ProblemTypes.PasswordNotYours), ("§NOTYOURS_TITLE", PeopleWords.PasswordNotYoursTitle), ("§NOTYOURS_DETAIL", PeopleWords.PasswordNotYoursDetail)));

        if (plan.First is { } first)
        {
            // the name and the hash are bound; the other values of the row are written into the statement
            var columns = new List<string>();
            var placeholders = new List<string>();
            var binds = new StringBuilder();
            int index = 0;
            foreach (var v in first.Values)
            {
                columns.Add(Q(v.Column));
                switch (v.Kind)
                {
                    case FirstPersonKind.Name:
                        placeholders.Add(di.Placeholder(++index));
                        binds.Append(v.Length is { } n ? $".bind(name.chars().take({n}).collect::<String>())" : ".bind(name.clone())");
                        break;
                    case FirstPersonKind.Hash:
                        placeholders.Add(di.Placeholder(++index));
                        binds.Append(".bind(hash)");
                        break;
                    case FirstPersonKind.True: placeholders.Add(di.BooleanLiteral(true)); break;
                    case FirstPersonKind.False: placeholders.Add(di.BooleanLiteral(false)); break;
                    case FirstPersonKind.AdminRole: placeholders.Add("'" + v.Text!.Replace("'", "''") + "'"); break;
                    case FirstPersonKind.Zero: placeholders.Add("0"); break;
                    default: placeholders.Add("CURRENT_TIMESTAMP"); break;
                }
            }
            string insert = $"INSERT INTO {users} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";
            string tenantPart = "";
            if (members)
            {
                var tenantTable = plan.TenantTable!;
                var tenantKey = plan.TenantKey!;
                var soft = TenantPlan.DeletedFlagOf(tenantTable);
                var active = tenantTable.IsActiveColumn;
                var conditions = new[] { active is null ? null : $"{Q(active)} = {di.BooleanLiteral(true)}", soft is null ? null : $"{Q(soft)} = {di.BooleanLiteral(false)}" }.OfType<string>().ToList();
                string tenants = di.QuoteTable(tenantTable.SchemaName, tenantTable.DbTableName);
                string firstTenant = $"SELECT {Q(tenantKey)} FROM {tenants}" + (conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions)) + $" ORDER BY {Q(tenantKey)}";
                string findKey = $"SELECT {Q(plan.Key)} FROM {users} WHERE {Q(plan.User.UserName)} = {di.Placeholder(1)}";
                string members_ = di.QuoteTable(membership!.Table.SchemaName, membership.Table.DbTableName);
                string attach = $"INSERT INTO {members_} ({Q(membership.User)}, {Q(membership.Tenant)}" + (membership.Role is null ? "" : $", {Q(membership.Role)}") + $") VALUES ({di.Placeholder(1)}, {di.Placeholder(2)}" + (membership.Role is null ? "" : $", {di.Placeholder(3)}") + ")";
                tenantPart = Fill(FirstMember, ("§FIRSTTENANT_SQL", Raw(firstTenant)), ("§FINDKEY_SQL", Raw(findKey)), ("§ATTACH_SQL", Raw(attach)), ("§ROLEBIND", membership.Role is null ? "" : ".bind(ADMIN_ROLE)"),
                    ("§LOGINBIND", first.Values[0].Length is { } n ? $"name.chars().take({n}).collect::<String>()" : "name.clone()"));
            }
            Add(Fill(FirstText, ("§INSERT_SQL", Raw(insert)), ("§BINDS", binds.ToString()), ("§FIRSTMEMBER", tenantPart), ("§FIRSTPATH", PeopleWords.FirstPersonPath),
                ("§FIRSTPERSON_TYPE", ProblemTypes.FirstPersonInvalid), ("§FIRSTPERSON_TITLE", PeopleWords.FirstPersonInvalidTitle),
                ("§TAKEN_TYPE", ProblemTypes.FirstPersonTaken), ("§TAKEN_TITLE", PeopleWords.FirstPersonTakenTitle), ("§TAKEN_DETAIL", PeopleWords.FirstPersonTakenDetail)));
        }

        string router = $"pub fn router() -> Router<Pool> {{\n    Router::new()\n        .route(\"{PeopleWords.PasswordPath}\", put(change_password))";
        if (members)
        {
            var tenantTable = plan.TenantTable!;
            string membersTable = di.QuoteTable(membership!.Table.SchemaName, membership.Table.DbTableName);
            string nameColumn = plan.NameColumn is null ? "" : $", u.{Q(plan.NameColumn)}";
            string roleColumn = membership.Role is null ? "" : $", m.{Q(membership.Role)}";
            string list = $"SELECT u.{Q(plan.Key)}, u.{Q(plan.User.UserName)}{nameColumn}{roleColumn} FROM {membersTable} AS m JOIN {users} AS u ON u.{Q(plan.Key)} = m.{Q(membership.User)} WHERE m.{Q(membership.Tenant)} = {di.Placeholder(1)}";
            string candidates = $"SELECT u.{Q(plan.Key)}, u.{Q(plan.User.UserName)}{nameColumn} FROM {users} AS u WHERE u.{Q(plan.Key)} NOT IN (SELECT m.{Q(membership.User)} FROM {membersTable} AS m WHERE m.{Q(membership.Tenant)} = {di.Placeholder(1)})";
            string personExists = $"SELECT COUNT(*) FROM {users} WHERE {Q(plan.Key)} = {di.Placeholder(1)}";
            string isMember = $"SELECT COUNT(*) FROM {membersTable} WHERE {Q(membership.User)} = {di.Placeholder(1)} AND {Q(membership.Tenant)} = {di.Placeholder(2)}";
            string add = $"INSERT INTO {membersTable} ({Q(membership.User)}, {Q(membership.Tenant)}" + (membership.Role is null ? "" : $", {Q(membership.Role)}") + $") VALUES ({di.Placeholder(1)}, {di.Placeholder(2)}" + (membership.Role is null ? "" : $", {di.Placeholder(3)}") + ")";
            string setRole = membership.Role is null ? "" : $"UPDATE {membersTable} SET {Q(membership.Role)} = {di.Placeholder(1)} WHERE {Q(membership.User)} = {di.Placeholder(2)} AND {Q(membership.Tenant)} = {di.Placeholder(3)}";
            string remove = $"DELETE FROM {membersTable} WHERE {Q(membership.User)} = {di.Placeholder(1)} AND {Q(membership.Tenant)} = {di.Placeholder(2)}";
            string login = $"SELECT {Q(plan.User.UserName)} FROM {users} WHERE {Q(plan.Key)} = {di.Placeholder(1)}";
            string listType = "(i32, Option<String>" + (plan.NameColumn is null ? "" : ", Option<String>") + (membership.Role is null ? "" : ", Option<String>") + ")";
            string candidateType = "(i32, Option<String>" + (plan.NameColumn is null ? "" : ", Option<String>") + ")";
            string listName = plan.NameColumn is null ? "None" : "row.2";
            string listRole = membership.Role is null ? "None" : (plan.NameColumn is null ? "row.2" : "row.3");
            string text = Fill(MembersText, ("§LIST_SQL", Raw(list)), ("§CANDIDATES_SQL", Raw(candidates)), ("§PERSON_SQL", Raw(personExists)), ("§ISMEMBER_SQL", Raw(isMember)), ("§ADD_SQL", Raw(add)), ("§REMOVE_SQL", Raw(remove)), ("§LOGIN_SQL", Raw(login)),
                ("§LISTTYPE", listType), ("§CANDIDATETYPE", candidateType), ("§LISTNAME", listName), ("§CANDIDATENAME", plan.NameColumn is null ? "None" : "row.2"), ("§LISTROLE", listRole),
                ("§ROLEBINDADD", membership.Role is null ? "" : ".bind(body.role.as_deref().map(str::trim).filter(|role| !role.is_empty()))"),
                ("§ADMINS_TYPE", ProblemTypes.RoleNotAllowed), ("§ADMINS_TITLE", PeopleWords.MembersAdminsOnlyTitle), ("§ADMINS_DETAIL", PeopleWords.MembersAdminsOnlyDetail),
                ("§EXISTS_TYPE", ProblemTypes.MemberExists), ("§EXISTS_TITLE", PeopleWords.MemberExistsTitle), ("§EXISTS_DETAIL", PeopleWords.MemberExistsDetail),
                ("§SELF_TYPE", ProblemTypes.MemberSelf), ("§SELF_TITLE", PeopleWords.MemberSelfTitle), ("§SELF_DETAIL", PeopleWords.MemberSelfDetail),
                ("§ROLEFIELD", membership.Role is null ? "" : ",\n    role: Option<String>"));
            Add(text.Replace("§ROLEJSON", membership.Role is null ? "" : ", \"role\": row_role"));
            if (membership.Role is not null)
                Add(Fill(RoleText, ("§SETROLE_SQL", Raw(setRole))));
            router += $"\n        .route(\"{PeopleWords.MembersPath}\", get(list_members).post(add_member))\n        .route(\"{PeopleWords.CandidatesPath}\", get(list_candidates))";
            router += membership.Role is null ? $"\n        .route(\"{PeopleWords.MembersPath}/{{person_id}}\", delete(remove_member))" : $"\n        .route(\"{PeopleWords.MembersPath}/{{person_id}}\", put(change_role).delete(remove_member))";
        }
        router += "\n}";
        Add(router);
        Add($"\n/// The routes that need no sign-in, merged into the sign-in's own router.\npub fn public_router() -> Router<Pool> {{\n    Router::new()\n        .route(\"{PeopleWords.FirstPersonPath}\", {(plan.First is null ? "get(first_person_state)" : "get(first_person_state).post(create_first_person)")})\n}}");
        return o.ToString();
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Head = """"
// The people routes: the first person of an empty user table (no sign-in), a person's new password and, with tenants, the members of the caller's tenant.
// The same routes, answers and sentences as the ASP.NET API. Written by RS_UserDirectory from the UserTable settings; regenerate it instead of editing it.
#![allow(dead_code, unused_imports)]
use axum::{
    Extension, Json, Router,
    extract::{Path, State},
    http::StatusCode,
    routing::{delete, get, post, put},
};
use serde::Deserialize;

use crate::auth::{ADMIN_ROLE, CurrentUser, hash_password};
use crate::support::{ApiError, ApiJson, ApiResult, Pool};
use crate::users::MINIMUM_PASSWORD_LENGTH;
§TENANTUSE
const COUNT_PEOPLE: &str = §COUNT_SQL;
const SET_PASSWORD: &str = §SETPASSWORD_SQL;

#[derive(Deserialize)]
pub struct FirstPersonBody {
    #[serde(rename = "userName")]
    user_name: Option<String>,
    password: Option<String>,
}

#[derive(Deserialize)]
pub struct PasswordBody {
    #[serde(rename = "userName")]
    user_name: Option<String>,
    password: Option<String>,
}

async fn no_people(pool: &Pool) -> ApiResult<bool> {
    Ok(sqlx::query_scalar::<_, i64>(COUNT_PEOPLE).fetch_one(pool).await? == 0)
}

/// Hashing takes about a tenth of a second of the processor: it does not run on a thread that serves requests.
async fn hashed(password: String) -> ApiResult<String> {
    tokio::task::spawn_blocking(move || hash_password(&password)).await.map_err(|_| ApiError::BadRequest("The password could not be hashed.".into()))
}
"""";

    private const string Routes = """"

/// Whether the user table is empty, so the sign-in offers to create the first administrator.
async fn first_person_state(State(pool): State<Pool>) -> ApiResult<Json<serde_json::Value>> {
    Ok(Json(serde_json::json!({ "open": no_people(&pool).await? })))
}

/// A new password: an administrator for anyone, anyone else only for their own name.
async fn change_password(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, ApiJson(body): ApiJson<PasswordBody>) -> ApiResult<StatusCode> {
    let name = body.user_name.as_deref().map(str::trim).unwrap_or_default().to_string();
    let password = body.password.unwrap_or_default();
    if name.is_empty() || password.chars().count() < MINIMUM_PASSWORD_LENGTH {
        return Err(ApiError::Problem(400, "§PASSWORD_TYPE", "§PASSWORD_TITLE", None));
    }
    if !user.name.eq_ignore_ascii_case(&name) && !user.is_admin() {
        return Err(ApiError::Problem(403, "§NOTYOURS_TYPE", "§NOTYOURS_TITLE", Some("§NOTYOURS_DETAIL")));
    }
    let hash = hashed(password).await?;
    let done = sqlx::query(SET_PASSWORD).bind(hash).bind(name).execute(&pool).await?;
    if done.rows_affected() == 0 { Err(ApiError::NotFound) } else { Ok(StatusCode::NO_CONTENT) }
}
"""";

    private const string FirstText = """"

const INSERT_PERSON: &str = §INSERT_SQL;

/// Adds the first person, the administrator, while the user table is empty; whoever reaches an empty system first claims it.
async fn create_first_person(State(pool): State<Pool>, ApiJson(body): ApiJson<FirstPersonBody>) -> ApiResult<StatusCode> {
    let name = body.user_name.as_deref().map(str::trim).unwrap_or_default().to_string();
    let password = body.password.unwrap_or_default();
    if name.is_empty() || password.chars().count() < MINIMUM_PASSWORD_LENGTH {
        return Err(ApiError::Problem(400, "§FIRSTPERSON_TYPE", "§FIRSTPERSON_TITLE", None));
    }
    if !no_people(&pool).await? {
        return Err(ApiError::Problem(409, "§TAKEN_TYPE", "§TAKEN_TITLE", Some("§TAKEN_DETAIL")));
    }
    let hash = hashed(password).await?;
    sqlx::query(INSERT_PERSON)§BINDS.execute(&pool).await?;
§FIRSTMEMBER    Ok(StatusCode::NO_CONTENT)
}
"""";

    private const string FirstMember = """"
    const FIRST_TENANT: &str = §FIRSTTENANT_SQL;
    const FIND_KEY: &str = §FINDKEY_SQL;
    const ATTACH_FIRST: &str = §ATTACH_SQL;
    if let Some(tenant) = sqlx::query_scalar::<_, i32>(FIRST_TENANT).fetch_optional(&pool).await? {
        let person = sqlx::query_scalar::<_, i32>(FIND_KEY).bind(§LOGINBIND).fetch_one(&pool).await?;
        sqlx::query(ATTACH_FIRST).bind(person).bind(tenant)§ROLEBIND.execute(&pool).await?;
    }

"""";

    private const string MembersText = """"

const LIST_MEMBERS: &str = §LIST_SQL;
const LIST_CANDIDATES: &str = §CANDIDATES_SQL;
const PERSON_EXISTS: &str = §PERSON_SQL;
const IS_MEMBER: &str = §ISMEMBER_SQL;
const ADD_MEMBER: &str = §ADD_SQL;
const REMOVE_MEMBER: &str = §REMOVE_SQL;
const PERSON_LOGIN: &str = §LOGIN_SQL;

#[derive(Deserialize)]
pub struct MemberBody {
    #[serde(rename = "personId")]
    person_id: Option<i32>§ROLEFIELD,
}

fn person_text(name: Option<String>, login: &str) -> String {
    match name.filter(|name| !name.is_empty()) {
        Some(name) => format!("{name} ({login})"),
        None => login.to_string(),
    }
}

/// Only an administrator who works in a tenant manages its members.
fn tenant_admin(user: &CurrentUser) -> ApiResult<i32> {
    if !user.is_admin() {
        return Err(ApiError::Problem(403, "§ADMINS_TYPE", "§ADMINS_TITLE", Some("§ADMINS_DETAIL")));
    }
    user.tenant_id.ok_or_else(no_tenant)
}

async fn list_members(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>) -> ApiResult<Json<serde_json::Value>> {
    let tenant = tenant_admin(&user)?;
    let rows = sqlx::query_as::<_, §LISTTYPE>(LIST_MEMBERS).bind(tenant).fetch_all(&pool).await?;
    let mut answer: Vec<(String, serde_json::Value)> = rows
        .into_iter()
        .map(|row| {
            let login = row.1.clone().unwrap_or_default();
            let person = person_text(§LISTNAME, &login);
            let row_role: Option<String> = §LISTROLE;
            (person.to_lowercase(), serde_json::json!({ "personId": row.0, "login": login, "person": person§ROLEJSON }))
        })
        .collect();
    answer.sort_by(|a, b| a.0.cmp(&b.0));
    Ok(Json(serde_json::Value::Array(answer.into_iter().map(|(_, member)| member).collect())))
}

/// The people who are not yet in the caller's tenant.
async fn list_candidates(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>) -> ApiResult<Json<serde_json::Value>> {
    let tenant = tenant_admin(&user)?;
    let rows = sqlx::query_as::<_, §CANDIDATETYPE>(LIST_CANDIDATES).bind(tenant).fetch_all(&pool).await?;
    let mut answer: Vec<(String, serde_json::Value)> = rows
        .into_iter()
        .map(|row| {
            let name = person_text(§CANDIDATENAME, &row.1.clone().unwrap_or_default());
            (name.to_lowercase(), serde_json::json!({ "id": row.0, "name": name }))
        })
        .collect();
    answer.sort_by(|a, b| a.0.cmp(&b.0));
    Ok(Json(serde_json::Value::Array(answer.into_iter().map(|(_, candidate)| candidate).collect())))
}

async fn add_member(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, ApiJson(body): ApiJson<MemberBody>) -> ApiResult<StatusCode> {
    let tenant = tenant_admin(&user)?;
    let Some(person) = body.person_id else { return Err(ApiError::NotFound) };
    if sqlx::query_scalar::<_, i64>(PERSON_EXISTS).bind(person).fetch_one(&pool).await? == 0 {
        return Err(ApiError::NotFound);
    }
    if sqlx::query_scalar::<_, i64>(IS_MEMBER).bind(person).bind(tenant).fetch_one(&pool).await? > 0 {
        return Err(ApiError::Problem(409, "§EXISTS_TYPE", "§EXISTS_TITLE", Some("§EXISTS_DETAIL")));
    }
    sqlx::query(ADD_MEMBER).bind(person).bind(tenant)§ROLEBINDADD.execute(&pool).await?;
    Ok(StatusCode::NO_CONTENT)
}

async fn remove_member(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(person_id): Path<i32>) -> ApiResult<StatusCode> {
    let tenant = tenant_admin(&user)?;
    let login = sqlx::query_scalar::<_, Option<String>>(PERSON_LOGIN).bind(person_id).fetch_optional(&pool).await?.flatten();
    if login.is_some_and(|login| login.eq_ignore_ascii_case(&user.name)) {
        return Err(ApiError::Problem(403, "§SELF_TYPE", "§SELF_TITLE", Some("§SELF_DETAIL")));
    }
    let done = sqlx::query(REMOVE_MEMBER).bind(person_id).bind(tenant).execute(&pool).await?;
    if done.rows_affected() == 0 { Err(ApiError::NotFound) } else { Ok(StatusCode::NO_CONTENT) }
}
"""";

    private const string RoleText = """"

const SET_ROLE: &str = §SETROLE_SQL;

async fn change_role(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, Path(person_id): Path<i32>, ApiJson(body): ApiJson<MemberBody>) -> ApiResult<StatusCode> {
    let tenant = tenant_admin(&user)?;
    let role = body.role.as_deref().map(str::trim).filter(|role| !role.is_empty());
    let done = sqlx::query(SET_ROLE).bind(role).bind(person_id).bind(tenant).execute(&pool).await?;
    if done.rows_affected() == 0 { Err(ApiError::NotFound) } else { Ok(StatusCode::NO_CONTENT) }
}
"""";
}
