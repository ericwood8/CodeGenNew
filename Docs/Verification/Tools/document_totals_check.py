"""Plays DocumentTotals over HTTP against a running generated API over a scratch copy of Schemas/document_totals.sql.

python document_totals_check.py <base url> [<sqlite file>]   (or the file in TOTALS_DB)

The API is generated with DocumentTotals=Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Allowances=AllowanceTotal:Charges=ChargeTotal:TaxExclusive=TaxExclusive:Tax=TaxTotal:TaxInclusive=TaxInclusive:Prepaid=Prepaid:Rounding=Rounding:Payable=Payable:Currency=Currency:Round=CHF/0.05,
TaxLines and AllowanceCharges. Plays: totals a client writes are replaced; each line, allowance and charge added, changed or removed brings the totals to the sums of the rows; the prepaid amount (the one column a
client writes) is subtracted; a Swiss-franc invoice is rounded to 0.05; the check route lists a header whose stored total was altered under the server (with the sqlite file given). With ApiAuth set API_USER and
API_PASSWORD. The script adds rows and removes them again, so use a scratch database."""
import os
import sqlite3
import sys
import uuid

import httpx

base = sys.argv[1]
database = sys.argv[2] if len(sys.argv) > 2 else os.environ.get("TOTALS_DB")
client = httpx.Client(base_url=base, timeout=30)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
INVOICES, LINES, EXTRAS, CATEGORIES = "/api/invoices", "/api/invoicelines", "/api/invoiceallowancecharges", "/api/taxcategories"
failures = []
tag = uuid.uuid4().hex[:6].upper()
made = {"lines": [], "extras": [], "invoices": [], "categories": []}


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:400]))
    if not ok:
        failures.append(label)


def invoice_body(key=0, currency="USD", prepaid=0, **extra):
    return {"invoiceId": key, "customer": "Check " + tag, "invoiceDate": "2024-06-01", "currency": currency, "itemsTotal": 0, "allowanceTotal": 0, "chargeTotal": 0, "taxExclusive": 0, "taxTotal": 0,
            "taxInclusive": 0, "prepaid": prepaid, "rounding": 0, "payable": 0, **extra}


def totals(key):
    row = client.get(f"{INVOICES}/{key}").json()
    return {n: round(float(row[c]), 2) for n, c in (("items", "itemsTotal"), ("allowances", "allowanceTotal"), ("charges", "chargeTotal"), ("exclusive", "taxExclusive"), ("tax", "taxTotal"),
                                                    ("inclusive", "taxInclusive"), ("prepaid", "prepaid"), ("rounding", "rounding"), ("payable", "payable"))}


def expect(label, key, **want):
    got = totals(key)
    check(label, all(abs(got[n] - v) < 0.0051 for n, v in want.items()), {n: (got[n], v) for n, v in want.items() if abs(got[n] - v) >= 0.0051})


def line(key, invoice, total, category=None, line_key=0):
    row = {"invoiceLineId": line_key, "invoiceId": invoice, "lineDate": "2024-06-01", "description": "Check " + tag, "taxCategoryId": category, "taxRate": 0, "lineTotal": total, "taxAmount": 0}
    response = client.post(LINES, json=row) if line_key == 0 else client.put(f"{LINES}/{line_key}", json=row)
    if response.status_code == 201:
        made["lines"].append(response.json()["invoiceLineId"])
    return response


def extra(invoice, charge, base, percentage, amount):
    response = client.post(EXTRAS, json={"invoiceAllowanceChargeId": 0, "invoiceId": invoice, "isCharge": charge, "reasonCode": None, "baseAmount": base, "percentage": percentage, "amount": amount})
    if response.status_code == 201:
        made["extras"].append(response.json()["invoiceAllowanceChargeId"])
    return response


