using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/test_tenants.py</c> of the Python API (PY_TestTenants), beside the <c>conftest.py</c> the approval tests share: the tests sign in as an administrator of a tenant by replacing <c>require_user</c>, and play the
/// tenant-owned tables (another tenant cannot read, change, delete or find a row, the tenant column is the caller's whatever the body says, a caller in no tenant sees nothing), the Name and IsActive tables of a
/// tenant (trimmed, not empty, unique among the active rows of one tenant) and the tenant table (only the own tenant is shown, only a platform administrator creates one, none is deleted). The same cases and
/// words as the Rust and ASP.NET tests (<see cref="RustTenantTests"/>, <see cref="TenantTests"/>). </summary>
public static class PythonTenantTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c> over a tenant API and a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        database.Dialect != SqlDialect.SqlServer && TenantTestPlan.Applies(database, project, project.PythonTests);

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var tenantTable = TenantPlan.TenantTableOf(database, project)!;
        var key = TenantTestPlan.TenantKey(database, project);
        string delete = TenantTestPlan.RemoveSql(database, project, "");
        var owned = TenantTestPlan.Owned(database, project);
        var named = TenantTestPlan.OwnedNamed(database, project);

        var o = new StringBuilder();
        o.Append(Fill(Head, ("§ADMIN", PythonNames.Str(project.AuthAdminRole)), ("§FIRST", PythonNames.Str(TenantTestPlan.FirstTenantsSql(database, project))),
            ("§ROUTE", PythonNames.Str(RustTestBodies.Route(tenantTable.TableName))), ("§KEY", PythonNames.Str(JsonNames.Camel(key.Name))), ("§DELETE", PythonNames.Str(delete)),
            ("§TENANTBODY", BodyFunction("tenant_body", tenantTable, project)), ("§ONBOARD_TYPE", TenantWords.OnboardingType)));
        if (TenantTestPlan.ActiveKey(database, project) is { } active)
            o.Append(Fill(SwitchedOff, ("§SWITCHOFF", PythonNames.Str(TenantTestPlan.SwitchOffSql(database, project, ""))), ("§ACTIVE", PythonNames.Str(active)), ("§INACTIVE_TYPE", TenantWords.InactiveType)));
        o.Append(Fill(OnboardCase, ("§ONBOARD_TYPE", TenantWords.OnboardingType)));
        if (TenantTestPlan.NewAdmin(database, project))
        {
            var userTable = UserTable.Resolve(database, project).Plan!.Table;
            string members = TenantTestPlan.RemoveMembersSql(database, project, "");
            o.Append(Fill(PersonCase, ("§PERSONBODY", BodyFunction("person_body", userTable, project)), ("§LOGINKEY", PythonNames.Str(TenantTestPlan.LoginKey(database, project))),
                ("§REMOVEPERSON", PythonNames.Str(TenantTestPlan.RemovePersonSql(database, project, ""))),
                ("§REMOVEMEMBERSCONST", members.Length == 0 ? "" : $"REMOVE_MEMBERS = {PythonNames.Str(members)}\n"),
                ("§REMOVEMEMBERSITEM", members.Length == 0 ? "" : "REMOVE_MEMBERS + str(key), ")));
            if (TenantPlan.PeopleOf(database, project) is { } people && people.User.Table.HasPythonRustApi(project))
                o.Append(Fill(PeopleCase, ("§USERROUTE", PythonNames.Str(RustTestBodies.Route(people.User.Table.TableName))), ("§USERKEY", PythonNames.Str(JsonNames.Camel(people.Key.Name))),
                    ("§REMOVEMEMBERSITEM", members.Length == 0 ? "" : "REMOVE_MEMBERS + str(tenant_a), ")));
            if (TenantBypassPlan.HostColumn(database, project) is { } hostColumn)
                o.Append(Fill(HostLoginCase, ("§SLUG", PythonNames.Str(JsonNames.Camel(hostColumn.Name))), ("§ACTIVELINE", TenantTestPlan.ActiveKey(database, project) is { } flag2 ? $"    own_body[{PythonNames.Str(flag2)}] = True\n    other_body[{PythonNames.Str(flag2)}] = True\n" : ""),
                    ("§REMOVEMEMBERSITEM", members.Length == 0 ? "" : "REMOVE_MEMBERS + str(own_key), ")));
        }
        if (TenantBypassPlan.HostColumn(database, project) is { } host)
            o.Append(Fill(HostCase, ("§SLUG", PythonNames.Str(JsonNames.Camel(host.Name))), ("§ACTIVELINE", TenantTestPlan.ActiveKey(database, project) is { } flag ? $"    body[{PythonNames.Str(flag)}] = True\n" : ""),
                ("§HOST_TYPE", TenantWords.HostType)));
        if (owned.Count > 0)
        {
            foreach (var t in owned)
                o.Append("\n\n\n").Append(BodyFunction(RustNames.Snake(t.Table.TableName) + "_body", t.Table, project));
            o.Append("\n\n\nOWNED = [\n");
            foreach (var t in owned)
                o.Append($"    ({PythonNames.Str(RustTestBodies.Route(t.Table.TableName))}, {PythonNames.Str(JsonNames.Camel(t.Table.PrimaryKeyColumns[0].Name))}, {PythonNames.Str(JsonNames.Camel(t.Column.Name))}, {RustNames.Snake(t.Table.TableName)}_body),\n");
            o.Append("]\n");
            o.Append(Fill(OwnedCases, ("§NOTENANT_TYPE", TenantWords.NoTenantType)));
        }
        if (named.Count > 0)
        {
            foreach (var t in named)
                o.Append("\n\n\n").Append(BodyFunction(RustNames.Snake(t.Table.TableName) + "_body", t.Table, project));
            o.Append("\n\n\nNAMED = [\n");
            foreach (var t in named)
            {
                var plan = NameActivePlan.For(t.Table, project)!;
                o.Append($"    ({PythonNames.Str(RustTestBodies.Route(t.Table.TableName))}, {PythonNames.Str(JsonNames.Camel(t.Table.PrimaryKeyColumns[0].Name))}, {PythonNames.Str(JsonNames.Camel(plan.Name.Name))}, "
                    + $"{PythonNames.Str(JsonNames.Camel(plan.Active.Name))}, {Math.Max(plan.Name.CharacterLength, 0)}, {RustNames.Snake(t.Table.TableName)}_body),\n");
            }
            o.Append("]\n");
            o.Append(Fill(NamedCases, ("§T_REQUIRED", ProblemTypes.NameRequired), ("§T_DUPLICATE", ProblemTypes.DuplicateName),
                ("§DETAIL", NameActiveWords.DuplicateDetailFormat.Replace("{0}", "{text}"))));
        }
        o.Append(Bypass(database, project, [.. owned, .. named]));
        return o.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary> The cases of what the query filter does not reach: the dashboard and the junction tables. The tenant of a request is replaced, so a key no row belongs to stands for a tenant that holds nothing. </summary>
    private static string Bypass(DatabaseModel database, ProjectSettings project, IReadOnlyList<TenantOwned> tables)
    {
        var dashboard = TenantBypassPlan.Dashboard(database, project);
        var junctions = TenantBypassPlan.Junctions(database, project);
        if (dashboard.Count == 0 && junctions.Count == 0)
            return "";
        var withBody = tables.Select(t => t.Table.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Make(TableModel t) => withBody.Contains(t.TableName) ? RustNames.Snake(t.TableName) + "_body" : "None";
        string Key(TableModel t) => PythonNames.Str(JsonNames.Camel(t.PrimaryKeyColumns[0].Name));
        var o = new StringBuilder("\n\n\nEMPTY_TENANT = 2147483000\n");
        if (dashboard.Count > 0)
        {
            o.Append("\n\nDASHBOARD = [\n");
            foreach (var d in dashboard)
                o.Append($"    ({PythonNames.Str(RustTestBodies.Route(d.Table.TableName))}, {Key(d.Table)}, {Make(d.Table)}, [{string.Join(", ", d.WidgetIds.Select(PythonNames.Str))}]),\n");
            o.Append("]\n");
            o.Append(DashboardCases);
        }
        if (junctions.Count > 0)
        {
            o.Append("\n\nJUNCTIONS = [\n");
            foreach (var j in junctions)
            {
                var junction = j.Junction.Junction!;
                string anchorKey = PythonNames.Str(JsonNames.Camel(junction.AnchorForeignKey.ReferencedColumns[0])), targetKey = PythonNames.Str(JsonNames.Camel(junction.TargetForeignKey.ReferencedColumns[0]));
                o.Append($"    ({PythonNames.Str(BlazorNames.ApiRoute(j.Junction.TableName))}, {PythonNames.Str(RustTestBodies.Route(j.Anchor.TableName))}, {anchorKey}, {Make(j.Anchor)}, "
                    + $"{PythonNames.Str(RustTestBodies.Route(j.Target.TableName))}, {targetKey}, {Make(j.Target)}, {(j.AnchorOwned ? "True" : "False")}),\n");
            }
            o.Append("]\n");
            o.Append(JunctionCases);
        }
        return o.ToString();
    }

    /// <summary> A function that makes the body of a request that adds a row, or None when a parent has no row to point at. </summary>
    private static string BodyFunction(string name, TableModel table, ProjectSettings project)
    {
        var members = TenantTestPlan.Body(table, project);
        var o = new StringBuilder();
        o.Append($"def {name}(call, tenant):\n");
        foreach (var m in members.Where(m => m.Kind == TenantTestPlan.BodyKind.Parent))
        {
            string variable = "parent_" + RustNames.Snake(m.Json);
            o.Append($"    {variable} = parent_key(call, tenant, {PythonNames.Str(m.ParentRoute!)}, {PythonNames.Str(m.ParentKey!)}, {Literal(m.Value)})\n");
            o.Append($"    if {variable} is None:\n        return None\n");
        }
        o.Append("    return {\n");
        foreach (var m in members)
        {
            string value = m.Kind switch
            {
                TenantTestPlan.BodyKind.Text => $"unique({PythonNames.Str(m.Value)}, {m.Length})",
                TenantTestPlan.BodyKind.Parent => "parent_" + RustNames.Snake(m.Json),
                _ => Literal(m.Value)
            };
            o.Append($"        {PythonNames.Str(m.Json)}: {value},\n");
        }
        o.Append("    }");
        return o.ToString();
    }

    /// <summary> A JSON literal of the sample values as a Python one. </summary>
    private static string Literal(string json) => json switch { "true" => "True", "false" => "False", "null" => "None", _ => json };

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Head = """"
"""The tenant table, the tenant-owned tables and their isolation through the routes. Written by PY_TestTenants; regenerated with the tables."""
import uuid

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import text

from app.auth import CurrentUser, require_user

from conftest import run

ADMIN_ROLE = §ADMIN
FIRST_TENANTS = §FIRST
TENANTS = §ROUTE
TENANT_KEY = §KEY
REMOVE_TENANT = §DELETE


def unique(value, length):
    """The text with a random ending, within the length of its column (0: no limit)."""
    ending = uuid.uuid4().hex[:8]
    if length and length < len(value) + len(ending):
        value = value[: max(length - len(ending), 0)]
        ending = ending[: length - len(value)]
    return value + ending


def parent_key(call, tenant, route, key, fallback):
    """The key of the first row of the parent's list in the tenant for a foreign key to point at; the fallback when the parent has no list; None when it has no row."""
    reply = call(tenant, "GET", route)
    if reply.status_code != 200:
        return fallback
    rows = reply.json()
    return rows[0][key] if rows else None


@pytest.fixture
def call(application):
    """call(tenant, method, path, **kw): a request made as an administrator who works in that tenant (None: in no tenant, a platform administrator)."""
    app, _ = application
    who = {"tenant": None}
    app.dependency_overrides[require_user] = lambda: CurrentUser(name="tenant-test", role=ADMIN_ROLE, owner_id=None, tenant_id=who["tenant"])
    client = TestClient(app)

    def make(tenant, method, path, **kw):
        who["tenant"] = tenant
        return client.request(method, path, **kw)

    yield make
    app.dependency_overrides.pop(require_user, None)


@pytest.fixture
def tenants(application):
    """The keys of the first two active tenants of the scratch database."""
    _, engine = application
    with engine.connect() as connection:
        keys = [row[0] for row in connection.execute(text(FIRST_TENANTS))]
    if not keys:
        pytest.skip("the database needs an active tenant")
    return keys


§TENANTBODY


def test_a_caller_reads_their_own_tenant_and_no_other(call, tenants):
    mine = tenants[0]
    assert call(mine, "GET", f"{TENANTS}/{mine}").status_code == 200
    if len(tenants) > 1:
        assert call(mine, "GET", f"{TENANTS}/{tenants[1]}").status_code == 404, "another tenant is not shown"
    assert call(None, "GET", f"{TENANTS}/{mine}").status_code == 200, "a platform administrator is shown every tenant"


def test_a_tenant_is_created_by_a_platform_administrator_only_and_never_deleted(call, tenants, application):
    _, engine = application
    mine = tenants[0]
    refused = call(mine, "POST", TENANTS, json=tenant_body(call, mine))
    assert refused.status_code == 403, refused.text
    assert refused.json()["type"] == "§ONBOARD_TYPE"
    assert call(mine, "DELETE", f"{TENANTS}/{mine}").status_code == 403
    made = call(None, "POST", TENANTS, json=tenant_body(call, mine))
    assert made.status_code == 201, made.text
    key = made.json()[TENANT_KEY]
    try:
        assert call(None, "GET", f"{TENANTS}/{key}").status_code == 200
        assert call(None, "DELETE", f"{TENANTS}/{key}").status_code == 403, "a tenant is never deleted"
    finally:
        run(engine, [REMOVE_TENANT + str(key)])


def test_without_a_token_the_tenant_is_refused(application):
    app, _ = application
    app.dependency_overrides.pop(require_user, None)
    assert TestClient(app).get(TENANTS).status_code == 401
"""";

    private const string OnboardCase = """"


def test_only_a_platform_administrator_onboards_a_tenant(call, tenants, application):
    _, engine = application
    mine = tenants[0]
    refused = call(mine, "POST", f"{TENANTS}/onboard", json={"tenant": tenant_body(call, mine)})
    assert refused.status_code == 403, refused.text
    assert refused.json()["type"] == "§ONBOARD_TYPE"
    onboarded = call(None, "POST", f"{TENANTS}/onboard", json={"tenant": tenant_body(call, mine)})
    assert onboarded.status_code == 201, onboarded.text
    run(engine, [REMOVE_TENANT + str(onboarded.json()[TENANT_KEY])])
"""";

    private const string PersonCase = """"


PERSON_LOGIN = §LOGINKEY
PERSON_PASSWORD = "Onboard-1234-password"
REMOVE_PERSON = §REMOVEPERSON
§REMOVEMEMBERSCONST

§PERSONBODY


def test_a_platform_administrator_onboards_a_tenant_with_a_new_first_administrator_who_signs_in(call, tenants, application):
    app, engine = application
    mine = tenants[0]
    person = person_body(call, mine)
    if person is None:
        return
    login = person[PERSON_LOGIN]
    route = f"{TENANTS}/onboard"
    weak = call(None, "POST", route, json={"tenant": tenant_body(call, mine), "admin": person, "adminPassword": "short"})
    assert weak.status_code == 400, weak.text
    onboarded = call(None, "POST", route, json={"tenant": tenant_body(call, mine), "admin": person, "adminPassword": PERSON_PASSWORD})
    assert onboarded.status_code == 201, onboarded.text
    key = onboarded.json()[TENANT_KEY]
    try:
        taken = call(None, "POST", route, json={"tenant": tenant_body(call, mine), "admin": person, "adminPassword": PERSON_PASSWORD})
        assert taken.status_code == 400, taken.text
        app.dependency_overrides.pop(require_user, None)
        worker = TestClient(app)
        assert worker.post("/api/auth/login", json={"userName": login, "password": "not-the-password"}).status_code == 401
        signed = worker.post("/api/auth/login", json={"userName": login, "password": PERSON_PASSWORD})
        assert signed.status_code == 200, signed.text
    finally:
        run(engine, [§REMOVEMEMBERSITEMREMOVE_PERSON + "'" + login + "'", REMOVE_TENANT + str(key)])
"""";

    private const string PeopleCase = """"


USER_ROUTE = §USERROUTE
USER_KEY = §USERKEY


def test_a_person_added_inside_a_tenant_belongs_to_it_and_another_tenant_does_not_see_them(call, tenants, application):
    _, engine = application
    mine = tenants[0]
    person = person_body(call, mine)
    if person is None:
        return
    login = person[PERSON_LOGIN]
    person.pop(USER_KEY, None)
    route = f"{TENANTS}/onboard"
    first = call(None, "POST", route, json={"tenant": tenant_body(call, mine)})
    second = call(None, "POST", route, json={"tenant": tenant_body(call, mine)})
    assert first.status_code == 201, first.text
    assert second.status_code == 201, second.text
    tenant_a, tenant_b = first.json()[TENANT_KEY], second.json()[TENANT_KEY]
    try:
        created = call(tenant_a, "POST", USER_ROUTE, json=person)
        assert created.status_code == 201, created.text
        person_id = created.json()[USER_KEY]
        assert call(tenant_a, "GET", f"{USER_ROUTE}/{person_id}").status_code == 200, "the person belongs to the tenant they were added in"
        assert call(tenant_b, "GET", f"{USER_ROUTE}/{person_id}").status_code == 404, "another tenant does not see the person"
        assert call(None, "GET", f"{USER_ROUTE}/{person_id}").status_code == 200, "a platform administrator sees every person"
    finally:
        run(engine, [§REMOVEMEMBERSITEMREMOVE_PERSON + "'" + login + "'", REMOVE_TENANT + str(tenant_a), REMOVE_TENANT + str(tenant_b)])
"""";

    private const string HostLoginCase = """"


def test_a_sign_in_on_the_host_of_a_tenant_works_in_that_tenant_and_not_in_another(call, tenants, application):
    from app.routes.tenancy import forget_hosts

    app, engine = application
    mine = tenants[0]
    person = person_body(call, mine)
    if person is None:
        return
    login = person[PERSON_LOGIN]
    own, other = "o" + uuid.uuid4().hex[:10], "p" + uuid.uuid4().hex[:10]
    own_body, other_body = tenant_body(call, mine), tenant_body(call, mine)
    own_body[§SLUG], other_body[§SLUG] = own, other
§ACTIVELINE    route = f"{TENANTS}/onboard"
    first = call(None, "POST", route, json={"tenant": own_body, "admin": person, "adminPassword": PERSON_PASSWORD})
    assert first.status_code == 201, first.text
    own_key = first.json()[TENANT_KEY]
    second = call(None, "POST", route, json={"tenant": other_body})
    assert second.status_code == 201, second.text
    other_key = second.json()[TENANT_KEY]
    try:
        forget_hosts()
        app.dependency_overrides.pop(require_user, None)
        worker = TestClient(app)

        def sign_in(host, tenant=None):
            body = {"userName": login, "password": PERSON_PASSWORD}
            if tenant is not None:
                body["tenantId"] = tenant
            return worker.post("/api/auth/login", json=body, headers={"Host": host})

        assert sign_in(f"{own}.example.test").status_code == 200
        assert sign_in(f"{other}.example.test").status_code == 403, "a person who is not a member is refused on another tenant's host"
        assert sign_in(f"{own}.example.test", other_key).status_code == 403, "a sign-in naming another tenant than the host is refused"
        assert sign_in("localhost").status_code == 200, "localhost names no tenant"
    finally:
        run(engine, [§REMOVEMEMBERSITEMREMOVE_PERSON + "'" + login + "'", REMOVE_TENANT + str(own_key), REMOVE_TENANT + str(other_key)])
"""";

    private const string HostCase = """"


def test_a_token_is_refused_on_the_host_of_another_tenant_and_not_restricted_on_a_host_that_names_none(call, tenants, application):
    from datetime import datetime, timedelta, timezone

    from app.auth import DirectoryUser, create_token
    from app.routes.tenancy import forget_hosts

    app, engine = application
    label = "t" + uuid.uuid4().hex[:10]
    body = tenant_body(call, tenants[0])
§ACTIVELINE    body[§SLUG] = label
    made = call(None, "POST", TENANTS, json=body)
    assert made.status_code == 201, made.text
    other = made.json()[TENANT_KEY]
    try:
        forget_hosts()
        app.dependency_overrides.pop(require_user, None)
        worker = TestClient(app)
        expires = datetime.now(timezone.utc) + timedelta(minutes=5)

        def get(tenant, host):
            token = create_token(DirectoryUser("tenant-test", "", ADMIN_ROLE, None), expires, tenant)
            return worker.get(TENANTS, headers={"Authorization": f"Bearer {token}", "Host": host})

        host = f"{label}.example.test"
        refused = get(tenants[0], host)
        assert refused.status_code == 403, refused.text
        assert refused.json()["type"] == "§HOST_TYPE"
        assert get(other, host).status_code == 200, "a token works on its own tenant's host"
        assert get(None, host).status_code == 200, "a platform token is not restricted on any host"
        assert get(tenants[0], f"nobody{label}.example.test").status_code == 200, "a host that names no tenant is not restricted"
        assert get(tenants[0], "localhost").status_code == 200, "localhost names no tenant"
    finally:
        run(engine, [REMOVE_TENANT + str(other)])
"""";

    private const string SwitchedOff = """"


SWITCH_OFF_TENANT = §SWITCHOFF


def test_a_token_of_a_tenant_that_was_switched_off_is_refused(call, tenants, application):
    from datetime import datetime, timedelta, timezone

    from app.auth import DirectoryUser, create_token
    from app.routes.tenancy import forget_tenant

    app, engine = application
    body = tenant_body(call, tenants[0])
    body[§ACTIVE] = True
    made = call(None, "POST", TENANTS, json=body)
    assert made.status_code == 201, made.text
    key = made.json()[TENANT_KEY]
    try:
        app.dependency_overrides.pop(require_user, None)
        worker = TestClient(app)
        token = create_token(DirectoryUser("tenant-test", "", ADMIN_ROLE, None), datetime.now(timezone.utc) + timedelta(minutes=5), key)
        headers = {"Authorization": f"Bearer {token}"}
        before = worker.get(f"{TENANTS}/{key}", headers=headers)
        assert before.status_code == 200, before.text
        run(engine, [SWITCH_OFF_TENANT + str(key)])
        forget_tenant(key)
        after = worker.get(f"{TENANTS}/{key}", headers=headers)
        assert after.status_code == 401, "a token keeps no tenant that was switched off: " + after.text
        assert after.json()["type"] == "§INACTIVE_TYPE"
    finally:
        run(engine, [REMOVE_TENANT + str(key)])
"""";

    private const string OwnedCases = """"


@pytest.mark.parametrize("route, key, tenant_column, make", OWNED, ids=[row[0] for row in OWNED])
def test_another_tenant_cannot_read_change_delete_or_find_a_row(call, tenants, route, key, tenant_column, make):
    if len(tenants) < 2:
        pytest.skip("the database needs two active tenants")
    mine, other = tenants[0], tenants[1]
    body = make(call, mine)
    if body is None:
        pytest.skip("a parent row is missing")
    created = call(mine, "POST", route, json=body)
    assert created.status_code == 201, created.text
    row = created.json()
    path = f"{route}/{row[key]}"
    try:
        assert call(other, "GET", path).status_code == 404, "another tenant does not read the row"
        assert call(other, "PUT", path, json=row).status_code == 404, "another tenant does not change the row"
        assert call(other, "DELETE", path).status_code != 200, "another tenant does not delete the row"
        assert call(mine, "GET", path).status_code == 200, "the row is still its own tenant's"
        assert all(listed[key] != row[key] for listed in call(other, "GET", route).json()), "another tenant's list does not hold the row"
    finally:
        call(mine, "DELETE", path)


@pytest.mark.parametrize("route, key, tenant_column, make", OWNED, ids=[row[0] for row in OWNED])
def test_a_new_row_belongs_to_the_callers_tenant_whatever_the_body_says(call, tenants, route, key, tenant_column, make):
    mine = tenants[0]
    elsewhere = tenants[1] if len(tenants) > 1 else 2147483000
    body = make(call, mine)
    if body is None:
        pytest.skip("a parent row is missing")
    body[tenant_column] = elsewhere
    created = call(mine, "POST", route, json=body)
    assert created.status_code == 201, created.text
    row = created.json()
    path = f"{route}/{row[key]}"
    try:
        assert row[tenant_column] == mine, "the row is the caller's tenant's"
        row[tenant_column] = elsewhere
        updated = call(mine, "PUT", path, json=row)
        assert updated.status_code == 200, updated.text
        assert call(mine, "GET", path).json()[tenant_column] == mine, "an update keeps the tenant"
    finally:
        call(mine, "DELETE", path)


@pytest.mark.parametrize("route, key, tenant_column, make", OWNED, ids=[row[0] for row in OWNED])
def test_a_caller_in_no_tenant_sees_no_row_and_cannot_create_one(call, tenants, route, key, tenant_column, make):
    body = make(call, tenants[0])
    if body is None:
        pytest.skip("a parent row is missing")
    listed = call(None, "GET", route)
    assert listed.status_code == 200, listed.text
    assert listed.json() == [], "a caller in no tenant is shown no row"
    created = call(None, "POST", route, json=body)
    assert created.status_code == 403, created.text
    assert created.json()["type"] == "§NOTENANT_TYPE"
"""";

    private const string DashboardCases = """"


@pytest.mark.parametrize("route, key, make, widget_ids", DASHBOARD, ids=[row[0] for row in DASHBOARD])
def test_the_dashboard_shows_a_tenant_without_rows_none_of_the_rows(call, tenants, route, key, make, widget_ids):
    mine = tenants[0]
    body = make(call, mine)
    if body is None:
        pytest.skip("a parent row is missing")
    created = call(mine, "POST", route, json=body)
    assert created.status_code == 201, created.text
    row = created.json()[key]
    try:
        for tenant in (EMPTY_TENANT, None):
            reply = call(tenant, "GET", "/api/dashboard")
            assert reply.status_code == 200, reply.text
            widgets = {widget["id"]: widget for widget in reply.json()["widgets"]}
            for widget_id in widget_ids:
                widget = widgets.get(widget_id)
                if widget is None:
                    continue
                assert all(point["value"] in (None, 0) for point in widget["points"]), f"{widget_id}: {widget['points']}"
                assert widget["rows"] == [], f"{widget_id}: a tenant without rows is shown rows"
    finally:
        call(mine, "DELETE", f"{route}/{row}")
"""";

    private const string JunctionCases = """"


def side(call, tenant, route, key, make, made):
    """The key of a row of one side of a junction table in the tenant: a new row made with the side's body (remembered in made to be removed), or the first row of its list; None when there is none."""
    if make is not None:
        body = make(call, tenant)
        if body is None:
            return None
        created = call(tenant, "POST", route, json=body)
        assert created.status_code == 201, created.text
        made.append((route, created.json()[key]))
        return created.json()[key]
    reply = call(tenant, "GET", route)
    rows = reply.json() if reply.status_code == 200 else []
    return rows[0][key] if rows else None


def target_of(item):
    return item.get("targetId", item.get("TargetId"))


def selected(item):
    return item.get("isSelected", item.get("IsSelected"))


@pytest.mark.parametrize("route, anchor_route, anchor_key, anchor_make, target_route, target_key, target_make, anchor_owned", JUNCTIONS, ids=[row[0] for row in JUNCTIONS])
def test_another_tenant_cannot_list_link_or_unlink_what_it_does_not_see(call, tenants, route, anchor_route, anchor_key, anchor_make, target_route, target_key, target_make, anchor_owned):
    mine = tenants[0]
    made = []
    link = None
    try:
        anchor = side(call, mine, anchor_route, anchor_key, anchor_make, made)
        target = side(call, mine, target_route, target_key, target_make, made)
        if anchor is None or target is None:
            pytest.skip("each side of the junction table needs a row")
        link = {"anchorId": anchor, "targetId": target}
        assert call(mine, "POST", f"{route}/junction/link", json=link).status_code == 200
        listed = call(EMPTY_TENANT, "GET", f"{route}/junction/{anchor}")
        if anchor_owned:
            assert listed.status_code == 404, "another tenant is not shown the links of a row it does not see"
        else:
            assert listed.status_code == 200, listed.text
            assert all(str(target_of(item)) != str(target) for item in listed.json()), "another tenant is not offered a target it does not see"
        assert call(EMPTY_TENANT, "POST", f"{route}/junction/link", json=link).status_code == 404, "another tenant cannot link"
        assert call(EMPTY_TENANT, "POST", f"{route}/junction/unlink", json=link).status_code == 404, "another tenant cannot unlink"
        own = call(mine, "GET", f"{route}/junction/{anchor}").json()
        assert any(str(target_of(item)) == str(target) and selected(item) for item in own), "the link is still there"
    finally:
        if link is not None:
            call(mine, "POST", f"{route}/junction/unlink", json=link)
        for made_route, made_key in reversed(made):
            call(mine, "DELETE", f"{made_route}/{made_key}")
"""";

    private const string NamedCases = """"


@pytest.mark.parametrize("route, key, name, active, length, make", NAMED, ids=[row[0] for row in NAMED])
def test_names_are_trimmed_and_unique_among_the_active_rows_of_a_tenant(call, tenants, route, key, name, active, length, make):
    mine = tenants[0]
    body = make(call, mine)
    if body is None:
        pytest.skip("a parent row is missing")
    text = unique("Name", length)
    body[name] = f"  {text}  "
    body[active] = True
    first = call(mine, "POST", route, json=body)
    assert first.status_code == 201, first.text
    assert first.json()[name] == text, "the name is trimmed"
    made = [(mine, first.json()[key])]
    try:
        body[name] = text
        duplicate = call(mine, "POST", route, json=body)
        assert duplicate.status_code == 409, duplicate.text
        assert duplicate.json()["type"] == "§T_DUPLICATE"
        assert duplicate.json()["detail"] == f"§DETAIL"
        body[active] = False
        inactive = call(mine, "POST", route, json=body)
        assert inactive.status_code == 201, "an inactive row may repeat the name: " + inactive.text
        made.append((mine, inactive.json()[key]))
        body[name] = "   "
        blank = call(mine, "POST", route, json=body)
        assert blank.status_code == 400, blank.text
        assert blank.json()["type"] == "§T_REQUIRED"
        if len(tenants) > 1:
            body[name] = text
            body[active] = True
            elsewhere = call(tenants[1], "POST", route, json=body)
            assert elsewhere.status_code == 201, "the same name in another tenant is not a duplicate: " + elsewhere.text
            made.append((tenants[1], elsewhere.json()[key]))
    finally:
        for tenant, row in made:
            call(tenant, "DELETE", f"{route}/{row}")


@pytest.mark.parametrize("route, key, name, active, length, make", NAMED, ids=[row[0] for row in NAMED])
def test_rows_of_another_tenant_are_not_reached(call, tenants, route, key, name, active, length, make):
    if len(tenants) < 2:
        pytest.skip("the database needs two active tenants")
    mine, other = tenants[0], tenants[1]
    body = make(call, mine)
    if body is None:
        pytest.skip("a parent row is missing")
    body[name] = unique("Name", length)
    body[active] = True
    created = call(mine, "POST", route, json=body)
    assert created.status_code == 201, created.text
    row = created.json()
    path = f"{route}/{row[key]}"
    try:
        assert call(other, "GET", path).status_code == 404
        assert call(other, "PUT", path, json=row).status_code == 404
        assert call(other, "DELETE", path).status_code != 200
        assert all(listed[key] != row[key] for listed in call(other, "GET", f"{route}/active").json()), "another tenant's active list does not hold the row"
    finally:
        call(mine, "DELETE", path)
"""";
}
