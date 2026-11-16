"""Plays the PartyTables rule on identifiers saved with their party (EditableChildren=PartyIdentifier) against a running ASP.NET API over a scratch copy of Schemas/parties.sql.

python party_rows_check.py <base url>

The API is generated with PartyTables=Party:PartyIdentifier and EditableChildren=PartyIdentifier: a create or update of a party carries its identifiers as the list partyIdentifiers.
With ApiAuth set API_USER and API_PASSWORD. The script adds parties and identifiers and removes them again, so use a scratch database."""
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
PARTIES = "/api/parties"
IDENTIFIERS = "/api/partyidentifiers"
TAKEN = "party-identifier-taken"
tag = uuid.uuid4().hex[:8].upper()


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def row(scheme, value, key=0, owner=0):
    return {"partyIdentifierId": key, "partyId": owner, "scheme": scheme, "value": value, "issuedBy": None, "notes": None}


def party(name, rows, key=0):
    return {"partyId": key, "legalName": name + " " + tag, "tradingName": None, "city": None, "phone": None, "notes": None, "partyIdentifiers": rows}


def listed(route):
    rows = client.get(route, params={"pageSize": 500}).json()
    return rows["items"] if isinstance(rows, dict) else rows


def held(owner):
    """The scheme and value pairs of a party, read from the identifier table."""
    return sorted((r["scheme"], r["value"]) for r in listed(IDENTIFIERS) if r["partyId"] == owner)


def named(name):
    return [r["partyId"] for r in listed(PARTIES) if r["legalName"] == name + " " + tag]


made = client.post(PARTIES, json=party("Acme", [row("VAT", "  NL" + tag + " "), row(" DUNS ", "111" + tag)]))
check("a party is saved with two identifiers", made.status_code == 201, made.text)
acme = made.json()["partyId"]
check("their scheme and value are trimmed", held(acme) == [("DUNS", "111" + tag), ("VAT", "NL" + tag)], held(acme))

clash = client.post(PARTIES, json=party("Bolt", [row("vat", "nl" + tag.lower())]))
check("a new party with a pair another party holds is refused with the 409 of the rule", clash.status_code == 409 and TAKEN in clash.text, clash.text)
check("the refused party is not kept", named("Bolt") == [], named("Bolt"))

twice = client.post(PARTIES, json=party("Core", [row("LEI", "X" + tag), row("lei", " x" + tag.lower())]))
check("a pair twice in one list is refused", twice.status_code == 409 and TAKEN in twice.text, twice.text)
check("that party is not kept either", named("Core") == [], named("Core"))

made = client.post(PARTIES, json=party("Bolt", [row("VAT", "DE" + tag)]))
check("a party with a free pair is saved", made.status_code == 201, made.text)
bolt = made.json()["partyId"]
mine = [r for r in listed(IDENTIFIERS) if r["partyId"] == bolt]

again = client.put(f"{PARTIES}/{bolt}", json=party("Bolt", [row("VAT", "DE" + tag, mine[0]["partyIdentifierId"], bolt)], bolt))
check("the party saved again with its own pair is not refused", again.status_code in (200, 204), again.text)
taken = client.put(f"{PARTIES}/{bolt}", json=party("Bolt", [row("VAT", "DE" + tag, mine[0]["partyIdentifierId"], bolt), row("Vat", "NL" + tag)], bolt))
check("an added row with a pair another party holds is refused", taken.status_code == 409 and TAKEN in taken.text, taken.text)
changed = client.put(f"{PARTIES}/{bolt}", json=party("Bolt", [row("DUNS", "111" + tag, mine[0]["partyIdentifierId"], bolt)], bolt))
check("a row changed to a pair another party holds is refused", changed.status_code == 409 and TAKEN in changed.text, changed.text)
check("the refused saves changed nothing", held(bolt) == [("VAT", "DE" + tag)], held(bolt))
more = client.put(f"{PARTIES}/{bolt}", json=party("Bolt", [row("VAT", "DE" + tag, mine[0]["partyIdentifierId"], bolt), row("DUNS", " 222" + tag)], bolt))
check("a free pair is added with the party, trimmed", more.status_code in (200, 204) and held(bolt) == [("DUNS", "222" + tag), ("VAT", "DE" + tag)], (more.text, held(bolt)))
found = client.get(PARTIES + "/by-identifier", params={"value": "222" + tag})
check("the party is found by the identifier saved with it", [r["partyId"] for r in found.json()] == [bolt], found.text)

for key in (acme, bolt):
    client.put(f"{PARTIES}/{key}", json=party("Gone", [], key))
    client.delete(f"{PARTIES}/{key}")
check("the parties are removed", client.get(f"{PARTIES}/{acme}").status_code == 404 and client.get(f"{PARTIES}/{bolt}").status_code == 404)
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
