"""Plays two usage tables, each with its own monthly totals, and the scheduled roll-up, against a running ASP.NET, Python or Rust API over a scratch copy of Schemas/usage_two.sql.

python usage_two_check.py <base url> <sqlite file> [scheduled <seconds>]

The API is generated like the one of usage_check.py with UsageTables=UsageEvent,ApiCall and UsageRollup=UsageMonthly,ApiCallMonthly. Without the third argument the script rolls both ledgers up through
their routes and checks that each total went to its own monthly table and that each summary reads its own. With `scheduled <seconds>` it only adds old events, waits, and checks that the API rolled them
up by itself: the API must be started with UsageRollupHours set, and its generated schedule changed to run every few seconds (the hours of the text become seconds), since the interval is in hours."""
import base64
import hashlib
import sqlite3
import sys
import time

import httpx

base, db = sys.argv[1], sys.argv[2]
scheduled = len(sys.argv) > 4 and sys.argv[3] == "scheduled"


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


connection = sqlite3.connect(db)
for login in ("ada", "grace", "pat"):
    connection.execute("UPDATE Employee SET PasswordHash = ? WHERE Login = ?", (hash_password("secret-pass"), login))
for table in ("UsageEvent", "UsageMonthly", "ApiCall", "ApiCallMonthly"):
    connection.execute(f"DELETE FROM {table}")
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


def line(rows, kind, month):
    return next((r for r in rows if r["kind"] == kind and r["month"] == month), None)


def count(sql):
    return connection.execute(sql).fetchone()[0]


grace, pat = sign_in("grace", 2), sign_in("pat")
connection.execute("INSERT INTO UsageEvent (TenantId, Kind, Quantity, OccurredOn) VALUES (2, 'api_calls', 10, '2019-03-10 12:00:00'), (2, 'api_calls', 20, '2019-03-20 12:00:00')")
connection.execute("INSERT INTO ApiCall (TenantId, Kind, Quantity, OccurredOn) VALUES (2, 'hits', 3, '2019-03-10 12:00:00'), (2, 'hits', 4, '2019-03-20 12:00:00')")
connection.commit()

if scheduled:
    seconds = float(sys.argv[4])
    time.sleep(seconds)
    check("the API rolled the first ledger up by itself", count("SELECT COUNT(*) FROM UsageEvent") == 0 and count("SELECT Quantity FROM UsageMonthly WHERE Kind = 'api_calls'") == 30)
    check("the API rolled the second ledger up by itself", count("SELECT COUNT(*) FROM ApiCall") == 0 and count("SELECT Quantity FROM ApiCallMonthly WHERE Kind = 'hits'") == 7)
    check("the summary still adds the rolled month", (line(client.get("/api/usageevents", headers=grace).json(), "api_calls", "2019-03") or {}).get("quantity") == 30)
else:
    check("an administrator of a tenant may not roll the second ledger up", client.post("/api/apicalls/roll-up?before=2019-05-01", headers=grace).status_code == 403)
    first = client.post("/api/usageevents/roll-up?before=2019-05-01", headers=pat)
    second = client.post("/api/apicalls/roll-up?before=2019-05-01", headers=pat)
    check("the first ledger rolls up its two events", first.status_code == 200 and first.json().get("events") == 2, first.text)
    check("the second ledger rolls up its two events", second.status_code == 200 and second.json().get("events") == 2, second.text)
    check("the first monthly table holds the first ledger's units", count("SELECT Quantity FROM UsageMonthly WHERE TenantId = 2 AND Kind = 'api_calls'") == 30)
    check("the second monthly table holds the second ledger's units", count("SELECT Quantity FROM ApiCallMonthly WHERE TenantId = 2 AND Kind = 'hits'") == 7)
    check("neither monthly table holds the other's kind", count("SELECT COUNT(*) FROM UsageMonthly WHERE Kind = 'hits'") == 0 and count("SELECT COUNT(*) FROM ApiCallMonthly WHERE Kind = 'api_calls'") == 0)
    check("the old events are gone from both ledgers", count("SELECT COUNT(*) FROM UsageEvent") + count("SELECT COUNT(*) FROM ApiCall") == 0)
    one = client.get("/api/usageevents", headers=grace).json()
    two = client.get("/api/apicalls", headers=grace).json()
    check("the first summary reads its own monthly table", (line(one, "api_calls", "2019-03") or {}).get("quantity") == 30 and line(one, "hits", "2019-03") is None, one)
    check("the second summary reads its own monthly table", (line(two, "hits", "2019-03") or {}).get("quantity") == 7 and (line(two, "hits", "2019-03") or {}).get("events") == 0 and line(two, "api_calls", "2019-03") is None, two)
    made = client.post("/api/apicalls", headers=pat, json={"tenantId": 2, "kind": "hits", "quantity": 2})
    check("the second ledger records an event of its own", made.status_code == 201 and made.json().get("used") == 2, made.text)
    check("the event is in the second summary only", any(r["kind"] == "hits" and r["events"] == 1 for r in client.get("/api/apicalls", headers=grace).json())
          and not any(r["kind"] == "hits" for r in client.get("/api/usageevents", headers=grace).json()))

print("failures: %d" % len(failures))
sys.exit(1 if failures else 0)
