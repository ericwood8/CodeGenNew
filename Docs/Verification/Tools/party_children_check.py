"""Plays the child tables of a party master over HTTP against a running ASP.NET, Python or Rust API over a scratch copy of Schemas/parties_full.sql.

python party_children_check.py <base url> [--no-tax]

The API is generated with PartyTables=Party:PartyIdentifier, DefaultRowTables=PartyName:IsPrimary:PartyId+NameType,ContactMethod:IsPreferred:PartyId+MethodType and
EffectiveDatedTables=TaxRegistration:ValidFrom:ValidTo:PartyId+Country. The shapes it plays: one primary name per party and name type, one preferred contact method per party and type, tax registrations
of a party and country that do not overlap, and the roles of a party through the PartyRole junction. With ApiAuth set API_USER and API_PASSWORD. The script adds a party with rows and removes them
again, so use a scratch database. --no-tax skips the tax registrations, which only the ASP.NET API has (EffectiveDatedTables)."""
import os
import sys
import uuid

import httpx

base = sys.argv[1]
client = httpx.Client(base_url=base, timeout=30)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
failures = []
tag = uuid.uuid4().hex[:6].upper()


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def made(route, body, key):
    response = client.post(route, json={key: 0, **body})
    assert response.status_code == 201, (route, response.status_code, response.text)
    return response.json()[key]


def flag(route, key, column):
    return client.get(f"{route}/{key}").json()[column]


party = made("/api/parties", {"legalName": "Kids " + tag}, "partyId")
other = made("/api/parties", {"legalName": "Other " + tag}, "partyId")

# names: one primary per party and name type
n1 = made("/api/partynames", {"partyId": party, "nameType": "legal", "name": "Kids Ltd " + tag, "isPrimary": False}, "partyNameId")
check("the first name of a type is the primary one", flag("/api/partynames", n1, "isPrimary") is True)
n2 = made("/api/partynames", {"partyId": party, "nameType": "legal", "name": "Kids Limited " + tag, "isPrimary": False}, "partyNameId")
check("a second name of the type is not", flag("/api/partynames", n2, "isPrimary") is False)
t1 = made("/api/partynames", {"partyId": party, "nameType": "trading", "name": "Kids " + tag, "isPrimary": False}, "partyNameId")
check("the first name of another type is primary for that type", flag("/api/partynames", t1, "isPrimary") is True)
check("make-default moves the mark", client.post(f"/api/partynames/{n2}/make-default").status_code in (200, 204)
      and flag("/api/partynames", n2, "isPrimary") is True and flag("/api/partynames", n1, "isPrimary") is False)
client.delete(f"/api/partynames/{n2}")
check("deleting the primary name hands the mark on", flag("/api/partynames", n1, "isPrimary") is True)

# contact methods: one preferred per party and type
c1 = made("/api/contactmethods", {"partyId": party, "methodType": "email", "value": "a@" + tag.lower() + ".test", "isPreferred": False}, "contactMethodId")
c2 = made("/api/contactmethods", {"partyId": party, "methodType": "email", "value": "b@" + tag.lower() + ".test", "isPreferred": True}, "contactMethodId")
check("a contact method saved with the mark takes it from the other of its type", flag("/api/contactmethods", c2, "isPreferred") is True and flag("/api/contactmethods", c1, "isPreferred") is False)
c3 = made("/api/contactmethods", {"partyId": party, "methodType": "phone", "value": "555 " + tag, "isPreferred": False}, "contactMethodId")
check("each type has its own preferred method", flag("/api/contactmethods", c3, "isPreferred") is True and flag("/api/contactmethods", c2, "isPreferred") is True)
c4 = made("/api/contactmethods", {"partyId": other, "methodType": "email", "value": "c@" + tag.lower() + ".test", "isPreferred": False}, "contactMethodId")
check("each party has its own preferred method", flag("/api/contactmethods", c4, "isPreferred") is True)

# tax registrations: no overlap inside a party and country
if "--no-tax" not in sys.argv:
    def registration(country, start, end, party_id=party):
        return client.post("/api/taxregistrations", json={"taxRegistrationId": 0, "partyId": party_id, "country": country, "taxNumber": "TX" + tag, "validFrom": start, "validTo": end})

    first = registration("DE", "2024-01-01", "2024-12-31")
    check("a tax registration is saved", first.status_code == 201, first.text)
    overlap = registration("DE", "2024-06-01", "2025-06-30")
    check("an overlapping registration of the same party and country is refused", overlap.status_code in (400, 409, 422), (overlap.status_code, overlap.text))
    check("a registration of another country overlaps nothing", registration("FR", "2024-06-01", None).status_code == 201)
    check("the next period of the same country is saved", registration("DE", "2025-01-01", None).status_code == 201)
    backwards = registration("IT", "2024-06-01", "2024-01-01")
    check("a period that runs backwards is refused", backwards.status_code in (400, 409, 422), (backwards.status_code, backwards.text))
    held = client.post("/api/taxregistrations", json={"taxRegistrationId": 0, "partyId": other, "country": "DE", "taxNumber": "TX" + tag, "validFrom": "2024-01-01", "validTo": None})
    check("a country, number and first day another party holds is the 409 of a duplicate value", held.status_code == 409 and "duplicate-value" in held.text, (held.status_code, held.text))
    current = client.get("/api/taxregistrations/current", params={"on": "2024-07-01", "partyId": party})
    check("the rows in effect on a day are listed", current.status_code == 200 and sorted(r["country"] for r in current.json()) == ["DE", "FR"], current.text)

# roles: the junction of a party and a role
roles = client.get("/api/roles").json()
roles = roles["items"] if isinstance(roles, dict) else roles
customer, supplier = (next(r["roleId"] for r in roles if r["roleName"] == name) for name in ("Customer", "Supplier"))
for role in (customer, supplier):
    linked = client.post("/api/partyroles/junction/link", json={"anchorId": party, "targetId": role})
    check("a party is linked to a role", linked.status_code in (200, 201, 204), (linked.status_code, linked.text))
listed = client.get(f"/api/partyroles/junction/{party}")
chosen = sorted(i["roleName"] for i in listed.json() if i["isSelected"]) if listed.status_code == 200 else []
check("the roles of the party are listed, the ones it has marked", chosen == ["Customer", "Supplier"], listed.text)
check("linking the same role again does not repeat it", client.post("/api/partyroles/junction/link", json={"anchorId": party, "targetId": customer}).status_code in (200, 201, 204, 409))
links = [row for row in client.get("/api/partyroles/junction").json() if row.get("anchorId") == party]
check("a role is held once", sorted(r["targetId"] for r in links) == sorted([customer, supplier]), links)
check("a role is taken off the party", client.post("/api/partyroles/junction/unlink", json={"anchorId": party, "targetId": supplier}).status_code in (200, 204))
links = [row for row in client.get("/api/partyroles/junction").json() if row.get("anchorId") == party]
check("only the other role is left", [r["targetId"] for r in links] == [customer], links)

print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
