"""Plays the stock rules over HTTP against a running generated API over a scratch copy of Schemas/stock.sql (or its SQL Server, PostgreSQL and MySQL versions).

python stock_check.py <base url> [wait]

The API is generated with StockTables=StockLevel (the ledger StockLedger carries the stock routes, and the ledger and the holds are also log tables). The rules it plays: receive puts stock on hand and writes a ledger row;
reserve is one guarded update, so what is reserved never goes above what is on hand (409 insufficient-stock) and the last item goes to one buyer only; commit turns a hold into a sale, release gives it back, and a hold that is
closed or unknown is refused (409 reservation-closed, 404 stock-unknown); a hold that has run out is given back by expire and cannot be committed; adjust is a stock-take that writes an adjustment row and refuses a count below
what is reserved; low lists the items at or under their reorder level; check works every level out again from the ledger; the level table cannot be edited by hand (400 stock-managed) and the ledger and the holds are never changed (405).
Two buyers racing for the last item are played with threads. A hold that runs out is played by moving its end into the past when STOCK_SQLITE names the SQLite file, and by waiting out a one-minute hold when the argument wait is given.
With ApiAuth set API_USER and API_PASSWORD. The ledger keeps its rows, so use a scratch database."""
import os
import sqlite3
import sys
import time
import uuid
from concurrent.futures import ThreadPoolExecutor

import httpx

base = sys.argv[1]
wait = "wait" in sys.argv[2:]
client = httpx.Client(base_url=base, timeout=60)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
failures = []
run = uuid.uuid4().hex[:8]
LEDGER = os.environ.get("STOCK_LEDGER_ROUTE", "/api/stockledgers")
LEVELS = os.environ.get("STOCK_LEVEL_ROUTE", "/api/stocklevels")
HOLDS = os.environ.get("STOCK_HOLD_ROUTE", "/api/stockreservations")
ITEMS = os.environ.get("STOCK_ITEM_ROUTE", "/api/stockitems")


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def problem(response, status, kind):
    return response.status_code == status and response.json().get("type", "").endswith(kind)


def post(route, body=None):
    return client.post(f"{LEDGER}/{route}", json=body or {})


def new_item(label):
    response = client.post(ITEMS, json={"stockItemId": 0, "name": f"Stock {label} {run}"})
    response.raise_for_status()
    body = response.json()
    return next(v for k, v in body.items() if k.lower() == "stockitemid")


def level(item):
    response = client.get(f"{LEDGER}/available", params={"item": item})
    body = response.json() if response.status_code == 200 else {}
    return response, (body.get("onHand"), body.get("reserved"), body.get("available"))


def ledger_rows(item):
    response = client.get(LEDGER, params={"search": "", "pageSize": 200})
    body = response.json()
    rows = body if isinstance(body, list) else body.get("items") or body.get("data") or []
    return [r for r in rows if r.get("itemId") == item]


item = new_item("A")
check("an item with no stock row has nothing available", level(item)[1] == (0, 0, 0), level(item)[0].text)
check("available needs the item", client.get(f"{LEDGER}/available").status_code == 400)
check("available for an item that does not exist is refused", problem(client.get(f"{LEDGER}/available", params={"item": 987654}), 404, "stock-unknown"))

check("a receipt of zero is refused", problem(post("receive", {"item": item, "quantity": 0}), 400, "stock-invalid"))
check("a receipt without an item is refused", problem(post("receive", {"quantity": 5}), 400, "stock-invalid"))
check("a receipt for an item that does not exist is refused", problem(post("receive", {"item": 987654, "quantity": 5}), 400, "stock-invalid"))
check("a receipt of another kind is refused", problem(post("receive", {"item": item, "quantity": 5, "kind": "sale"}), 400, "stock-invalid"))
first = post("receive", {"item": item, "quantity": 10, "reference": "PO-" + run})
check("the first receipt adds the level and answers 201", first.status_code == 201 and (first.json().get("onHand"), first.json().get("reserved")) == (10, 0), first.text)
check("a return puts stock back on hand", post("receive", {"item": item, "quantity": 5, "kind": "return"}).status_code == 201 and level(item)[1] == (15, 0, 15))

