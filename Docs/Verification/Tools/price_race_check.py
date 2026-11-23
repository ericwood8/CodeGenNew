"""Sends the same new price break many times at the same moment to a running API generated with PriceListTables (Schemas/price_lists.sql).

python price_race_check.py <base url> [requests] [rounds]

The price table needs its unique index on the list, the item and the minimum quantity. Each round sends `requests` (default 20) creates of one break at once, with different prices: exactly one must be saved (201)
and every other one refused with the 409 of the rule (price-taken), whether the rule's own read or the unique index stopped it, and the break must be held by one row. Then two rows are moved to one free break at the
same moment: one change is saved and the other gets the same 409. With ApiAuth set API_USER and API_PASSWORD. The script adds rows and removes them again, so use a scratch or test database."""
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
ROUTE = "/api/pricelistitems"
TAKEN = "price-taken"
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:400]))
    if not ok:
        failures.append(label)


def body_of(list_id, item, minimum, price, key=0):
    return {"priceListItemId": key, "priceListId": list_id, "itemId": item, "minQuantity": minimum, "price": price}


def create(arguments):
    with httpx.Client(base_url=base, timeout=120, headers=headers) as client:
        reply = client.post(ROUTE, json=body_of(*arguments))
        return reply.status_code, reply.text


def change(arguments):
    key, *rest = arguments
    with httpx.Client(base_url=base, timeout=120, headers=headers) as client:
        reply = client.put(f"{ROUTE}/{key}", json=body_of(*rest, key=key))
        return reply.status_code, reply.text


main = httpx.Client(base_url=base, timeout=60, headers=headers)
tag = uuid.uuid4().hex[:6]
item = main.post("/api/priceitems", json={"priceItemId": 0, "name": "Race " + tag, "sku": None, "unitName": None, "notes": None}).json()["priceItemId"]
price_list = main.post("/api/pricelists", json={"priceListId": 0, "name": "Race " + tag, "currencyCode": "RQ" + tag[:1].upper(), "validFrom": "2030-01-01", "validTo": None, "customerGroup": None}).json()["priceListId"]
keys = []

for turn in range(1, rounds + 1):
    with concurrent.futures.ThreadPoolExecutor(max_workers=count) as pool:
        answers = list(pool.map(create, [(price_list, item, turn * 10, 1 + n / 100) for n in range(count)]))
    saved = [text for code, text in answers if code == 201]
    refused = [text for code, text in answers if code == 409 and TAKEN in text]
    others = [(code, text[:200]) for code, text in answers if code != 201 and not (code == 409 and TAKEN in text)]
    check(f"round {turn}: exactly one of {count} creates is saved", len(saved) == 1, [code for code, _ in answers])
    check(f"round {turn}: every other one is the 409 of the rule", len(refused) == count - 1 and not others, others[:3])
    for text in saved:
        keys.append(httpx.Response(200, text=text).json()["priceListItemId"])
    held = main.get("/api/pricelistitems/price", params={"item": item, "quantity": turn * 10, "currency": "RQ" + tag[:1].upper(), "on": "2030-06-01"})
    check(f"round {turn}: the break holds the price that was saved", held.status_code == 200 and held.json()["priceListItemId"] in keys, held.text)

first = main.post(ROUTE, json=body_of(price_list, item, 1000, 2)).json()["priceListItemId"]
second = main.post(ROUTE, json=body_of(price_list, item, 2000, 3)).json()["priceListItemId"]
keys += [first, second]
for turn in range(1, rounds + 1):
    target = 5000 + turn
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        answers = list(pool.map(change, [(first, price_list, item, target, 2), (second, price_list, item, target, 3)]))
    codes = sorted(code for code, _ in answers)
    good = codes in ([200, 409], [204, 409]) and all(TAKEN in text for code, text in answers if code == 409)
    check(f"round {turn}: two moves to one free break, one saved and one refused by the rule", good, [(code, text[:200]) for code, text in answers])
    held = sorted(main.get(f"{ROUTE}/{key}").json()["minQuantity"] for key in (first, second))
    check(f"round {turn}: one row holds the new break", held.count(target) == 1, held)
    loser = first if main.get(f"{ROUTE}/{first}").json()["minQuantity"] != target else second
    winner = second if loser == first else first
    row = main.get(f"{ROUTE}/{winner}").json()
    row["minQuantity"] = 6000 + turn
    main.put(f"{ROUTE}/{winner}", json=row)

for key in keys:
    main.delete(f"{ROUTE}/{key}")
main.delete(f"/api/pricelists/{price_list}")
main.delete(f"/api/priceitems/{item}")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
