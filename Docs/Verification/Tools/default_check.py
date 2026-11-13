"""Plays the DefaultRowTables rules against a running API over a scratch copy of Schemas/default_rows.sql (or the CustomerAddress table of InvoiceSystem).

python default_check.py <base url> [--existing] [--address-route /api/customer_addresses] [--snake] [--soft]

The API is a generated one (ASP.NET) with DefaultRowTables=CustomerAddress:IsDefault:CustomerId+AddressType. With ApiAuth set API_USER and API_PASSWORD.
The script adds customers and addresses and removes them again, so use a scratch database. With --existing the two customers are the first two of GET /api/customers (a database that already has customers whose other columns the script cannot fill in) and are left alone."""
import os
import re
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
ADDRESSES = sys.argv[sys.argv.index("--address-route") + 1] if "--address-route" in sys.argv else "/api/customeraddresses"
tag = uuid.uuid4().hex[:8]


SNAKE = "--snake" in sys.argv   # a project whose JSON names keep the database's snake_case (customer_address_id)


def K(name):
    return re.sub(r"([A-Z])", lambda m: "_" + m.group(1).lower(), name) if SNAKE else name


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def rows(route):
    data = client.get(route).json()
    return data if isinstance(data, list) else next(v for v in data.values() if isinstance(v, list))


existing = [r[K("customerId")] for r in rows("/api/customers")[:2]] if "--existing" in sys.argv else []


def customer(name):
    if existing:
        return existing.pop(0)
    response = client.post("/api/customers", json={K("customerId"): 0, "name": name + " " + tag, "email": None, K("city"): None, "phone": None, "notes": None})
    assert response.status_code == 201, response.text
    return response.json()[K("customerId")]


def address(owner, kind, default, line="1 Main St"):
    return client.post(ADDRESSES, json={K("customerAddressId"): 0, K("customerId"): owner, K("addressType"): kind, K("line1"): line, K("line2"): None, K("city"): "Springfield",
                                                          K("region"): None, K("postalCode"): None, K("country"): "US", K("isDefault"): default, **({K("isDeleted"): False} if "--soft" in sys.argv else {})})


def of(owner, kind):
    return sorted((r for r in rows(ADDRESSES) if r[K("customerId")] == owner and r[K("addressType")] == kind), key=lambda r: r[K("customerAddressId")])


def defaults(owner, kind):
    return [r[K("customerAddressId")] for r in of(owner, kind) if r[K("isDefault")]]


owner = customer("Home")
other = customer("Office")
made = []

first = address(owner, "billing", False)
made.append(first.json()[K("customerAddressId")] if first.status_code == 201 else 0)
check("the first address of a group is saved", first.status_code == 201, first.text)
check("the first address of a group is the default whatever was sent", defaults(owner, "billing") == [made[0]], of(owner, "billing"))

second = address(owner, "billing", False, "2 Side St")
made.append(second.json()[K("customerAddressId")])
check("a later address sent without the mark is not the default", defaults(owner, "billing") == [made[0]], of(owner, "billing"))

third = address(owner, "billing", True, "3 New Rd")
made.append(third.json()[K("customerAddressId")])
check("an address saved with the mark takes it from the old default", defaults(owner, "billing") == [made[2]], of(owner, "billing"))

ship = address(owner, "shipping", False, "4 Ship Ln")
made.append(ship.json()[K("customerAddressId")])
check("each type has a default of its own", defaults(owner, "shipping") == [made[3]] and defaults(owner, "billing") == [made[2]], (of(owner, "shipping"), of(owner, "billing")))
elsewhere = address(other, "billing", False, "5 Far Ave")
made.append(elsewhere.json()[K("customerAddressId")])
check("each customer has a default of its own", defaults(other, "billing") == [made[4]] and defaults(owner, "billing") == [made[2]])

row = next(r for r in of(owner, "billing") if r[K("customerAddressId")] == made[0])
moved = client.put(f"{ADDRESSES}/{made[0]}", json={**row, K("isDefault"): True})
check("an update that sets the mark moves it", moved.status_code in (200, 204) and defaults(owner, "billing") == [made[0]], (moved.text, of(owner, "billing")))
row = next(r for r in of(owner, "billing") if r[K("customerAddressId")] == made[0])
cleared = client.put(f"{ADDRESSES}/{made[0]}", json={**row, K("isDefault"): False})
check("an update that clears the only default leaves it set", cleared.status_code in (200, 204) and defaults(owner, "billing") == [made[0]], (cleared.text, of(owner, "billing")))

make = client.post(f"{ADDRESSES}/{made[1]}/make-default")
check("make-default moves the mark", make.status_code == 200 and defaults(owner, "billing") == [made[1]], (make.text, of(owner, "billing")))
again = client.post(f"{ADDRESSES}/{made[1]}/make-default")
check("make-default on the default changes nothing", again.status_code == 200 and defaults(owner, "billing") == [made[1]], again.text)
check("make-default of a row that is not there is a 404", client.post(f"{ADDRESSES}/987654/make-default").status_code == 404)

gone = client.delete(f"{ADDRESSES}/{made[1]}")
check("the default row can be deleted", gone.status_code in (200, 204), gone.text)
left = defaults(owner, "billing")
check("the mark moved to the next row of the group", len(left) == 1 and left[0] == min(r[K("customerAddressId")] for r in of(owner, "billing")), (left, of(owner, "billing")))
only = defaults(owner, "shipping")
last = client.delete(f"{ADDRESSES}/{only[0]}")
check("the only row of a group is deleted", last.status_code in (200, 204) and not of(owner, "shipping"), last.text)

for key in [r[K("customerAddressId")] for r in of(owner, "billing") + of(other, "billing")]:
    client.delete(f"{ADDRESSES}/{key}")
for key in (owner, other):
    if "--existing" not in sys.argv:
        client.delete(f"/api/customers/{key}")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
