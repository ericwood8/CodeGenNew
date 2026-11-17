"""Plays the RateTables rule through the OData routes of a running ASP.NET API over a scratch copy of Schemas/rates.sql.

python rate_odata_check.py <base url>

The API is generated with RateTables=ExchangeRate and OData=true. A rate written with POST, PUT or PATCH under /odata goes through the rule of the REST routes: the currencies are trimmed and upper case,
two different ones, a rate above zero, and a pair and day another row holds is the 409 of the rule. With ApiAuth set API_USER and API_PASSWORD. The script adds rates and removes them again, so use a
scratch database."""
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
ROUTE = "/odata/exchangerates"
TAKEN = "rate-taken"
INVALID = "rate-invalid"
made = []
tag = uuid.uuid4().hex[:3].upper()
pair = ("O" + tag[:2], "Z" + tag[2:] + "Y")


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def row(source, target, rate, day, key=0):
    return {"exchangeRateId": key, "fromCurrency": source, "toCurrency": target, "rate": rate, "rateDate": day}


def save(source, target, rate, day):
    reply = client.post(ROUTE, json=row(source, target, rate, day))
    if reply.status_code == 201:
        made.append(reply.json()["exchangeRateId"])
    return reply


def stored(key):
    found = client.get(f"{ROUTE}({key})").json()
    return found["fromCurrency"], found["toCurrency"], float(found["rate"]), found["rateDate"][:10]


first = save(" " + pair[0].lower() + " ", pair[1], 1.5, "2024-01-01")
check("a rate is created through OData", first.status_code == 201, first.text)
one = first.json()["exchangeRateId"]
check("its currencies are trimmed and upper case", stored(one)[:2] == pair, stored(one))
twin = save(pair[0].lower(), pair[1], 1.6, "2024-01-01")
check("a pair and day that differ only in case is refused with the 409 of the rule", twin.status_code == 409 and TAKEN in twin.text, twin.text)
same = save(pair[0], pair[0], 1, "2024-02-01")
check("two equal currencies are refused", same.status_code == 400 and INVALID in same.text, (same.status_code, same.text))
zero = save(pair[0], pair[1], 0, "2024-02-01")
check("a rate that is not above zero is refused", zero.status_code == 400 and INVALID in zero.text, (zero.status_code, zero.text))
second = save(pair[0], pair[1], 1.7, "2024-03-01")
check("a later day is created", second.status_code == 201, second.text)
two = second.json()["exchangeRateId"]

put = client.put(f"{ROUTE}({two})", json=row(pair[0], pair[1], 1.7, "2024-01-01", two))
check("a PUT to a day the pair holds is refused", put.status_code == 409 and TAKEN in put.text, put.text)
patch = client.patch(f"{ROUTE}({two})", json={"rateDate": "2024-01-01"})
check("a PATCH to a day the pair holds is refused", patch.status_code == 409 and TAKEN in patch.text, patch.text)
bad = client.patch(f"{ROUTE}({two})", json={"rate": -1})
check("a PATCH to a rate that is not above zero is refused", bad.status_code == 400 and INVALID in bad.text, (bad.status_code, bad.text))
check("the refused changes kept nothing", stored(two)[2:] == (1.7, "2024-03-01"), stored(two))
own = client.put(f"{ROUTE}({one})", json=row(pair[0], pair[1], 1.55, "2024-01-01", one))
check("a row saved again with its own pair and day is not refused", own.status_code in (200, 204), own.text)
free = client.patch(f"{ROUTE}({two})", json={"fromCurrency": " " + pair[1].lower(), "toCurrency": pair[0], "rateDate": "2024-04-01"})
check("a PATCH to a free pair and day is saved, trimmed", free.status_code in (200, 204) and stored(two) == (pair[1], pair[0], 1.7, "2024-04-01"), (free.text, stored(two)))
found = client.get(ROUTE, params={"$filter": f"fromCurrency eq '{pair[0]}' and toCurrency eq '{pair[1]}'"})
check("the rates of the pair are listed", found.status_code == 200 and [r["exchangeRateId"] for r in found.json()["value"]] == [one], found.text)

for key in made:
    client.delete(f"{ROUTE}({key})")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
