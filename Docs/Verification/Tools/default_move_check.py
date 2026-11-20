"""Plays a default row that changes group (DefaultRowTables=CustomerAddress:IsDefault:CustomerId+AddressType) against a running API over a scratch copy of Schemas/default_rows.sql.

python default_move_check.py <base url> [--address-route /api/customeraddresses] [--snake]

The rule every stack follows: the default row of a group cannot be moved to another group while its own group has other rows (409: make another row the default first), nor into a group that has a
default of its own (409); a row that is not the default moves freely and joins the new group without the mark; the only row of a group may move into a group that has no rows. A refused save
changes nothing. With ApiAuth set API_USER and API_PASSWORD. The script adds a customer and addresses and removes them again, so use a scratch database.
"""
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
ADDRESSES = sys.argv[sys.argv.index("--address-route") + 1] if "--address-route" in sys.argv else "/api/customeraddresses"
SNAKE = "--snake" in sys.argv
tag = uuid.uuid4().hex[:8]
failures = []


def K(name):
    return re.sub(r"([A-Z])", lambda m: "_" + m.group(1).lower(), name) if SNAKE else name


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:400]))
    if not ok:
        failures.append(label)


def rows(route):
    data = client.get(route).json()
    return data if isinstance(data, list) else next(v for v in data.values() if isinstance(v, list))


def address(owner, kind, default, line):
    response = client.post(ADDRESSES, json={K("customerAddressId"): 0, K("customerId"): owner, K("addressType"): kind, K("line1"): line, K("line2"): None, K("city"): "Springfield",
                                            K("region"): None, K("postalCode"): None, K("country"): "US", K("isDefault"): default})
    assert response.status_code == 201, response.text
    return response.json()[K("customerAddressId")]


def of(owner, kind):
    return sorted((r for r in rows(ADDRESSES) if r[K("customerId")] == owner and r[K("addressType")] == kind), key=lambda r: r[K("customerAddressId")])


def defaults(owner, kind):
    return [r[K("customerAddressId")] for r in of(owner, kind) if r[K("isDefault")]]


def row(key):
    return next(r for r in rows(ADDRESSES) if r[K("customerAddressId")] == key)


def move(key, kind):
    return client.put(f"{ADDRESSES}/{key}", json={**row(key), K("addressType"): kind})


def snapshot(owner):
    return {kind: [(r[K("customerAddressId")], r[K("isDefault")]) for r in of(owner, kind)] for kind in ("billing", "shipping")}


customer = client.post("/api/customers", json={K("customerId"): 0, "name": "Mover " + tag, "email": None, K("city"): None, "phone": None, "notes": None})
assert customer.status_code == 201, customer.text
owner = customer.json()[K("customerId")]
made = []

# billing: b1 (default), b2; shipping: s1 (default)
b1 = address(owner, "billing", False, "1 Main St")
b2 = address(owner, "billing", False, "2 Side St")
s1 = address(owner, "shipping", False, "3 Ship Ln")
made += [b1, b2, s1]
check("setup: one default per group", defaults(owner, "billing") == [b1] and defaults(owner, "shipping") == [s1], snapshot(owner))

# a default row moved into a group that has a default, from a group with other rows
before = snapshot(owner)
refused = move(b1, "shipping")
check("the default row of a group with other rows cannot move into a group that has a default (409)", refused.status_code == 409, (refused.status_code, refused.text))
check("a refused move changes nothing", snapshot(owner) == before, (before, snapshot(owner)))

# a default row moved from a group with other rows into a group with no default (there is none: an empty group), still refused
# (b2 first becomes the only other row; here the group has b2, so the move is refused for the same reason)
refused = move(b1, "billing")  # same group: not a move
check("a save that keeps the group is not a move", refused.status_code in (200, 204) and defaults(owner, "billing") == [b1], (refused.status_code, refused.text, snapshot(owner)))

# a row that is not the default moves freely and joins the new group without the mark
moved = move(b2, "shipping")
check("a row that is not the default moves into another group", moved.status_code in (200, 204), (moved.status_code, moved.text))
check("it joins the new group without the mark and leaves both defaults as they were", defaults(owner, "shipping") == [s1] and defaults(owner, "billing") == [b1], snapshot(owner))

# now b1 is the only billing row: it may move into a group that has no rows, but not into one with a default
refused = move(b1, "shipping")
check("the only row of a group cannot move into a group that has a default (409)", refused.status_code == 409, (refused.status_code, refused.text))
check("and nothing changed", defaults(owner, "billing") == [b1] and defaults(owner, "shipping") == [s1], snapshot(owner))


# a second customer: the default row of a group with other rows cannot move into an empty group (its own group would have no default), and the only row of a group can
second = client.post("/api/customers", json={K("customerId"): 0, "name": "Mover2 " + tag, "email": None, K("city"): None, "phone": None, "notes": None}).json()[K("customerId")]
c1 = address(second, "billing", False, "6 North St")
c2 = address(second, "billing", False, "7 South St")
made += [c1, c2]
leaves = move(c1, "shipping")
check("the default row of a group with other rows cannot move into an empty group (409, with the sentence)", leaves.status_code == 409 and "make another row the default" in leaves.text, (leaves.status_code, leaves.text))
check("and nothing changed (second customer)", defaults(second, "billing") == [c1] and not of(second, "shipping"), snapshot(second))
client.delete(f"{ADDRESSES}/{c2}")
alone = move(c1, "shipping")
check("the only row of a group moves into an empty group and stays the default", alone.status_code in (200, 204) and defaults(second, "shipping") == [c1] and not of(second, "billing"), (alone.status_code, alone.text, snapshot(second)))
check("the refusal of a move into a taken group names the other default", "already the default of that group" in refused.text, refused.text)
client.delete(f"{ADDRESSES}/{c1}")
client.delete(f"/api/customers/{second}")

for key in made:
    client.delete(f"{ADDRESSES}/{key}")
client.delete(f"/api/customers/{owner}")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