try:
    category = client.post(CATEGORIES, json={"taxCategoryId": 0, "code": "T20" + tag, "name": "Check " + tag, "rate": 0.2, "exemptionReason": None, "validFrom": "2000-01-01", "validTo": None})
    check("a tax category is created", category.status_code == 201, category.text)
    cat = category.json()["taxCategoryId"]
    made["categories"].append(cat)

    created = client.post(INVOICES, json=invoice_body(itemsTotal=999, payable=999, taxTotal=5))
    check("an invoice is created with 201", created.status_code == 201, created.text)
    key = created.json()["invoiceId"]
    made["invoices"].append(key)
    expect("the totals a client wrote on a new invoice are replaced", key, items=0, tax=0, payable=0)
    check("and the answer to the create shows the worked-out ones", float(created.json()["payable"]) == 0, created.json())

    check("a taxed line is created", line(key, key, 100, cat).status_code == 201)
    expect("a line brings the lines, the tax and the payable amount", key, items=100, exclusive=100, tax=20, inclusive=120, payable=120)
    second = line(key, key, 50.5)
    check("an untaxed line is created", second.status_code == 201, second.text)
    expect("a line with no category adds to the lines and not to the tax", key, items=150.5, exclusive=150.5, tax=20, inclusive=170.5, payable=170.5)

    check("an allowance of 10 percent is created", extra(key, False, 150.5, 10, 0).status_code == 201)
    expect("an allowance is taken off before the tax", key, allowances=15.05, exclusive=135.45, inclusive=155.45, payable=155.45)
    check("a charge is created", extra(key, True, None, None, 10).status_code == 201)
    expect("a charge is added before the tax", key, charges=10, exclusive=145.45, inclusive=165.45, payable=165.45)

    paid = client.put(f"{INVOICES}/{key}", json=invoice_body(key, prepaid=65.45, itemsTotal=1, payable=1))
    check("the invoice can be saved with a prepaid amount", paid.status_code in (200, 204), paid.text)
    expect("the prepaid amount is taken off and the totals a client wrote are replaced", key, prepaid=65.45, items=150.5, payable=100)

    first = made["lines"][0]
    check("a line is changed", line(key, key, 200, cat, first).status_code in (200, 204))
    expect("a changed line brings the totals again", key, items=250.5, exclusive=245.45, tax=40, inclusive=285.45, payable=220)

    removed = client.delete(f"{LINES}/{made['lines'][1]}")
    check("a line is deleted", removed.status_code == 200, removed.text)
    made["lines"].pop(1)
    expect("a deleted line leaves the totals", key, items=200, exclusive=194.95, tax=40, inclusive=234.95, payable=169.5)

    franc = client.put(f"{INVOICES}/{key}", json=invoice_body(key, currency="CHF", prepaid=65.47))
    check("the invoice is saved in Swiss francs", franc.status_code in (200, 204), franc.text)
    expect("a Swiss-franc invoice is rounded to 0.05", key, rounding=0.02, payable=169.5)

    other = client.post(INVOICES, json=invoice_body())
    other_key = other.json()["invoiceId"]
    made["invoices"].append(other_key)
    check("a line of another invoice leaves this one alone", line(key, other_key, 10).status_code == 201)
    expect("the other invoice has its own lines", other_key, items=10, payable=10)
    expect("and the first one is unchanged", key, items=200, payable=169.5)

    moved = client.put(f"{LINES}/{made['lines'][-1]}", json={"invoiceLineId": made["lines"][-1], "invoiceId": key, "lineDate": "2024-06-01", "description": "Check " + tag, "taxCategoryId": None, "taxRate": 0, "lineTotal": 10, "taxAmount": 0})
    check("a line is moved to another invoice", moved.status_code in (200, 204), moved.text)
    expect("the invoice it left is brought up to date", other_key, items=0, payable=0)
    expect("and the invoice it joined too", key, items=210)

    differing = client.get(f"{INVOICES}/document-totals-check", params={"id": key})
    check("the check route finds nothing wrong", differing.status_code == 200 and differing.json()["differing"] == 0, differing.text)
    if database:
        with sqlite3.connect(database) as db:
            db.execute("UPDATE Invoice SET Payable = Payable + 5 WHERE InvoiceId = ?", (key,))
        differing = client.get(f"{INVOICES}/document-totals-check", params={"id": key})
        listed = differing.json() if differing.status_code == 200 else {}
        check("the check route lists a header whose stored total was altered under the server", listed.get("differing") == 1 and listed["headers"][0]["parts"][0]["part"] == "Payable", differing.text)
        client.put(f"{INVOICES}/{key}", json=invoice_body(key, currency="CHF", prepaid=65.47))
        differing = client.get(f"{INVOICES}/document-totals-check", params={"id": key})
        check("a save of the header brings it right", differing.json().get("differing") == 0, differing.text)
finally:
    for route, ids in ((EXTRAS, made["extras"]), (LINES, made["lines"]), (INVOICES, made["invoices"]), (CATEGORIES, made["categories"])):
        for item in ids:
            client.delete(f"{route}/{item}")
print("FAILED" if failures else "ALL OK", len(failures))
sys.exit(1 if failures else 0)
