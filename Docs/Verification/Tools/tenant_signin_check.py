"""Plays the tenant drop-down of the sign-in screen against a running multi-tenant API over a scratch copy of Schemas/tenants_signin.sql.

python tenant_signin_check.py <base url> <sqlite file>

The API is a generated one (ASP.NET, Python or Rust) with TenantTable=Tenant, TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role, UserTable=Employee, UserNameColumn=Login,
UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator and ApiAuth. It checks that GET /api/auth/sign-in-tenants answers without a sign-in with the names of the tenants that are
active and not marked deleted, by name, and that a sign-in with the id of a listed tenant works. It writes test password hashes into the scratch copy's Employee rows."""
import base64
import hashlib
import sqlite3
import sys

import httpx

base = sys.argv[1]
db = sys.argv[2]
client = httpx.Client(base_url=base, timeout=30)
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


connection = sqlite3.connect(db)
for login in ("ada", "grace"):
    connection.execute("UPDATE Employee SET PasswordHash = ? WHERE Login = ?", (hash_password("secret-pass"), login))
connection.commit()
connection.close()

r = client.get("/api/auth/sign-in-tenants")
check("the list needs no sign-in", r.status_code == 200, (r.status_code, r.text))
names = [t["name"] for t in r.json()] if r.status_code == 200 else []
check("it lists the active tenants that are not deleted, by name", names == ["Contoso Services", "Northwind Traders"], names)
check("an entry is only an id and a name", all(set(t) == {"id", "name"} for t in r.json()), r.text)
ids = {t["name"]: t["id"] for t in r.json()}


def login(user, tenant=None):
    body = {"userName": user, "password": "secret-pass"}
    if tenant is not None:
        body["tenantId"] = tenant
    return client.post("/api/auth/login", json=body)


r = login("ada", ids["Contoso Services"])
check("a sign-in with the id of a listed tenant works", r.status_code == 200 and r.json()["user"].get("tenantId") == ids["Contoso Services"], (r.status_code, r.text))
r = login("ada")
check("a sign-in with no tenant works in the first one", r.status_code == 200 and r.json()["user"].get("tenantId") == ids["Northwind Traders"], (r.status_code, r.text))
r = login("grace", ids["Northwind Traders"])
check("a person who is not a member of the picked tenant is refused", r.status_code == 403, (r.status_code, r.text))

print("\nFAILED: " + ", ".join(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
