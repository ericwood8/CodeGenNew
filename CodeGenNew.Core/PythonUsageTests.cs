using static CodeGenNew.Core.TemplateText;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> <c>tests/test_usage_&lt;table&gt;.py</c> of the Python API (PY_TestUsage), beside the <c>conftest.py</c> of PY_TestTenants: pytest tests of a usage ledger through its routes. A call without a token is a 401,
/// a role that is not the administrator's a 403, the summary adds the events per month and kind (and can be limited by kind and dates), another tenant's events are not in it, a platform administrator names
/// the tenant and is the one who records usage, a roll-up totals the old events into the months, and nothing edits or deletes an event. Each test records its events through the record route and removes them
/// again. The same cases and sentences as the ASP.NET tests (<see cref="ApiUsageTests"/>). </summary>
public static class PythonUsageTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c> over a tenant API with a usage ledger and a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        PythonTenantTests.Applies(database, project) && UsageTestPlan.Ledgers(database, project).Count > 0;

    public static string FileName(UsageShape t) => "tests/test_usage_" + RustNames.Snake(t.Table.TableName) + ".py";

    public static string File(UsageShape t, DatabaseModel database, ProjectSettings project, UsageRollupShape? rollup)
    {
        string Str(string value) => PythonNames.Str(value);
        var (q1, q2, q3, sum) = UsageTestPlan.Units(t);
        bool units = t.Quantity is not null;
        string months = UsageTestPlan.RemoveMonths(database, rollup, ":tenant", ":kind") is { } removeMonths ? $"REMOVE_MONTHS = {Str(removeMonths)}\n" : "";
        var o = new StringBuilder();
        o.Append(FillAll(Head, ("§TABLE", t.Table.TableName), ("§ADMIN", Str(project.AuthAdminRole)), ("§FIRST", Str(TenantTestPlan.FirstTenantsSql(database, project))),
            ("§ROUTE", Str(BlazorNames.ApiRoute(t.Table.TableName))), ("§REMOVEEVENTS", Str(UsageTestPlan.RemoveEvents(database, t, ":tenant", ":kind"))), ("§REMOVEMONTHS", months),
            ("§LENGTH", UsageTestPlan.KindLength(t).ToString()),
            ("§CLEANMONTHS", rollup is null ? "" : "            connection.execute(text(REMOVE_MONTHS), {\"tenant\": tenant, \"kind\": kind})\n")));
        string Qty(string value) => units ? ", " + value : "";
        o.Append(FillAll(SummaryCases, ("§Q1", Qty(q1)), ("§Q2", Qty(q2)), ("§Q3", Qty(q3)),
            ("§SUMCHECK", units ? $"    assert float(line(everything, one, this_month())[\"quantity\"]) == pytest.approx({sum.ToString(System.Globalization.CultureInfo.InvariantCulture)}), \"the units of the two events of this month\"\n"
                + $"    assert float(line(everything, two, \"2020-01\")[\"quantity\"]) == pytest.approx({q3})\n"
                : "    assert float(line(everything, one, this_month())[\"quantity\"]) == 2, \"each event is one unit\"\n    assert float(line(everything, two, \"2020-01\")[\"quantity\"]) == 1\n")));
        o.Append(FillAll(RecordCases, ("§FRACTION", units && !t.QuantityIsDecimal
            ? "    fraction = call(None, \"POST\", ROUTE, json={\"tenantId\": tenant, \"kind\": kind, \"quantity\": 1.5})\n    assert fraction.status_code == 400, \"a whole-number ledger refuses a fraction\"\n" : ""),
            ("§POS6", units ? ", 6" : ""), ("§QUANTITY0", units ? ", \"quantity\": 0" : ""),
            ("§USEDLINE", units ? "    assert float(recorded[\"used\"]) == 6\n" : ""),
            ("§HALF", t.QuantityIsDecimal ? "    half = call(None, \"POST\", ROUTE, json={\"tenantId\": tenant, \"kind\": kind, \"quantity\": 1.5})\n    assert half.status_code == 201, half.text\n" : ""),
            ("§LINECHECK", units ? $"    assert float(line(lines_of(call(tenant, \"GET\", ROUTE)), kind, this_month())[\"quantity\"]) == pytest.approx({(t.QuantityIsDecimal ? "7.5" : "6")})\n"
                : "    assert line(lines_of(call(tenant, \"GET\", ROUTE)), kind, this_month()) is not None\n")));
        if (rollup is not null && units)
            o.Append(Rollup);
        if (UsageTestPlan.LimitCaseOf(database, project, t, PythonSubscriptionWriter.Applies(project) && PythonSubscriptionTests.Applies(database, project), "") is { } limit)
        {
            var sql = limit.Sql;
            string List(IEnumerable<string> statements) => "[" + string.Join(", ", statements.Select(Str)) + "]";
            string removeMonths2 = UsageTestPlan.RemoveTenantMonths(database, rollup) is { } months2 ? $"REMOVE_TENANT_MONTHS = {Str(months2)}\n" : "";
            o.Append(FillAll(LimitHead, ("§PLAN", sql.PlanKey.ToString()), ("§ADDPLAN", Str(sql.AddPlan)), ("§REMOVEPLAN", List(sql.RemovePlan)), ("§CLEARSUBSCRIPTION", Str(sql.ClearSubscription)),
                ("§KEY", Str(limit.Key)), ("§REMOVETENANTEVENTS", Str(UsageTestPlan.RemoveTenantEvents(database, t))), ("§REMOVETENANTMONTHS", removeMonths2),
                ("§CLEANMONTHS", rollup is null ? "" : "    statements.insert(0, REMOVE_TENANT_MONTHS + str(tenant))\n"),
                ("§ACTIVELINE", TenantTestPlan.ActiveKey(database, project) is { } flag ? $"    body[{Str(flag)}] = True\n" : "")));
            var limits = new[] { ("§LIMIT5", List(sql.SetLimit(limit.Key, 5))), ("§LIMIT100", List(sql.SetLimit(limit.Key, 100))), ("§LIMIT_TYPE", SubscriptionWords.LimitType) };
            o.Append(FillAll(project.UsageRefuse ? RefuseCases : OverCases, limits));
        }
        o.Append(NoEdit);
        return o.ToString().TrimEnd('\n') + "\n";
    }

    private const string Head = """"
"""The usage ledger §TABLE through the routes. Written by PY_TestUsage; regenerated with the tables. Each test records its events through the record route and removes them again."""
import uuid
from datetime import datetime, timezone

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import text

from app.auth import CurrentUser, require_user

ADMIN_ROLE = §ADMIN
FIRST_TENANTS = §FIRST
ROUTE = §ROUTE
REMOVE_EVENTS = §REMOVEEVENTS
§REMOVEMONTHS


@pytest.fixture
def call(application):
    """call(tenant, method, path, role=...): a request made as a person of that role who works in that tenant (None: in no tenant, a platform administrator)."""
    app, _ = application
    who = {"tenant": None, "role": ADMIN_ROLE}
    app.dependency_overrides[require_user] = lambda: CurrentUser(name="usage-test", role=who["role"], owner_id=None, tenant_id=who["tenant"])
    client = TestClient(app)

    def make(tenant, method, path, role=ADMIN_ROLE, **kw):
        who["tenant"] = tenant
        who["role"] = role
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


@pytest.fixture
def ledger(application, call):
    """add(tenant, kind, quantity=None, occurred=None) records an event as a platform administrator; the events (and monthly totals) of every kind it recorded are removed again."""
    _, engine = application
    kinds = []

    def add(tenant, kind, quantity=None, occurred=None):
        body = {"tenantId": tenant, "kind": kind}
        if quantity is not None:
            body["quantity"] = quantity
        if occurred is not None:
            body["occurredOn"] = occurred
        kinds.append((tenant, kind))
        reply = call(None, "POST", ROUTE, json=body)
        assert reply.status_code == 201, reply.text
        return reply.json()

    yield add
    with engine.begin() as connection:
        for tenant, kind in kinds:
            connection.execute(text(REMOVE_EVENTS), {"tenant": tenant, "kind": kind})
§CLEANMONTHS

def new_kind():
    return "t" + uuid.uuid4().hex[: §LENGTH - 1]


def this_month():
    return datetime.now(timezone.utc).strftime("%Y-%m")


def lines_of(reply):
    assert reply.status_code == 200, reply.text
    return reply.json()


def line(lines, kind, month):
    return next((l for l in lines if l["kind"] == kind and l["month"] == month), None)


def test_without_a_token_the_routes_answer_401(application):
    app, _ = application
    app.dependency_overrides.pop(require_user, None)
    client = TestClient(app)
    assert client.get(ROUTE).status_code == 401
    assert client.post(ROUTE, json={"tenantId": 1, "kind": new_kind()}).status_code == 401


def test_a_role_that_is_not_the_administrators_cannot_read_the_usage(call, tenants):
    assert call(tenants[0], "GET", ROUTE, role="NoSuchRole").status_code == 403
"""";

    private const string SummaryCases = """"


def test_the_summary_adds_the_events_per_month_and_kind(call, ledger, tenants):
    tenant = tenants[0]
    one, two = new_kind(), new_kind()
    ledger(tenant, one§Q1)
    ledger(tenant, one§Q2)
    ledger(tenant, two§Q3, "2020-01-15T12:00:00")
    everything = lines_of(call(tenant, "GET", ROUTE))
§SUMCHECK    assert line(everything, one, this_month())["events"] == 2
    by_kind = lines_of(call(tenant, "GET", f"{ROUTE}?kind={one}"))
    assert by_kind and all(l["kind"] == one for l in by_kind), "the kind filter"
    early = lines_of(call(tenant, "GET", f"{ROUTE}?from=2019-01-01&to=2020-06-01"))
    assert line(early, two, "2020-01") is not None, "the date filter keeps the old event"
    assert line(early, one, this_month()) is None, "and leaves this month out"
    months = [l["month"] for l in everything]
    assert months == sorted(months, reverse=True), "newest month first"


def test_another_tenants_events_are_not_in_the_summary(call, ledger, tenants):
    if len(tenants) < 2:
        pytest.skip("the database has only one tenant")
    kind = new_kind()
    ledger(tenants[1], kind, 2)
    assert line(lines_of(call(tenants[0], "GET", ROUTE)), kind, this_month()) is None, "a tenant never reads another tenant's usage"
    assert line(lines_of(call(tenants[1], "GET", ROUTE)), kind, this_month()) is not None


def test_a_platform_administrator_names_the_tenant_to_read(call, ledger, tenants):
    tenant = tenants[0]
    kind = new_kind()
    ledger(tenant, kind, 1)
    assert call(None, "GET", ROUTE).status_code == 400
    assert line(lines_of(call(None, "GET", f"{ROUTE}?tenantId={tenant}")), kind, this_month()) is not None
"""";

    private const string RecordCases = """"


def test_only_a_platform_administrator_records_usage_and_it_reaches_the_summary(call, ledger, tenants):
    tenant = tenants[0]
    kind = new_kind()
    assert call(tenant, "POST", ROUTE, json={"tenantId": tenant, "kind": kind}).status_code == 403
    assert call(None, "POST", ROUTE, json={"tenantId": tenant, "kind": " "}).status_code == 400
    assert call(None, "POST", ROUTE, json={"kind": kind}).status_code == 400
    assert call(None, "POST", ROUTE, json={"tenantId": tenant, "kind": kind§QUANTITY0}).status_code == 400
    assert call(None, "POST", ROUTE, json={"tenantId": 2000000000, "kind": kind}).status_code == 404
§FRACTION    recorded = ledger(tenant, kind§POS6)
    assert recorded["over"] is False, "a kind no plan limits is never over"
§USEDLINE§HALF§LINECHECK
"""";

    private const string Rollup = """"


def test_rolling_up_totals_old_events_into_the_months_and_the_summary_still_adds_them(call, ledger, tenants):
    tenant = tenants[0]
    kind = new_kind()
    ledger(tenant, kind, 2, "2019-03-10T12:00:00")
    ledger(tenant, kind, 3, "2019-03-20T12:00:00")
    ledger(tenant, kind, 4)
    assert call(tenant, "POST", f"{ROUTE}/roll-up?before=2019-04-01").status_code == 403, "only a platform administrator rolls up"
    rolled = call(None, "POST", f"{ROUTE}/roll-up?before=2019-04-01")
    assert rolled.status_code == 200, rolled.text
    assert rolled.json()["events"] >= 2, "the two old events were totalled"
    lines = lines_of(call(tenant, "GET", ROUTE))
    march = line(lines, kind, "2019-03")
    assert march is not None, "the rolled month is still in the summary"
    assert float(march["quantity"]) == 5, "2 and 3 units"
    assert march["events"] == 0, "no event of that month is left"
    assert float(line(lines, kind, this_month())["quantity"]) == 4, "this month was not rolled"
    assert line(lines_of(call(tenant, "GET", f"{ROUTE}?from=2019-03-10")), kind, "2019-03") is None, "a rolled month is left out when From cuts into it"
    assert line(lines_of(call(tenant, "GET", f"{ROUTE}?to=2019-03-20")), kind, "2019-03") is None, "a rolled month is left out when Before cuts into it"
    assert line(lines_of(call(tenant, "GET", f"{ROUTE}?from=2019-03-01&to=2019-04-01")), kind, "2019-03") is not None, "a rolled month is kept when the whole month is inside"
    ledger(tenant, kind, 6, "2019-03-05T12:00:00")
    assert call(None, "POST", f"{ROUTE}/roll-up?before=2019-04-01").status_code == 200
    assert float(line(lines_of(call(tenant, "GET", ROUTE)), kind, "2019-03")["quantity"]) == 11, "a second roll-up adds to the month"
"""";

    private const string LimitHead = """"


from concurrent.futures import ThreadPoolExecutor

from app.routes.subscriptions import forget_state

from conftest import run
from test_tenants import TENANT_KEY, TENANTS, REMOVE_TENANT, tenant_body

PLAN = §PLAN
ADD_PLAN = §ADDPLAN
REMOVE_PLAN = §REMOVEPLAN
CLEAR_SUBSCRIPTION = §CLEARSUBSCRIPTION
KEY = §KEY
REMOVE_TENANT_EVENTS = §REMOVETENANTEVENTS
§REMOVETENANTMONTHS

@pytest.fixture
def billed(application, call):
    """A tenant on a plan of the test's own (its limits are set by the test), taken out again with its events and its subscription."""
    _, engine = application
    run(engine, REMOVE_PLAN)
    run(engine, [ADD_PLAN])
    body = tenant_body(call, None)
