"""Plays plan limits that belong to different usage tables against a running ASP.NET, Python or Rust API over a scratch copy of Schemas/usage_two.sql.

python usage_limit_table_check.py <base url> <sqlite file>

The API is generated like the one of usage_two_check.py with UsageLimits=api_calls,emails,hits:ApiCall (api_calls and emails belong to the first table listed, UsageEvent; hits to ApiCall) and
UsageRefuse=true. The script gives the Free plan 5 hits a month, records events for tenant 2 through both routes and checks that a limit is judged by the events of its own table only: hits recorded in
ApiCall are limited and counted in the subscription view, the same kind recorded in UsageEvent is not limited, and api_calls recorded in ApiCall are not counted against the UsageEvent limit."""
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
for login in ("grace", "pat"):
    connection.execute("UPDATE Employee SET PasswordHash = ? WHERE Login = ?", (hash_password("secret-pass"), login))
for table in ("UsageEvent", "UsageMonthly", "ApiCall", "ApiCallMonthly"):
    connection.execute(f"DELETE FROM {table}")
connection.execute("UPDATE PlanLimit SET LimitValue = 100 WHERE PlanId = 1 AND LimitKey = 'api_calls'")
connection.execute("DELETE FROM PlanLimit WHERE PlanId = 1 AND LimitKey = 'hits'")
connection.execute("INSERT INTO PlanLimit (PlanId, LimitKey, LimitValue) VALUES (1, 'hits', 5)")
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
    return {"Authorization": "Bearer " + client.post("/api/auth/login", json=body).json()["token"]}


def record(route, kind, quantity):
    return client.post(route, headers=pat, json={"tenantId": 2, "kind": kind, "quantity": quantity})


def limit_of(info, key):
    return next((l for l in info["limits"] if l["key"] == key), None)


grace, pat = sign_in("grace", 2), sign_in("pat")

first = record("/api/apicalls", "hits", 3)
check("hits in ApiCall are judged against the 5 hits of the plan", first.status_code == 201 and first.json().get("allowed") == 5 and first.json().get("over") is False, first.text)
past = record("/api/apicalls", "hits", 3)
check("hits past the plan are refused", past.status_code == 402, past.text)
check("the refused hits are not written", connection.execute("SELECT COALESCE(SUM(Quantity), 0) FROM ApiCall WHERE Kind = 'hits'").fetchone()[0] == 3)
check("hits up to the plan are written", record("/api/apicalls", "hits", 2).status_code == 201)
check("one more hit is refused", record("/api/apicalls", "hits", 1).status_code == 402)

other = record("/api/usageevents", "hits", 50)
check("the same kind in the other table is not limited there", other.status_code == 201 and other.json().get("allowed") is None, other.text)
calls = record("/api/apicalls", "api_calls", 500)
check("api_calls in ApiCall are not limited there (the limit belongs to UsageEvent)", calls.status_code == 201 and calls.json().get("allowed") is None, calls.text)
inside = record("/api/usageevents", "api_calls", 100)
check("api_calls in UsageEvent are judged against the plan", inside.status_code == 201 and inside.json().get("allowed") == 100 and inside.json().get("used") == 100, inside.text)
check("one more api_call in UsageEvent is refused", record("/api/usageevents", "api_calls", 1).status_code == 402)

info = client.get("/api/subscription", headers=grace).json()
check("the view counts hits from ApiCall only", (limit_of(info, "hits") or {}).get("used") == 5, info)
check("the view counts api_calls from UsageEvent only", (limit_of(info, "api_calls") or {}).get("used") == 100, info)

print("failures: %d" % len(failures))
sys.exit(1 if failures else 0)
