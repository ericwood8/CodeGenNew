"""Plays the price list rules over HTTP against a running generated API over a scratch copy of Schemas/price_lists.sql.

python price_check.py <base url>

The API is generated with PriceListTables=PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList:CurrencyCode:ValidFrom:ValidTo:CustomerGroup. The rules it plays: a minimum quantity above zero, a price that is not negative, one price per
list, item and minimum quantity (409 price-taken, also on an update), the lists of one currency and group that may not overlap in time (409 effective-overlap), and GET price?item=&quantity=&on=&currency=&group= answering the row with the
largest minimum quantity not above the quantity among the lists of the currency valid on the day (404 price-missing otherwise; a group's lists first, then the lists with no group).
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
made = {"priceitems": [], "pricelists": [], "pricelistitems": []}


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def create(route, key, body):
    response = client.post("/api/" + route, json={key: 0, **body})
    if response.status_code == 201:
        made[route].append(response.json()[key])
    return response


def make_item(name):
    return create("priceitems", "priceItemId", {"name": name, "sku": None, "unitName": "each", "notes": None})


def make_list(name, currency, start, end, group=None):
    return create("pricelists", "priceListId", {"name": name, "currencyCode": currency, "validFrom": start, "validTo": end, "customerGroup": group})


def make_price(list_id, item, minimum, price):
    return create("pricelistitems", "priceListItemId", {"priceListId": list_id, "itemId": item, "minQuantity": minimum, "price": price})


def price_on(item, quantity=None, day=None, currency="USD", group=None):
    params = {"item": item, "currency": currency}
    if quantity is not None:
        params["quantity"] = quantity
    if day:
        params["on"] = day
    if group:
        params["group"] = group
    return client.get("/api/pricelistitems/price", params=params)


def amount(response):
    return float(response.json()["price"]) if response.status_code == 200 else None


# a currency no other run of this script uses keeps the lists of the table apart from this run's
tag = uuid.uuid4().hex[:6]
item = make_item("Item " + tag).json()["priceItemId"]
other_item = make_item("Other " + tag).json()["priceItemId"]

first = make_list("Standard " + tag, "USD", "2024-01-01", "2024-12-31")
check("a price list is saved", first.status_code == 201, first.text)
if first.status_code != 201:
    sys.exit(1)
standard = first.json()["priceListId"]
overlap = make_list("Overlap " + tag, "usd", "2024-06-01", "2025-06-30")
check("a list of the same currency (whatever the case) that overlaps in time is refused (409 effective-overlap)", overlap.status_code == 409 and "effective-overlap" in overlap.text, (overlap.status_code, overlap.text))
gold = make_list("Gold " + tag, "USD", "2024-01-01", "2024-12-31", "GOLD").json()["priceListId"]
check("a list of another group may cover the same days", gold is not None)
later = make_list("Later " + tag, "USD", "2025-01-01", None).json()["priceListId"]
check("the next period of the same currency and group may follow", later is not None)
euro = make_list("Euro " + tag, "EUR", "2024-01-01", "2024-12-31").json()["priceListId"]
check("a list of another currency may cover the same days", euro is not None)

# the rule on a row
check("a minimum quantity of 1 is saved", make_price(standard, item, 1, 10.00).status_code == 201)
check("a minimum quantity of 10 is saved", make_price(standard, item, 10, 9.00).status_code == 201)
check("a minimum quantity of 100 is saved", make_price(standard, item, 100, 8.00).status_code == 201)
taken = make_price(standard, item, 10, 9.50)
check("a second price for the list, item and minimum quantity is the 409 of a price taken", taken.status_code == 409 and "price-taken" in taken.text, (taken.status_code, taken.text))
check("the same minimum quantity in another list is another price", make_price(gold, item, 1, 9.50).status_code == 201)
check("the same minimum quantity for another item is another price", make_price(standard, other_item, 1, 3.00).status_code == 201)
zero = make_price(standard, item, 0, 11.00)
check("a minimum quantity of zero is refused", zero.status_code == 400 and "price-invalid" in zero.text, (zero.status_code, zero.text))
check("a negative minimum quantity is refused", make_price(standard, item, -5, 11.00).status_code == 400)
negative = make_price(standard, item, 500, -1)
check("a negative price is refused", negative.status_code == 400 and "price-invalid" in negative.text, (negative.status_code, negative.text))
check("a price of zero is a free price", make_price(standard, item, 1000, 0).status_code == 201)
check("a break with decimals is saved", make_price(standard, item, 2.5, 9.75).status_code == 201)
check("a price for the later list is saved", make_price(later, item, 1, 11.00).status_code == 201)

# the price in force
check("quantity 1 gets the first break", amount(price_on(item, 1, "2024-06-01")) == 10.00, price_on(item, 1, "2024-06-01").text)
check("no quantity means 1", amount(price_on(item, None, "2024-06-01")) == 10.00)
check("quantity 2 still gets the first break", amount(price_on(item, 2, "2024-06-01")) == 10.00)
check("quantity 2.5 gets the break that starts there", amount(price_on(item, 2.5, "2024-06-01")) == 9.75)
check("quantity 9 gets the break below it", amount(price_on(item, 9, "2024-06-01")) == 9.75)
check("quantity 10 gets the break that starts at 10", amount(price_on(item, 10, "2024-06-01")) == 9.00)
check("quantity 99.999 stays on the break below 100", amount(price_on(item, 99.999, "2024-06-01")) == 9.00)
check("quantity 100 gets the break that starts at 100", amount(price_on(item, 100, "2024-06-01")) == 8.00)
check("a large quantity gets the largest break", amount(price_on(item, 5000, "2024-06-01")) == 0.0)
check("the last day of a list still counts", amount(price_on(item, 1, "2024-12-31")) == 10.00)
check("the day before a list starts has no price (404 price-missing)", price_on(item, 1, "2023-12-31").status_code == 404 and "price-missing" in price_on(item, 1, "2023-12-31").text)
check("the day after the last day of a list falls to the next list", amount(price_on(item, 1, "2025-01-01")) == 11.00, price_on(item, 1, "2025-01-01").text)
check("a list with no end is in force for a later day", amount(price_on(item, 1, "2031-05-05")) == 11.00)
check("another item has its own price", amount(price_on(other_item, 4, "2024-06-01")) == 3.00)
check("an item with no row has no price", price_on(other_item + 100000, 1, "2024-06-01").status_code == 404)
check("a currency with no list has no price", price_on(item, 1, "2024-06-01", "eur").status_code == 404)
check("the currency is read in any case", amount(price_on(item, 1, "2024-06-01", " usd ")) == 10.00)
check("a group's list comes first", amount(price_on(item, 1, "2024-06-01", "USD", "GOLD")) == 9.50)
check("the group is read in any case", amount(price_on(item, 1, "2024-06-01", "USD", " gold ")) == 9.50)
check("a group's list wins even where the standard list has a larger break", amount(price_on(item, 10, "2024-06-01", "USD", "GOLD")) == 9.50)
check("an item the group's list has no row for falls back to the lists with no group", amount(price_on(other_item, 4, "2024-06-01", "USD", "GOLD")) == 3.00)
check("a day the group's list does not cover falls back to the lists with no group", amount(price_on(item, 1, "2025-02-01", "USD", "GOLD")) == 11.00)
check("with no group the group's list is not read", amount(price_on(item, 1, "2024-06-01")) == 10.00)
check("an unknown group reads the lists with no group", amount(price_on(item, 10, "2024-06-01", "USD", "NOBODY")) == 9.00)
check("a quantity of zero is refused", price_on(item, 0, "2024-06-01").status_code == 400)
check("a negative quantity is refused", price_on(item, -1, "2024-06-01").status_code == 400)
check("a lookup with no item is refused", client.get("/api/pricelistitems/price", params={"currency": "USD"}).status_code == 400)
check("a lookup with no currency is refused", client.get("/api/pricelistitems/price", params={"item": item}).status_code == 400)

# an update that moves a break onto one the list already holds
hundred = next(i for i in made["pricelistitems"] if client.get(f"/api/pricelistitems/{i}").json()["minQuantity"] == 100)
row = client.get(f"/api/pricelistitems/{hundred}").json()
row["minQuantity"] = 10
moved = client.put(f"/api/pricelistitems/{hundred}", json=row)
check("moving a break onto one the list already holds is refused", moved.status_code == 409 and "price-taken" in moved.text, (moved.status_code, moved.text))
row["minQuantity"] = 100
row["price"] = 7.50
check("changing the price of a row keeps its own break", client.put(f"/api/pricelistitems/{hundred}", json=row).status_code in (200, 204))
check("the changed price is the one in force", amount(price_on(item, 100, "2024-06-01")) == 7.50)
row["price"] = -3
check("an update to a negative price is refused", client.put(f"/api/pricelistitems/{hundred}", json=row).status_code == 400)

for route in ("pricelistitems", "pricelists", "priceitems"):
    for key in made[route]:
        client.delete(f"/api/{route}/{key}")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