§ACTIVELINE    made = call(None, "POST", TENANTS, json=body)
    assert made.status_code == 201, made.text
    tenant = made.json()[TENANT_KEY]
    forget_state(tenant)
    chosen = call(None, "PUT", f"/api/subscription/{tenant}", json={"planId": PLAN, "status": "active"})
    assert chosen.status_code == 200, chosen.text
    yield tenant
    forget_state(tenant)
    statements = [REMOVE_TENANT_EVENTS + str(tenant), CLEAR_SUBSCRIPTION + str(tenant), REMOVE_TENANT + str(tenant)]
§CLEANMONTHS    run(engine, statements)
    run(engine, REMOVE_PLAN)


def post(call, tenant, quantity, occurred=None):
    body = {"tenantId": tenant, "kind": KEY, "quantity": quantity}
    if occurred is not None:
        body["occurredOn"] = occurred
    return call(None, "POST", ROUTE, json=body)


def record(call, tenant, quantity, occurred=None):
    reply = post(call, tenant, quantity, occurred)
    assert reply.status_code == 201, reply.text
    return reply.json()


def used(call, tenant):
    found = line(lines_of(call(tenant, "GET", ROUTE)), KEY, this_month())
    return float(found["quantity"]) if found else 0.0
"""";

    private const string OverCases = """"


