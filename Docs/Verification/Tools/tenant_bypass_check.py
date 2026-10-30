"""Plays what the query filter of a multi-tenant API does not reach (the dashboard, a junction table, the row approvals and the host name of a tenant) against a running API.

python tenant_bypass_check.py <base url> <sqlite file> [dashboard,junction,approvals,host]

The API is a generated one over a scratch copy of a SQLite database made from Schemas/tenants_bypass.sql (tenants.sql plus a Tag table, a ProjectTag junction table and a
ProjectApproval side table, with the rows the checks expect); the API runs with TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role, UserTable=Employee, UserRoleColumn=IsManager, RowApprovals=Project and Dashboard=true
(the host section also needs TenantHostColumn=Slug). The script writes test password hashes into the scratch
copy's Employee rows and adds and removes link rows; never point it at a database that matters."""
import base64
import hashlib
import sqlite3
import sys

import httpx

base = sys.argv[1]
db = sys.argv[2]
sections = sys.argv[3].split(",") if len(sys.argv) > 3 else ["dashboard", "junction", "approvals", "host"]


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


connection = sqlite3.connect(db)
for login in ("ada", "grace", "pat", "linus"):
    connection.execute("UPDATE Employee SET PasswordHash = ? WHERE Login = ?", (hash_password("secret-pass"), login))
connection.commit()
connection.close()

client = httpx.Client(base_url=base, timeout=60)
failures = []


def check(label, condition, detail=""):
    print(("ok   " if condition else "FAIL ") + label + ("" if condition else "  " + str(detail)[:400]))
    if not condition:
        failures.append(label)


def sign_in(name, tenant=None, host=None):
    body = {"userName": name, "password": "secret-pass"}
    if tenant is not None:
        body["tenantId"] = tenant
    return client.post("/api/auth/login", json=body, headers={"Host": host} if host else {})


def auth(response, host=None):
    headers = {"Authorization": "Bearer " + response.json()["token"]}
    if host:
        headers["Host"] = host
    return headers


def widget(headers, widget_id):
    found = [w for w in client.get("/api/dashboard", headers=headers).json()["widgets"] if w["id"] == widget_id]
    return found[0] if found else None


ada1 = auth(sign_in("ada"))
ada2 = auth(sign_in("ada", 2))
pat = auth(sign_in("pat"))

if "dashboard" in sections:
    w1 = widget(ada1, "project-child-count")
    w2 = widget(ada2, "project-child-count")
    values1 = {p["label"]: p["value"] for p in w1["points"]} if w1 else None
    values2 = {p["label"]: p["value"] for p in w2["points"]} if w2 else None
    check("dashboard counts the rows of tenant 1 only", values1 == {"Projects": 2.0, "Project Approvals": 1.0}, values1)
    check("dashboard counts the rows of tenant 2 only (the cache is per tenant)", values2 == {"Projects": 1.0, "Project Approvals": 1.0}, values2)
    wp = widget(pat, "project-child-count")
    check("a caller in no tenant counts nothing", wp is not None and all(p["value"] == 0 for p in wp["points"]), wp)

if "junction" in sections:
    r = client.get("/api/projecttags/junction/1", headers=ada1)
    check("junction list of an own row answers", r.status_code == 200, (r.status_code, r.text))
    if r.status_code == 200:
        check("junction list holds only the tenant's tags", [i["TargetId"] if "TargetId" in i else i.get("targetId") for i in r.json()] == [1], r.text)
    check("junction list of another tenant's row is 404", client.get("/api/projecttags/junction/3", headers=ada1).status_code == 404)
    check("link to another tenant's project is 404", client.post("/api/projecttags/junction/link", headers=ada1, json={"anchorId": 3, "targetId": 1}).status_code == 404)
    check("link to another tenant's tag is 404", client.post("/api/projecttags/junction/link", headers=ada1, json={"anchorId": 1, "targetId": 2}).status_code == 404)
    check("unlink of another tenant's link is 404", client.post("/api/projecttags/junction/unlink", headers=ada1, json={"anchorId": 3, "targetId": 2}).status_code == 404)
    check("link of own rows works", client.post("/api/projecttags/junction/link", headers=ada1, json={"anchorId": 2, "targetId": 1}).status_code == 200)
    check("unlink of own rows works", client.post("/api/projecttags/junction/unlink", headers=ada1, json={"anchorId": 2, "targetId": 1}).status_code == 200)
    check("another tenant's link row is still there", sqlite3.connect(db).execute("SELECT COUNT(*) FROM ProjectTag WHERE ProjectId = 3 AND TagId = 2").fetchone()[0] == 1)

