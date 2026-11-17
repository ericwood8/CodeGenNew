"""Sends the same new rate many times at the same moment to a running API generated with RateTables=ExchangeRate.

python rate_race_check.py <base url> [requests] [rounds]

The rate table needs its unique index on the pair and the day (Schemas/rates*.sql have it). Each round sends `requests` (default 20) creates of one pair and day at once, with different rates: exactly one
must be saved (201) and every other one refused with the 409 of the rule (rate-taken), whether the rule's own read or the unique index stopped it, and the day must hold one row. Then two rows are
moved to one free day at the same moment: one change is saved and the other gets the same 409. With ApiAuth set API_USER and API_PASSWORD.
The script adds rates and removes them again, so use a scratch or test database."""
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
ROUTE = "/api/exchangerates"
TAKEN = "rate-taken"
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:400]))
    if not ok:
        failures.append(label)


def body_of(source, target, rate, day, key=0):
    return {"exchangeRateId": key, "fromCurrency": source, "toCurrency": target, "rate": rate, "rateDate": day}


def create(item):
    source, target, rate, day = item
    with httpx.Client(base_url=base, timeout=120, headers=headers) as client:
        reply = client.post(ROUTE, json=body_of(source, target, rate, day))
        return reply.status_code, reply.text


def change(item):
    key, source, target, rate, day = item
    with httpx.Client(base_url=base, timeout=120, headers=headers) as client:
        reply = client.put(f"{ROUTE}/{key}", json=body_of(source, target, rate, day, key))
        return reply.status_code, reply.text


main = httpx.Client(base_url=base, timeout=60, headers=headers)
tag = uuid.uuid4().hex[:3].upper()
pair = ("R" + tag[:2], "Z" + tag[2:] + "Y")
keys = []


for turn in range(1, rounds + 1):
    day = f"2030-0{turn}-01"
    with concurrent.futures.ThreadPoolExecutor(max_workers=count) as pool:
        answers = list(pool.map(create, [(pair[0], pair[1], 1 + n / 100, day) for n in range(count)]))
    saved = [text for code, text in answers if code == 201]
    refused = [text for code, text in answers if code == 409 and TAKEN in text]
    others = [(code, text[:200]) for code, text in answers if code != 201 and not (code == 409 and TAKEN in text)]
    check(f"round {turn}: exactly one of {count} creates is saved", len(saved) == 1, [code for code, _ in answers])
    check(f"round {turn}: every other one is the 409 of the rule", len(refused) == count - 1 and not others, others[:3])
    for text in saved:
        keys.append(httpx.Response(200, text=text).json()["exchangeRateId"])
    held = main.get(ROUTE + "/rate", params={"from": pair[0], "to": pair[1], "on": day})
    check(f"round {turn}: the day holds the rate that was saved", held.status_code == 200 and held.json()["exchangeRateId"] in keys, held.text)

first = main.post(ROUTE, json=body_of(pair[1], pair[0], 2, "2031-01-01")).json()["exchangeRateId"]
second = main.post(ROUTE, json=body_of(pair[1], pair[0], 3, "2031-02-01")).json()["exchangeRateId"]
keys += [first, second]
for turn in range(1, rounds + 1):
    target = f"2032-0{turn}-01"
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        answers = list(pool.map(change, [(first, pair[1], pair[0], 2, target), (second, pair[1], pair[0], 3, target)]))
    codes = sorted(code for code, _ in answers)
    good = codes in ([200, 409], [204, 409]) and all(TAKEN in text for code, text in answers if code == 409)
    check(f"round {turn}: two moves to one free day, one saved and one refused by the rule", good, [(code, text[:200]) for code, text in answers])
    days = sorted(main.get(f"{ROUTE}/{key}").json()["rateDate"][:10] for key in (first, second))
    check(f"round {turn}: one row holds the new day", days.count(target[:10]) == 1, days)
    loser = first if not main.get(f"{ROUTE}/{first}").json()["rateDate"].startswith(target[:10]) else second
    winner = second if loser == first else first
    row = main.get(f"{ROUTE}/{winner}").json()
    row["rateDate"] = f"2033-0{turn}-01"
    main.put(f"{ROUTE}/{winner}", json=row)

for key in keys:
    main.delete(f"{ROUTE}/{key}")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