def test_the_answer_says_when_a_tenant_passes_its_plan_and_the_subscription_shows_it(call, billed, application):
    _, engine = application
    run(engine, §LIMIT5)
    first = record(call, billed, 3)
    assert first["allowed"] == 5 and float(first["used"]) == 3 and first["over"] is False
    second = record(call, billed, 3)
    assert float(second["used"]) == 6 and second["over"] is True, "the event is written and the answer says the tenant is over"
    row = next(item for item in call(billed, "GET", "/api/subscription").json()["limits"] if item["key"] == KEY)
    assert row["allowed"] == 5 and float(row["used"]) == 6
    run(engine, §LIMIT100)
    assert record(call, billed, 1)["over"] is False, "raising the limit lifts the flag"
"""";

    private const string RefuseCases = """"


def test_a_request_past_the_plan_is_refused_and_writes_nothing(call, billed, application):
    _, engine = application
    run(engine, §LIMIT5)
    assert record(call, billed, 3)["over"] is False
    refused = post(call, billed, 3)
    assert refused.status_code == 402, refused.text
    assert refused.json()["type"] == "§LIMIT_TYPE"
    assert used(call, billed) == 3, "a refused request writes nothing"
    reached = record(call, billed, 2)
    assert float(reached["used"]) == 5 and reached["over"] is False, "reaching the limit exactly is allowed"
    assert post(call, billed, 1).status_code == 402, "one more is refused"
    assert record(call, billed, 1000, "2020-01-15T12:00:00")["over"] is not None, "an event of an earlier month is judged against nothing"
    row = next(item for item in call(billed, "GET", "/api/subscription").json()["limits"] if item["key"] == KEY)
    assert row["allowed"] == 5 and float(row["used"]) == 5
    run(engine, §LIMIT100)
    assert post(call, billed, 3).status_code == 201, "raising the limit lifts the refusal"


def test_requests_at_the_same_moment_cannot_both_pass_the_limit(application, call, billed):
    app, engine = application
    run(engine, §LIMIT100)
    app.dependency_overrides[require_user] = lambda: CurrentUser(name="usage-test", role=ADMIN_ROLE, owner_id=None, tenant_id=None)
    client = TestClient(app)

    def send(_):
        return client.post(ROUTE, json={"tenantId": billed, "kind": KEY, "quantity": 10}).status_code

    with ThreadPoolExecutor(max_workers=24) as pool:
        codes = list(pool.map(send, range(30)))
    assert sorted(set(codes)) == [201, 402], codes
    assert codes.count(201) == 10, "exactly the limit fits"
    found = line(lines_of(client.get(f"{ROUTE}?tenantId={billed}")), KEY, this_month())
    assert float(found["quantity"]) == 100, "the ledger holds exactly the limit"
"""";

    private const string NoEdit = """"


def test_nothing_edits_or_deletes_an_event(call):
    for method in ("PUT", "DELETE", "PATCH"):
        assert call(None, method, f"{ROUTE}/1").status_code in (404, 405), "the ledger has no edit or delete route"
"""";
}
