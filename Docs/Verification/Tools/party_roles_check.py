"""Plays WriteRoles on the party routes of a running ASP.NET, Python or Rust API over a scratch copy of Schemas/parties_auth.sql.

python party_roles_check.py <base url>

The API is generated with PartyTables=Party:PartyIdentifier, ApiAuth=true, UserTable=Employee, UserNameColumn=Login, UserRoleColumn=IsManager, UserAdminColumn=IsAdministrator
and WriteRoles=Manager. Before the run the copy has the first administrator (POST /api/auth/first-person) and two more people, with IsManager 0 and 1, whose logins are in
ROLE_READER and ROLE_WRITER. The passwords come from the environment (ROLE_ADMIN_PASSWORD, ROLE_READER_PASSWORD, ROLE_WRITER_PASSWORD, ROLE_ADMIN_USER); the administrator sets the
two people's passwords. A person who may not write can read and search; every write, on the party and on its identifiers, is 403. The script adds rows and removes them again."""
import os
import sys
import uuid

import httpx

base = sys.argv[1]
client = httpx.Client(base_url=base, timeout=30)
failures = []
tag = uuid.uuid4().hex[:8].upper()


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def sign_in(name, password):
    login = client.post("/api/auth/login", json={"userName": name, "password": password})
    login.raise_for_status()
    body = login.json()
    return {"Authorization": "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)}


admin_name = os.environ["ROLE_ADMIN_USER"]
admin_password = os.environ["ROLE_ADMIN_PASSWORD"]
if client.get("/api/auth/first-person").json().get("open"):
    client.post("/api/auth/first-person", json={"userName": admin_name, "password": admin_password}).raise_for_status()
admin = sign_in(admin_name, admin_password)
people = {}
for kind in ("READER", "WRITER"):
    name, password = os.environ["ROLE_" + kind], os.environ["ROLE_" + kind + "_PASSWORD"]
    client.put("/api/auth/password", headers=admin, json={"userName": name, "password": password}).raise_for_status()
    people[kind] = sign_in(name, password)
reader, writer = people["READER"], people["WRITER"]

check("the routes need a sign-in", client.get("/api/parties").status_code == 401)
made = client.post("/api/parties", headers=admin, json={"partyId": 0, "legalName": "Role " + tag, "tradingName": None, "city": None, "phone": None, "notes": None})
check("an administrator adds a party", made.status_code == 201, made.text)
party = made.json()["partyId"]
found = client.post("/api/partyidentifiers", headers=admin, json={"partyIdentifierId": 0, "partyId": party, "scheme": "VAT", "value": "RL" + tag, "issuedBy": None, "notes": None})
check("an administrator adds an identifier", found.status_code == 201, found.text)
identifier = found.json()["partyIdentifierId"]

check("a reader lists the parties", reader and client.get("/api/parties", headers=reader).status_code == 200)
check("a reader searches by identifier", client.get("/api/parties/by-identifier", headers=reader, params={"value": "RL" + tag}).status_code == 200)
for label, response in (
    ("a reader may not add a party", client.post("/api/parties", headers=reader, json={"partyId": 0, "legalName": "No " + tag})),
    ("a reader may not change a party", client.put(f"/api/parties/{party}", headers=reader, json={"partyId": party, "legalName": "No " + tag})),
    ("a reader may not delete a party", client.delete(f"/api/parties/{party}", headers=reader)),
    ("a reader may not add an identifier", client.post("/api/partyidentifiers", headers=reader, json={"partyIdentifierId": 0, "partyId": party, "scheme": "DUNS", "value": "RL" + tag})),
    ("a reader may not change an identifier", client.put(f"/api/partyidentifiers/{identifier}", headers=reader, json={"partyIdentifierId": identifier, "partyId": party, "scheme": "LEI", "value": "RL" + tag})),
    ("a reader may not delete an identifier", client.delete(f"/api/partyidentifiers/{identifier}", headers=reader)),
):
    check(label, response.status_code == 403 and "role-not-allowed" in response.text, (response.status_code, response.text))
check("the refused writes kept nothing", client.get(f"/api/partyidentifiers/{identifier}", headers=admin).json()["scheme"] == "VAT")

check("a manager adds an identifier", client.post("/api/partyidentifiers", headers=writer, json={"partyIdentifierId": 0, "partyId": party, "scheme": "DUNS", "value": "RL" + tag}).status_code == 201)
twin = client.post("/api/partyidentifiers", headers=writer, json={"partyIdentifierId": 0, "partyId": party, "scheme": "duns", "value": "rl" + tag.lower()})
check("a manager gets the rule's 409 for a pair that is held", twin.status_code == 409 and "party-identifier-taken" in twin.text, (twin.status_code, twin.text))

listed = client.get("/api/partyidentifiers", headers=admin, params={"pageSize": 100}).json()
for row in listed if isinstance(listed, list) else listed.get("items", []):
    if row["partyId"] == party:
        client.delete(f"/api/partyidentifiers/{row['partyIdentifierId']}", headers=admin)
check("the party is removed", client.delete(f"/api/parties/{party}", headers=admin).status_code in (200, 204))
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