hold = post("reserve", {"item": item, "quantity": 4, "reference": "cart-" + run})
check("a reservation answers 201 with the hold and the level", hold.status_code == 201 and hold.json().get("level", {}).get("available") == 11, hold.text)
hold_id = hold.json().get("reservation")
check("a hold is on the level: reserved goes up, on hand stays", level(item)[1] == (15, 4, 11))
check("a reservation of more than is available is refused", problem(post("reserve", {"item": item, "quantity": 12}), 409, "insufficient-stock"))
check("a refused reservation changed nothing", level(item)[1] == (15, 4, 11))
check("a reservation of zero is refused", problem(post("reserve", {"item": item, "quantity": 0}), 400, "stock-invalid"))
check("a reservation for an item with no stock is refused", problem(post("reserve", {"item": new_item("Empty"), "quantity": 1}), 409, "insufficient-stock"))
check("a hold of no minutes is refused", problem(post("reserve", {"item": item, "quantity": 1, "minutes": 0}), 400, "stock-invalid"))
rest = post("reserve", {"item": item, "quantity": 11})
check("the rest can be reserved", rest.status_code == 201 and level(item)[1] == (15, 15, 0), rest.text)
check("the last item goes to nobody else", problem(post("reserve", {"item": item, "quantity": 1}), 409, "insufficient-stock"))

released = post(f"release/{rest.json()['reservation']}")
check("a release gives the hold back", released.status_code == 200 and level(item)[1] == (15, 4, 11), released.text)
check("a hold that is released cannot be released again", problem(post(f"release/{rest.json()['reservation']}"), 409, "reservation-closed"))
check("a hold that is released cannot be committed", problem(post(f"commit/{rest.json()['reservation']}"), 409, "reservation-closed"))
committed = post(f"commit/{hold_id}")
check("a commit turns the hold into a sale: on hand and reserved both go down", committed.status_code == 200 and level(item)[1] == (11, 0, 11), committed.text)
check("a hold that is committed cannot be committed again", problem(post(f"commit/{hold_id}"), 409, "reservation-closed"))
check("an unknown hold is refused", problem(post("commit/987654"), 404, "stock-unknown"))

kinds = sorted(r["kind"] for r in ledger_rows(item))
check("the ledger has a row for each change", kinds == sorted(["receipt", "return", "reservation", "reservation", "release", "release", "sale"]), kinds)
check("a sale is a negative row", [r["quantity"] for r in ledger_rows(item) if r["kind"] == "sale"] == [-4])

count = post("adjust", {"item": item, "counted": 9, "reference": "count-" + run})
check("a stock-take sets what is on hand and answers the difference", count.status_code == 200 and count.json().get("difference") == -2 and level(item)[1] == (9, 0, 9), count.text)
check("a stock-take writes an adjustment row of the difference", [r["quantity"] for r in ledger_rows(item) if r["kind"] == "adjustment"] == [-2])
check("a stock-take that finds no difference still writes a row", post("adjust", {"item": item, "counted": 9}).json().get("difference") == 0 and len([r for r in ledger_rows(item) if r["kind"] == "adjustment"]) == 2)
check("a stock-take needs the counted quantity", problem(post("adjust", {"item": item}), 400, "stock-invalid"))
check("a negative count is refused", problem(post("adjust", {"item": item, "counted": -1}), 400, "stock-invalid"))
held = post("reserve", {"item": item, "quantity": 5})
check("a count below what is reserved is refused", problem(post("adjust", {"item": item, "counted": 4}), 409, "stock-below-reserved"))
check("a refused count changed nothing", level(item)[1] == (9, 5, 4))
check("a stock-take of an item with no stock row adds it", post("adjust", {"item": new_item("Counted"), "counted": 3}).status_code == 200)

check("the level is worked out again from the ledger without a difference for this item", all(d["item"] != item for d in client.get(f"{LEDGER}/check").json().get("differences", [])), client.get(f"{LEDGER}/check").text)

# two buyers, one item left
race_item = new_item("Race")
post("receive", {"item": race_item, "quantity": 1})
with ThreadPoolExecutor(max_workers=8) as pool:
    answers = list(pool.map(lambda _: post("reserve", {"item": race_item, "quantity": 1}).status_code, range(8)))
