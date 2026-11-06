"""Plays the usage ledger against a running multi-tenant API (ASP.NET, Python or Rust) over a scratch copy of Schemas/usage.sql.

python usage_check.py <base url> <sqlite file>

The API is a generated one with ApiAuth, TenantTable=Tenant, TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager,
UserAdminColumn=IsAdministrator, SubscriptionTable=Subscription, PlanTable=Plan, PlanLimitTable=PlanLimit, PlanLimits=Project:projects, UsageTables=UsageEvent and UsageLimits=api_calls,emails.
The script writes test password hashes into the copy's Employee rows, empties UsageEvent and changes its plan limit rows; never point it at a database that matters. It signs in as ada
(administrator of tenant 1, on Pro), grace (administrator of tenant 2, on Free: 100 api_calls and 2 emails a month) and pat (a platform administrator). It checks: no sign-in is a 401; a platform
administrator records usage and nobody else may; the answer says what the month holds and whether the tenant is over its plan; the summary adds the events per month and kind and never shows another
tenant's; an event of an earlier month is not counted against this month's limit; the subscription view shows the used units of a usage limit; raising the limit lifts the over flag; a kind that no plan
limits is never over; and nothing edits or deletes an event."""
import base64
import hashlib
import sqlite3
import sys
from datetime import datetime, timezone

import httpx

base, db = sys.argv[1], sys.argv[2]


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


connection = sqlite3.connect(db)
for login in ("ada", "grace", "pat"):
    connection.execute("UPDATE Employee SET PasswordHash = ? WHERE Login = ?", (hash_password("secret-pass"), login))
connection.execute("DELETE FROM UsageEvent")
connection.execute("DELETE FROM UsageMonthly")
connection.execute("UPDATE PlanLimit SET LimitValue = 100 WHERE PlanId = 1 AND LimitKey = 'api_calls'")
connection.commit()

client = httpx.Client(base_url=base, timeout=60)
failures = []


def check(label, condition, detail=""):
    print(("ok   " if condition else "FAIL ") + label + ("" if condition else "  " + str(detail)[:400]))
    if not condition:
        failures.append(label)


def sign_in(name, tenant=None):
    body = {"userName": name, "password": "secret-pass"}
    if tenant is not None:
        body["tenantId"] = tenant
    reply = client.post("/api/auth/login", json=body)
    return {"Authorization": "Bearer " + reply.json()["token"]}


def record(tenant, kind, quantity=None, **extra):
    body = {"tenantId": tenant, "kind": kind, **extra}
    if quantity is not None:
        body["quantity"] = quantity
    return client.post("/api/usageevents", headers=pat, json=body)


def line(rows, kind, month):
    return next((r for r in rows if r["kind"] == kind and r["month"] == month), None)


def limit_of(info, key):
    return next(l for l in info["limits"] if l["key"] == key)


now = datetime.now(timezone.utc)
this_month = now.strftime("%Y-%m")
ada, grace, pat = sign_in("ada", 1), sign_in("grace", 2), sign_in("pat")

check("a summary without a sign-in is a 401", client.get("/api/usageevents").status_code == 401)
check("recording without a sign-in is a 401", client.post("/api/usageevents", json={"tenantId": 2, "kind": "api_calls"}).status_code == 401)
check("an administrator of a tenant may not record", client.post("/api/usageevents", headers=grace, json={"tenantId": 2, "kind": "api_calls"}).status_code == 403)
check("the summary of an empty ledger is empty", client.get("/api/usageevents", headers=grace).json() == [])
check("a platform administrator must name the tenant to read", client.get("/api/usageevents", headers=pat).status_code == 400)

first = record(2, "api_calls", 60)
check("recording is a 201 with the key of the event", first.status_code == 201 and first.json().get("id"), first.text)
check("the answer says what the month holds and what the plan allows", first.json().get("used") == 60 and first.json().get("allowed") == 100 and first.json().get("over") is False, first.text)
second = record(2, "api_calls", 50)
check("a second event takes the tenant past its plan", second.json().get("used") == 110 and second.json().get("over") is True, second.text)
check("the event is still written (the ledger is what happened)", connection.execute("SELECT SUM(Quantity) FROM UsageEvent WHERE TenantId = 2 AND Kind = 'api_calls'").fetchone()[0] == 110)

