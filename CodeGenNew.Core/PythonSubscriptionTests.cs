using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/test_subscriptions.py</c> of the Python API (PY_TestSubscriptions), beside the <c>conftest.py</c> and <c>test_tenants.py</c> of PY_TestTenants: pytest tests of the pause, the plan limits and the subscription
/// routes. They sign in with real tokens (the check lives in <c>require_user</c>, which the tenant tests replace), make a tenant through the API and a plan of their own with SQL, and take both out again. The same cases
/// and words as the ASP.NET tests (<see cref="ApiSubscriptionTests"/>). </summary>
public static class PythonSubscriptionTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c> over a Python API with subscriptions and a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        PythonTenantTests.Applies(database, project) && PythonSubscriptionWriter.Applies(project) && SubscriptionPlan.Problems(database, project).Count == 0
        && SubscriptionTestSql.For(database, project, "", out _) is not null;

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var sql = SubscriptionTestSql.For(database, project, "", out _)!;
        var subscription = SubscriptionPlan.SubscriptionOf(database, project)!;
        string Str(string value) => PythonNames.Str(value);
        string List(IEnumerable<string> statements) => "[" + string.Join(", ", statements.Select(Str)) + "]";
        bool trialing = SubscriptionPlan.ActiveStatuses(project).Contains("trialing", StringComparer.OrdinalIgnoreCase);
        var write = sql.WriteTable;

        var o = new StringBuilder();
        o.Append(Fill(Head, ("§ADMIN_IMPORT", ""), ("§PLAN", sql.PlanKey.ToString()), ("§ADDPLAN", Str(sql.AddPlan)), ("§REMOVEPLAN", List(sql.RemovePlan)),
            ("§CLEARSUBSCRIPTION", Str(sql.ClearSubscription)), ("§CLEARROWS", sql.ClearRows is null ? "None" : Str(sql.ClearRows)),
            ("§ACTIVELINE", TenantTestPlan.ActiveKey(database, project) is { } flag ? $"    body[{Str(flag)}] = True\n" : ""),
            ("§WRITE", write is null ? "None" : Str(RustTestBodies.Route(write.TableName))),
            ("§INACTIVE_TYPE", SubscriptionWords.InactiveType), ("§LIMIT_TYPE", SubscriptionWords.LimitType), ("§PLATFORM_TYPE", SubscriptionWords.OnlyPlatformType)));
        if (write is not null)
        {
            o.Append("\n\n").Append(PythonTenantTests.BodyFunction("write_body", write, project)).Append('\n');
            o.Append(Fill(WriteCases, ("§INACTIVE_TYPE", SubscriptionWords.InactiveType),
                ("§NOSUBSCRIPTION", project.SubscriptionRequired
                    ? "    assert refused.status_code == 402, refused.text\n    assert refused.json()[\"type\"] == \"" + SubscriptionWords.InactiveType + "\"\n"
                    : "    assert refused.status_code == 201, refused.text\n"),
                ("§NOSUBSCRIPTION_NAME", project.SubscriptionRequired ? "cannot_write_when_a_subscription_is_required" : "is_on_no_plan_and_may_write")));
            if (sql.LimitKey is { } key)
            {
                o.Append(Fill(LimitCase, ("§LIMIT_TYPE", SubscriptionWords.LimitType), ("§KEY", Str(key)),
                    ("§ONE", List(sql.SetLimit(key, 1))), ("§TWO", List(sql.SetLimit(key, 2)))));
            }
            if (subscription.PeriodEnd is not null && trialing)
                o.Append(Fill(TrialCase, ("§INACTIVE_TYPE", SubscriptionWords.InactiveType)));
        }
        if (subscription.PeriodEnd is not null)
            o.Append(RenewCase);
        if (project.SubscriptionPlanChoice)
        {
            string tooSmall = write is null || sql.LimitKey is not { } limitKey ? ""
                : "    run(engine, " + List(sql.SetLimit(limitKey, 5)) + ")\n    made = api(tenant, \"POST\", WRITE, json=write_body(api, tenant))\n    assert made.status_code == 201, made.text\n"
                + "    run(engine, " + List(sql.SetLimit(limitKey, 0)) + ")\n    refused = api(tenant, \"PUT\", \"/api/subscription/plan\", json={\"planId\": PLAN})\n    assert refused.status_code == 409, refused.text\n"
                + "    assert refused.json()[\"type\"] == \"" + SubscriptionWords.PlanTooSmallType + "\"\n    run(engine, " + List(sql.SetLimit(limitKey, 1)) + ")\n";
            o.Append(Fill(ChooseCase, ("§TOOSMALL", tooSmall)));
        }
        o.Append(Fill(RouteCases, ("§PLATFORM_TYPE", SubscriptionWords.OnlyPlatformType)));
        o.Append(Fill(PlansCase, ("§PRICELINE", subscription.PlanPrice is null ? "" : "    assert info[\"planPrice\"] == mine[\"price\"], \"the view shows the price of the tenant's plan\"\n"),
            ("§LIMITCOUNT", SubscriptionPlan.LimitedTables(database, project).Count.ToString())));
        if (SubscriptionPlan.HasWebhook(database, project))
        {
            o.Append(Fill(WebhookCase, ("§SECRETNAME", project.SubscriptionWebhookSecret!), ("§WEBHOOKPATH", SubscriptionWords.WebhookPath), ("§HEADER", SubscriptionWords.WebhookHeader),
                ("§EXPIRED_TYPE", SubscriptionWords.WebhookExpiredType), ("§INACTIVE_TYPE", SubscriptionWords.InactiveType),
                ("§PAUSEDLINES", write is null ? "" : "    paused = api(tenant, \"POST\", WRITE, json=write_body(api, tenant))\n    assert paused.status_code == 402, paused.text\n    assert paused.json()[\"type\"] == \"" + SubscriptionWords.InactiveType + "\"\n")));
        }
        return o.ToString().TrimEnd('\n') + "\n";
    }

    private static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    private const string Head = """"
"""Plans, subscriptions and limits through the routes. Written by PY_TestSubscriptions; regenerated with the tables. The tests sign in with real tokens, because the check that pauses a tenant's writes lives in require_user.
Each test makes a tenant through the API and a plan of its own with SQL, and takes both out again."""
import hashlib
import hmac
import json
import time
import uuid
from datetime import datetime, timedelta, timezone

import pytest
from fastapi.testclient import TestClient

from app.auth import DirectoryUser, create_token, require_user
from app.routes.subscriptions import forget_state

from conftest import run
from test_tenants import ADMIN_ROLE, REMOVE_TENANT, TENANT_KEY, TENANTS, parent_key, tenant_body, unique

PLAN = §PLAN
ADD_PLAN = §ADDPLAN
REMOVE_PLAN = §REMOVEPLAN
CLEAR_SUBSCRIPTION = §CLEARSUBSCRIPTION
CLEAR_ROWS = §CLEARROWS
WRITE = §WRITE


@pytest.fixture
def world(application):
    """(api, engine): api(tenant, method, path, role=..., **kw) makes a request with a real token of a person who works in that tenant (None: in no tenant, a platform administrator)."""
    app, engine = application
    app.dependency_overrides.pop(require_user, None)
    client = TestClient(app)

    def api(tenant, method, path, role=ADMIN_ROLE, **kw):
        token = create_token(DirectoryUser("subscription-test", "", role, None), datetime.now(timezone.utc) + timedelta(minutes=5), tenant)
        return client.request(method, path, headers={"Authorization": f"Bearer {token}"}, **kw)

    run(engine, REMOVE_PLAN)
    run(engine, [ADD_PLAN])
    yield api, engine
    run(engine, REMOVE_PLAN)


@pytest.fixture
def tenant(world):
    """A tenant made by a platform administrator, taken out again with its subscription and its rows."""
    api, engine = world
    body = tenant_body(api, None)
§ACTIVELINE    made = api(None, "POST", TENANTS, json=body)
    assert made.status_code == 201, made.text
    key = made.json()[TENANT_KEY]
    forget_state(key)
    yield key
    forget_state(key)
    statements = [CLEAR_SUBSCRIPTION + str(key), REMOVE_TENANT + str(key)]
    if CLEAR_ROWS is not None:
        statements.insert(0, CLEAR_ROWS + str(key))
    run(engine, statements)


def set_subscription(api, tenant, **change):
    reply = api(None, "PUT", f"/api/subscription/{tenant}", json=change)
    assert reply.status_code == 200, reply.text
    return reply.json()
"""";

    private const string WriteCases = """"


def test_writes_are_paused_while_the_subscription_is_not_active_and_reads_go_on(world, tenant):
    api, _ = world
    set_subscription(api, tenant, planId=PLAN, status="cancelled")
    refused = api(tenant, "POST", WRITE, json=write_body(api, tenant))
    assert refused.status_code == 402, refused.text
    assert refused.json()["type"] == "§INACTIVE_TYPE"
    assert api(tenant, "GET", WRITE).status_code == 200, "reads are never paused"
    set_subscription(api, tenant, status="active")
    created = api(tenant, "POST", WRITE, json=write_body(api, tenant))
    assert created.status_code == 201, created.text


def test_a_tenant_with_no_subscription_§NOSUBSCRIPTION_NAME(world, tenant):
    api, _ = world
    refused = api(tenant, "POST", WRITE, json=write_body(api, tenant))
§NOSUBSCRIPTION
"""";

    private const string LimitCase = """"


def test_a_create_past_the_plan_limit_is_refused_until_the_limit_is_raised(world, tenant):
    api, engine = world
    run(engine, §ONE)
    set_subscription(api, tenant, planId=PLAN, status="active")
    first = api(tenant, "POST", WRITE, json=write_body(api, tenant))
    assert first.status_code == 201, first.text
    second = api(tenant, "POST", WRITE, json=write_body(api, tenant))
    assert second.status_code == 402, second.text
    assert second.json()["type"] == "§LIMIT_TYPE"
    shown = api(tenant, "GET", "/api/subscription")
    assert shown.status_code == 200, shown.text
    row = next(item for item in shown.json()["limits"] if item["key"] == §KEY)
    assert row["allowed"] == 1
    assert row["used"] == 1
    run(engine, §TWO)
    third = api(tenant, "POST", WRITE, json=write_body(api, tenant))
    assert third.status_code == 201, third.text
"""";

    private const string RenewCase = """"


def test_a_renewal_adds_a_period_that_starts_when_the_old_one_ends(world, tenant):
    api, _ = world
    set_subscription(api, tenant, planId=PLAN, status="active", periodEnd=(datetime.now(timezone.utc) + timedelta(days=5)).isoformat())
    refused = api(tenant, "POST", f"/api/subscription/{tenant}/renew", json={"days": 10})
    assert refused.status_code == 403, refused.text
    renewed = api(None, "POST", f"/api/subscription/{tenant}/renew", json={"days": 10})
    assert renewed.status_code == 200, renewed.text
    end = datetime.fromisoformat(renewed.json()["periodEnd"].replace("Z", "+00:00"))
    if end.tzinfo is None:
        end = end.replace(tzinfo=timezone.utc)
    assert abs((end - (datetime.now(timezone.utc) + timedelta(days=15))).total_seconds()) < 300, end
    assert api(None, "POST", f"/api/subscription/{tenant}/renew", json={"days": 0}).status_code == 400
    assert api(None, "POST", "/api/subscription/2147483647/renew", json={"days": 1}).status_code == 404
"""";

    private const string ChooseCase = """"


def test_an_administrator_chooses_a_plan_unless_it_allows_less_than_the_tenant_has(world, tenant):
    api, engine = world
    set_subscription(api, tenant, planId=PLAN, status="active")
    shown = api(tenant, "GET", "/api/subscription")
    assert shown.json()["canChoose"] is True
    assert api(tenant, "PUT", "/api/subscription/plan", json={"planId": 2147483647}).status_code == 400
§TOOSMALL    chosen = api(tenant, "PUT", "/api/subscription/plan", json={"planId": PLAN})
    assert chosen.status_code == 200, chosen.text
    assert chosen.json()["planId"] == PLAN
"""";

    private const string TrialCase = """"


def test_a_trial_ends_at_its_period_end(world, tenant):
    api, _ = world
    set_subscription(api, tenant, planId=PLAN, status="trialing", periodEnd=(datetime.now(timezone.utc) - timedelta(days=1)).isoformat())
    ended = api(tenant, "POST", WRITE, json=write_body(api, tenant))
    assert ended.status_code == 402, ended.text
    assert ended.json()["type"] == "§INACTIVE_TYPE"
    set_subscription(api, tenant, periodEnd=(datetime.now(timezone.utc) + timedelta(days=14)).isoformat())
    running = api(tenant, "POST", WRITE, json=write_body(api, tenant))
    assert running.status_code == 201, running.text
"""";

    private const string PlansCase = """"


def test_the_view_lists_every_plan_with_what_it_allows_and_marks_the_tenants_own(world, tenant):
    api, _ = world
    set_subscription(api, tenant, planId=PLAN, status="active")
    info = api(tenant, "GET", "/api/subscription").json()
    mine = next(plan for plan in info["plans"] if plan["planId"] == PLAN)
    assert mine["current"] is True
    assert sum(1 for plan in info["plans"] if plan["current"]) == 1, "only the tenant's own plan is marked"
    assert info["planName"] == mine["name"]
§PRICELINE    assert len(mine["limits"]) == §LIMITCOUNT, "each plan lists every limit"
"""";

    private const string WebhookCase = """"


def signed(body, secret, at=None):
    stamp = int(time.time()) if at is None else at
    digest = hmac.new(secret.encode(), f"{stamp}.".encode() + body, hashlib.sha256).hexdigest()
    return {"Content-Type": "application/json", "§HEADER": f"t={stamp},v1={digest}"}


def message(gateway, status, event=None):
    body = {"gatewayId": gateway}
    if status is not None:
        body["status"] = status
    if event is not None:
        body["id"] = event
    return json.dumps(body).encode()


def test_the_webhook_sets_the_status_of_the_subscription_the_gateway_names_and_refuses_what_is_not_signed(world, tenant, application, monkeypatch):
    app, _ = application
    api, _ = world
    secret = "test-secret"
    monkeypatch.setenv("§SECRETNAME", secret)
    gateway = "gw-" + uuid.uuid4().hex
    set_subscription(api, tenant, planId=PLAN, status="active", gatewayId=gateway)
    anonymous = TestClient(app)
    path = "§WEBHOOKPATH"

    def status():
        return api(tenant, "GET", "/api/subscription").json()["status"]

    body = message(gateway, "cancelled")
    assert anonymous.post(path, content=body).status_code == 401, "a message with no signature is refused"
    assert anonymous.post(path, content=body, headers=signed(body, "not-the-secret")).status_code == 401, "a message signed with another secret is refused"
    old = anonymous.post(path, content=body, headers=signed(body, secret, int(time.time()) - 3600))
    assert old.status_code == 400, "a signed message an hour old is a replay: " + old.text
    assert old.json()["type"] == "§EXPIRED_TYPE"
    assert status() == "active", "the refused messages changed nothing"

    past_due = message(gateway, "past_due", "evt-" + uuid.uuid4().hex)
    applied = anonymous.post(path, content=past_due, headers=signed(past_due, secret))
    assert applied.status_code == 200, applied.text
    assert applied.json()["applied"] is True
    assert status() == "past_due"
§PAUSEDLINES    repeated = past_due.replace(b"past_due", b"active")
    assert anonymous.post(path, content=repeated, headers=signed(repeated, secret)).json()["applied"] is False, "an event id is applied once"
    assert status() == "past_due"
    resumed = message(gateway, "active", "evt-" + uuid.uuid4().hex)
    assert anonymous.post(path, content=resumed, headers=signed(resumed, secret)).status_code == 200
    assert status() == "active"
    unknown = message("gw-nobody", "active")
    assert anonymous.post(path, content=unknown, headers=signed(unknown, secret)).status_code == 404
    incomplete = message(gateway, None)
    assert anonymous.post(path, content=incomplete, headers=signed(incomplete, secret)).status_code == 400
"""";

    private const string RouteCases = """"


def test_only_a_platform_administrator_changes_a_subscription(world, tenant):
    api, _ = world
    refused = api(tenant, "PUT", f"/api/subscription/{tenant}", json={"planId": PLAN, "status": "active"})
    assert refused.status_code == 403, "an administrator inside a tenant cannot give themselves a plan: " + refused.text
    assert refused.json()["type"] == "§PLATFORM_TYPE"
    assert api(None, "PUT", "/api/subscription/2147483000", json={"planId": PLAN, "status": "active"}).status_code == 404
    assert api(None, "PUT", f"/api/subscription/{tenant}", json={"status": "active"}).status_code == 400, "a tenant with no subscription needs a plan and a status"
    assert api(None, "PUT", f"/api/subscription/{tenant}", json={"planId": 2147483000, "status": "active"}).status_code == 400, "an unknown plan is refused"


def test_the_administrator_sees_the_subscription_and_a_member_does_not(world, tenant):
    api, _ = world
    set_subscription(api, tenant, planId=PLAN, status="active")
    shown = api(tenant, "GET", "/api/subscription")
    assert shown.status_code == 200, shown.text
    info = shown.json()
    assert info["status"] == "active"
    assert info["planId"] == PLAN
    assert info["writable"] is True
    assert api(tenant, "GET", "/api/subscription", role="Member").status_code == 403
"""";
}
