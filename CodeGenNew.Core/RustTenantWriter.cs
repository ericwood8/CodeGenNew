using System.Text;

namespace CodeGenNew.Core;

/// <summary> The Rust (Axum, sqlx) counterpart of <see cref="TenantWriter"/> and <see cref="PythonTenantWriter"/>: <c>src/tenancy.rs</c>, the tenant of a sign-in, <c>GET /api/auth/tenants</c> and <c>POST /api/auth/switch-tenant</c> for a person in several
/// tenants, the check that a token's tenant is still switched on, and the helpers the tables' routers and repositories call to show, change and stamp only the caller's tenant (the rules and sentences of <see cref="TenantPlan"/> and <see cref="TenantWords"/>,
/// the same as the ASP.NET API). Every statement is fixed text for the database that was read. </summary>
public static class RustTenantWriter
{
    /// <summary> Whether the project asks for tenants over a Rust API. </summary>
    public static bool Applies(ProjectSettings project) => TenantPlan.HasApiTenancy(project);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var key = TenantPlan.KeyOf(tenantTable)!;
        var membership = TenantPlan.MembershipOf(database, project);
        var (userPlan, _) = UserTable.Resolve(database, project);
        var userOwned = userPlan is null ? null : TenantPlan.OwnedBy(userPlan.Table, project);
        var active = tenantTable.IsActiveColumn;
        var host = TenantPlan.HostColumnOf(database, project);
        var text = tenantTable.Columns.FirstOrDefault(c => c.IsStringColumn && !c.IsPrimaryKey && (c.Name.EqualsIgnoreCase("Name") || c.Name.EqualsIgnoreCase("Title")));
        var di = DialectInfo.For(tenantTable.Dialect);
        string Table(TableModel table) => di.QuoteTable(table.SchemaName, table.DbTableName);
        string Q(ColumnModel column) => di.Quote(column.DbName);
        string Raw(string sql) => RustTable.Raw(sql);

