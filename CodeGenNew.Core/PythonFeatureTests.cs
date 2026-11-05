using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/test_features.py</c> of the Python API (PY_TestFeatures), beside the <c>conftest.py</c> and <c>test_tenants.py</c> of PY_TestTenants: pytest tests of the feature flag routes with real tokens. They make
/// tenants through the API and flags of their own with SQL, and take both out again. The same cases and words as the ASP.NET tests (<see cref="ApiFeatureTests"/>). </summary>
public static class PythonFeatureTests
{
    private const long FirstKey = 90011;

    /// <summary> Whether the tests are written: <c>PythonTests</c> over a Python API with feature flags that have a tenant column on the override table, a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        database.Dialect != SqlDialect.SqlServer && PythonTenantTests.Applies(database, project) && PythonFeatureWriter.PlanOf(database, project) is { OverrideTenant: not null } plan
        && FeatureFlagTables.FlagRowsAreSimple(plan) && plan.FlagKey.IsIntegerColumn;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var plan = PythonFeatureWriter.PlanOf(database, project)!;
        var di = DialectInfo.For(database.Dialect);
        string Q(ColumnModel c) => di.Quote(c.DbName);
        string flagTable = di.QuoteTable(plan.Flag.SchemaName, plan.Flag.DbTableName), overrideTable = di.QuoteTable(plan.Override.SchemaName, plan.Override.DbTableName);
        string Str(string value) => PythonNames.Str(value);
        string Insert(long key, string name, bool on) =>
            $"INSERT INTO {flagTable} ({Q(plan.FlagKey)}, {Q(plan.FlagName)}, {Q(plan.FlagDefault)}) VALUES ({key}, '{name}', {di.BooleanLiteral(on)})";
        string keys = $"{FirstKey}, {FirstKey + 1}";
        var addFlags = new[] { Insert(FirstKey, "test-feature-on", true), Insert(FirstKey + 1, "test-feature-off", false) };
        string[] removeFlags = [$"DELETE FROM {overrideTable} WHERE {Q(plan.OverrideFlag)} IN ({keys})", $"DELETE FROM {flagTable} WHERE {Q(plan.FlagKey)} IN ({keys})"];
        string clearTenant = $"DELETE FROM {overrideTable} WHERE {Q(plan.OverrideTenant!)} = ";
        string person = plan.OverridePerson is null ? ""
            : "    somebody = api(tenant, \"PUT\", \"" + FeatureFlagTables.Path + "/\" + OFF, json={\"enabled\": True, \"personId\": 1})\n    assert somebody.status_code == 403, somebody.text\n";

