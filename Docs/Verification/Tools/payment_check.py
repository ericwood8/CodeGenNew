"""Plays the payment rules over HTTP against a running generated API over a scratch copy of Schemas/payments.sql.

python payment_check.py <base url>

The API is generated with BankAccountTables=PartyBankAccount, InstallmentTables=PaymentInstallment:PayDocumentId:PayDocument:Total and PaymentTermsTables=PaymentMeans:PayDocumentId:PayDocument:OrderedOn.
The rules it plays: a bank account needs a valid IBAN (written without spaces, in upper case) or an account number with a valid routing number (400 payment-invalid); the installments of a document are numbered
(a repeated number is refused), fall due in the order of their numbers, are above zero and add up to no more than the document's total (400 installment-over-total); GET schedule?document= answers the
total, the sum scheduled and what is left; a payment means row with payment terms and no due date gets the document's date plus the days to pay, and a due date before the document's date is refused.
With ApiAuth set API_USER and API_PASSWORD. The script adds rows and removes them again, so use a scratch database."""
import os
import sys

import httpx

base = sys.argv[1]
client = httpx.Client(base_url=base, timeout=30)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
failures = []
made = {"paymentinstallments": [], "paymentmeans": [], "partybankaccounts": [], "paymentterms": [], "paydocuments": []}


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def create(route, key, body):
    response = client.post("/api/" + route, json={key: 0, **body})
    if response.status_code == 201:
        made[route].append(response.json()[key])
    return response


def account(iban=None, number=None, routing=None):
    return create("partybankaccounts", "partyBankAccountId", {"accountHolder": "Holder", "iban": iban, "accountNumber": number, "routingNumber": routing, "bankName": None})


def installment(document, sequence, due, amount):
    return create("paymentinstallments", "paymentInstallmentId", {"payDocumentId": document, "sequenceNo": sequence, "dueDate": due, "amount": amount})


def means(document, terms, due):
    return create("paymentmeans", "paymentMeansId", {"payDocumentId": document, "paymentTermsId": terms, "meansCode": "transfer", "dueDate": due})


def problem(response, kind):
    return response.status_code == 400 and response.json().get("type", "").endswith(kind)


