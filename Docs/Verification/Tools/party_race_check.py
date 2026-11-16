"""Sends the same new identifier many times at the same moment to a running API generated with PartyTables=Party:PartyIdentifier.

python party_race_check.py <base url> [requests] [rounds]

The identifier table needs its unique index on the scheme and the value (Schemas/parties.sql has it). Each round sends `requests` (default 20) creates of one scheme and value pair at once: exactly one
must be saved (201) and every other one refused with the 409 of the rule (party-identifier-taken), whether the rule's own read or the unique index stopped it, and the pair must be held once.
Then two rows are changed to one free pair at the same moment: one change is saved and the other gets the same 409. With ApiAuth set API_USER and API_PASSWORD.
The script adds parties and identifiers and removes them again, so use a scratch or test database."""
import concurrent.futures
import os
import sys
import uuid

import httpx

base = sys.argv[1]
count = int(sys.argv[2]) if len(sys.argv) > 2 else 20
rounds = int(sys.argv[3]) if len(sys.argv) > 3 else 3
headers = {}
if os.environ.get("API_USER"):
    login = httpx.post(base + "/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]}, timeout=60)
    login.raise_for_status()
    body = login.json()
    headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
PARTIES = "/api/parties"
IDENTIFIERS = "/api/partyidentifiers"
TAKEN = "party-identifier-taken"
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:400]))
    if not ok:
        failures.append(label)


def body_of(owner, scheme, value, key=0):
    return {"partyIdentifierId": key, "partyId": owner, "scheme": scheme, "value": value, "issuedBy": None, "notes": None}


def create(item):
    owner, scheme, value = item
    with httpx.Client(base_url=base, timeout=120, headers=headers) as client:
        reply = client.post(IDENTIFIERS, json=body_of(owner, scheme, value))
        return reply.status_code, reply.text


def change(item):
    key, owner, scheme, value = item
    with httpx.Client(base_url=base, timeout=120, headers=headers) as client:
        reply = client.put(f"{IDENTIFIERS}/{key}", json=body_of(owner, scheme, value, key))
        return reply.status_code, reply.text


main = httpx.Client(base_url=base, timeout=60, headers=headers)
tag = uuid.uuid4().hex[:8].upper()
made = main.post(PARTIES, json={"partyId": 0, "legalName": "Race " + tag, "tradingName": None, "city": None, "phone": None, "notes": None})
assert made.status_code == 201, made.text
owner = made.json()["partyId"]


def rows_with(value):
    """The parties the search finds by the value."""
    found = main.get(PARTIES + "/by-identifier", params={"value": value})
    return found.json() if found.status_code == 200 else found.status_code


keys = []
for turn in range(1, rounds + 1):
    value = f"RACE{tag}{turn}"
    with concurrent.futures.ThreadPoolExecutor(max_workers=count) as pool:
        answers = list(pool.map(create, [(owner, "VAT", value)] * count))
    saved = [text for code, text in answers if code == 201]
    refused = [text for code, text in answers if code == 409 and TAKEN in text]
    others = [(code, text[:200]) for code, text in answers if code != 201 and not (code == 409 and TAKEN in text)]
    check(f"round {turn}: exactly one of {count} creates is saved", len(saved) == 1, [code for code, _ in answers])
    check(f"round {turn}: every other one is the 409 of the rule", len(refused) == count - 1 and not others, others[:3])
    for text in saved:
        keys.append(httpx.Response(200, text=text).json()["partyIdentifierId"])
    check(f"round {turn}: the party is found by the value", [row["partyId"] for row in rows_with(value)] == [owner], rows_with(value))

first = main.post(IDENTIFIERS, json=body_of(owner, "DUNS", "A" + tag)).json()["partyIdentifierId"]
second = main.post(IDENTIFIERS, json=body_of(owner, "DUNS", "B" + tag)).json()["partyIdentifierId"]
keys += [first, second]
for turn in range(1, rounds + 1):
    target = f"MOVE{tag}{turn}"
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        answers = list(pool.map(change, [(first, owner, "DUNS", target), (second, owner, "DUNS", target)]))
    codes = sorted(code for code, _ in answers)
    good = codes in ([200, 409], [204, 409]) and all(TAKEN in text for code, text in answers if code == 409)
    check(f"round {turn}: two changes to one free pair, one saved and one refused by the rule", good, [(code, text[:200]) for code, text in answers])
    values = sorted(main.get(f"{IDENTIFIERS}/{key}").json()["value"] for key in (first, second))
    check(f"round {turn}: one row holds the new pair", values.count(target) == 1, values)
    loser = first if main.get(f"{IDENTIFIERS}/{first}").json()["value"] != target else second
    winner = second if loser == first else first
    main.put(f"{IDENTIFIERS}/{winner}", json=body_of(owner, "DUNS", f"BACK{tag}{turn}", winner))

for key in keys:
    main.delete(f"{IDENTIFIERS}/{key}")
check("the party can be removed once its identifiers are gone", main.delete(f"{PARTIES}/{owner}").status_code in (200, 204))
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