if "approvals" in sections:
    linus = auth(sign_in("linus"))
    waiting = client.get("/api/projects/toapprove", headers=ada1)
    check("to-approve holds the cycles of the tenant's rows only", waiting.status_code == 200 and [c["rowId"] for c in waiting.json()] == [1], waiting.text)
    _h = client.get("/api/projects/3/approvals", headers=ada1)
    check("history of another tenant's row shows nothing", _h.status_code == 404 or (_h.status_code == 200 and _h.json() == []), _h.text)
    check("history of an own row answers", client.get("/api/projects/1/approvals", headers=ada1).status_code == 200)
    decide = client.post("/api/projects/3/approve", headers=ada1, json={"note": "x"})
    check("approving another tenant's row is refused", decide.status_code in (404, 409), (decide.status_code, decide.text))
    check("another tenant's cycle is still pending", sqlite3.connect(db).execute("SELECT ApprovalStatusId FROM ProjectApproval WHERE ProjectId = 3").fetchone()[0] == 1)
    request = client.post("/api/projects/3/request", headers=linus, json={"reason": "x"})
    check("requesting another tenant's row is 404", request.status_code == 404, (request.status_code, request.text))
    approved = client.post("/api/projects/1/approve", headers=ada1, json={"note": "ok"})
    check("approving an own row works", approved.status_code == 200 and approved.json()["status"] == "Approved", approved.text)

if "host" in sections:
    own = "northwind.example.test"
    other = "contoso.example.test"
    r_own = client.get("/api/projects", headers={**ada1, "Host": own})
    r_other = client.get("/api/projects", headers={**ada1, "Host": other})
    r_plain = client.get("/api/projects", headers={**ada1, "Host": "localhost:5080"})
    r_unknown = client.get("/api/projects", headers={**ada1, "Host": "nobody.example.test"})
    check("a token works on its own tenant's host", r_own.status_code == 200, r_own.text)
    check("a token is refused on another tenant's host", r_other.status_code == 403 and r_other.json()["type"] == "/problems/tenant-host", (r_other.status_code, r_other.text))
    check("localhost names no tenant", r_plain.status_code == 200, r_plain.text)
    check("a host that names no tenant is not restricted", r_unknown.status_code == 200, r_unknown.text)
    login_host = sign_in("ada", host=other)
    check("a sign-in on a tenant's host works in that tenant", login_host.status_code == 200 and login_host.json()["user"]["tenantId"] == 2, login_host.text)
    check("a sign-in naming another tenant than the host is refused", sign_in("ada", tenant=1, host=other).status_code == 403)
    check("a person who is not a member is refused on the host", sign_in("grace", host=own).status_code == 403)
    check("a platform administrator is refused on a tenant's host", sign_in("pat", host=own).status_code == 403)
    switch = client.post("/api/auth/switch-tenant", headers={**ada1, "Host": own}, json={"tenantId": 2})
    check("a switch to another tenant is refused on a tenant's host", switch.status_code == 403, (switch.status_code, switch.text))
    check("a platform token works on a tenant's host", client.get("/api/tenants", headers={**pat, "Host": own}).status_code == 200)

print("\nFAILED: " + ", ".join(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
