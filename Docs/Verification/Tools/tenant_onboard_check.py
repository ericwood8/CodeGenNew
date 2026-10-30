"""Plays POST <tenants>/onboard against a running multi-tenant API over a scratch copy of Schemas/tenants_bypass.sql.

python tenant_onboard_check.py <base url> <sqlite file>

The API is a generated one (ASP.NET, Python or Rust) with the settings of tenant_bypass_check.py. The script signs in as the platform administrator pat and as ada (an administrator of tenant 1), whose
test password hashes tenant_bypass_check.py writes into the copy (run that script first), then checks the refusals, the first administrator and an onboarding without one. It adds rows to the copy."""
import sqlite3
import sys

import httpx

base, db = sys.argv[1], sys.argv[2]
client = httpx.Client(base_url=base, timeout=30)


def sign_in(name, tenant=None):
    body = {"userName": name, "password": "secret-pass"}
    if tenant is not None:
        body["tenantId"] = tenant
    return {"Authorization": "Bearer " + client.post("/api/auth/login", json=body).json()["token"]}


pat, ada = sign_in("pat"), sign_in("ada", 1)
tenant = {"tenantId": 0, "name": "Onboarded Inc", "slug": "onboarded", "isActive": True, "createdOn": "2026-10-30T00:00:00"}
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


r = client.post("/api/tenants/onboard", headers=ada, json={"tenant": tenant, "adminId": 1})
check("a tenant member cannot onboard", r.status_code == 403 and r.json()["type"].endswith("tenant-onboarding"), (r.status_code, r.text))
r = client.post("/api/tenants/onboard", headers=pat, json={"tenant": tenant, "adminId": 999})
check("an unknown first administrator is a 400 and no tenant is saved", r.status_code == 400 and sqlite3.connect(db).execute("SELECT COUNT(*) FROM Tenant WHERE Slug = 'onboarded'").fetchone()[0] == 0, (r.status_code, r.text))
r = client.post("/api/tenants/onboard", headers=pat, json={"tenant": tenant, "adminId": 1})
check("a platform administrator onboards a tenant", r.status_code == 201, (r.status_code, r.text))
key = r.json().get("tenantId")
rows = sqlite3.connect(db).execute("SELECT Role FROM EmployeeTenant WHERE EmployeeId = 1 AND TenantId = ?", (key,)).fetchall()
check("the first administrator is attached with the administrator role", rows == [("Administrator",)], rows)
r = client.post("/api/tenants/onboard", headers=pat, json={"tenant": {**tenant, "name": "No Admin", "slug": "noadmin"}})
check("a tenant can be onboarded without a first administrator", r.status_code == 201, (r.status_code, r.text))
print("\nFAILED: " + ", ".join(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