check("a blank kind is a 400", record(2, " ").status_code == 400)
check("a quantity of zero is a 400", record(2, "api_calls", 0).status_code == 400)
check("a negative quantity is a 400", record(2, "api_calls", -5).status_code == 400)
check("an unknown tenant is a 404", record(2000000000, "api_calls").status_code == 404)
check("a missing tenant is a 400", client.post("/api/usageevents", headers=pat, json={"kind": "api_calls"}).status_code == 400)

emails = record(2, "emails", 2)
check("a kind exactly at its limit is not over", emails.json().get("used") == 2 and emails.json().get("allowed") == 2 and emails.json().get("over") is False, emails.text)
check("one more is over", record(2, "emails").json().get("over") is True)
unlimited = record(2, "storage_gb", 500)
check("a kind that no plan limits is never over", unlimited.json().get("allowed") is None and unlimited.json().get("over") is False, unlimited.text)
pro = record(1, "api_calls", 1000)
check("a plan with no limit row for the kind (Pro: NULL) is unlimited", pro.json().get("allowed") is None and pro.json().get("over") is False, pro.text)

connection.execute("INSERT INTO UsageEvent (TenantId, Kind, Quantity, OccurredOn) VALUES (2, 'api_calls', 500, '2020-01-15 12:00:00')")
connection.commit()
rows = client.get("/api/usageevents", headers=grace).json()
check("the summary adds the events of this month per kind", line(rows, "api_calls", this_month) and line(rows, "api_calls", this_month)["quantity"] == 110 and line(rows, "api_calls", this_month)["events"] == 2, rows)
check("an earlier month is its own line", line(rows, "api_calls", "2020-01") and line(rows, "api_calls", "2020-01")["quantity"] == 500, rows)
months = [r["month"] for r in rows]
check("the newest month is first", months == sorted(months, reverse=True), months)
check("the other tenant's events are not in the summary", line(rows, "api_calls", this_month)["quantity"] == 110 and all(r["quantity"] != 1000 for r in rows), rows)
check("the kind filter keeps one kind", {r["kind"] for r in client.get("/api/usageevents?kind=emails", headers=grace).json()} == {"emails"})
old = client.get("/api/usageevents?from=2019-01-01&to=2020-06-01", headers=grace).json()
check("the date filter keeps the old event and leaves this month out", len(old) == 1 and old[0]["month"] == "2020-01", old)
ada_rows = client.get("/api/usageevents", headers=ada).json()
check("the first tenant reads its own usage only", line(ada_rows, "api_calls", this_month)["quantity"] == 1000 and line(ada_rows, "api_calls", "2020-01") is None, ada_rows)
check("a platform administrator reads a named tenant", line(client.get("/api/usageevents?tenantId=2", headers=pat).json(), "emails", this_month) is not None)

info = client.get("/api/subscription", headers=grace).json()
check("the subscription view counts the month's usage against the plan (the old month is not counted)", limit_of(info, "api_calls")["allowed"] == 100 and limit_of(info, "api_calls")["used"] == 110, info)
check("the view lists the usage limits of every plan", {l["key"] for l in info["plans"][0]["limits"]} >= {"api_calls", "emails"}, info["plans"][0])
pro_info = client.get("/api/subscription", headers=ada).json()
check("Pro has no api_calls limit and shows what is used", limit_of(pro_info, "api_calls")["allowed"] is None and limit_of(pro_info, "api_calls")["used"] == 1000, pro_info)

connection.execute("UPDATE PlanLimit SET LimitValue = 1000 WHERE PlanId = 1 AND LimitKey = 'api_calls'")
connection.commit()
raised = record(2, "api_calls", 1)
check("raising the limit lifts the over flag at once", raised.json().get("allowed") == 1000 and raised.json().get("over") is False, raised.text)