        string tenants = Table(tenantTable);
        var ownedTables = TenantPlan.OwnedTables(database, project);
        var o = new StringBuilder();
        var people = TenantPlan.PeopleOf(database, project);
        string membersOf = people is null ? "\"\"" : Raw($"SELECT {Q(people.Membership.User)} FROM {Table(people.Membership.Table)} WHERE {Q(people.Membership.Tenant)} = ");
        string ofMembers = people is null ? "let _ = user;\n        Tenancy::Any" : "if user.is_platform() { Tenancy::Any } else { user.tenant_id.map_or(Tenancy::Nothing, Tenancy::Members) }";
        o.Append(Fill(Shared, ("§MEMBERS_OF_SQL", membersOf), ("§OF_MEMBERS", ofMembers), ("§NOTENANT_TYPE", TenantWords.NoTenantType), ("§NOTMEMBER_TYPE", TenantWords.NotMemberType), ("§ONBOARD_TYPE", TenantWords.OnboardingType), ("§INACTIVE_TYPE", TenantWords.InactiveType),
            ("§NOTENANT_TEXT", TenantWords.NoTenant), ("§NOMEMBER_TEXT", TenantWords.NotMember), ("§ONLYPLATFORM", TenantWords.OnlyPlatform), ("§NEVERDELETED", TenantWords.NeverDeleted),
            ("§CREATEBYONBOARDING", TenantWords.CreateByOnboarding)));
        if (ownedTables.Count > 0)
        {
            o.Append($"\n/// The tenant column of a tenant-owned table, as it is written in a statement.\npub const TENANT_COLUMN: &str = {Raw(Q(ownedTables[0].Column))};\n");
            o.Append($"\nconst TENANT_OWNED: &[&str] = &[{string.Join(", ", ownedTables.Select(t => "\"" + t.Table.DbTableName + "\""))}];\n");
            o.Append("\n/// Whether a table (by its database name) holds the rows of tenants.\npub fn owns(table: &str) -> bool {\n    TENANT_OWNED.contains(&table)\n}\n");
        }

        if (host is not null)
            o.Append(Fill(HostLookup, ("§HOST_SQL", Raw($"SELECT {Q(key)} FROM {tenants} WHERE LOWER({Q(host)}) = {di.Placeholder(1)}")), ("§HOST_TYPE", TenantWords.HostType), ("§HOST_TEXT", TenantWords.Host)));
        string hostArg = host is null ? "_host" : "host";

        if (active is null)
            o.Append("\n/// The tenant table has no active flag, so a token never outlives its tenant.\npub async fn ensure_active(_pool: &Pool, _tenant: i32) -> ApiResult<()> {\n    Ok(())\n}\n\n/// Nothing is remembered of a tenant.\npub fn forget_tenant(_tenant: i32) {}\n");
        else
            o.Append(Fill(ActiveCheck, ("§ACTIVE_SQL", Raw($"SELECT COUNT(*) FROM {tenants} WHERE {Q(key)} = {di.Placeholder(1)} AND {Q(active)} = {di.BooleanLiteral(true)}")),
                ("§INACTIVE_TEXT", TenantWords.Inactive)));

        if (membership is not null && userPlan is not null)
        {
            string users = Table(userPlan.Table), userKey = Q(userPlan.Table.PrimaryKeyColumns.First()), userName = Q(userPlan.UserName);
            string members = Table(membership.Table);
            string textColumn = text is null ? $"t.{Q(key)}" : $"t.{Q(text)}";
            string textType = text is null ? "i32" : "Option<String>";
            string textConversion = text is null ? "row.1.to_string()" : "row.1.unwrap_or_default()";
            string select = $"SELECT m.{Q(membership.Tenant)}, {textColumn}" + (membership.Role is null ? "" : $", m.{Q(membership.Role)}")
                + $" FROM {members} AS m JOIN {tenants} AS t ON t.{Q(key)} = m.{Q(membership.Tenant)}"
                + $" WHERE m.{Q(membership.User)} = (SELECT u.{userKey} FROM {users} AS u WHERE u.{userName} = {di.Placeholder(1)})"
                + (active is null ? "" : $" AND t.{Q(active)} = {di.BooleanLiteral(true)}") + $" ORDER BY m.{Q(membership.Tenant)}";
            string insert = $"INSERT INTO {members} ({Q(membership.User)}, {Q(membership.Tenant)}" + (membership.Role is null ? "" : $", {Q(membership.Role)}") + ")"
                + $" SELECT u.{userKey}, {di.Placeholder(1)}" + (membership.Role is null ? "" : $", {di.Placeholder(2)}")
                + $" FROM {users} AS u WHERE u.{userName} = {di.Placeholder(membership.Role is null ? 2 : 3)}";
            string personExists = $"SELECT COUNT(*) FROM {users} WHERE {userKey} = {di.Placeholder(1)}";
            string attachAdmin = $"INSERT INTO {members} ({Q(membership.User)}, {Q(membership.Tenant)}" + (membership.Role is null ? "" : $", {Q(membership.Role)}") + ")"
                + $" VALUES ({di.Placeholder(1)}, {di.Placeholder(2)}" + (membership.Role is null ? "" : $", {di.Placeholder(3)}") + ")";
            o.Append(Fill(Members, ("§PERSON_SQL", Raw(personExists)), ("§ADMIN_SQL", Raw(attachAdmin)), ("§HOSTARG", hostArg), ("§HEADERSNAME", host is null ? "_headers" : "headers"),
                ("§HOSTLOGIN", host is null ? "" : HostLoginLines + "\n"), ("§HOSTSWITCH", host is null ? "" : HostSwitchLines + "\n"), ("§MEMBERS_SQL", Raw(select)), ("§ATTACH_SQL", Raw(insert)), ("§ROWTYPE", "(i32, " + textType + (membership.Role is null ? "" : ", Option<String>") + ")"),
                ("§TEXTCONVERSION", textConversion), ("§ROLECONVERSION", membership.Role is null ? "None" : "row.2"), ("§ROLEBIND", membership.Role is null ? "" : ".bind(ADMIN_ROLE)")));
            o.Append(NewPerson(userPlan, project, membership, null, di, Raw));
            o.Append(People(people, di, Raw));
        }
        else if (userOwned is not null && userPlan is not null)
        {
            string find = $"SELECT u.{Q(userOwned.Column)} FROM {Table(userPlan.Table)} AS u WHERE u.{Q(userPlan.UserName)} = {di.Placeholder(1)}";
            string activeCount = $"SELECT COUNT(*) FROM {tenants} WHERE {Q(key)} = {di.Placeholder(1)}" + (active is null ? "" : $" AND {Q(active)} = {di.BooleanLiteral(true)}");
            o.Append(Fill(OwnColumn, ("§HOSTARG", hostArg), ("§HOSTCHECK", host is null ? "" : HostOwnCheck + "\n"), ("§OWN_SQL", Raw(find)), ("§OWNACTIVE_SQL", Raw(activeCount)), ("§NOTMEMBER_TYPE", TenantWords.NotMemberType), ("§NOMEMBER_TEXT", TenantWords.NotMember)));
            o.Append(NewPerson(userPlan, project, null, userOwned, di, Raw));
        }
        else
            o.Append(None).Append(NoPerson);
        return o.ToString();
    }

    /// <summary> <c>check_new_admin</c> and <c>attach_new_admin</c>: the first administrator of an onboarded tenant as a new person, saved with the tenant through the user table's own repository, the password hashed the way <c>set-password</c> does. </summary>
    private static string NewPerson(UserTablePlan? userPlan, ProjectSettings project, TenantMembership? membership, TenantOwned? userOwned, DialectInfo di, Func<string, string> raw)
    {
        if (userPlan is null)
            return NoPerson;
        var table = RustTable.Of(userPlan.Table, project);
        string users = di.QuoteTable(userPlan.Table.SchemaName, userPlan.Table.DbTableName);
        string nameTaken = $"SELECT COUNT(*) FROM {users} WHERE {di.Quote(userPlan.UserName.DbName)} = {di.Placeholder(1)}";
        string setHash = $"UPDATE {users} SET {di.Quote(userPlan.PasswordHash.DbName)} = {di.Placeholder(1)} WHERE {di.Quote(userPlan.Table.PrimaryKeyColumns.First().DbName)} = {di.Placeholder(2)}";
        string tenantLink = userOwned is null ? "" : $"    person.{table.FieldOf(userOwned.Column).Name} = tenant_id;\n";
        string adminFlag = membership?.Role is null && userPlan.Admin is { } flag ? $"    person.{table.FieldOf(flag).Name} = true;\n" : "";
        string both = membership is null ? "" : "    if admin_id.is_some() {\n        return Err(bad_person(\"Send adminId (a person who exists) or admin (a new person), not both.\"));\n    }\n";
        string tie = membership is null ? "" : "    saved = saved && sqlx::query(ATTACH_ADMIN).bind(key).bind(tenant_id)" + (membership.Role is null ? "" : ".bind(ADMIN_ROLE)") + ".execute(pool).await.is_ok();\n";
        return Fill(NewPersonText, ("§MODULE", table.Module), ("§STRUCT", table.Struct), ("§NAME_SQL", raw(nameTaken)), ("§HASH_SQL", raw(setHash)), ("§NAMEFIELD", table.FieldOf(userPlan.UserName).Name),
            ("§KEYFIELD", table.Key.Name), ("§TENANTLINK", tenantLink), ("§ADMINFLAG", adminFlag), ("§BOTH", both), ("§TIE", tie));
    }

    /// <summary> <c>refuse_non_member</c>, <c>attach_person</c> and <c>detach_person</c>: a person added inside a tenant belongs to it, one removed leaves it, and a person of another tenant is not found. </summary>
    private static string People(TenantPeople? people, DialectInfo di, Func<string, string> raw)
    {
        if (people is null)
            return PeoplePassThrough;
        string members = di.QuoteTable(people.Membership.Table.SchemaName, people.Membership.Table.DbTableName), user = di.Quote(people.Membership.User.DbName), tenant = di.Quote(people.Membership.Tenant.DbName);
        string isMember = $"SELECT COUNT(*) FROM {members} WHERE {user} = {di.Placeholder(1)} AND {tenant} = {di.Placeholder(2)}";
        string attach = $"INSERT INTO {members} ({user}, {tenant}) VALUES ({di.Placeholder(1)}, {di.Placeholder(2)})";
        string detach = $"DELETE FROM {members} WHERE {user} = {di.Placeholder(1)} AND {tenant} = {di.Placeholder(2)} AND NOT EXISTS (SELECT 1 FROM {members} AS other WHERE other.{user} = {di.Placeholder(3)} AND other.{tenant} <> {di.Placeholder(4)})";
        return Fill(PeopleText, ("§IS_MEMBER_SQL", raw(isMember)), ("§ATTACH_PERSON_SQL", raw(attach)), ("§DETACH_PERSON_SQL", raw(detach)));
    }

    private const string PeopleText = """"

const IS_MEMBER: &str = §IS_MEMBER_SQL;
const ATTACH_PERSON: &str = §ATTACH_PERSON_SQL;
const DETACH_PERSON: &str = §DETACH_PERSON_SQL;

/// A person of another tenant is not found (a platform administrator reaches every person).
pub async fn refuse_non_member(pool: &Pool, person_id: i32, user: &CurrentUser) -> ApiResult<()> {
    if user.is_platform() {
        return Ok(());
    }
    match user.tenant_id {
        Some(tenant) if sqlx::query_scalar::<_, i64>(IS_MEMBER).bind(person_id).bind(tenant).fetch_one(pool).await? > 0 => Ok(()),
        _ => Err(ApiError::NotFound),
    }
}

/// A person added inside a tenant belongs to it.
pub async fn attach_person(pool: &Pool, person_id: i32, user: &CurrentUser) -> ApiResult<()> {
    if let Some(tenant) = user.tenant_id {
        sqlx::query(ATTACH_PERSON).bind(person_id).bind(tenant).execute(pool).await?;
    }
    Ok(())
}

/// A person removed inside a tenant leaves it; a person who belongs to another tenant too keeps that membership, so the foreign key refuses the removal.
pub async fn detach_person(pool: &Pool, person_id: i32, user: &CurrentUser) -> ApiResult<()> {
    if let Some(tenant) = user.tenant_id {
        sqlx::query(DETACH_PERSON).bind(person_id).bind(tenant).bind(person_id).bind(tenant).execute(pool).await?;
    }
    Ok(())
}
"""";

    private const string PeoplePassThrough = """"

pub async fn refuse_non_member(_pool: &Pool, _person_id: i32, _user: &CurrentUser) -> ApiResult<()> {
    Ok(())
}

pub async fn attach_person(_pool: &Pool, _person_id: i32, _user: &CurrentUser) -> ApiResult<()> {
    Ok(())
}

pub async fn detach_person(_pool: &Pool, _person_id: i32, _user: &CurrentUser) -> ApiResult<()> {
    Ok(())
}
"""";

    private const string NewPersonText = """"

pub type Person = crate::models::§MODULE::§STRUCT;

const NAME_TAKEN: &str = §NAME_SQL;
const SET_HASH: &str = §HASH_SQL;

fn bad_person(text: &str) -> ApiError {
    ApiError::BadRequest(text.into())
}

/// Refuses a first administrator as a new person that cannot be saved (a short password, a user name someone has, or both ways of naming the person), before the tenant is saved.
pub async fn check_new_admin(pool: &Pool, admin_id: Option<i32>, admin: Option<&Person>, password: Option<&str>) -> ApiResult<()> {
    let Some(person) = admin else { return Ok(()) };
§BOTH    if password.map_or(0, |text| text.chars().count()) < crate::users::MINIMUM_PASSWORD_LENGTH {
        return Err(bad_person(&format!("The first administrator needs a password of at least {} characters.", crate::users::MINIMUM_PASSWORD_LENGTH)));
    }
    let name = person.§NAMEFIELD.trim();
    if name.is_empty() || sqlx::query_scalar::<_, i64>(NAME_TAKEN).bind(name).fetch_one(pool).await? > 0 {
        return Err(bad_person("The first administrator needs a user name that no one has; send adminId for a person who exists."));
    }
    Ok(())
}

/// Saves the first administrator as a new person and ties them to the tenant that was just onboarded; when a step fails the person is removed again and the caller removes the tenant.
pub async fn attach_new_admin(pool: &Pool, tenant_id: i32, admin: Option<Person>, password: Option<String>) -> ApiResult<()> {
    let (Some(mut person), Some(password)) = (admin, password) else { return Ok(()) };
    person.§NAMEFIELD = person.§NAMEFIELD.trim().to_string();
§TENANTLINK§ADMINFLAG    let saved_row = crate::repos::§MODULE::insert(pool, &person).await.map_err(|_| bad_person("The first administrator could not be saved (a value is missing or wrong)."))?;
    let key = saved_row.§KEYFIELD;
    let hash = tokio::task::spawn_blocking(move || crate::auth::hash_password(&password)).await.map_err(|_| bad_person("The password could not be hashed."))?;
    let mut saved = sqlx::query(SET_HASH).bind(hash).bind(key).execute(pool).await.is_ok();
§TIE    if !saved {
        let _ = crate::repos::§MODULE::delete(pool, key).await;
        return Err(bad_person("The first administrator could not be saved."));
    }
    Ok(())
}
"""";

    private const string NoPerson = """"

pub type Person = serde_json::Value;

/// The tenant settings name no way to tie a new person to a tenant, so an onboarding that carries one is refused.
pub async fn check_new_admin(_pool: &Pool, _admin_id: Option<i32>, admin: Option<&Person>, _password: Option<&str>) -> ApiResult<()> {
    match admin {
        Some(_) => Err(ApiError::BadRequest("The tenant settings name no way to tie a new person to a tenant.".into())),
        None => Ok(()),
    }
}

pub async fn attach_new_admin(_pool: &Pool, _tenant_id: i32, _admin: Option<Person>, _password: Option<String>) -> ApiResult<()> {
    Ok(())
}
"""";

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Shared = """"
// Tenants (TenantTable): the tenant of a sign-in, the tenants a person can switch between, and the helpers every router and repository calls so a caller reads and writes only the rows of the tenant in their token.
// The same rules and sentences as the ASP.NET API (API_Tenants). Written by RS_Tenants; regenerate it instead of editing it.
#![allow(dead_code, unused_imports)] // a project without a membership table does not call every helper
use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

use axum::{
    Extension, Json, Router,
    extract::State,
    http::{HeaderMap, header},
    routing::{get, post},
};
use serde::Deserialize;
use sqlx::QueryBuilder;

use crate::auth::{ADMIN_ROLE, CurrentUser, DirectoryUser, session_response};
use crate::support::{ApiError, ApiJson, ApiResult, Db, Pool};

pub const PROBLEM_NO_TENANT: &str = "§NOTENANT_TYPE";
pub const PROBLEM_NOT_MEMBER: &str = "§NOTMEMBER_TYPE";
pub const PROBLEM_ONBOARDING: &str = "§ONBOARD_TYPE";
pub const PROBLEM_INACTIVE: &str = "§INACTIVE_TYPE";
const ACTIVE_SECONDS: u64 = 10;

pub fn no_tenant() -> ApiError {
    ApiError::Problem(403, PROBLEM_NO_TENANT, "No tenant", Some("§NOTENANT_TEXT"))
}

pub fn not_member() -> ApiError {
    ApiError::Problem(403, PROBLEM_NOT_MEMBER, "Not a member", Some("§NOMEMBER_TEXT"))
}

pub fn only_platform() -> ApiError {
    ApiError::Problem(403, PROBLEM_ONBOARDING, "Platform administrators only", Some("§ONLYPLATFORM"))
}

pub fn never_deleted() -> ApiError {
    ApiError::Problem(403, PROBLEM_ONBOARDING, "Tenants are not deleted", Some("§NEVERDELETED"))
}

pub fn onboarded() -> ApiError {
    ApiError::Problem(403, PROBLEM_ONBOARDING, "Tenants are onboarded", Some("§CREATEBYONBOARDING"))
}

/// The caller's tenant; a caller who works in none cannot write a tenant-owned row.
pub fn require_tenant(user: &CurrentUser) -> ApiResult<i32> {
    user.tenant_id.ok_or_else(no_tenant)
}

/// Another tenant's row is not found.
pub fn refuse_other_tenant(row_tenant: i32, user: &CurrentUser) -> ApiResult<()> {
    if user.tenant_id == Some(row_tenant) { Ok(()) } else { Err(ApiError::NotFound) }
}

/// The tenant table: the row is the caller's own tenant, or any one for a platform administrator.
pub fn refuse_other_tenant_row(row_key: i32, user: &CurrentUser) -> ApiResult<()> {
    if user.is_platform() || user.tenant_id == Some(row_key) { Ok(()) } else { Err(ApiError::NotFound) }
}

/// Which tenants' rows a caller may see in a query.
pub enum Tenancy {
    /// Every tenant (a platform administrator reading the tenant table).
    Any,
    /// Only the rows of this tenant.
    Only(i32),
    /// Only the people who belong to this tenant (the user table, by its membership rows).
    Members(i32),
    /// A caller in no tenant: no row.
    Nothing,
}

const PEOPLE_OF_TENANT: &str = §MEMBERS_OF_SQL;

impl Tenancy {
    /// The caller's own tenant.
    pub fn of(user: &CurrentUser) -> Tenancy {
        user.tenant_id.map_or(Tenancy::Nothing, Tenancy::Only)
    }

    /// The tenant table: the caller's own tenant, or every tenant for a platform administrator.
    pub fn of_tenants(user: &CurrentUser) -> Tenancy {
        if user.is_platform() { Tenancy::Any } else { Tenancy::of(user) }
    }

    /// The people of the user table: the members of the caller's tenant, or everyone for a platform administrator.
    pub fn of_members(user: &CurrentUser) -> Tenancy {
        §OF_MEMBERS
    }

    /// Adds the condition to a query that already has a WHERE clause; `column` is the quoted column of the tenant.
    pub fn apply(&self, builder: &mut QueryBuilder<Db>, column: &str) {
        match self {
            Tenancy::Any => {}
            Tenancy::Nothing => {
                builder.push(" AND 1 = 0");
            }
            Tenancy::Only(tenant) => {
                builder.push(" AND ").push(column).push(" = ").push_bind(*tenant);
            }
            Tenancy::Members(tenant) => {
                builder.push(" AND ").push(column).push(" IN (").push(PEOPLE_OF_TENANT).push_bind(*tenant).push(")");
            }
        }
    }
}
"""";

    private const string HostLookup = """"

pub const PROBLEM_HOST: &str = "§HOST_TYPE";
const HOST_SECONDS: u64 = 60;
const HOST_TENANT: &str = §HOST_SQL;

fn hosts() -> &'static Mutex<HashMap<String, (Option<i32>, Instant)>> {
    static HOSTS: OnceLock<Mutex<HashMap<String, (Option<i32>, Instant)>>> = OnceLock::new();
    HOSTS.get_or_init(Default::default)
}

pub fn other_host() -> ApiError {
    ApiError::Problem(403, PROBLEM_HOST, "Another tenant's address", Some("§HOST_TEXT"))
}

/// The tenant a host name belongs to: the first label of the name (acme in acme.example.com) compared with the tenant table's host column. A host that names no tenant (localhost, an address) has none, and is not restricted. A name is remembered a minute.
pub async fn host_tenant(pool: &Pool, host: Option<&str>) -> ApiResult<Option<i32>> {
    let name = host.unwrap_or_default().trim().to_lowercase();
    let name = if name.starts_with('[') { String::new() } else { name.split(':').next().unwrap_or_default().to_string() };
    if name.parse::<std::net::IpAddr>().is_ok() {
        return Ok(None);
    }
    let Some(dot) = name.find('.').filter(|dot| *dot > 0) else { return Ok(None) };
    let label = &name[..dot];
    let cached = hosts().lock().ok().and_then(|map| map.get(label).copied()).filter(|(_, at)| at.elapsed() < Duration::from_secs(HOST_SECONDS));
    if let Some((tenant, _)) = cached {
        return Ok(tenant);
    }
    let tenant = sqlx::query_scalar::<_, i32>(HOST_TENANT).bind(label).fetch_optional(pool).await?;
    if let Ok(mut map) = hosts().lock() {
        if map.len() > 1000 {
            map.clear();
        }
        map.insert(label.to_string(), (tenant, Instant::now()));
    }
    Ok(tenant)
}

/// Forgets the host names, so the next call asks the database (the generated tests change a tenant's host name).
pub fn forget_hosts() {
    if let Ok(mut map) = hosts().lock() {
        map.clear();
    }
}

/// A token that works in one tenant is refused (403) on the host of another. A caller in no tenant (a platform administrator) is not restricted.
pub async fn check_host(pool: &Pool, headers: &HeaderMap, tenant: i32) -> ApiResult<()> {
    match host_tenant(pool, headers.get(header::HOST).and_then(|value| value.to_str().ok())).await? {
        Some(on_host) if on_host != tenant => Err(other_host()),
        _ => Ok(()),
    }
}
"""";

    private const string HostLoginLines = """"
    let mut requested = requested;
    if let Some(on_host) = host_tenant(pool, host).await? {
        if requested.is_some_and(|wanted| wanted != on_host) {
            return Err(other_host());
        }
        requested = Some(on_host);
    }
"""";

    private const string HostOwnCheck = """"
    if host_tenant(pool, host).await?.is_some_and(|on_host| on_host != tenant) {
        return Err(other_host());
    }
"""";

    private const string HostSwitchLines = """"
    check_host(&pool, &headers, member.tenant_id).await?;
"""";

    private const string ActiveCheck = """"

const IS_ACTIVE: &str = §ACTIVE_SQL;

fn known() -> &'static Mutex<HashMap<i32, (bool, Instant)>> {
    static KNOWN: OnceLock<Mutex<HashMap<i32, (bool, Instant)>>> = OnceLock::new();
    KNOWN.get_or_init(Default::default)
}

/// A token of a tenant that has been switched off is a 401. The answer for a tenant is remembered for ACTIVE_SECONDS, so a switch-off reaches a token in use after that long.
pub async fn ensure_active(pool: &Pool, tenant: i32) -> ApiResult<()> {
    let cached = known().lock().ok().and_then(|map| map.get(&tenant).copied()).filter(|(_, at)| at.elapsed() < Duration::from_secs(ACTIVE_SECONDS));
    let alive = match cached {
        Some((alive, _)) => alive,
        None => {
            let alive = sqlx::query_scalar::<_, i64>(IS_ACTIVE).bind(tenant).fetch_one(pool).await? > 0;
            if let Ok(mut map) = known().lock() {
                map.insert(tenant, (alive, Instant::now()));
            }
            alive
        }
    };
    if alive { Ok(()) } else { Err(ApiError::Problem(401, PROBLEM_INACTIVE, "Tenant switched off", Some("§INACTIVE_TEXT"))) }
}

/// Forgets what is known of a tenant, so the next call asks the database.
pub fn forget_tenant(tenant: i32) {
    if let Ok(mut map) = known().lock() {
        map.remove(&tenant);
    }
}
"""";

    private const string Members = """"

const MEMBERS_OF: &str = §MEMBERS_SQL;
const ATTACH: &str = §ATTACH_SQL;

/// A tenant a person belongs to, with the role they have in it (None: their own role stays).
pub struct Member {
    pub tenant_id: i32,
    pub text: String,
    pub role: Option<String>,
}

/// The active tenants the person belongs to, by tenant id.
pub async fn members_of(pool: &Pool, user_name: &str) -> Result<Vec<Member>, sqlx::Error> {
    let rows = sqlx::query_as::<_, §ROWTYPE>(MEMBERS_OF).bind(user_name.trim()).fetch_all(pool).await?;
    Ok(rows.into_iter().map(|row| Member { tenant_id: row.0, text: §TEXTCONVERSION, role: §ROLECONVERSION }).collect())
}

/// The tenant (and the role in it) a sign-in works in: the one the login asks for, else the person's first. A person in no tenant (a platform administrator) signs in to none.
pub async fn login_tenant(pool: &Pool, user: &DirectoryUser, requested: Option<i32>, §HOSTARG: Option<&str>) -> ApiResult<(Option<i32>, Option<String>)> {
§HOSTLOGIN    let found = members_of(pool, &user.user_name).await?;
    if found.is_empty() {
        return if requested.is_none() { Ok((None, None)) } else { Err(not_member()) };
    }
    let chosen = match requested {
        None => found.into_iter().next(),
        Some(wanted) => found.into_iter().find(|member| member.tenant_id == wanted),
    };
    chosen.map(|member| (Some(member.tenant_id), member.role)).ok_or_else(not_member)
}

/// Makes the person who created a tenant (a platform administrator who is a row of the user table) its first administrator; nothing happens for a caller who is not a row (the bootstrap administrator).
pub async fn attach_creator(pool: &Pool, user: &CurrentUser, tenant_id: i32) -> ApiResult<()> {
    let result = sqlx::query(ATTACH).bind(tenant_id)§ROLEBIND.bind(user.name.trim()).execute(pool).await;
    // a membership table with a column that has no default cannot take the row: the tenant stays without a first administrator
    if let Err(sqlx::Error::Database(_)) = result {
        return Ok(());
    }
    result?;
    Ok(())
}

const PERSON_EXISTS: &str = §PERSON_SQL;
const ATTACH_ADMIN: &str = §ADMIN_SQL;

fn unknown_person() -> ApiError {
    ApiError::BadRequest("The tenant could not be saved (the person is unknown).".into())
}

/// Refuses a first administrator the user table does not know, before the tenant is saved.
pub async fn require_person(pool: &Pool, person_id: Option<i32>) -> ApiResult<()> {
    match person_id {
        Some(person) if sqlx::query_scalar::<_, i64>(PERSON_EXISTS).bind(person).fetch_one(pool).await? == 0 => Err(unknown_person()),
        _ => Ok(()),
    }
}

/// Makes the person the first administrator of a tenant that was just onboarded.
pub async fn attach_admin(pool: &Pool, person_id: Option<i32>, tenant_id: i32) -> ApiResult<()> {
    let Some(person) = person_id else { return Ok(()) };
    sqlx::query(ATTACH_ADMIN).bind(person).bind(tenant_id)§ROLEBIND.execute(pool).await.map_err(|_| unknown_person())?;
    Ok(())
}

pub fn router() -> Router<Pool> {
    Router::new().route("/api/auth/tenants", get(list_tenants)).route("/api/auth/switch-tenant", post(switch_tenant))
}

async fn list_tenants(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>) -> ApiResult<Json<serde_json::Value>> {
    let members = members_of(&pool, &user.name).await?;
    let choices: Vec<serde_json::Value> = members
        .iter()
        .map(|member| serde_json::json!({ "id": member.tenant_id, "text": member.text, "role": member.role, "current": Some(member.tenant_id) == user.tenant_id }))
        .collect();
    Ok(Json(serde_json::json!(choices)))
}

#[derive(Deserialize)]
struct SwitchRequest {
    #[serde(rename = "tenantId", default)]
    tenant_id: Option<i32>,
}

/// A new token for the same person, working in another tenant they belong to.
async fn switch_tenant(State(pool): State<Pool>, Extension(user): Extension<CurrentUser>, §HEADERSNAME: HeaderMap, ApiJson(body): ApiJson<SwitchRequest>) -> ApiResult<Json<serde_json::Value>> {
    let member = match body.tenant_id {
        Some(wanted) => members_of(&pool, &user.name).await?.into_iter().find(|member| member.tenant_id == wanted),
        None => None,
    }
    .ok_or_else(not_member)?;
§HOSTSWITCH    let person = DirectoryUser { user_name: user.name.clone(), password_hash: String::new(), role: member.role.clone().or(user.role.clone()), owner_id: user.owner_id };
    session_response(&person, Some(member.tenant_id))
}
"""";

    private const string OwnColumn = """"

const OWN_TENANT: &str = §OWN_SQL;
const OWN_ACTIVE: &str = §OWNACTIVE_SQL;

/// The tenant of the person's own row goes into the token; a person of a switched-off tenant cannot sign in.
pub async fn login_tenant(pool: &Pool, user: &DirectoryUser, _requested: Option<i32>, §HOSTARG: Option<&str>) -> ApiResult<(Option<i32>, Option<String>)> {
    let Some(tenant) = sqlx::query_scalar::<_, Option<i32>>(OWN_TENANT).bind(user.user_name.trim()).fetch_optional(pool).await?.flatten() else {
        return Ok((None, None));
    };
§HOSTCHECK    if sqlx::query_scalar::<_, i64>(OWN_ACTIVE).bind(tenant).fetch_one(pool).await? == 0 {
        return Err(not_member());
    }
    Ok((Some(tenant), None))
}

/// The people belong to a tenant by their own row, so a new tenant has no first administrator to attach.
pub async fn attach_creator(_pool: &Pool, _user: &CurrentUser, _tenant_id: i32) -> ApiResult<()> {
    Ok(())
}

/// The people belong to a tenant by their own row, so an onboarded tenant has no first administrator to attach.
pub async fn require_person(_pool: &Pool, _person_id: Option<i32>) -> ApiResult<()> {
    Ok(())
}

pub async fn attach_admin(_pool: &Pool, _person_id: Option<i32>, _tenant_id: i32) -> ApiResult<()> {
    Ok(())
}

pub fn router() -> Router<Pool> {
    Router::new()
}
"""";

    private const string None = """"

/// The sign-in works in no tenant: no membership table is named and the people are not tenant-owned.
pub async fn login_tenant(_pool: &Pool, _user: &DirectoryUser, _requested: Option<i32>, _host: Option<&str>) -> ApiResult<(Option<i32>, Option<String>)> {
    Ok((None, None))
}

/// No membership table is named, so an onboarded tenant has no first administrator to attach.
pub async fn require_person(_pool: &Pool, _person_id: Option<i32>) -> ApiResult<()> {
    Ok(())
}

pub async fn attach_admin(_pool: &Pool, _person_id: Option<i32>, _tenant_id: i32) -> ApiResult<()> {
    Ok(())
}

/// No membership table is named, so a new tenant has no first administrator to attach.
pub async fn attach_creator(_pool: &Pool, _user: &CurrentUser, _tenant_id: i32) -> ApiResult<()> {
    Ok(())
}

pub fn router() -> Router<Pool> {
    Router::new()
}
"""";
}
