"""Plays feature flags against a running multi-tenant ASP.NET API over a scratch copy of Schemas/features.sql.

python features_check.py <base url> <sqlite file>

The API is a generated one with ApiAuth, TenantTable=Tenant, TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager,
UserAdminColumn=IsAdministrator, UserOwnerColumn=EmployeeId and FeatureTables=Feature,FeatureOverride. The script writes test password hashes into the copy's Employee rows and changes its
FeatureOverride rows; never point it at a database that matters. It signs in as ada (administrator of tenant 1, an employee of tenant 2), grace (administrator of tenant 2) and pat (a platform
administrator) and checks the defaults, a tenant override, a person override that wins over it, removing an override, and who may change what."""
import base64
import hashlib
import os
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
connection.execute("DELETE FROM FeatureOverride")
connection.execute("UPDATE Employee SET IsAdministrator = 1 WHERE Login = 'pat'")
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


def flags(headers):
    reply = client.get("/api/features", headers=headers)
    return reply.json() if reply.status_code == 200 else {"status": reply.status_code}


def put(headers, name, body):
    return client.put("/api/features/" + name, headers=headers, json=body)


ada1 = sign_in("ada", 1)
ada2 = sign_in("ada", 2)
grace2 = sign_in("grace", 2)
pat = sign_in("pat")

check("a signed-out caller is refused", client.get("/api/features").status_code == 401)
seen = flags(ada1)
check("the defaults come back by name", seen.get("new-invoice-screen") is False and seen.get("csv-export") is True, seen)

reply = put(ada1, "new-invoice-screen", {"enabled": True})
check("a tenant's administrator switches a flag on for the tenant", reply.status_code == 200 and reply.json().get("new-invoice-screen") is True, reply.text)
check("the tenant sees it on", flags(ada1).get("new-invoice-screen") is True)
check("another tenant still sees the default", flags(grace2).get("new-invoice-screen") is False, flags(grace2))
check("the same person in another tenant sees the default", flags(ada2).get("new-invoice-screen") is False, flags(ada2))
check("a second switch updates the row, not a second one", put(ada1, "new-invoice-screen", {"enabled": True}).status_code == 200
      and connection.execute("SELECT COUNT(*) FROM FeatureOverride WHERE TenantId = 1").fetchone()[0] == 1)

check("an employee who is not an administrator is refused", put(ada2, "csv-export", {"enabled": False}).status_code == 403)
check("an unknown flag is a 404", put(ada1, "nope", {"enabled": True}).status_code == 404)
check("a tenant's administrator may not name another tenant", put(ada1, "csv-export", {"enabled": False, "tenantId": 2}).status_code == 403)
check("a tenant's administrator may not name a person", put(ada1, "csv-export", {"enabled": False, "personId": 2}).status_code == 403)

reply = put(pat, "csv-export", {"enabled": False, "tenantId": 2})
check("a platform administrator switches a flag for a named tenant", reply.status_code == 200 and flags(grace2).get("csv-export") is False, reply.text)
check("a platform administrator must name a tenant or a person", put(pat, "csv-export", {"enabled": False}).status_code == 400)
check("not both at once", put(pat, "csv-export", {"enabled": False, "tenantId": 1, "personId": 1}).status_code == 400)
check("an unknown tenant is a 404", put(pat, "csv-export", {"enabled": False, "tenantId": 99}).status_code == 404)
check("an unknown person is a 404", put(pat, "csv-export", {"enabled": False, "personId": 99}).status_code == 404)

reply = put(pat, "csv-export", {"enabled": True, "personId": 2})
check("a platform administrator switches a flag for a person", reply.status_code == 200, reply.text)
check("the person's override beats the tenant's", flags(grace2).get("csv-export") is True, flags(grace2))
check("the person's override is the person's only", flags(ada2).get("csv-export") is False, flags(ada2))

reply = put(pat, "csv-export", {"enabled": None, "personId": 2})
check("a null removes the person's override", reply.status_code == 200 and flags(grace2).get("csv-export") is False, reply.text)
reply = put(ada1, "new-invoice-screen", {"enabled": None})
check("a null removes the tenant's override", reply.status_code == 200 and flags(ada1).get("new-invoice-screen") is False
      and connection.execute("SELECT COUNT(*) FROM FeatureOverride WHERE TenantId = 1").fetchone()[0] == 0, reply.text)
check("the flag tables have no ordinary API", client.get("/api/feature", headers=pat).status_code == 404 and client.get("/api/featureoverride", headers=pat).status_code == 404)

print("\n%d failed" % len(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
