"""Plays TaxLines over HTTP against a running generated ASP.NET API over a scratch copy of Schemas/tax_lines.sql.

python tax_check.py <base url> [--dated]

--dated plays the category's validity (TaxLines=...:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code) and the subtotals route as well.

The API is generated with TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate. A line is written with the rate of its category and the tax worked out from it; the rate is kept
when the category stays, even after the category's own rate changes, and is taken again when the category changes; an unknown category is a 400 tax-category-unknown and a line with no category carries rate 0. With ApiAuth set API_USER
and API_PASSWORD. The script adds rows and removes them again, so use a scratch database."""
import os
import sys
import uuid

import httpx

base = sys.argv[1]
dated = "--dated" in sys.argv
client = httpx.Client(base_url=base, timeout=30)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
CATEGORIES = os.environ.get("TAX_CATEGORIES", "/api/taxcategories")
LINES = os.environ.get("TAX_LINES", "/api/invoicelines")
failures = []
categories = []
lines = []
tag = uuid.uuid4().hex[:6].upper()
invoice = 900000 + int(uuid.uuid4().int % 90000)


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def category(code, rate, valid_from="2024-01-01", valid_to="2024-12-31"):
    made = client.post(CATEGORIES, json={"taxCategoryId": 0, "code": code + tag, "name": "Check " + tag, "rate": rate, "exemptionReason": None, "validFrom": valid_from, "validTo": valid_to})
    assert made.status_code == 201, made.text
    categories.append(made.json()["taxCategoryId"])
    return categories[-1]


def line(cat, total, key=0, rate=99, tax=1, day="2024-06-01"):
    row = {"invoiceLineId": key, "invoiceId": invoice, "lineDate": day, "description": "Check " + tag, "taxCategoryId": cat, "taxRate": rate, "lineTotal": total, "taxAmount": tax}
    made = client.post(LINES, json=row) if key == 0 else client.put(f"{LINES}/{key}", json=row)
    if made.status_code == 201:
        lines.append(made.json()["invoiceLineId"])
    return made


def stored(key):
    return client.get(f"{LINES}/{key}").json()


try:
    twenty = category("T20", 0.2)
    made = line(twenty, 50, rate=7, tax=7)
    check("a line is created with 201", made.status_code == 201, made.text)
    key = made.json()["invoiceLineId"]
    row = stored(key)
    check("the rate is the category's, not the one sent", float(row["taxRate"]) == 0.2, row)
    check("the tax is the line total times the rate", float(row["taxAmount"]) == 10, row)

    rounded = line(twenty, 33.33)
    check("the tax is rounded to the scale of its column", rounded.status_code == 201 and float(stored(rounded.json()["invoiceLineId"])["taxAmount"]) == 6.67, rounded.text)

    changed = client.put(f"{CATEGORIES}/{twenty}", json={"taxCategoryId": twenty, "code": "T20" + tag, "name": "Check " + tag, "rate": 0.25, "exemptionReason": None, "validFrom": "2024-01-01", "validTo": "2024-12-31"})
    check("the category's rate can change", changed.status_code in (200, 204), changed.text)
    kept = line(twenty, 100, key=key, rate=1, tax=1)
    row = stored(key)
    check("an update keeps the rate the line was written with", kept.status_code in (200, 204) and float(row["taxRate"]) == 0.2, (kept.text, row))
    check("and works the tax out with it", float(row["taxAmount"]) == 20, row)

    fresh = line(twenty, 100)
    check("a new line takes the category's new rate", float(stored(fresh.json()["invoiceLineId"])["taxRate"]) == 0.25, fresh.text)

    zero = category("T00", 0)
    moved = line(zero, 100, key=key)
    row = stored(key)
    check("a changed category brings its rate", moved.status_code in (200, 204) and float(row["taxRate"]) == 0 and float(row["taxAmount"]) == 0, (moved.text, row))

    unknown = line(999999, 10)
    check("an unknown category is a 400 tax-category-unknown", unknown.status_code == 400 and unknown.json().get("type", "").endswith("tax-category-unknown"), unknown.text)

    none = line(None, 80, rate=5, tax=5)
    check("a line with no category carries rate 0 and tax 0", none.status_code == 201 and float(stored(none.json()["invoiceLineId"])["taxRate"]) == 0 and float(stored(none.json()["invoiceLineId"])["taxAmount"]) == 0, none.text)
    if dated:
        late = line(twenty, 10, day="2025-02-01")
        check("a day after the category ended is a 400 tax-category-not-in-force", late.status_code == 400 and late.json().get("type", "").endswith("tax-category-not-in-force"), late.text)
        early = line(twenty, 10, day="2023-12-31")
        check("a day before the category began is a 400 tax-category-not-in-force", early.status_code == 400 and early.json().get("type", "").endswith("tax-category-not-in-force"), early.text)
        edge = line(twenty, 10, day="2024-12-31")
        check("the last day of the category is in force", edge.status_code == 201, edge.text)
        open_ended = category("TOP", 0.05, "2020-01-01", None)
        far = line(open_ended, 200, day="2031-01-01")
        check("a category with no end is in force later", far.status_code == 201 and float(stored(far.json()["invoiceLineId"])["taxAmount"]) == 10, far.text)
        sub = client.get(LINES + "/tax-subtotals", params={"document": invoice})
        check("the subtotals answer 200", sub.status_code == 200, sub.text)
        by = {(r["category"], float(r["rate"])): r for r in sub.json()} if sub.status_code == 200 else {}
        row = by.get((twenty, 0.25))
        check("a subtotal per category and rate: the new rate", row is not None and row["lines"] == 2 and float(row["taxable"]) == 110 and float(row["tax"]) == 27.5, by)
        old = by.get((twenty, 0.2))
        check("the lines written at the old rate have their own subtotal", old is not None and old["lines"] == 1 and float(old["taxable"]) == 33.33 and float(old["tax"]) == 6.67, by)
        check("a subtotal carries the category's code", row is not None and str(row["label"]).startswith("T20"), row)
        none_row = by.get((None, 0.0))
        check("lines with no category are one subtotal", none_row is not None and float(none_row["taxable"]) == 80, by)
        check("the subtotals ask for a document", client.get(LINES + "/tax-subtotals").status_code == 400)
finally:
    for key in lines:
        client.delete(f"{LINES}/{key}")
    for key in categories:
        client.delete(f"{CATEGORIES}/{key}")

print("FAILED: " + ", ".join(failures) if failures else "all passed")
sys.exit(1 if failures else 0)
