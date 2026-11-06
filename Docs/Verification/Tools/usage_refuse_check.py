"""Plays UsageRefuse=true against a running ASP.NET, Python or Rust API over a scratch copy of Schemas/usage_decimal.sql.

python usage_refuse_check.py <base url> <sqlite file>

The API is generated like the one of usage_check.py, with UsageRefuse=true as well. The script writes test password hashes into the copy and empties the ledger. A platform administrator records
events for tenant 2 (the Free plan: 100 api_calls and 2 emails a month) and for tenant 1 (Pro, no limit on api_calls). It checks that an event that would pass the limit is a 402 with the plan-limit
problem and is not written, that an event that reaches the limit exactly is written, that a kind no plan limits and an unlimited plan are never refused, that an event of an earlier month is not counted
against this month, and that raising the limit lets the next event in."""
import base64
import hashlib
import sqlite3
import sys

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


reply = client.post("/api/auth/login", json={"userName": "pat", "password": "secret-pass"})
pat = {"Authorization": "Bearer " + reply.json()["token"]}


def record(tenant, kind, quantity=None, **extra):
    body = {"tenantId": tenant, "kind": kind, **extra}
    if quantity is not None:
        body["quantity"] = quantity
    return client.post("/api/usageevents", headers=pat, json=body)


def stored(tenant, kind):
    return connection.execute("SELECT COALESCE(SUM(Quantity), 0) FROM UsageEvent WHERE TenantId = ? AND Kind = ?", (tenant, kind)).fetchone()[0]


first = record(2, "api_calls", 60)
check("an event inside the limit is written", first.status_code == 201 and first.json().get("over") is False, first.text)
past = record(2, "api_calls", 50)
check("an event that would pass the limit is a 402", past.status_code == 402, past.text)
check("the 402 is the plan-limit problem", past.json().get("type", "").endswith("plan-limit") and past.json().get("title") == "Plan limit reached", past.text)
check("the refused event is not written", stored(2, "api_calls") == 60)
exact = record(2, "api_calls", 40)
check("an event that reaches the limit exactly is written", exact.status_code == 201 and exact.json().get("used") == 100 and exact.json().get("over") is False, exact.text)
check("one more unit is a 402", record(2, "api_calls", 1).status_code == 402)
check("a ledger at its limit still holds 100", stored(2, "api_calls") == 100)
check("emails up to the limit are written", record(2, "emails", 2).status_code == 201)
check("one email more is a 402", record(2, "emails").status_code == 402)
check("a kind that no plan limits is never refused", record(2, "storage_gb", 500).status_code == 201)
check("a plan with no limit for the kind is never refused", record(1, "api_calls", 1000).status_code == 201)
check("an event of an earlier month is not counted against this month", record(2, "api_calls", 500, occurredOn="2020-01-15T12:00:00").status_code == 201)
connection.execute("UPDATE PlanLimit SET LimitValue = 1000 WHERE PlanId = 1 AND LimitKey = 'api_calls'")
connection.commit()
raised = record(2, "api_calls", 100)
check("raising the limit lets the next event in", raised.status_code == 201 and raised.json().get("allowed") == 1000, raised.text)

print("failures: %d" % len(failures))
sys.exit(1 if failures else 0)
