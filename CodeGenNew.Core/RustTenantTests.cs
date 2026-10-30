using System.Text;

namespace CodeGenNew.Core;

/// <summary> The generated Rust tests of a multi-tenant API: <c>tests/api/tenant_support.rs</c> (which tenants the tests work in), the isolation tests appended to a tenant-owned table's module (RS_Test), and
/// <c>tests/api/tenant_routes.rs</c> (the tenant table and the tenant-owned Name and IsActive tables, which RS_Test does not cover). The same cases and words as the ASP.NET tests (<see cref="TenantTests"/>)
/// and the Python tests (<see cref="PythonTenantTests"/>). </summary>
public static class RustTenantTests
{
    public static bool Applies(DatabaseModel database, ProjectSettings project) => TenantTestPlan.Applies(database, project, project.RustTests);

    /// <summary> Whether the tests of the project's table module get the isolation cases. </summary>
    public static bool HasMethods(TableModel table, ProjectSettings project) => project.RustTests && TenantPlan.HasApiTenancy(project) && TenantPlan.OwnedBy(table, project) is not null;

    public static string SupportFile(DatabaseModel database, ProjectSettings project) =>
        Fill(SupportText, ("§CRATE", project.RustCrateName), ("§SQL", RustTable.Raw(TenantTestPlan.FirstTenantsSql(database, project)))) + "\n";

    /// <summary> The cases appended to the module of a tenant-owned table (it has <c>new_body</c> and <c>ROUTE</c>). </summary>
    public static string Methods(TableModel table, ProjectSettings project)
    {
        var owned = TenantPlan.OwnedBy(table, project);
        if (owned is null)
            return "";
        return Fill(MethodsText, ("§KEY", Str(JsonNames.Camel(table.PrimaryKeyColumns[0].Name))), ("§TENANT", Str(JsonNames.Camel(owned.Column.Name))), ("§NOTENANT_TYPE", TenantWords.NoTenantType));
    }

    public static string RoutesFile(DatabaseModel database, ProjectSettings project)
    {
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var di = DialectInfo.For(tenantTable.Dialect);
        string tenantKey = JsonNames.Camel(TenantTestPlan.TenantKey(database, project).Name);
        string delete = TenantTestPlan.RemoveSql(database, project, di.Placeholder(1));
        var o = new StringBuilder();
        string activeLine = TenantTestPlan.ActiveKey(database, project) is { } flag ? $"    body[{Str(flag)}] = json!(true);\n" : "";
        o.Append(Fill(RoutesHead, ("§ROUTE", Str(RustTestBodies.Route(tenantTable.TableName))), ("§KEY", Str(tenantKey)), ("§BODY", BodyFunction("tenant_body", tenantTable, project, "")),
            ("§DELETE", RustTable.Raw(delete)), ("§ONBOARD_TYPE", TenantWords.OnboardingType), ("§ACTIVELINE", activeLine), ("§CRATE", project.RustCrateName)));
        if (TenantTestPlan.ActiveKey(database, project) is { } active)
            o.Append(Fill(SwitchedOff, ("§SWITCHOFF", RustTable.Raw(TenantTestPlan.SwitchOffSql(database, project, di.Placeholder(1)))), ("§ACTIVE", Str(active)), ("§CRATE", project.RustCrateName),
                ("§INACTIVE_TYPE", TenantWords.InactiveType)));
        o.Append(Fill(OnboardCase, ("§ONBOARD_TYPE", TenantWords.OnboardingType)));
        if (TenantTestPlan.NewAdmin(database, project))
        {
            var userTable = UserTable.Resolve(database, project).Plan!.Table;
            string members = TenantTestPlan.RemoveMembersSql(database, project, di.Placeholder(1));
            o.Append(Fill(PersonCase, ("§PERSONBODY", BodyFunction("person_body", userTable, project, "")), ("§LOGINKEY", Str(TenantTestPlan.LoginKey(database, project))),
                ("§REMOVEPERSON", RustTable.Raw(TenantTestPlan.RemovePersonSql(database, project, di.Placeholder(1)))),
                ("§REMOVEMEMBERSCONST", members.Length == 0 ? "" : $"const REMOVE_MEMBERS: &str = {RustTable.Raw(members)};"),
                ("§REMOVEMEMBERSRUN", members.Length == 0 ? "" : "    sqlx::query(REMOVE_MEMBERS).bind(tenant).execute(&api.pool()).await.expect(\"the memberships are removed\");\n")));
            if (TenantPlan.PeopleOf(database, project) is { } people && people.User.Table.HasPythonRustApi(project))
                o.Append(Fill(PeopleCase, ("§USERROUTE", Str(RustTestBodies.Route(people.User.Table.TableName))), ("§USERKEY", Str(JsonNames.Camel(people.Key.Name)))));
            if (TenantBypassPlan.HostColumn(database, project) is { } hostColumn)
                o.Append(Fill(HostLoginCase, ("§SLUG", Str(JsonNames.Camel(hostColumn.Name))), ("§ACTIVELINES", TenantTestPlan.ActiveKey(database, project) is { } flag2 ? $"    own_body[{Str(flag2)}] = json!(true);\n    other_body[{Str(flag2)}] = json!(true);\n" : ""),
                    ("§CRATE", project.RustCrateName)));
        }
        if (TenantBypassPlan.HostColumn(database, project) is { } host)
            o.Append(Fill(HostCase, ("§SLUG", Str(JsonNames.Camel(host.Name))), ("§ACTIVELINE", TenantTestPlan.ActiveKey(database, project) is { } activeName ? $"    body[{Str(activeName)}] = json!(true);\n" : ""),
                ("§HOST_TYPE", TenantWords.HostType), ("§CRATE", project.RustCrateName)));
        foreach (var named in TenantTestPlan.OwnedNamed(database, project))
        {
            var plan = NameActivePlan.For(named.Table, project)!;
            string module = RustNames.Snake(named.Table.TableName);
            o.Append(Fill(NamedText, ("§MODULE", module), ("§BODY", BodyFunction(module + "_body", named.Table, project, "api")), ("§ROUTE", Str(RustTestBodies.Route(named.Table.TableName))),
                ("§KEY", Str(JsonNames.Camel(named.Table.PrimaryKeyColumns[0].Name))), ("§NAME", Str(JsonNames.Camel(plan.Name.Name))), ("§ACTIVE", Str(JsonNames.Camel(plan.Active.Name))),
                ("§LENGTH", Math.Max(plan.Name.CharacterLength, 0).ToString()), ("§T_REQUIRED", ProblemTypes.NameRequired), ("§T_DUPLICATE", ProblemTypes.DuplicateName),
                ("§DETAIL", Str(NameActiveWords.DuplicateDetailFormat)), ("§TENANT", Str(JsonNames.Camel(named.Column.Name)))));
        }
        o.Append(Bypass(database, project, [.. TenantTestPlan.OwnedNamed(database, project).Select(n => n.Table.TableName)]));
        return o.ToString() + "\n";
    }

