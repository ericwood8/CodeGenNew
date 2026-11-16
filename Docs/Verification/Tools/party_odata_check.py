"""Plays the PartyTables rule through the OData routes of a running ASP.NET API over a scratch copy of Schemas/parties.sql.

python party_odata_check.py <base url>

The API is generated with PartyTables=Party:PartyIdentifier and OData=true. An identifier written with POST, PUT or PATCH under /odata goes through the rule of the REST routes: the scheme and the value
are trimmed and a pair another row holds is the 409 of the rule. With ApiAuth set API_USER and API_PASSWORD. The script adds a party and identifiers and removes them again, so use a scratch database."""
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
PARTIES = "/odata/parties"
IDENTIFIERS = "/odata/partyidentifiers"
TAKEN = "party-identifier-taken"
tag = uuid.uuid4().hex[:8].upper()


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def row(owner, scheme, value, key=0):
    return {"partyIdentifierId": key, "partyId": owner, "scheme": scheme, "value": value, "issuedBy": None, "notes": None}


def stored(key):
    found = client.get(f"{IDENTIFIERS}({key})").json()
    return found["scheme"], found["value"]


made = client.post(PARTIES, json={"partyId": 0, "legalName": "OData " + tag, "tradingName": None, "city": None, "phone": None, "notes": None})
assert made.status_code == 201, made.text
party = made.json()["partyId"]

first = client.post(IDENTIFIERS, json=row(party, " VAT ", "  OD" + tag + " "))
check("an identifier is created through OData", first.status_code == 201, first.text)
one = first.json()["partyIdentifierId"]
check("its scheme and value are trimmed", stored(one) == ("VAT", "OD" + tag), stored(one))
twin = client.post(IDENTIFIERS, json=row(party, "vat", "od" + tag.lower()))
check("a pair that differs only in case is refused with the 409 of the rule", twin.status_code == 409 and TAKEN in twin.text, twin.text)
second = client.post(IDENTIFIERS, json=row(party, "DUNS", "OD" + tag))
check("the same value under another scheme is created", second.status_code == 201, second.text)
two = second.json()["partyIdentifierId"]

put = client.put(f"{IDENTIFIERS}({two})", json=row(party, " Vat", "OD" + tag, two))
check("a PUT to a pair another row holds is refused", put.status_code == 409 and TAKEN in put.text, put.text)
patch = client.patch(f"{IDENTIFIERS}({two})", json={"scheme": "VAT "})
check("a PATCH to a pair another row holds is refused", patch.status_code == 409 and TAKEN in patch.text, patch.text)
check("the refused changes kept nothing", stored(two) == ("DUNS", "OD" + tag), stored(two))
own = client.put(f"{IDENTIFIERS}({one})", json=row(party, "VAT", "OD" + tag, one))
check("a row saved again with its own pair is not refused", own.status_code in (200, 204), own.text)
free = client.patch(f"{IDENTIFIERS}({two})", json={"scheme": " LEI "})
check("a PATCH to a free pair is saved, trimmed", free.status_code in (200, 204) and stored(two) == ("LEI", "OD" + tag), (free.text, stored(two)))

for key in (one, two):
    client.delete(f"{IDENTIFIERS}({key})")
check("the party is removed", client.delete(f"{PARTIES}({party})").status_code in (200, 204))
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
