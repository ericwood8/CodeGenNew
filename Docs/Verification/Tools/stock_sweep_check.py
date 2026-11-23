"""Proves the scheduled sweep of the stock holds (StockSweepMinutes=1) over a scratch SQLite copy of Schemas/stock.sql.

python stock_sweep_check.py <base url>

STOCK_SQLITE names the SQLite file. The check reserves stock, moves the end of the hold into the past, then touches no stock route and reads the database until the hold is expired: the sweep did it, not a reserve, a commit,
a release or the expire route. The first run is one interval after the start, so the check waits up to two and a half minutes."""
import os
import sqlite3
import sys
import time
import uuid

import httpx

base = sys.argv[1]
database = os.environ["STOCK_SQLITE"]
client = httpx.Client(base_url=base, timeout=60)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
LEDGER = os.environ.get("STOCK_LEDGER_ROUTE", "/api/stockledgers")
ITEMS = os.environ.get("STOCK_ITEM_ROUTE", "/api/stockitems")
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


created = client.post(ITEMS, json={"stockItemId": 0, "name": f"Sweep {uuid.uuid4().hex[:8]}"})
created.raise_for_status()
item = next(v for k, v in created.json().items() if k.lower() == "stockitemid")
client.post(f"{LEDGER}/receive", json={"item": item, "quantity": 4}).raise_for_status()
hold = client.post(f"{LEDGER}/reserve", json={"item": item, "quantity": 4, "minutes": 1})
check("a hold is made", hold.status_code == 201, hold.text)
hold_id = hold.json()["reservation"]
with sqlite3.connect(database) as db:
    db.execute("UPDATE StockReservation SET ExpiresOn = '2000-01-01 00:00:00' WHERE StockReservationId = ?", (hold_id,))


def status():
    with sqlite3.connect(database) as db:
        return db.execute("SELECT Status FROM StockReservation WHERE StockReservationId = ?", (hold_id,)).fetchone()[0]


check("the hold is still held before the sweep runs", status() == "held", status())
deadline = time.time() + 150
while time.time() < deadline and status() == "held":
    time.sleep(5)
check("the sweep expired the hold by itself", status() == "expired", status())
with sqlite3.connect(database) as db:
    released = db.execute("SELECT COALESCE(SUM(Quantity), 0) FROM StockLedger WHERE ItemId = ? AND Kind = 'release'", (item,)).fetchone()[0]
check("the sweep wrote the release row", abs(released) == 4, released)
available = client.get(f"{LEDGER}/available", params={"item": item}).json()
check("the stock is available again", (available.get("onHand"), available.get("reserved"), available.get("available")) == (4, 0, 4), available)
print("FAILED: " + ", ".join(failures) if failures else "ALL OK")
sys.exit(1 if failures else 0)