    /// <summary> The cases of what the query filter does not reach: the dashboard and the junction tables, each in a tenant the test makes. The approvals are in the approval tests (<see cref="RustApprovalTests"/>). </summary>
    private static string Bypass(DatabaseModel database, ProjectSettings project, HashSet<string> defined)
    {
        var dashboard = TenantBypassPlan.Dashboard(database, project);
        var junctions = TenantBypassPlan.Junctions(database, project);
        if (dashboard.Count == 0 && junctions.Count == 0)
            return "";
        var o = new StringBuilder();
        void Body(TableModel table)
        {
            if (!defined.Add(table.TableName))
                return;
            o.Append("\n#[allow(dead_code)]\n").Append(BodyFunction(RustNames.Snake(table.TableName) + "_body", table, project, "api")).Append('\n');
        }
        var owned = TenantTestPlan.Owned(database, project).Concat(TenantTestPlan.OwnedNamed(database, project)).Select(t => t.Table.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string BodyName(TableModel table) => RustNames.Snake(table.TableName) + "_body";
        if (dashboard.Count > 0)
        {
            foreach (var d in dashboard)
                Body(d.Table);
            o.Append(NothingShown);
            foreach (var d in dashboard)
                o.Append(Fill(DashboardCase, ("§MODULE", RustNames.Snake(d.Table.TableName)), ("§ROUTE", Str(RustTestBodies.Route(d.Table.TableName))), ("§KEY", Str(JsonNames.Camel(d.Table.PrimaryKeyColumns[0].Name))),
                    ("§IDS", string.Join(", ", d.WidgetIds.Select(Str)))));
        }
        if (junctions.Count > 0)
        {
            foreach (var j in junctions)
            {
                if (owned.Contains(j.Anchor.TableName))
                    Body(j.Anchor);
                if (owned.Contains(j.Target.TableName))
                    Body(j.Target);
            }
            o.Append(SideHelper);
            foreach (var j in junctions)
            {
                var junction = j.Junction.Junction!;
                string SideLines(string name, TableModel side, string route, string key)
                {
                    string call = owned.Contains(side.TableName)
                        ? $"    let Some({name}_body) = {BodyName(side)}(&api).await else {{ return skip(\"a parent row is missing\") }};\n    let {name} = side(&api, {Str(route)}, {Str(key)}, Some({name}_body), &mut made).await;\n"
                        : $"    let {name} = side(&api, {Str(route)}, {Str(key)}, None, &mut made).await;\n";
                    return call;
                }
                string lines = SideLines("anchor", j.Anchor, RustTestBodies.Route(j.Anchor.TableName), JsonNames.Camel(junction.AnchorForeignKey.ReferencedColumns[0]))
                    + SideLines("target", j.Target, RustTestBodies.Route(j.Target.TableName), JsonNames.Camel(junction.TargetForeignKey.ReferencedColumns[0]));
                o.Append(Fill(JunctionCase, ("§NAME", RustNames.Snake(j.Junction.TableName) + (j.AnchorOwned ? "_another_tenant_cannot_list_link_or_unlink_what_it_does_not_see" : "_another_tenant_cannot_list_link_or_unlink_a_target_it_does_not_see")),
                    ("§JUNCTION", Str(BlazorNames.ApiRoute(j.Junction.TableName))), ("§SIDES", lines.TrimEnd('\n')),
                    ("§LISTED", j.AnchorOwned
                        ? "    assert_eq!(listed.status, StatusCode::NOT_FOUND, \"another tenant is not shown the links of a row it does not see\");"
                        : "    assert_eq!(listed.status, StatusCode::OK, \"{}\", listed.body);\n    assert!(!listed.json().as_array().is_some_and(|items| items.iter().any(|item| item[\"targetId\"] == target)), \"another tenant is not offered a target it does not see\");")));
            }
        }
        return o.ToString();
    }

    /// <summary> A function that makes the body of a request that adds a row; with <c>async</c> it takes the client to find a row of each parent. </summary>
    private static string BodyFunction(string name, TableModel table, ProjectSettings project, string client)
    {
        var members = TenantTestPlan.Body(table, project);
        var parents = members.Where(m => m.Kind == TenantTestPlan.BodyKind.Parent).ToList();
        bool async = client.Length > 0;
        var o = new StringBuilder();
        void L(string text) => o.Append(text).Append('\n');
        if (async)
            L($"async fn {name}({(parents.Count == 0 ? "_api" : "api")}: &Client) -> Option<Value> {{");
        else
            L($"fn {name}() -> Value {{");
        foreach (var m in parents.Where(_ => async))
            L($"    let parent_{RustNames.Snake(m.Json)} = parent_key(api, {Str(m.ParentRoute!)}, {Str(m.ParentKey!)}, json!({m.Value}), None, 0).await?;");
        L(async ? "    Some(json!({" : "    json!({");
        foreach (var m in members)
        {
            string value = m.Kind switch
            {
                TenantTestPlan.BodyKind.Text => $"unique({Str(m.Value)}, {m.Length})",
                TenantTestPlan.BodyKind.Parent when async => "parent_" + RustNames.Snake(m.Json),
                _ => m.Value
            };
            L($"        {Str(m.Json)}: {value},");
        }
        L(async ? "    }))" : "    })");
        o.Append('}');
        return o.ToString();
    }

    private static string Str(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string SupportText = """"
// Which tenants the tests work in. Written by RS_TestTenants; regenerated with the tables.
use §CRATE::Pool;

const FIRST_TENANTS: &str = §SQL;

/// The keys of the first two active tenants of the scratch database, in key order: the tokens of the tests work in the first, and the second is the tenant the isolation tests reach for.
pub async fn first_tenants(pool: &Pool) -> Vec<i32> {
    sqlx::query_scalar::<_, i32>(FIRST_TENANTS).fetch_all(pool).await.unwrap_or_default()
}
"""";

    private const string MethodsText = """"

#[tokio::test]
async fn another_tenant_cannot_read_change_delete_or_find_a_row() {
    let Some(api) = Client::start().await else { return };
    let Some(other) = api.other_tenant else { return skip("the database needs two active tenants") };
    let Some(body) = new_body(&api).await else { return };
    let created = api.post(ROUTE, &body).await;
    assert_eq!(created.status, StatusCode::CREATED, "{}", created.body);
    let row = created.json();
    let id = segment(&row[§KEY]);
    let rival = api.in_tenant(other, ADMIN_ROLE);
    assert_eq!(rival.get(&format!("{ROUTE}/{id}")).await.status, StatusCode::NOT_FOUND, "another tenant does not read the row");
    assert_eq!(rival.put(&format!("{ROUTE}/{id}"), &row).await.status, StatusCode::NOT_FOUND, "another tenant does not change the row");
    assert_ne!(rival.delete(&format!("{ROUTE}/{id}")).await.status, StatusCode::OK, "another tenant does not delete the row");
    assert_eq!(api.get(&format!("{ROUTE}/{id}")).await.status, StatusCode::OK, "the row is still its own tenant's");
    let listed = rival.get(ROUTE).await;
    assert!(!listed.json().as_array().is_some_and(|rows| rows.iter().any(|r| segment(&r[§KEY]) == id)), "another tenant's list does not hold the row");
    api.delete(&format!("{ROUTE}/{id}")).await;
}

#[tokio::test]
async fn a_new_row_belongs_to_the_callers_tenant_whatever_the_body_says() {
    let Some(api) = Client::start().await else { return };
    let Some(mine) = api.tenant else { return skip("the database needs an active tenant") };
    let elsewhere = api.other_tenant.unwrap_or(2_147_483_000);
    let Some(mut body) = new_body(&api).await else { return };
    body[§TENANT] = json!(elsewhere);
    let created = api.post(ROUTE, &body).await;
    assert_eq!(created.status, StatusCode::CREATED, "{}", created.body);
    let mut row = created.json();
    assert_eq!(row[§TENANT], json!(mine), "the row is the caller's tenant's");
    let id = segment(&row[§KEY]);
    row[§TENANT] = json!(elsewhere);
    let updated = api.put(&format!("{ROUTE}/{id}"), &row).await;
    assert_eq!(updated.status, StatusCode::OK, "{}", updated.body);
    assert_eq!(api.get(&format!("{ROUTE}/{id}")).await.json()[§TENANT], json!(mine), "an update keeps the tenant");
    api.delete(&format!("{ROUTE}/{id}")).await;
}

#[tokio::test]
async fn a_caller_in_no_tenant_sees_no_row_and_cannot_create_one() {
    let Some(api) = Client::start().await else { return };
    let Some(body) = new_body(&api).await else { return };
    let platform = api.platform();
    let listed = platform.get(ROUTE).await;
    assert_eq!(listed.status, StatusCode::OK, "{}", listed.body);
    assert_eq!(listed.json(), json!([]), "a caller in no tenant is shown no row");
    let created = platform.post(ROUTE, &body).await;
    assert_eq!(created.status, StatusCode::FORBIDDEN, "{}", created.body);
    assert_eq!(created.json()["type"], "§NOTENANT_TYPE");
}
"""";

    private const string RoutesHead = """"
// The tenant table and the tenant-owned tables with a Name and an IsActive column, through the router. Written by RS_TestTenants; regenerated with the tables.
use axum::http::StatusCode;
use serde_json::{Value, json};

use crate::test_support::*;

const TENANTS: &str = §ROUTE;
const TENANT_KEY: &str = §KEY;
const REMOVE_TENANT: &str = §DELETE;

#[allow(dead_code)]
§BODY

/// Adds a tenant with a platform token and answers its key; the test removes it again. Every test works in a database of its own, so the key may be one that another test switched off: what the
/// process remembers of it is forgotten.
#[allow(dead_code)]
pub async fn make_tenant(api: &Client) -> i32 {
    #[allow(unused_mut)]
    let mut body = tenant_body();
§ACTIVELINE    let made = api.platform().post(TENANTS, &body).await;
    assert_eq!(made.status, StatusCode::CREATED, "{}", made.body);
    let tenant = made.json()[TENANT_KEY].as_i64().unwrap_or(0) as i32;
    §CRATE::forget_tenant(tenant);
    tenant
}

#[allow(dead_code)]
pub async fn remove_tenant(api: &Client, tenant: i32) {
    sqlx::query(REMOVE_TENANT).bind(tenant).execute(&api.pool()).await.expect("the tenant is removed");
}

#[tokio::test]
async fn a_caller_reads_their_own_tenant_and_no_other() {
    let Some(api) = Client::start().await else { return };
    let Some(mine) = api.tenant else { return skip("the database needs an active tenant") };
    assert_eq!(api.get(&format!("{TENANTS}/{mine}")).await.status, StatusCode::OK);
    if let Some(other) = api.other_tenant {
        assert_eq!(api.get(&format!("{TENANTS}/{other}")).await.status, StatusCode::NOT_FOUND, "another tenant is not shown");
    }
    assert_eq!(api.platform().get(&format!("{TENANTS}/{mine}")).await.status, StatusCode::OK, "a platform administrator is shown every tenant");
}

#[tokio::test]
async fn a_tenant_is_created_by_a_platform_administrator_only_and_never_deleted() {
    let Some(api) = Client::start().await else { return };
    let Some(mine) = api.tenant else { return skip("the database needs an active tenant") };
    let refused = api.post(TENANTS, &tenant_body()).await;
    assert_eq!(refused.status, StatusCode::FORBIDDEN, "{}", refused.body);
    assert_eq!(refused.json()["type"], "§ONBOARD_TYPE");
    assert_eq!(api.delete(&format!("{TENANTS}/{mine}")).await.status, StatusCode::FORBIDDEN);

    let platform = api.platform();
    let made = platform.post(TENANTS, &tenant_body()).await;
    assert_eq!(made.status, StatusCode::CREATED, "{}", made.body);
    let key = made.json()[TENANT_KEY].clone();
    let id = segment(&key);
    assert_eq!(platform.get(&format!("{TENANTS}/{id}")).await.status, StatusCode::OK);
    assert_eq!(platform.delete(&format!("{TENANTS}/{id}")).await.status, StatusCode::FORBIDDEN, "a tenant is never deleted");
    sqlx::query(REMOVE_TENANT).bind(key.as_i64().unwrap_or(0) as i32).execute(&api.pool()).await.expect("the tenant is removed");
}

#[tokio::test]
async fn without_a_token_the_tenant_is_refused() {
    let Some(api) = Client::start().await else { return };
    assert_eq!(api.anonymous().get(TENANTS).await.status, StatusCode::UNAUTHORIZED);
}
"""";

    private const string OnboardCase = """"

#[tokio::test]
async fn only_a_platform_administrator_onboards_a_tenant() {
    let Some(api) = Client::start().await else { return };
    if api.tenant.is_none() {
        return skip("the database needs an active tenant");
    }
    let refused = api.post(&format!("{TENANTS}/onboard"), &json!({ "tenant": tenant_body() })).await;
    assert_eq!(refused.status, StatusCode::FORBIDDEN, "an administrator inside a tenant is not a platform administrator: {}", refused.body);
    assert_eq!(refused.json()["type"], "§ONBOARD_TYPE");
    let onboarded = api.platform().post(&format!("{TENANTS}/onboard"), &json!({ "tenant": tenant_body() })).await;
    assert_eq!(onboarded.status, StatusCode::CREATED, "{}", onboarded.body);
    remove_tenant(&api, onboarded.json()[TENANT_KEY].as_i64().unwrap_or(0) as i32).await;
}
"""";

    private const string PersonCase = """"

const PERSON_LOGIN: &str = §LOGINKEY;
const PERSON_PASSWORD: &str = "Onboard-1234-password";
const REMOVE_PERSON: &str = §REMOVEPERSON;
§REMOVEMEMBERSCONST

#[allow(dead_code)]
§PERSONBODY

async fn remove_person(api: &Client, tenant: i32, login: &str) {
§REMOVEMEMBERSRUN    sqlx::query(REMOVE_PERSON).bind(login).execute(&api.pool()).await.expect("the person is removed");
    remove_tenant(api, tenant).await;
}

#[tokio::test]
async fn a_platform_administrator_onboards_a_tenant_with_a_new_first_administrator_who_signs_in() {
    let Some(api) = Client::start().await else { return };
    if api.tenant.is_none() {
        return skip("the database needs an active tenant");
    }
    let person = person_body();
    let login = person[PERSON_LOGIN].as_str().unwrap_or_default().to_string();
    let route = format!("{TENANTS}/onboard");
    let weak = api.platform().post(&route, &json!({ "tenant": tenant_body(), "admin": person, "adminPassword": "short" })).await;
    assert_eq!(weak.status, StatusCode::BAD_REQUEST, "{}", weak.body);
    let onboarded = api.platform().post(&route, &json!({ "tenant": tenant_body(), "admin": person, "adminPassword": PERSON_PASSWORD })).await;
    assert_eq!(onboarded.status, StatusCode::CREATED, "{}", onboarded.body);
    let tenant = onboarded.json()[TENANT_KEY].as_i64().unwrap_or(0) as i32;
    let taken = api.platform().post(&route, &json!({ "tenant": tenant_body(), "admin": person, "adminPassword": PERSON_PASSWORD })).await;
    let wrong = api.post("/api/auth/login", &json!({ "userName": login, "password": "not-the-password" })).await;
    let signed = api.post("/api/auth/login", &json!({ "userName": login, "password": PERSON_PASSWORD })).await;
    remove_person(&api, tenant, &login).await;
    assert_eq!(taken.status, StatusCode::BAD_REQUEST, "a user name that someone has is refused: {}", taken.body);
    assert_eq!(wrong.status, StatusCode::UNAUTHORIZED, "{}", wrong.body);
    assert_eq!(signed.status, StatusCode::OK, "{}", signed.body);
}
"""";

    private const string PeopleCase = """"

const USER_ROUTE: &str = §USERROUTE;
const USER_KEY: &str = §USERKEY;

#[tokio::test]
async fn a_person_added_inside_a_tenant_belongs_to_it_and_another_tenant_does_not_see_them() {
    let Some(api) = Client::start().await else { return };
    if api.tenant.is_none() {
        return skip("the database needs an active tenant");
    }
    let mut person = person_body();
    let login = person[PERSON_LOGIN].as_str().unwrap_or_default().to_string();
    if let Some(fields) = person.as_object_mut() {
        fields.remove(USER_KEY);
    }
    let tenant_a = make_tenant(&api).await;
    let tenant_b = make_tenant(&api).await;
    let created = api.in_tenant(tenant_a, ADMIN_ROLE).post(USER_ROUTE, &person).await;
    let person_id = created.json()[USER_KEY].as_i64().unwrap_or(0);
    let by_a = api.in_tenant(tenant_a, ADMIN_ROLE).get(&format!("{USER_ROUTE}/{person_id}")).await;
    let by_b = api.in_tenant(tenant_b, ADMIN_ROLE).get(&format!("{USER_ROUTE}/{person_id}")).await;
    let by_platform = api.platform().get(&format!("{USER_ROUTE}/{person_id}")).await;
    remove_person(&api, tenant_a, &login).await;
    remove_tenant(&api, tenant_b).await;
    assert_eq!(created.status, StatusCode::CREATED, "{}", created.body);
    assert_eq!(by_a.status, StatusCode::OK, "the person belongs to the tenant they were added in: {}", by_a.body);
    assert_eq!(by_b.status, StatusCode::NOT_FOUND, "another tenant does not see the person: {}", by_b.body);
    assert_eq!(by_platform.status, StatusCode::OK, "a platform administrator sees every person: {}", by_platform.body);
}
"""";

    private const string HostLoginCase = """"

#[tokio::test]
async fn a_sign_in_on_the_host_of_a_tenant_works_in_that_tenant_and_not_in_another() {
    let Some(api) = Client::start().await else { return };
    if api.tenant.is_none() {
        return skip("the database needs an active tenant");
    }
    let person = person_body();
    let login = person[PERSON_LOGIN].as_str().unwrap_or_default().to_string();
    let own = format!("o{}", &uuid::Uuid::new_v4().simple().to_string()[..10]);
    let other = format!("p{}", &uuid::Uuid::new_v4().simple().to_string()[..10]);
    let route = format!("{TENANTS}/onboard");
    #[allow(unused_mut)]
    let (mut own_body, mut other_body) = (tenant_body(), tenant_body());
    own_body[§SLUG] = json!(own);
    other_body[§SLUG] = json!(other);
§ACTIVELINES    let first = api.platform().post(&route, &json!({ "tenant": own_body, "admin": person, "adminPassword": PERSON_PASSWORD })).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    let own_key = first.json()[TENANT_KEY].as_i64().unwrap_or(0) as i32;
    let second = api.platform().post(&route, &json!({ "tenant": other_body })).await;
    assert_eq!(second.status, StatusCode::CREATED, "{}", second.body);
    let other_key = second.json()[TENANT_KEY].as_i64().unwrap_or(0) as i32;
    §CRATE::forget_hosts();
    let sign_in = |host: String, tenant: Option<i32>| {
        let body = match tenant {
            Some(tenant) => json!({ "userName": login, "password": PERSON_PASSWORD, "tenantId": tenant }),
            None => json!({ "userName": login, "password": PERSON_PASSWORD }),
        };
        let client = api.on_host(&host);
        async move { client.post("/api/auth/login", &body).await }
    };
    let on_own = sign_in(format!("{own}.example.test"), None).await;
    let on_other = sign_in(format!("{other}.example.test"), None).await;
    let naming_other = sign_in(format!("{own}.example.test"), Some(other_key)).await;
    let plain = sign_in("localhost".into(), None).await;
    remove_person(&api, own_key, &login).await;
    remove_tenant(&api, other_key).await;
    assert_eq!(on_own.status, StatusCode::OK, "{}", on_own.body);
    assert_eq!(on_other.status, StatusCode::FORBIDDEN, "a person who is not a member is refused on another tenant's host: {}", on_other.body);
    assert_eq!(naming_other.status, StatusCode::FORBIDDEN, "a sign-in naming another tenant than the host is refused: {}", naming_other.body);
    assert_eq!(plain.status, StatusCode::OK, "localhost names no tenant: {}", plain.body);
}
"""";

    private const string HostCase = """"

#[tokio::test]
async fn a_token_is_refused_on_the_host_of_another_tenant_and_not_restricted_on_a_host_that_names_none() {
    let Some(api) = Client::start().await else { return };
    if api.tenant.is_none() {
        return skip("the database needs an active tenant");
    }
    let label = format!("t{}", &uuid::Uuid::new_v4().simple().to_string()[..10]);
    let mut body = tenant_body();
§ACTIVELINE    body[§SLUG] = json!(label);
    let made = api.platform().post(TENANTS, &body).await;
    assert_eq!(made.status, StatusCode::CREATED, "{}", made.body);
    let other = made.json()[TENANT_KEY].as_i64().unwrap_or(0) as i32;
    §CRATE::forget_tenant(other);
    §CRATE::forget_hosts();
    let host = format!("{label}.example.test");
    let refused = api.on_host(&host).get(TENANTS).await;
    let own = api.in_tenant(other, ADMIN_ROLE).on_host(&host).get(TENANTS).await;
    let platform = api.platform().on_host(&host).get(TENANTS).await;
    let unknown = api.on_host(&format!("nobody{label}.example.test")).get(TENANTS).await;
    let plain = api.on_host("localhost").get(TENANTS).await;
    remove_tenant(&api, other).await;
    assert_eq!(refused.status, StatusCode::FORBIDDEN, "{}", refused.body);
    assert_eq!(refused.json()["type"], "§HOST_TYPE");
    assert_eq!(own.status, StatusCode::OK, "a token works on its own tenant's host: {}", own.body);
    assert_eq!(platform.status, StatusCode::OK, "a platform token is not restricted on any host: {}", platform.body);
    assert_eq!(unknown.status, StatusCode::OK, "a host that names no tenant is not restricted: {}", unknown.body);
    assert_eq!(plain.status, StatusCode::OK, "localhost names no tenant: {}", plain.body);
}
"""";

    private const string SwitchedOff = """"

const SWITCH_OFF_TENANT: &str = §SWITCHOFF;

#[tokio::test]
async fn a_token_of_a_tenant_that_was_switched_off_is_refused() {
    let Some(api) = Client::start().await else { return };
    let mut body = tenant_body();
    body[§ACTIVE] = json!(true);
    let made = api.platform().post(TENANTS, &body).await;
    assert_eq!(made.status, StatusCode::CREATED, "{}", made.body);
    let key = made.json()[TENANT_KEY].clone();
    let tenant = key.as_i64().unwrap_or(0) as i32;
    let worker = api.in_tenant(tenant, ADMIN_ROLE);
    let before = worker.get(&format!("{TENANTS}/{tenant}")).await;
    sqlx::query(SWITCH_OFF_TENANT).bind(tenant).execute(&api.pool()).await.expect("the tenant is switched off");
    §CRATE::forget_tenant(tenant);
    let after = worker.get(&format!("{TENANTS}/{tenant}")).await;
    §CRATE::forget_tenant(tenant);
    sqlx::query(REMOVE_TENANT).bind(tenant).execute(&api.pool()).await.expect("the tenant is removed");
    assert_eq!(before.status, StatusCode::OK, "{}", before.body);
    assert_eq!(after.status, StatusCode::UNAUTHORIZED, "a token keeps no tenant that was switched off: {}", after.body);
    assert_eq!(after.json()["type"], "§INACTIVE_TYPE");
}
"""";

    private const string NothingShown = """"


/// Every point of the widget is zero (or empty) and it lists no row.
async fn assert_nothing_shown(caller: &Client, ids: &[&str]) {
    let reply = caller.get("/api/dashboard").await;
    assert_eq!(reply.status, StatusCode::OK, "{}", reply.body);
    let widgets = reply.json()["widgets"].as_array().cloned().unwrap_or_default();
    for id in ids {
        let Some(widget) = widgets.iter().find(|widget| widget["id"] == *id) else { continue };
        for point in widget["points"].as_array().into_iter().flatten() {
            assert!(point["value"].is_null() || point["value"].as_f64() == Some(0.0), "{id}: a tenant without rows is shown {point}");
        }
        assert!(widget["rows"].as_array().is_none_or(|rows| rows.is_empty()), "{id}: a tenant without rows is shown rows");
    }
}
"""";

    private const string DashboardCase = """"

#[tokio::test]
async fn the_dashboard_shows_a_tenant_without_rows_none_of_§MODULE() {
    let Some(api) = Client::start().await else { return };
    if api.tenant.is_none() {
        return skip("the database needs an active tenant");
    }
    let Some(body) = §MODULE_body(&api).await else { return };
    let created = api.post(§ROUTE, &body).await;
    assert_eq!(created.status, StatusCode::CREATED, "{}", created.body);
    let id = segment(&created.json()[§KEY]);
    let empty = make_tenant(&api).await;
    assert_nothing_shown(&api.in_tenant(empty, ADMIN_ROLE), &[§IDS]).await;
    assert_nothing_shown(&api.platform(), &[§IDS]).await;
    remove_tenant(&api, empty).await;
    api.delete(&format!("{}/{id}", §ROUTE)).await;
}
"""";

    private const string SideHelper = """"


/// The key of a row of one side of a junction table: a new row made with the side's body (its path is remembered in made, to be removed), or the first row of the side's list; None when there is none.
async fn side(api: &Client, route: &str, key: &str, body: Option<Value>, made: &mut Vec<String>) -> Option<Value> {
    if let Some(body) = body {
        let created = api.post(route, &body).await;
        assert_eq!(created.status, StatusCode::CREATED, "{}", created.body);
        let id = created.json()[key].clone();
        made.push(format!("{route}/{}", segment(&id)));
        return Some(id);
    }
    let listed = api.get(route).await;
    listed.json().as_array().and_then(|rows| rows.first()).map(|row| row[key].clone())
}
"""";

    private const string JunctionCase = """"

#[tokio::test]
async fn §NAME() {
    let Some(api) = Client::start().await else { return };
    if api.tenant.is_none() {
        return skip("the database needs an active tenant");
    }
    let mut made: Vec<String> = Vec::new();
§SIDES
    let (Some(anchor), Some(target)) = (anchor, target) else {
        for path in made.iter().rev() {
            api.delete(path).await;
        }
        return skip("each side of the junction table needs a row");
    };
    let link = json!({ "anchorId": anchor, "targetId": target });
    let linked = api.post(&format!("{}/junction/link", §JUNCTION), &link).await;
    assert_eq!(linked.status, StatusCode::OK, "{}", linked.body);
    let empty = make_tenant(&api).await;
    let rival = api.in_tenant(empty, ADMIN_ROLE);
    let listed = rival.get(&format!("{}/junction/{}", §JUNCTION, segment(&anchor))).await;
§LISTED
    assert_eq!(rival.post(&format!("{}/junction/link", §JUNCTION), &link).await.status, StatusCode::NOT_FOUND, "another tenant cannot link");
    assert_eq!(rival.post(&format!("{}/junction/unlink", §JUNCTION), &link).await.status, StatusCode::NOT_FOUND, "another tenant cannot unlink");
    let own = api.get(&format!("{}/junction/{}", §JUNCTION, segment(&anchor))).await;
    assert!(own.json().as_array().is_some_and(|items| items.iter().any(|item| item["targetId"] == target && item["isSelected"] == true)), "the link is still there");
    api.post(&format!("{}/junction/unlink", §JUNCTION), &link).await;
    remove_tenant(&api, empty).await;
    for path in made.iter().rev() {
        api.delete(path).await;
    }
}
"""";

    private const string NamedText = """"

// §ROUTE

#[allow(dead_code)]
§BODY

#[tokio::test]
async fn §MODULE_names_are_trimmed_and_unique_among_the_active_rows_of_a_tenant() {
    let Some(api) = Client::start().await else { return };
    let Some(mut body) = §MODULE_body(&api).await else { return };
    let name = unique("Name", §LENGTH);
    body[§NAME] = json!(format!("  {name}  "));
    body[§ACTIVE] = json!(true);
    let first = api.post(§ROUTE, &body).await;
    assert_eq!(first.status, StatusCode::CREATED, "{}", first.body);
    assert_eq!(first.json()[§NAME], json!(name), "the name is trimmed");
    let first_id = segment(&first.json()[§KEY]);

    body[§NAME] = json!(name);
    let duplicate = api.post(§ROUTE, &body).await;
    assert_eq!(duplicate.status, StatusCode::CONFLICT, "{}", duplicate.body);
    assert_eq!(duplicate.json()["type"], "§T_DUPLICATE");
    assert_eq!(duplicate.json()["detail"], json!(format!(§DETAIL, name)));

    body[§ACTIVE] = json!(false);
    let inactive = api.post(§ROUTE, &body).await;
    assert_eq!(inactive.status, StatusCode::CREATED, "an inactive row may repeat the name: {}", inactive.body);
    api.delete(&format!("{}/{}", §ROUTE, segment(&inactive.json()[§KEY]))).await;

    body[§NAME] = json!("   ");
    let blank = api.post(§ROUTE, &body).await;
    assert_eq!(blank.status, StatusCode::BAD_REQUEST, "{}", blank.body);
    assert_eq!(blank.json()["type"], "§T_REQUIRED");

    if let Some(other) = api.other_tenant {
        body[§NAME] = json!(name);
        body[§ACTIVE] = json!(true);
        let rival = api.in_tenant(other, ADMIN_ROLE);
        let elsewhere = rival.post(§ROUTE, &body).await;
        assert_eq!(elsewhere.status, StatusCode::CREATED, "the same name in another tenant is not a duplicate: {}", elsewhere.body);
        rival.delete(&format!("{}/{}", §ROUTE, segment(&elsewhere.json()[§KEY]))).await;
    }
    api.delete(&format!("{}/{first_id}", §ROUTE)).await;
}

#[tokio::test]
async fn §MODULE_rows_of_another_tenant_are_not_reached() {
    let Some(api) = Client::start().await else { return };
    let Some(other) = api.other_tenant else { return skip("the database needs two active tenants") };
    let Some(mut body) = §MODULE_body(&api).await else { return };
    body[§NAME] = json!(unique("Name", §LENGTH));
    body[§ACTIVE] = json!(true);
    body[§TENANT] = json!(other);
    let created = api.post(§ROUTE, &body).await;
    assert_eq!(created.status, StatusCode::CREATED, "{}", created.body);
    let row = created.json();
    assert_ne!(row[§TENANT], json!(other), "the row is the caller's tenant's whatever the body says");
    let id = segment(&row[§KEY]);
    let rival = api.in_tenant(other, ADMIN_ROLE);
    assert_eq!(rival.get(&format!("{}/{id}", §ROUTE)).await.status, StatusCode::NOT_FOUND);
    assert_eq!(rival.put(&format!("{}/{id}", §ROUTE), &row).await.status, StatusCode::NOT_FOUND);
    assert_ne!(rival.delete(&format!("{}/{id}", §ROUTE)).await.status, StatusCode::OK);
    let listed = rival.get(&format!("{}/active", §ROUTE)).await;
    assert!(!listed.json().as_array().is_some_and(|rows| rows.iter().any(|r| segment(&r[§KEY]) == id)), "another tenant's active list does not hold the row");
    api.delete(&format!("{}/{id}", §ROUTE)).await;
}
"""";
}
