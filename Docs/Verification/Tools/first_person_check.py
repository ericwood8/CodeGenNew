"""Plays the first administrator and the "set password" route against a running API over a scratch copy of Schemas/tenants_signin.sql.

python first_person_check.py <base url> <sqlite file>

The API is a generated one with TenantTable=Tenant, TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager,
UserAdminColumn=IsAdministrator and ApiAuth. It empties the Employee and EmployeeTenant tables of the copy, then checks: GET /api/auth/first-person says the table is open; a blank name and a short
password are refused (400) and add nobody; POST /api/auth/first-person adds the administrator (204) and joins the first active tenant; the table is no longer open and a second POST is 409; the new
person signs in as an administrator of that tenant; PUT /api/auth/password changes the person's own password (the old one stops working) and an administrator's change of another person's password works,
while a person who is not an administrator is refused (403) for anyone but themself, an unknown person is 404 and a short password is 400. It then plays the members of the tenant
(GET, GET candidates, POST, PUT the role, DELETE; only an administrator, no duplicate, no removing yourself). It writes test people into the copy."""
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


def login(name, password):
    return client.post("/api/auth/login", json={"userName": name, "password": password})


def bearer(response):
    return {"Authorization": "Bearer " + response.json()["token"]}


connection = sqlite3.connect(db)
connection.execute("delete from EmployeeTenant")
connection.execute("delete from Employee")
connection.commit()

state = client.get("/api/auth/first-person")
check("an empty user table is open for its first person", state.status_code == 200 and state.json().get("open") is True, state.text)

check("a blank user name is refused", client.post("/api/auth/first-person", json={"userName": " ", "password": "long-enough-1"}).status_code == 400)
check("a short password is refused", client.post("/api/auth/first-person", json={"userName": "owner", "password": "short"}).status_code == 400)
check("nobody was added by the refused tries", connection.execute("select count(*) from Employee").fetchone()[0] == 0)

created = client.post("/api/auth/first-person", json={"userName": "owner", "password": "long-enough-1"})
check("the first person is created", created.status_code == 204, created.text)
row = connection.execute("select Login, IsAdministrator, IsManager, FullName from Employee").fetchone()
check("the person is an administrator and a manager", row is not None and row[1] == 1 and row[2] == 1 and row[0] == "owner", row)
membership = connection.execute("select TenantId, Role from EmployeeTenant").fetchall()
check("the person joined the first active tenant as its administrator", membership == [(1, "Administrator")], membership)
check("the table is no longer open", client.get("/api/auth/first-person").json().get("open") is False)
check("a second person cannot claim it", client.post("/api/auth/first-person", json={"userName": "second", "password": "long-enough-1"}).status_code == 409)
check("still one person", connection.execute("select count(*) from Employee").fetchone()[0] == 1)

signed = login("owner", "long-enough-1")
check("the first person signs in", signed.status_code == 200, signed.text)
user = signed.json()["user"] if signed.status_code == 200 else {}
check("as an administrator of the first tenant", user.get("role") == "Administrator" and user.get("tenantId") == 1, user)
admin = bearer(signed)

check("a short new password is refused", client.put("/api/auth/password", headers=admin, json={"userName": "owner", "password": "short"}).status_code == 400)
check("an unknown person is 404", client.put("/api/auth/password", headers=admin, json={"userName": "nobody", "password": "long-enough-2"}).status_code == 404)
check("the password change needs a sign-in", client.put("/api/auth/password", json={"userName": "owner", "password": "long-enough-2"}).status_code == 401)
changed = client.put("/api/auth/password", headers=admin, json={"userName": "owner", "password": "long-enough-2"})
check("a person changes their own password", changed.status_code == 204, changed.text)
check("the old password stops working", login("owner", "long-enough-1").status_code == 401)
check("the new password works", login("owner", "long-enough-2").status_code == 200)

connection.execute("insert into Employee (FullName, Login, PasswordHash, IsManager, IsAdministrator) values ('Grace Hopper', 'grace', ?, 0, 0)", (hash_password("grace-pass-1"),))
grace = connection.execute("select EmployeeId from Employee where Login = 'grace'").fetchone()[0]
connection.execute("insert into EmployeeTenant (EmployeeId, TenantId, Role) values (?, 1, NULL)", (grace,))
connection.commit()
check("an administrator sets another person's password", client.put("/api/auth/password", headers=bearer(login("owner", "long-enough-2")), json={"userName": "grace", "password": "grace-pass-2"}).status_code == 204)
check("the other person signs in with it", login("grace", "grace-pass-2").status_code == 200)

