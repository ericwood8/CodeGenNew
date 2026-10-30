"""Plays the EffectiveDatedTables rules against a running API over a scratch copy of Schemas/row_shapes.sql.

python effective_check.py <base url> [route]

The API is a generated one (ASP.NET, Python or Rust) with EffectiveDatedTables=Price:ValidFrom:ValidTo; with ApiAuth set API_USER and API_PASSWORD (the bootstrap administrator). The route defaults to /api/prices.
The script adds Price rows for the item 2 and removes them again, so use a scratch database."""
import os
import sys

import httpx

base = sys.argv[1]
route = sys.argv[2] if len(sys.argv) > 2 else "/api/prices"
client = httpx.Client(base_url=base, timeout=30)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
failures = []
made = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def price(item, start, end, amount=5):
    return {"priceId": 0, "itemId": item, "amount": amount, "currency": "USD", "validFrom": start, "validTo": end, "note": None}


def make(item, start, end):
    r = client.post(route, json=price(item, start, end))
    if r.status_code == 201:
        made.append(r.json())
    return r


r = make(2, "2026-07-01", None)
check("a period after the last one is accepted", r.status_code == 201, (r.status_code, r.text))
first = r.json()
r = make(2, "2026-06-30", "2026-08-01")
check("a period that overlaps another is refused", r.status_code == 409 and r.json()["type"].endswith("effective-overlap"), (r.status_code, r.text))
r = make(2, "2027-01-01", None)
check("an open end overlaps a later period", r.status_code == 409, (r.status_code, r.text))
r = make(2, "2026-09-01", "2026-08-01")
check("a period that runs backwards is refused", r.status_code == 400 and r.json()["type"].endswith("effective-range"), (r.status_code, r.text))
r = make(1, "2024-01-01", "2024-12-31")
check("the same days for another group are accepted", r.status_code == 201, (r.status_code, r.text))

r = client.put(f"{route}/{first['priceId']}", json={**first, "note": "same period"})
check("a row does not overlap itself", r.status_code == 200, (r.status_code, r.text))
r = client.put(f"{route}/{first['priceId']}", json={**first, "validFrom": "2026-06-01"})
check("moving the start into the previous period is refused", r.status_code == 409, (r.status_code, r.text))
r = client.put(f"{route}/{first['priceId']}", json={**first, "validTo": "2026-01-01"})
check("an end before the start is refused", r.status_code == 400, (r.status_code, r.text))

rows = client.get(route + "/current", params={"on": "2026-03-01"}).json()
check("current lists the rows in effect on a day", sorted(x["itemId"] for x in rows) == [1, 2], rows)
rows = client.get(route + "/current", params={"on": "2026-03-01", "itemId": 2}).json()
check("current can be narrowed by group", [x["itemId"] for x in rows] == [2], rows)
rows = client.get(route + "/current", params={"on": "2025-06-01"}).json()
check("current includes the first and last day", [x["itemId"] for x in rows] == [1] and client.get(route + "/current", params={"on": "2025-12-31"}).json()[0]["amount"] == rows[0]["amount"], rows)
check("current without a day answers", client.get(route + "/current").status_code == 200)

for row in made:
    client.delete(f"{route}/{row['priceId']}")
print("\nFAILED: " + ", ".join(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