try:
    # bank accounts
    good = account(iban="de89 3704 0044 0532 0130 00")
    check("a valid IBAN is stored without spaces and in upper case", good.status_code == 201 and good.json().get("iban") == "DE89370400440532013000", good.text)
    check("an IBAN with a wrong check digit is refused", problem(account(iban="DE89370400440532013001"), "payment-invalid"))
    check("an IBAN of the wrong length is refused", problem(account(iban="DE8937040044053201300"), "payment-invalid"))
    check("an IBAN of an unknown country is refused", problem(account(iban="ZZ89370400440532013000"), "payment-invalid"))
    us = account(number="123456789", routing="011000015")
    check("an account number with a valid routing number is stored", us.status_code == 201, us.text)
    check("a routing number whose check fails is refused", problem(account(number="123456789", routing="011000016"), "payment-invalid"))
    check("an account number with letters is refused", problem(account(number="12AB5678", routing="011000015"), "payment-invalid"))
    check("a bank account with neither an IBAN nor a pair is refused", problem(account(), "payment-invalid"))
    check("an account number with no routing number is refused", problem(account(number="123456789"), "payment-invalid"))
    changed = client.put(f"/api/partybankaccounts/{good.json()['partyBankAccountId']}", json={**good.json(), "iban": "DE89370400440532013001"})
    check("an update to a bad IBAN is refused", problem(changed, "payment-invalid"), changed.text)

    # installments
    document = create("paydocuments", "payDocumentId", {"number": "D1", "orderedOn": "2027-03-01", "total": 300})
    doc = document.json()["payDocumentId"]
    first = installment(doc, 1, "2027-04-01", 100)
    check("the first installment is stored", first.status_code == 201, first.text)
    check("the second installment is stored", installment(doc, 2, "2027-05-01", 100).status_code == 201)
    check("a repeated sequence number is refused", problem(installment(doc, 2, "2027-05-15", 50), "payment-invalid"))
    check("an installment due before an earlier one is refused", problem(installment(doc, 3, "2027-04-15", 50), "payment-invalid"))
    later = client.put(f"/api/paymentinstallments/{first.json()['paymentInstallmentId']}", json={**first.json(), "dueDate": "2027-06-01"})
    check("an installment moved to a day after a later one is refused", problem(later, "payment-invalid"), later.text)
    check("an amount of zero is refused", problem(installment(doc, 3, "2027-06-01", 0), "payment-invalid"))
    over = installment(doc, 3, "2027-06-01", 100.01)
    check("installments that add up to more than the total are refused", over.status_code == 400 and over.json().get("type", "").endswith("installment-over-total"), over.text)
    last = installment(doc, 3, "2027-06-01", 100)
    check("the installment that makes up the total is stored", last.status_code == 201, last.text)
    schedule = client.get("/api/paymentinstallments/schedule", params={"document": doc})
    body = schedule.json() if schedule.status_code == 200 else {}
    check("the schedule answers the total, the sum and what is left", schedule.status_code == 200 and float(body.get("total", -1)) == 300 and float(body.get("scheduled", -1)) == 300 and float(body.get("remaining", -1)) == 0 and body.get("complete") is True, schedule.text)
    raised = client.put(f"/api/paymentinstallments/{first.json()['paymentInstallmentId']}", json={**first.json(), "amount": 150})
    check("raising an installment past the total is refused", raised.status_code == 400 and raised.json().get("type", "").endswith("installment-over-total"), raised.text)
    kept = client.put(f"/api/paymentinstallments/{first.json()['paymentInstallmentId']}", json={**first.json(), "amount": 100})
    check("an installment can be saved again unchanged", kept.status_code in (200, 204), kept.text)
    check("the schedule of a document that does not exist is refused", problem(client.get("/api/paymentinstallments/schedule", params={"document": 987654}), "payment-invalid"))

    # due dates
    terms = create("paymentterms", "paymentTermsId", {"code": "NET30", "name": "Net 30", "daysToPay": 30, "earlyDiscountPercent": 2, "earlyDiscountDays": 10, "latePenaltyPercent": 1.5})
    net = terms.json()["paymentTermsId"]
    worked = means(doc, net, None)
    check("a row with terms and no due date gets the document date plus the days to pay", worked.status_code == 201 and str(worked.json().get("dueDate", ""))[:10] == "2027-03-31", worked.text)
    given = means(doc, net, "2027-06-15")
    check("a due date that is given is kept", given.status_code == 201 and str(given.json().get("dueDate", ""))[:10] == "2027-06-15", given.text)
    check("a due date before the date of the document is refused", problem(means(doc, net, "2027-02-01"), "payment-invalid"))
    plain = means(doc, None, None)
    check("a row with no terms and no due date stays without a due date", plain.status_code == 201 and plain.json().get("dueDate") is None, plain.text)

    # early-payment discount and late penalty
    def payable(row, day=None):
        params = {"id": row.json()["paymentMeansId"]}
        if day:
            params["on"] = day
        return client.get("/api/paymentmeans/payable", params=params)

    def figures(response):
        body = response.json() if response.status_code == 200 else {}
        return body.get("rule"), float(body.get("discount", -1)), float(body.get("penalty", -1)), float(body.get("payable", -1))

    check("paid inside the discount days the discount is taken off the total", figures(payable(worked, "2027-03-08")) == ("early-discount", 6.0, 0.0, 294.0), payable(worked, "2027-03-08").text)
    check("the last discount day still counts", figures(payable(worked, "2027-03-11"))[0] == "early-discount", payable(worked, "2027-03-11").text)
    check("the day after the discount days the total is payable", figures(payable(worked, "2027-03-12")) == ("on-time", 0.0, 0.0, 300.0), payable(worked, "2027-03-12").text)
    check("paid on the due day there is no penalty", figures(payable(worked, "2027-03-31")) == ("on-time", 0.0, 0.0, 300.0), payable(worked, "2027-03-31").text)
    check("paid after the due day the penalty is added", figures(payable(worked, "2027-04-05")) == ("late", 0.0, 4.5, 304.5), payable(worked, "2027-04-05").text)
    check("a row with no terms owes the total", figures(payable(plain, "2027-03-02")) == ("on-time", 0.0, 0.0, 300.0), payable(plain, "2027-03-02").text)
    check("a payment that does not exist is refused", problem(client.get("/api/paymentmeans/payable", params={"id": 987654}), "payment-invalid"))
    check("a payable answer needs the payment", client.get("/api/paymentmeans/payable").status_code == 400)
finally:
    for route in made:
        for key in reversed(made[route]):
            client.delete(f"/api/{route}/{key}")

print("ALL OK" if not failures else f"{len(failures)} FAILED")
sys.exit(1 if failures else 0)