        // the plan link: a plan of its own (the statements of the subscription tests), the link row, and the removal of the link before the flags
        var link = plan.PlanLink;
        var sql = link is null ? null : SubscriptionTestSql.For(database, project, "", out _);
        bool planCase = link is not null && sql is not null && PlanLinkIsInsertable(link);
        string unlink = planCase ? $"DELETE FROM {di.QuoteTable(link!.Table.SchemaName, link.Table.DbTableName)} WHERE {Q(link.Flag)} IN ({keys})" : "";
        if (planCase)
            removeFlags = [unlink, .. removeFlags];

        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§ADDFLAGS", "[" + string.Join(", ", addFlags.Select(Str)) + "]"), ("§REMOVEFLAGS", "[" + string.Join(", ", removeFlags.Select(Str)) + "]"),
            ("§CLEARTENANT", Str(clearTenant)), ("§ACTIVELINE", TenantTestPlan.ActiveKey(database, project) is { } flag ? $"        body[{Str(flag)}] = True\n" : ""), ("§PATH", FeatureFlagTables.Path),
            ("§PLANIMPORT", planCase ? "from app.routes.subscriptions import forget_state\n" : "")));
        o.Append(FillAll(Cases, ("§PATH", FeatureFlagTables.Path), ("§PERSON", person)));
        if (planCase)
        {
            string link1 = $"INSERT INTO {di.QuoteTable(link!.Table.SchemaName, link.Table.DbTableName)} ({Q(link.Plan)}, {Q(link.Flag)}) VALUES ({sql!.PlanKey}, {FirstKey + 1})";
            o.Append(FillAll(PlanCase, ("§PATH", FeatureFlagTables.Path), ("§PLAN", sql.PlanKey.ToString()), ("§ADDPLAN", Str(sql.AddPlan)), ("§REMOVEPLAN", "[" + string.Join(", ", sql.RemovePlan.Select(Str)) + "]"),
                ("§CLEARSUBSCRIPTION", Str(sql.ClearSubscription)), ("§LINK", Str(link1)), ("§UNLINK", Str(unlink))));
        }
        return o.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary> Whether a row of the plan link can be made from the plan and the flag alone: its key is handed out by the database or is the two keys themselves. </summary>
    public static bool PlanLinkIsInsertable(FeaturePlanLink link) => link.Table.PrimaryKeyColumns.All(c => c.IsIdentity || c == link.Flag || c == link.Plan);

    private const string Head = """"
"""Feature flags through the routes. Written by PY_TestFeatures; regenerated with the tables. The tests sign in with real tokens. Each test makes tenants through the API and flags of its own with SQL, and takes both out again."""
from datetime import datetime, timedelta, timezone

import pytest
from fastapi.testclient import TestClient

from app.auth import DirectoryUser, create_token, require_user
from app.routes.features import clear_cache
§PLANIMPORT
from conftest import run
from test_tenants import ADMIN_ROLE, REMOVE_TENANT, TENANT_KEY, TENANTS, tenant_body

ADD_FLAGS = §ADDFLAGS
REMOVE_FLAGS = §REMOVEFLAGS
CLEAR_TENANT = §CLEARTENANT
ON = "test-feature-on"
OFF = "test-feature-off"
NOBODY = "Nobody"


@pytest.fixture
def world(application):
    """(api, make_tenant): api(tenant, method, path, role=..., **kw) makes a request with a real token of a person who works in that tenant (None: in no tenant, a platform administrator)."""
    app, engine = application
    app.dependency_overrides.pop(require_user, None)
    client = TestClient(app)

    def api(tenant, method, path, role=ADMIN_ROLE, **kw):
        token = create_token(DirectoryUser("feature-test", "", role, None), datetime.now(timezone.utc) + timedelta(minutes=5), tenant)
        return client.request(method, path, headers={"Authorization": f"Bearer {token}"}, **kw)

    run(engine, REMOVE_FLAGS)
    run(engine, ADD_FLAGS)
    clear_cache()
    made = []

    def make_tenant():
        body = tenant_body(api, None)
§ACTIVELINE        reply = api(None, "POST", TENANTS, json=body)
        assert reply.status_code == 201, reply.text
        made.append(reply.json()[TENANT_KEY])
        return made[-1]

    yield api, make_tenant
    run(engine, REMOVE_FLAGS)
    clear_cache()
    for key in made:
        run(engine, [CLEAR_TENANT + str(key), REMOVE_TENANT + str(key)])


def seen(api, tenant, name, role=NOBODY):
    reply = api(tenant, "GET", "§PATH", role=role)
    assert reply.status_code == 200, reply.text
    return reply.json().get(name)
"""";

    private const string PlanCase = """"


PLAN = §PLAN
ADD_PLAN = §ADDPLAN
REMOVE_PLAN = §REMOVEPLAN
CLEAR_SUBSCRIPTION = §CLEARSUBSCRIPTION
LINK_PLAN = §LINK
UNLINK_PLAN = §UNLINK


def test_a_flag_the_plan_lists_is_on_for_a_tenant_on_that_plan_and_the_tenants_override_wins(world, application):
    api, make_tenant = world
    _, engine = application
    tenant = make_tenant()
    run(engine, REMOVE_PLAN)
    run(engine, [ADD_PLAN, LINK_PLAN])
    clear_cache()
    try:
        subscribed = api(None, "PUT", f"/api/subscription/{tenant}", json={"planId": PLAN, "status": "active"})
        assert subscribed.status_code == 200, subscribed.text
        assert seen(api, tenant, OFF) is True, "the tenant's plan lists the flag"
        off = api(tenant, "PUT", "§PATH/" + OFF, json={"enabled": False})
        assert off.status_code == 200, off.text
        assert seen(api, tenant, OFF) is False, "the tenant's override wins over its plan"
        back = api(tenant, "PUT", "§PATH/" + OFF, json={"enabled": None})
        assert back.status_code == 200, back.text
        assert seen(api, tenant, OFF) is True, "without the override the plan decides again"
    finally:
        forget_state(tenant)
        run(engine, [UNLINK_PLAN, CLEAR_SUBSCRIPTION + str(tenant)])
        run(engine, REMOVE_PLAN)
"""";

    private const string Cases = """"


def test_a_flag_is_read_with_its_default(world):
    api, make_tenant = world
    tenant = make_tenant()
    assert seen(api, tenant, ON) is True
    assert seen(api, tenant, OFF) is False
    assert seen(api, tenant, "test-feature-nothing") is None, "a name that is not a flag is not listed"


def test_a_tenants_override_reaches_that_tenant_only_and_a_null_removes_it(world):
    api, make_tenant = world
    first, second = make_tenant(), make_tenant()
    on = api(first, "PUT", "§PATH/" + OFF, json={"enabled": True})
    assert on.status_code == 200, on.text
    assert seen(api, first, OFF) is True
    assert seen(api, second, OFF) is False
    again = api(first, "PUT", "§PATH/" + OFF, json={"enabled": True})
    assert again.status_code == 200, again.text
    off = api(first, "PUT", "§PATH/" + OFF, json={"enabled": None})
    assert off.status_code == 200, off.text
    assert seen(api, first, OFF) is False


def test_only_an_administrator_changes_a_flag_and_only_for_their_own_tenant(world):
    api, make_tenant = world
    tenant, other = make_tenant(), make_tenant()
    assert api(tenant, "PUT", "§PATH/" + OFF, role=NOBODY, json={"enabled": True}).status_code == 403
    assert api(tenant, "PUT", "§PATH/" + OFF, json={"enabled": True, "tenantId": other}).status_code == 403
    assert api(tenant, "PUT", "§PATH/test-feature-nothing", json={"enabled": True}).status_code == 404
§PERSON    assert seen(api, tenant, OFF) is False, "nothing was changed"


def test_a_platform_administrator_names_the_tenant_and_exactly_one_target(world):
    api, make_tenant = world
    tenant = make_tenant()
    assert api(None, "PUT", "§PATH/" + OFF, json={"enabled": True}).status_code == 400
    assert api(None, "PUT", "§PATH/" + OFF, json={"enabled": True, "tenantId": 2147483000}).status_code == 404
    named = api(None, "PUT", "§PATH/" + OFF, json={"enabled": True, "tenantId": tenant})
    assert named.status_code == 200, named.text
    assert seen(api, tenant, OFF) is True
"""";
}
