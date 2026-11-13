"""Plays the SnapshotColumns rules against a running API over a scratch copy of Schemas/snapshot.sql.

python snapshot_check.py <base url> [--snake] [--soft]

The API is a generated one (ASP.NET, Python or Rust) with
SnapshotColumns=OrderItem:ProductName<-Product.Name+UnitPrice<-Product.Price+TaxRate<-TaxCategory.Rate:LineTotal=Quantity*UnitPrice.
With ApiAuth set API_USER and API_PASSWORD. The script adds a product, a tax category, an order and lines and removes the lines again, so use a scratch database."""
import os
import re
import sys
import uuid
from decimal import Decimal

import httpx

base = sys.argv[1]
client = httpx.Client(base_url=base, timeout=30)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
failures = []
SNAKE = "--snake" in sys.argv
tag = uuid.uuid4().hex[:8]
ITEMS = "/api/order_items" if SNAKE else "/api/orderitems"
PRODUCTS = "/api/products"
TAXES = "/api/tax_categories" if SNAKE else "/api/taxcategories"
ORDERS = "/api/sales_orders" if SNAKE else "/api/salesorders"


def K(name):
    return re.sub(r"([A-Z])", lambda m: "_" + m.group(1).lower(), name) if SNAKE else name


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def money(value):
    return None if value is None else Decimal(str(value))


def make(route, payload, key):
    response = client.post(route, json=payload)
    assert response.status_code == 201, response.text
    return response.json()[K(key)]


product = make(PRODUCTS, {K("productId"): 0, "name": "Widget " + tag, "price": 10.5, "sku": None, "description": None, "unit": None}, "productId")
tax = make(TAXES, {K("taxCategoryId"): 0, "name": "Standard " + tag, "rate": 8.25, "code": None, "notes": None, "region": None}, "taxCategoryId")
order = make(ORDERS, {K("salesOrderId"): 0, "reference": "SO-" + tag, "customer": "Acme", "note": None, "channel": None, "remarks": None}, "salesOrderId")


def line(quantity=3, tax_id=None, **over):
    payload = {K("orderItemId"): 0, K("salesOrderId"): order, K("productId"): product, K("taxCategoryId"): tax_id, K("productName"): "", K("unitPrice"): 0, K("taxRate"): None, "quantity": quantity, K("lineTotal"): 0}
    payload.update(over)
    return client.post(ITEMS, json=payload)


def get(item_id):
    return client.get(f"{ITEMS}/{item_id}").json()


made = []
first = line(3, tax)
check("a line is created", first.status_code == 201, first.text)
row = first.json()
made.append(row[K("orderItemId")])
check("the product name is copied", row[K("productName")] == "Widget " + tag, row)
check("the product price is copied", money(row[K("unitPrice")]) == Decimal("10.5"), row)
check("the tax rate is copied", money(row[K("taxRate")]) == Decimal("8.25"), row)
check("the total is the quantity times the price", money(row[K("lineTotal")]) == Decimal("31.5"), row)

wrong = line(2, None, **{K("productName"): "Anything", K("unitPrice"): 99, K("lineTotal"): 5})
check("what the caller sends for a copy is ignored", wrong.status_code == 201 and wrong.json()[K("productName")] == "Widget " + tag and money(wrong.json()[K("unitPrice")]) == Decimal("10.5"), wrong.text)
made.append(wrong.json()[K("orderItemId")])
check("a line without a tax category has no rate", wrong.json()[K("taxRate")] is None, wrong.text)
check("the total of that line ignores what was sent", money(wrong.json()[K("lineTotal")]) == Decimal("21"), wrong.text)

client.put(f"{PRODUCTS}/{product}", json={K("productId"): product, "name": "Gadget " + tag, "price": 20.5, "sku": None, "description": None, "unit": None}).raise_for_status()
check("a change of the product does not touch the stored line", get(made[0])[K("productName")] == "Widget " + tag and money(get(made[0])[K("unitPrice")]) == Decimal("10.5"), get(made[0]))

bare = client.post(ITEMS, json={K("orderItemId"): 0, K("salesOrderId"): order, K("productId"): product, K("taxCategoryId"): tax, "quantity": 2})
check("a body without the copied columns and the total is accepted", bare.status_code == 201 and bare.json()[K("productName")] == "Gadget " + tag and money(bare.json()[K("lineTotal")]) == Decimal("41"), bare.text)
if bare.status_code == 201:
    made.append(bare.json()[K("orderItemId")])

stored = get(made[0])
stored.update({K("productName"): "Changed", K("unitPrice"): 1, K("taxRate"): 50, "quantity": 4})
update = client.put(f"{ITEMS}/{made[0]}", json=stored)
check("an update is accepted", update.status_code in (200, 204), update.text)
after = get(made[0])
check("an update keeps the copies", after[K("productName")] == "Widget " + tag and money(after[K("unitPrice")]) == Decimal("10.5") and money(after[K("taxRate")]) == Decimal("8.25"), after)
check("an update works the total out again", money(after[K("lineTotal")]) == Decimal("42"), after)

later = line(1)
check("a line made after the change copies the new product", later.status_code == 201 and later.json()[K("productName")] == "Gadget " + tag and money(later.json()[K("unitPrice")]) == Decimal("20.5"), later.text)
made.append(later.json()[K("orderItemId")])

missing = line(1, **{K("productId"): 999999})
check("a product that is not there is refused", missing.status_code == 400 and "snapshot-parent-missing" in missing.text, missing.text)
nope = line(1, 999999)
check("a tax category that is not there is refused", nope.status_code == 400 and "snapshot-parent-missing" in nope.text, nope.text)

for item in made:
    client.delete(f"{ITEMS}/{item}")
check("a line can be deleted", client.get(f"{ITEMS}/{made[0]}").status_code == 404)
print("OK" if not failures else "FAILED: " + ", ".join(failures))
sys.exit(1 if failures else 0)
