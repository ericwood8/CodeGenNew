"""Plays the delete of a person who belongs to two tenants against a running multi-tenant API over a scratch copy of Schemas/tenants_bypass.sql.

python tenant_leave_check.py <base url> <sqlite file>

The API is a generated one (ASP.NET) with the settings of tenant_bypass_check.py. The script writes test password hashes into the copy's Employee rows and signs in as ada (an administrator of tenant 1) and
grace (an administrator of tenant 2). linus belongs to both tenants: deleting him in tenant 1 only removes that membership, deleting him in tenant 2 removes the person. It deletes rows of the copy."""
import base64
import hashlib
import sqlite3
import sys

import httpx

base, db = sys.argv[1], sys.argv[2]
client = httpx.Client(base_url=base, timeout=30)
failures = []


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


connection = sqlite3.connect(db)
connection.execute("UPDATE Employee SET PasswordHash = ?", (hash_password("secret-pass"),))
connection.commit()
connection.close()


def sign_in(name, tenant):
    body = {"userName": name, "password": "secret-pass", "tenantId": tenant}
    return {"Authorization": "Bearer " + client.post("/api/auth/login", json=body).json()["token"]}


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def memberships(person):
    return sqlite3.connect(db).execute("SELECT TenantId FROM EmployeeTenant WHERE EmployeeId = ? ORDER BY TenantId", (person,)).fetchall()


def people(person):
    return sqlite3.connect(db).execute("SELECT COUNT(*) FROM Employee WHERE EmployeeId = ?", (person,)).fetchone()[0]


ada, grace = sign_in("ada", 1), sign_in("grace", 2)
check("linus belongs to both tenants at the start", memberships(4) == [(1,), (2,)], memberships(4))
r = client.delete("/api/employees/4", headers=ada)
check("an administrator removes a person who belongs to another tenant too", r.status_code in (200, 204), (r.status_code, r.text))
check("only the membership of the tenant goes", memberships(4) == [(2,)] and people(4) == 1, (memberships(4), people(4)))
r = client.get("/api/employees/4", headers=ada)
check("the person is gone for the tenant they left", r.status_code == 404, (r.status_code, r.text))
r = client.get("/api/employees/4", headers=grace)
check("the person is still there for the other tenant", r.status_code == 200, (r.status_code, r.text))
r = client.delete("/api/employees/4", headers=grace)
check("the last tenant to remove the person removes the row", r.status_code in (200, 204) and memberships(4) == [] and people(4) == 0, (r.status_code, r.text, memberships(4), people(4)))
print("\nFAILED: " + ", ".join(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
