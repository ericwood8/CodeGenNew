"""Sends many line creates for one invoice at the same moment and checks that the totals add up all of them (the header row is updated first, so each save waits for the one before it).

python document_totals_race_check.py <base url> [requests] [rounds]

Against the API generated with the settings of Matrix/document_totals.settings. With ApiAuth set API_USER and API_PASSWORD. The script adds rows and removes them again."""
import concurrent.futures
import os
import sys
import uuid

import httpx

base = sys.argv[1]
requests = int(sys.argv[2]) if len(sys.argv) > 2 else 20
rounds = int(sys.argv[3]) if len(sys.argv) > 3 else 3
client = httpx.Client(base_url=base, timeout=60)
if os.environ.get("API_USER"):
    token = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]}).json()
    client.headers["Authorization"] = "Bearer " + next(token[k] for k in ("token", "accessToken", "access_token") if k in token)
tag = uuid.uuid4().hex[:6].upper()
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


invoice = client.post("/api/invoices", json={"invoiceId": 0, "customer": "Race " + tag, "invoiceDate": "2024-06-01", "currency": "USD", "itemsTotal": 0, "allowanceTotal": 0, "chargeTotal": 0, "taxExclusive": 0, "taxTotal": 0,
                                              "taxInclusive": 0, "prepaid": 0, "rounding": 0, "payable": 0}).json()["invoiceId"]
made = []


def add(_):
    row = {"invoiceLineId": 0, "invoiceId": invoice, "lineDate": "2024-06-01", "description": "Race " + tag, "taxCategoryId": None, "taxRate": 0, "lineTotal": 10, "taxAmount": 0}
    response = httpx.post(base + "/api/invoicelines", json=row, headers=client.headers, timeout=60)
    return response.status_code, response.json().get("invoiceLineId") if response.status_code == 201 else None


try:
    for number in range(1, rounds + 1):
        with concurrent.futures.ThreadPoolExecutor(max_workers=requests) as pool:
            results = list(pool.map(add, range(requests)))
        made += [key for status, key in results if key]
        created = sum(1 for status, _ in results if status == 201)
        row = client.get(f"/api/invoices/{invoice}").json()
        expected = 10 * len(made)
        check(f"round {number}: {created} of {requests} lines were created", created == requests, [status for status, _ in results if status != 201][:5])
        check(f"round {number}: the lines total adds up every line created ({expected})", abs(float(row["itemsTotal"]) - expected) < 0.005, row["itemsTotal"])
        check(f"round {number}: the payable amount follows", abs(float(row["payable"]) - expected) < 0.005, row["payable"])
finally:
    for key in made:
        client.delete(f"/api/invoicelines/{key}")
    client.delete(f"/api/invoices/{invoice}")
print("FAILED" if failures else "ALL OK", len(failures))
sys.exit(1 if failures else 0)
