"""Plays the exchange rate rules over HTTP against a running generated ASP.NET API over a scratch copy of Schemas/rates.sql.

python rate_check.py <base url> [table]

The API is generated with RateTables=ExchangeRate (or the table named: a rate table with the columns of Schemas/rates.sql, its day a date or a date and time). The rules it plays: currencies written trimmed and in upper case, two different currencies, a rate above zero, one rate per pair
and day (409 rate-taken, also when only the case differs), and GET rate?from=&to=&on= answering the latest rate on or before a day (404 rate-missing before the first one).
With ApiAuth set API_USER and API_PASSWORD. The script adds rows and removes them again, so use a scratch database."""
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
made = []
TABLE = sys.argv[2] if len(sys.argv) > 2 else "ExchangeRate"
route = "/api/" + TABLE.lower() + "s"
KEY = TABLE[0].lower() + TABLE[1:] + "Id"


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def save(source, target, rate, day):
    response = client.post(route, json={KEY: 0, "fromCurrency": source, "toCurrency": target, "rate": rate, "rateDate": day})
    if response.status_code == 201:
        made.append(response.json()[KEY])
    return response


def rate_on(source, target, day=None):
    params = {"from": source, "to": target}
    if day:
        params["on"] = day
    return client.get(route + "/rate", params=params)


# a pair no other run of this script uses
tag = uuid.uuid4().hex[:3].upper()
pair = ("Q" + tag[:2], "Z" + tag[2:] + "Y")

first = save(" " + pair[0].lower() + " ", pair[1], 1.5, "2024-01-01")
check("a rate is saved", first.status_code == 201, first.text)
check("the currencies are written trimmed and in upper case", first.status_code == 201 and (first.json()["fromCurrency"], first.json()["toCurrency"]) == pair, first.text)
taken = save(pair[0].lower(), pair[1], 1.6, "2024-01-01")
check("a second rate for the pair and day is the 409 of a rate taken, whatever the case", taken.status_code == 409 and "rate-taken" in taken.text, (taken.status_code, taken.text))
check("the same day for the opposite pair is another rate", save(pair[1], pair[0], 0.66, "2024-01-01").status_code == 201)
same = save(pair[0], pair[0], 1, "2024-02-01")
check("two equal currencies are refused", same.status_code == 400 and "rate-invalid" in same.text, (same.status_code, same.text))
zero = save(pair[0], pair[1], 0, "2024-02-01")
check("a rate that is not above zero is refused", zero.status_code == 400 and "rate-invalid" in zero.text, (zero.status_code, zero.text))
check("a negative rate is refused", save(pair[0], pair[1], -2, "2024-02-01").status_code == 400)
blank = save("  ", pair[1], 1, "2024-02-01")
check("a blank currency is refused", blank.status_code == 400 and "rate-invalid" in blank.text, (blank.status_code, blank.text))
check("a later rate is saved", save(pair[0], pair[1], 1.7, "2024-03-01").status_code == 201)

# the rate in force
early = rate_on(pair[0].lower(), pair[1], "2023-12-31")
check("a day before the first rate has none (404 rate-missing)", early.status_code == 404 and "rate-missing" in early.text, (early.status_code, early.text))
between = rate_on(pair[0], pair[1], "2024-02-15")
check("a day between two rates gets the earlier one", between.status_code == 200 and float(between.json()["rate"]) == 1.5, between.text)
on_day = rate_on(pair[0], pair[1], "2024-03-01")
check("the day a rate starts gets that rate", on_day.status_code == 200 and float(on_day.json()["rate"]) == 1.7, on_day.text)
latest = rate_on(pair[0], pair[1])
check("no day means today: the latest rate", latest.status_code == 200 and float(latest.json()["rate"]) == 1.7, latest.text)
check("the opposite pair has its own rates", float(rate_on(pair[1], pair[0], "2024-01-15").json()["rate"]) == 0.66)
check("a pair with no rates has none", rate_on(pair[0], "XXQ", "2024-06-01").status_code == 404)
check("the same currency twice is refused", rate_on(pair[0], pair[0]).status_code == 400)
check("a lookup with a currency missing is refused", client.get(route + "/rate", params={"from": pair[0]}).status_code == 400)

# an update that moves a rate onto a day another one holds
second = next(i for i in made if client.get(f"{route}/{i}").json()["rateDate"].startswith("2024-03-01"))
row = client.get(f"{route}/{second}").json()
row["rateDate"] = "2024-01-01"
moved = client.put(f"{route}/{second}", json=row)
check("moving a rate onto a day the pair already holds is refused", moved.status_code == 409 and "rate-taken" in moved.text, (moved.status_code, moved.text))
row["rateDate"] = "2024-03-01"
row["rate"] = 1.8
check("changing the rate of a row keeps its own pair and day", client.put(f"{route}/{second}", json=row).status_code in (200, 204))

for key in made:
    client.delete(f"{route}/{key}")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