check("the last item goes to one buyer only", answers.count(201) == 1 and answers.count(409) == 7, answers)
check("the level stays whole after the race", level(race_item)[1] == (1, 1, 0))

# a hold that runs out
expiring = new_item("Expiring")
post("receive", {"item": expiring, "quantity": 3})
short = post("reserve", {"item": expiring, "quantity": 3, "minutes": 1})
check("a short hold is made", short.status_code == 201 and level(expiring)[1] == (3, 3, 0), short.text)
database = os.environ.get("STOCK_SQLITE")
ran_out = False
if database:
    with sqlite3.connect(database) as db:
        db.execute("UPDATE StockReservation SET ExpiresOn = '2000-01-01 00:00:00' WHERE StockReservationId = ?", (short.json()["reservation"],))
    ran_out = True
elif wait:
    time.sleep(65)
    ran_out = True
if ran_out:
    check("a hold that has run out cannot be committed", problem(post(f"commit/{short.json()['reservation']}"), 409, "reservation-closed"))
    check("the refused commit gave the stock back", level(expiring)[1] == (3, 0, 3), level(expiring)[0].text)
    again = post("reserve", {"item": expiring, "quantity": 3, "minutes": 1})
    check("the stock can be reserved again", again.status_code == 201)
    if database:
        with sqlite3.connect(database) as db:
            db.execute("UPDATE StockReservation SET ExpiresOn = '2000-01-01 00:00:00' WHERE StockReservationId = ?", (again.json()["reservation"],))
        expired = post("expire")
        check("expire gives back every hold that has run out", expired.status_code == 200 and expired.json().get("expired", 0) >= 1 and level(expiring)[1] == (3, 0, 3), expired.text)
        check("a reservation frees what has run out by itself", post("reserve", {"item": expiring, "quantity": 3}).status_code == 201)
else:
    print("skip a hold that runs out (set STOCK_SQLITE or pass wait)")
check("expire answers the number of holds it closed", isinstance(post("expire").json().get("expired"), int))

low = client.get(f"{LEDGER}/low")
check("low answers a list", low.status_code == 200 and isinstance(low.json(), list), low.text)

check("the level table cannot be edited by hand: a row starts empty", problem(client.post(LEVELS, json={"stockLevelId": 0, "itemId": new_item("Hand"), "onHand": 5, "reserved": 0, "reorderLevel": 0}), 400, "stock-managed"))
rows = client.get(LEVELS, params={"pageSize": 200}).json()
rows = rows if isinstance(rows, list) else rows.get("items") or rows.get("data") or []
mine = next((r for r in rows if r.get("itemId") == item), None)
check("the level table is listed", mine is not None, rows[:1])
if mine:
    check("an update that changes the quantity is refused", problem(client.put(f"{LEVELS}/{mine['stockLevelId']}", json={**mine, "onHand": mine["onHand"] + 1}), 400, "stock-managed"))
    check("an update of the reorder level is allowed", client.put(f"{LEVELS}/{mine['stockLevelId']}", json={**mine, "reorderLevel": 6}).status_code in (200, 204))
    check("a level that holds stock cannot be deleted", problem(client.delete(f"{LEVELS}/{mine['stockLevelId']}"), 409, "stock-managed"))
    low_now = client.get(f"{LEDGER}/low").json()
    check("an item under its reorder level is on the low list", any(r["item"] == item for r in low_now) and next(r for r in low_now if r["item"] == item)["reorderLevel"] == 6, low_now[:3])

some = ledger_rows(item)[0]
check("a ledger row is never changed", client.put(f"{LEDGER}/{some['stockLedgerId']}", json=some).status_code == 405)
check("a ledger row is never deleted", client.delete(f"{LEDGER}/{some['stockLedgerId']}").status_code == 405)
check("the ledger has no plain POST", client.post(LEDGER, json={}).status_code == 405)
check("the holds are listed and never changed", client.get(HOLDS).status_code == 200 and client.post(HOLDS, json={}).status_code == 405)

print("ALL OK" if not failures else f"{len(failures)} FAILED")
sys.exit(1 if failures else 0)
