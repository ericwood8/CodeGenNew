"""Plays the payment ledger rules over HTTP against a running generated API over a scratch copy of Schemas/payments.sql.

python ledger_check.py <base url>

The API is generated with LedgerTables=PayTransaction:PayDocumentId:PayDocument:Total (a ledger table is also a log table). The rules it plays: POST record stores a row and answers 201 with it;
the kind and the status are written in lower case; a refund is a negative amount and any other kind a positive one; a gateway id and an idempotency key are each recorded once (409 ledger-duplicate);
a succeeded refund cannot be more than what has been paid so far (400 refund-over-paid); a row is never changed or deleted (405); GET paid?document= answers the captured amount, the refunded amount,
what is paid and what is left, counting only succeeded rows.
With ApiAuth set API_USER and API_PASSWORD. The ledger keeps its rows, so use a scratch database."""
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
run = uuid.uuid4().hex[:10]
count = 0


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def record(document, kind, amount, status="succeeded", gateway=None, key=None, payload=None):
    global count
    count += 1
    gateway = gateway or f"gw-{run}-{count}"
    key = key or f"key-{run}-{count}"
    return client.post("/api/paytransactions/record", json={"payTransactionId": 0, "payDocumentId": document, "kind": kind, "status": status, "amount": amount, "currencyCode": "USD", "gatewayId": gateway, "idempotencyKey": key, "payload": payload})


def problem(response, status, kind):
    return response.status_code == status and response.json().get("type", "").endswith(kind)


def paid(document):
    response = client.get("/api/paytransactions/paid", params={"document": document})
    body = response.json() if response.status_code == 200 else {}
    return response, tuple(float(body.get(k, -1)) for k in ("total", "captured", "refunded", "paid", "remaining")), body


document = client.post("/api/paydocuments", json={"payDocumentId": 0, "number": "L" + run[:6], "orderedOn": "2027-03-01", "total": 300})
doc = document.json()["payDocumentId"]

first = record(doc, "Authorize", 300)
check("an authorize row is stored and answers 201", first.status_code == 201, first.text)
check("the kind is written in lower case", first.status_code == 201 and first.json().get("kind") == "authorize", first.text)
check("the time of the row is filled in", first.status_code == 201 and bool(first.json().get("createdOn")), first.text)
check("an authorize is not paid yet", paid(doc)[1] == (300.0, 0.0, 0.0, 0.0, 300.0), paid(doc)[0].text)

capture = record(doc, "capture", 200, payload='{"card":"tok_1"}')
check("a capture is stored", capture.status_code == 201, capture.text)
check("the paid amount is worked out from the ledger", paid(doc)[1] == (300.0, 200.0, 0.0, 200.0, 100.0), paid(doc)[0].text)
check("a pending capture is not counted", record(doc, "capture", 50, status="pending").status_code == 201 and paid(doc)[1][1] == 200.0)
check("a failed capture is not counted", record(doc, "capture", 50, status="failed").status_code == 201 and paid(doc)[1][1] == 200.0)

check("a refund with a positive amount is refused", problem(record(doc, "refund", 50), 400, "payment-invalid"))
refund = record(doc, "refund", -50)
check("a refund is a new negative row", refund.status_code == 201 and float(refund.json()["amount"]) == -50, refund.text)
check("a refund lowers what is paid", paid(doc)[1] == (300.0, 200.0, 50.0, 150.0, 150.0), paid(doc)[0].text)
check("a refund of more than has been paid is refused", problem(record(doc, "refund", -151), 400, "refund-over-paid"))
check("a refund of exactly what has been paid is stored", record(doc, "refund", -150).status_code == 201 and paid(doc)[1][3] == 0.0)
check("a failed refund is not counted against the paid amount", record(doc, "refund", -999, status="failed").status_code == 201)

check("a capture with a negative amount is refused", problem(record(doc, "capture", -10), 400, "payment-invalid"))
check("an amount of zero is refused", problem(record(doc, "void", 0), 400, "payment-invalid"))
check("a void is stored", record(doc, "void", 300).status_code == 201)
check("an unknown kind is refused", problem(record(doc, "charge", 10), 400, "payment-invalid"))
check("an unknown status is refused", problem(record(doc, "capture", 10, status="done"), 400, "payment-invalid"))
check("a document that does not exist is refused", problem(record(987654, "capture", 10), 400, "payment-invalid"))
check("a blank gateway id is refused", problem(record(doc, "capture", 10, gateway=" "), 400, "payment-invalid"))

check("a gateway id that is recorded is refused", problem(record(doc, "capture", 5, gateway=first.json()["gatewayId"]), 409, "ledger-duplicate"))
retry = record(doc, "capture", 5, key=first.json()["idempotencyKey"])
check("a retried call with the same idempotency key is refused and writes nothing", problem(retry, 409, "ledger-duplicate"))
check("the retry changed nothing", paid(doc)[1][1] == 200.0)

changed = client.put(f"/api/paytransactions/{first.json()['payTransactionId']}", json=first.json())
check("a row is never changed", changed.status_code == 405, changed.status_code)
removed = client.delete(f"/api/paytransactions/{first.json()['payTransactionId']}")
check("a row is never deleted", removed.status_code == 405, removed.status_code)
check("the table has no plain POST", client.post("/api/paytransactions", json={}).status_code == 405)
listed = client.get("/api/paytransactions")
check("the ledger is listed", listed.status_code == 200, listed.text)
check("a paid answer needs the document", client.get("/api/paytransactions/paid").status_code == 400)
check("a paid answer for a document that does not exist is refused", problem(client.get("/api/paytransactions/paid", params={"document": 987654}), 400, "payment-invalid"))
check("a document paid in full is settled", record(doc, "capture", 300).status_code == 201 and paid(doc)[2].get("settled") is True)

print("ALL OK" if not failures else f"{len(failures)} FAILED")
sys.exit(1 if failures else 0)
