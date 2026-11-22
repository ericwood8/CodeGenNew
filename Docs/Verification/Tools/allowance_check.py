"""Plays the allowance and charge rule over HTTP against a running ASP.NET API over a scratch copy of Schemas/allowance_charges.sql (AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId).

python allowance_check.py <base url> [document column in camel case [existing document key]]

The amount is worked out from the base and the percentage, or the percentage from the base and the amount; a negative value, a percentage with no base and a row with no amount are refused (400
allowance-charge-invalid); GET allowance-charge-totals?document= adds the allowances and the charges of one document. With ApiAuth set API_USER and API_PASSWORD. The script adds rows and removes them again."""
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
ROUTE = "/api/invoiceallowancecharges"
failures = []
made = []
document_name = sys.argv[2] if len(sys.argv) > 2 else "invoiceId"
document = int(sys.argv[3]) if len(sys.argv) > 3 else 900000 + int(uuid.uuid4().int % 90000)


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def row(**values):
    return {"invoiceAllowanceChargeId": 0, document_name: document, "isCharge": False, "reasonCode": None, "baseAmount": None, "percentage": None, "amount": 0, **values}


def save(**values):
    response = client.post(ROUTE, json=row(**values))
    if response.status_code == 201:
        made.append(response.json()["invoiceAllowanceChargeId"])
    return response


def near(a, b):
    return a is not None and abs(float(a) - float(b)) < 0.00001


try:
    r = save(baseAmount=200, percentage=10, amount=999)
    check("a base and a percentage give the amount (the amount sent is ignored)", r.status_code == 201 and near(r.json()["amount"], 20), (r.status_code, r.text))

    r = save(baseAmount=80, percentage=12.5, isCharge=True)
    check("the amount is rounded to the column's scale", r.status_code == 201 and near(r.json()["amount"], 10), (r.status_code, r.text))

    r = save(baseAmount=3, percentage=0.5)
    check("a half-way amount rounds away from zero (0.015 to 0.02)", r.status_code == 201 and near(r.json()["amount"], 0.02), (r.status_code, r.text))

    r = save(baseAmount=200, amount=50, isCharge=True)
    check("a base and an amount give the percentage", r.status_code == 201 and near(r.json()["percentage"], 25), (r.status_code, r.text))

    r = save(amount=15)
    check("an amount alone is kept", r.status_code == 201 and near(r.json()["amount"], 15) and r.json()["percentage"] is None, (r.status_code, r.text))

    r = save(percentage=10, baseAmount=None)
    check("a percentage with no base is refused", r.status_code == 400 and r.json().get("type", "").endswith("allowance-charge-invalid"), (r.status_code, r.text))

    r = save(amount=0)
    check("a row with no amount and no base is refused", r.status_code == 400 and r.json().get("type", "").endswith("allowance-charge-invalid"), (r.status_code, r.text))

    r = save(amount=-5)
    check("a negative amount is refused", r.status_code == 400 and r.json().get("type", "").endswith("allowance-charge-invalid"), (r.status_code, r.text))

    r = save(baseAmount=-100, percentage=5)
    check("a negative base is refused", r.status_code == 400, (r.status_code, r.text))

    first = made[0]
    r = client.put(f"{ROUTE}/{first}", json=row(invoiceAllowanceChargeId=first, baseAmount=200, percentage=20, amount=1))
    check("an update works the amount out again", r.is_success and near(client.get(f"{ROUTE}/{first}").json()["amount"], 40), (r.status_code, r.text))

    r = client.get(f"{ROUTE}/allowance-charge-totals", params={"document": document})
    totals = r.json() if r.status_code == 200 else {}
    # allowances: 40 (the first row, updated) + 0.02 + 15; charges: 10 + 50
    check("the totals add the allowances and the charges of the document", r.status_code == 200 and near(totals.get("allowances"), 55.02) and near(totals.get("charges"), 60) and near(totals.get("net"), 4.98) and totals.get("rows") == 5, (r.status_code, r.text))

    r = client.get(f"{ROUTE}/allowance-charge-totals")
    check("the totals ask for the document", r.status_code == 400, (r.status_code, r.text))

    r = client.get(f"{ROUTE}/allowance-charge-totals", params={"document": 2000000000})
    check("a document with no rows has zero totals", r.status_code == 200 and near(r.json().get("net"), 0) and r.json().get("rows") == 0, (r.status_code, r.text))
finally:
    for key in made:
        client.delete(f"{ROUTE}/{key}")

print(len(failures), "failed" if failures else "all ok")
sys.exit(1 if failures else 0)
