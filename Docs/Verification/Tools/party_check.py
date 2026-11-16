"""Plays the PartyTables rules against a running API over a scratch copy of Schemas/parties.sql.

python party_check.py <base url>

The API is a generated one (ASP.NET, Python or Rust) with PartyTables=Party:PartyIdentifier. With ApiAuth set API_USER and API_PASSWORD.
The script adds parties and identifiers and removes them again, so use a scratch database."""
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


def party(name):
    response = client.post(PARTIES, json={"partyId": 0, "legalName": name + " " + tag, "tradingName": None, "city": None, "phone": None, "notes": None})
    assert response.status_code == 201, response.text
    return response.json()["partyId"]


def identifier(owner, scheme, value):
    return client.post(IDENTIFIERS, json={"partyIdentifierId": 0, "partyId": owner, "scheme": scheme, "value": value, "issuedBy": None, "notes": None})


def found(value, scheme=None):
    params = {"value": value} if scheme is None else {"value": value, "scheme": scheme}
    response = client.get(PARTIES + "/by-identifier", params=params)
    return sorted(r["partyId"] for r in response.json()) if response.status_code == 200 else response.status_code


acme = party("Acme")
bolt = party("Bolt")
made = []

first = identifier(acme, "VAT", "  NL" + tag + "  ")
check("an identifier is saved", first.status_code == 201, first.text)
made.append(first.json()["partyIdentifierId"])
check("the scheme and the value are trimmed", first.json()["value"] == "NL" + tag, first.json())

same = identifier(bolt, "VAT", "NL" + tag)
check("the same scheme and value on another party is refused with 409", same.status_code == 409 and TAKEN in same.text, same.text)
case = identifier(bolt, "vat", ("NL" + tag).lower())
check("a pair that differs only in case is refused", case.status_code == 409, case.text)
other = identifier(bolt, "DUNS", "NL" + tag)
check("the same value under another scheme is saved", other.status_code == 201, other.text)
made.append(other.json()["partyIdentifierId"])
own = identifier(bolt, "VAT", "DE" + tag)
check("another value of the same scheme is saved", own.status_code == 201, own.text)
made.append(own.json()["partyIdentifierId"])

row = client.get(f"{IDENTIFIERS}/{made[0]}").json()
unchanged = client.put(f"{IDENTIFIERS}/{made[0]}", json=row)
check("a row saved again with its own pair is not refused", unchanged.status_code in (200, 204), unchanged.text)
row = client.get(f"{IDENTIFIERS}/{made[2]}").json()
clash = client.put(f"{IDENTIFIERS}/{made[2]}", json={**row, "value": "nl" + tag.lower()})
check("an update to a pair another row holds is refused", clash.status_code == 409, clash.text)

check("a value finds every party that holds it", found("NL" + tag) == sorted([acme, bolt]), found("NL" + tag))
check("a value and a scheme find the one party", found("NL" + tag, "VAT") == [acme], found("NL" + tag, "VAT"))
check("the search does not mind case or spaces", found("  nl" + tag.lower() + " ", "vat") == [acme], found("  nl" + tag.lower() + " ", "vat"))
check("a value nobody holds finds nothing", found("XX" + tag) == [], found("XX" + tag))
check("no value is a 400", found("") == 400 and client.get(PARTIES + "/by-identifier").status_code == 400)

for key in made:
    client.delete(f"{IDENTIFIERS}/{key}")
check("once the identifiers are gone the search finds nothing", found("NL" + tag) == [], found("NL" + tag))
for key in (acme, bolt):
    client.delete(f"{PARTIES}/{key}")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