connection.execute("DELETE FROM UsageMonthly")
connection.execute("INSERT INTO UsageEvent (TenantId, Kind, Quantity, OccurredOn) VALUES (2, 'api_calls', 10, '2019-03-10 12:00:00'), (2, 'api_calls', 20, '2019-03-20 12:00:00'), (2, 'emails', 1, '2019-04-02 12:00:00'), (1, 'api_calls', 5, '2019-03-11 12:00:00')")
connection.commit()
check("an administrator of a tenant may not roll up", client.post("/api/usageevents/roll-up?before=2019-05-01", headers=grace).status_code == 403)
rolled = client.post("/api/usageevents/roll-up?before=2019-05-01", headers=pat)
check("a platform administrator rolls up the old events", rolled.status_code == 200 and rolled.json().get("events") == 4 and rolled.json().get("months") == 3, rolled.text)
check("the monthly total holds the units of the events", connection.execute("SELECT Quantity FROM UsageMonthly WHERE TenantId = 2 AND Kind = 'api_calls' AND date(UsageMonth) = '2019-03-01'").fetchone()[0] == 30)
check("the rolled events are gone from the ledger", connection.execute("SELECT COUNT(*) FROM UsageEvent WHERE OccurredOn < '2019-05-01'").fetchone()[0] == 0)
rows = client.get("/api/usageevents", headers=grace).json()
check("the summary still shows the rolled month, with no events left in it", line(rows, "api_calls", "2019-03") and line(rows, "api_calls", "2019-03")["quantity"] == 30 and line(rows, "api_calls", "2019-03")["events"] == 0, rows)
check("another kind of another month is its own line", line(rows, "emails", "2019-04") and line(rows, "emails", "2019-04")["quantity"] == 1, rows)
check("another tenant's rolled month is not in the summary", line(client.get("/api/usageevents", headers=ada).json(), "api_calls", "2019-03")["quantity"] == 5)
check("the kind and date filters read the monthly totals too", [r["month"] for r in client.get("/api/usageevents?kind=emails&from=2019-01-01&to=2019-12-31", headers=grace).json()] == ["2019-04"])
check("a rolled month is left out when From or Before cuts into it", not line(client.get("/api/usageevents?from=2019-03-10", headers=grace).json(), "api_calls", "2019-03") and not line(client.get("/api/usageevents?to=2019-03-20", headers=grace).json(), "api_calls", "2019-03"))
check("a rolled month is kept when the whole month is inside", line(client.get("/api/usageevents?from=2019-03-01&to=2019-04-01", headers=grace).json(), "api_calls", "2019-03") is not None)
connection.execute("INSERT INTO UsageEvent (TenantId, Kind, Quantity, OccurredOn) VALUES (2, 'api_calls', 7, '2019-03-05 12:00:00')")
connection.commit()
again = client.post("/api/usageevents/roll-up?before=2019-05-01", headers=pat)
check("a second roll-up adds to the month", again.json().get("events") == 1 and connection.execute("SELECT Quantity FROM UsageMonthly WHERE TenantId = 2 AND Kind = 'api_calls' AND date(UsageMonth) = '2019-03-01'").fetchone()[0] == 37, again.text)
check("one row per tenant, kind and month", connection.execute("SELECT COUNT(*) FROM UsageMonthly WHERE TenantId = 2 AND Kind = 'api_calls' AND date(UsageMonth) = '2019-03-01'").fetchone()[0] == 1)
future = client.post("/api/usageevents/roll-up?before=2100-01-01", headers=pat)
check("a day in the future never rolls this month", future.status_code == 200 and connection.execute("SELECT COUNT(*) FROM UsageEvent WHERE TenantId = 2 AND Kind = 'api_calls' AND OccurredOn >= ?", (now.strftime("%Y-%m-01"),)).fetchone()[0] == 3)
check("the old event of 2020 was rolled by the default cutoff", connection.execute("SELECT Quantity FROM UsageMonthly WHERE TenantId = 2 AND Kind = 'api_calls' AND date(UsageMonth) = '2020-01-01'").fetchone()[0] == 500)
info = client.get("/api/subscription", headers=grace).json()
check("the plan limit still counts this month's events only", limit_of(info, "api_calls")["used"] == 111, info)

for method in ("put", "delete", "patch"):
    status = client.request(method.upper(), "/api/usageevents/1", headers=pat, json={}).status_code
    check(f"there is no {method} route for an event", status in (404, 405), status)

print("failures:", len(failures))
sys.exit(1 if failures else 0)
