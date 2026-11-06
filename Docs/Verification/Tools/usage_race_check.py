"""Plays UsageRefuse=true with many requests at the same moment against a running ASP.NET, Python or Rust API over a scratch copy of Schemas/usage_decimal.sql (or usage_two.sql).

python usage_race_check.py <base url> <sqlite file> [requests]

The API is generated like the one of usage_refuse_check.py. The Free plan allows 100 api_calls a month; the script sends `requests` (default 30) events of 10 units for tenant 2 at once. Exactly ten fit: ten
must be written (201), the rest refused (402), none may fail otherwise, and the ledger must hold exactly 100 units. Without the lock between the check and the write several requests read the same month
and more than ten are written. The script writes test password hashes into the copy and empties the ledger."""
import base64
import concurrent.futures
import hashlib
import sqlite3
import sys

import httpx

base, db = sys.argv[1], sys.argv[2]
count = int(sys.argv[3]) if len(sys.argv) > 3 else 30


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


connection = sqlite3.connect(db)
connection.execute("UPDATE Employee SET PasswordHash = ? WHERE Login = 'pat'", (hash_password("secret-pass"),))
connection.execute("DELETE FROM UsageEvent")
connection.execute("UPDATE PlanLimit SET LimitValue = 100 WHERE PlanId = 1 AND LimitKey = 'api_calls'")
connection.commit()

reply = httpx.post(base + "/api/auth/login", json={"userName": "pat", "password": "secret-pass"}, timeout=60)
pat = {"Authorization": "Bearer " + reply.json()["token"]}
failures = []


def check(label, condition, detail=""):
    print(("ok   " if condition else "FAIL ") + label + ("" if condition else "  " + str(detail)[:400]))
    if not condition:
        failures.append(label)


def record(_):
    with httpx.Client(base_url=base, timeout=120) as client:
        return client.post("/api/usageevents", headers=pat, json={"tenantId": 2, "kind": "api_calls", "quantity": 10}).status_code


with concurrent.futures.ThreadPoolExecutor(max_workers=count) as pool:
    codes = list(pool.map(record, range(count)))

written = codes.count(201)
stored = connection.execute("SELECT COALESCE(SUM(Quantity), 0) FROM UsageEvent WHERE TenantId = 2 AND Kind = 'api_calls'").fetchone()[0]
check("exactly ten events fit the limit", written == 10, f"{written} written: {sorted(set(codes))}")
check("every other request is a 402", codes.count(402) == count - 10, sorted(codes))
check("nothing fails any other way", set(codes) <= {201, 402}, sorted(set(codes)))
check("the ledger holds exactly the limit", stored == 100, stored)

print("failures: %d" % len(failures))
sys.exit(1 if failures else 0)