member = bearer(login("grace", "grace-pass-2"))
refused = client.put("/api/auth/password", headers=member, json={"userName": "owner", "password": "long-enough-3"})
check("a person who is not an administrator cannot set another's password", refused.status_code == 403, refused.text)
check("the administrator's password is unchanged", login("owner", "long-enough-2").status_code == 200)
check("a person who is not an administrator sets their own", client.put("/api/auth/password", headers=member, json={"userName": "grace", "password": "grace-pass-3"}).status_code == 204)
check("and signs in with it", login("grace", "grace-pass-3").status_code == 200)

# ---- the members of the tenant
connection.execute("insert into Employee (FullName, Login, PasswordHash, IsManager, IsAdministrator) values ('Alan Turing', 'alan', ?, 0, 0)", (hash_password("alan-pass-1"),))
connection.commit()
alan = connection.execute("select EmployeeId from Employee where Login = 'alan'").fetchone()[0]
owner_id = connection.execute("select EmployeeId from Employee where Login = 'owner'").fetchone()[0]
admin = bearer(login("owner", "long-enough-2"))
listed = client.get("/api/auth/members", headers=admin)
people = {m["login"]: m for m in listed.json()} if listed.status_code == 200 else {}
check("an administrator lists the members of the tenant", listed.status_code == 200 and set(people) == {"owner", "grace"}, listed.text)
check("a member has the person text and the role", people.get("owner", {}).get("person") == "owner (owner)" and people.get("owner", {}).get("role") == "Administrator", people.get("owner"))
check("a member with no role has none", people.get("grace", {}).get("role") is None, people.get("grace"))
candidates = client.get("/api/auth/members/candidates", headers=admin).json()
check("the people outside the tenant are offered", [c["name"] for c in candidates] == ["Alan Turing (alan)"], candidates)
check("a person who is not an administrator cannot list the members", client.get("/api/auth/members", headers=member).status_code == 403)
check("or add one", client.post("/api/auth/members", headers=member, json={"personId": alan, "role": "Employee"}).status_code == 403)
check("a member is added with a role", client.post("/api/auth/members", headers=admin, json={"personId": alan, "role": "Employee"}).status_code == 204)
check("the same person twice is 409", client.post("/api/auth/members", headers=admin, json={"personId": alan, "role": "Employee"}).status_code == 409)
check("an unknown person is 404", client.post("/api/auth/members", headers=admin, json={"personId": 9999, "role": "Employee"}).status_code == 404)
check("the role is kept", connection.execute("select Role from EmployeeTenant where EmployeeId = ? and TenantId = 1", (alan,)).fetchone() == ("Employee",))
check("the role is changed", client.put(f"/api/auth/members/{alan}", headers=admin, json={"role": "Auditor"}).status_code == 204)
check("and saved", connection.execute("select Role from EmployeeTenant where EmployeeId = ? and TenantId = 1", (alan,)).fetchone() == ("Auditor",))
check("an empty role is saved as none", client.put(f"/api/auth/members/{alan}", headers=admin, json={"role": ""}).status_code == 204 and connection.execute("select Role from EmployeeTenant where EmployeeId = ? and TenantId = 1", (alan,)).fetchone() == (None,))
check("a person who is not a member has no role to change", client.put("/api/auth/members/9999", headers=admin, json={"role": "x"}).status_code == 404)
check("a person cannot remove themself", client.delete(f"/api/auth/members/{owner_id}", headers=admin).status_code == 403)
check("a member is removed", client.delete(f"/api/auth/members/{alan}", headers=admin).status_code == 204)
check("and is gone from the tenant but still a person", connection.execute("select count(*) from EmployeeTenant where EmployeeId = ?", (alan,)).fetchone()[0] == 0 and connection.execute("select count(*) from Employee where EmployeeId = ?", (alan,)).fetchone()[0] == 1)
check("removing a non-member is 404", client.delete(f"/api/auth/members/{alan}", headers=admin).status_code == 404)

connection.close()
if failures:
    print(f"\n{len(failures)} failed")
    sys.exit(1)
print("\nall passed")
