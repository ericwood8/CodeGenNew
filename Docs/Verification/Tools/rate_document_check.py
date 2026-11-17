"""Plays RateDocuments and RateInverse over HTTP against a running generated ASP.NET API over a scratch copy of Schemas/rate_documents.sql.

python rate_document_check.py <base url> [table [rate table [base currency]]]

The rate table is ExchangeRate and the base currency USD unless others are named (RateDocuments=...:InvoiceDate:CardRate:EUR). The table of the invoices is SalesInvoice unless another is named (a table with the columns of Schemas/rate_documents.sql: CustomerName, CurrencyCode, FxRate, Total, BaseTotal, InvoiceDate, Notes).

The API is generated with RateTables=ExchangeRate, RateDocuments=SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate, BaseCurrency=USD and RateInverse=true. A sales invoice is written with
the rate in force on its date (1 for the base currency, 400 rate-needed when there is none), keeps that rate while its currency and date stay, and gets the amount times the rate as its converted
amount; the rate lookup answers a pair from the opposite pair (one divided by the rate, header X-Rate-Inverted) when that row is newer. With ApiAuth set API_USER and API_PASSWORD. The script adds
rates and invoices and removes them again, so use a scratch database."""
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
rates = []
invoices = []
RATE_TABLE = sys.argv[3] if len(sys.argv) > 3 else "ExchangeRate"
BASE = (sys.argv[4] if len(sys.argv) > 4 else "USD").upper()
RATES = "/api/" + RATE_TABLE.lower() + "s"
RATE_KEY = RATE_TABLE[0].lower() + RATE_TABLE[1:] + "Id"
TABLE = sys.argv[2] if len(sys.argv) > 2 else "SalesInvoice"
INVOICES = "/api/" + TABLE.lower() + "s"
KEY = TABLE[0].lower() + TABLE[1:] + "Id"
tag = uuid.uuid4().hex[:3].upper()
euro = "E" + tag[:2]
yen = "Y" + tag[1:]


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def rate(source, target, value, day):
    made = client.post(RATES, json={RATE_KEY: 0, "fromCurrency": source, "toCurrency": target, "rate": value, "rateDate": day})
    assert made.status_code == 201, made.text
    rates.append(made.json()[RATE_KEY])


def invoice(currency, total, day, fx=99, base_total=1, key=0):
    row = {KEY: key, "customerName": "Check " + tag, "currencyCode": currency, "fxRate": fx, "total": total, "baseTotal": base_total, "invoiceDate": day, "notes": None}
    made = client.post(INVOICES, json=row) if key == 0 else client.put(f"{INVOICES}/{key}", json=row)
    if made.status_code == 201:
        invoices.append(made.json()[KEY])
    return made


def stored(key):
    found = client.get(f"{INVOICES}/{key}").json()
    return float(found["fxRate"]), float(found["baseTotal"]), found["currencyCode"], found["invoiceDate"][:10]


rate(euro, BASE, 1.1, "2024-01-01")
rate(euro, BASE, 1.2, "2024-03-01")
rate(BASE, yen, 0.8, "2024-01-01")

home = invoice(" " + BASE.lower() + " ", 100, "2024-02-15")
check("an invoice in the base currency gets rate 1 and its own amount", home.status_code == 201 and stored(home.json()[KEY])[:3] == (1.0, 100.0, BASE), home.text)
first = invoice(" " + euro.lower() + " ", 100, "2024-02-15")
one = first.json().get(KEY, 0)
check("an invoice gets the rate in force on its date and the converted amount (the values the caller sent are ignored)", first.status_code == 201 and stored(one) == (1.1, 110.0, euro, "2024-02-15"), (first.text, stored(one) if one else ""))
second = invoice(euro, 100, "2024-03-01")
two = second.json().get(KEY, 0)
check("the day a rate starts gets that rate", second.status_code == 201 and stored(two)[:2] == (1.2, 120.0), second.text)
early = invoice(euro, 100, "2023-12-31")
check("a date before the first rate is refused (400 rate-needed)", early.status_code == 400 and "rate-needed" in early.text, (early.status_code, early.text))
unknown = invoice("XXQ", 100, "2024-02-15")
check("a currency with no rate is refused (400 rate-needed)", unknown.status_code == 400 and "rate-needed" in unknown.text, (unknown.status_code, unknown.text))
blank = invoice("  ", 100, "2024-02-15")
check("a blank currency is refused", blank.status_code == 400 and "rate-invalid" in blank.text, (blank.status_code, blank.text))
check("nothing was kept for the refused ones", len(invoices) == 3, invoices)

rate(euro, BASE, 1.3, "2024-02-10")
row = client.get(f"{INVOICES}/{one}").json()
row["total"] = 200
changed = client.put(f"{INVOICES}/{one}", json={**row, "fxRate": 5, "baseTotal": 5})
check("a change of the amount keeps the rate the row was written with, though a newer rate was added", changed.status_code in (200, 204) and stored(one)[:2] == (1.1, 220.0), (changed.text, stored(one)))
row["invoiceDate"] = "2024-03-01"
moved = client.put(f"{INVOICES}/{one}", json=row)
check("a new date takes the rate of that date", moved.status_code in (200, 204) and stored(one)[:2] == (1.2, 240.0), (moved.text, stored(one)))
row["currencyCode"] = BASE.lower()
back = client.put(f"{INVOICES}/{one}", json=row)
check("a change to the base currency takes rate 1", back.status_code in (200, 204) and stored(one)[:3] == (1.0, 200.0, BASE), (back.text, stored(one)))
row["currencyCode"] = "XXQ"
refused = client.put(f"{INVOICES}/{one}", json=row)
check("a change to a currency with no rate is refused and keeps the row", refused.status_code == 400 and stored(one)[2] == BASE, (refused.status_code, refused.text))

# the opposite pair
japan = invoice(yen, 100, "2024-02-15")
check("an invoice is converted by the opposite pair's rate", japan.status_code == 201 and stored(japan.json()[KEY])[:2] == (1.25, 125.0), japan.text)
look = client.get(RATES + "/rate", params={"from": yen, "to": BASE, "on": "2024-02-15"})
check("the lookup answers the inverse with the pair asked for and a header", look.status_code == 200 and look.headers.get("X-Rate-Inverted") == "true"
      and (look.json()["fromCurrency"], look.json()["toCurrency"], float(look.json()["rate"])) == (yen, BASE, 1.25), (look.status_code, look.text, dict(look.headers)))
direct = client.get(RATES + "/rate", params={"from": euro, "to": BASE, "on": "2024-02-15"})
check("a direct rate has no inverse header", direct.status_code == 200 and "X-Rate-Inverted" not in direct.headers and float(direct.json()["rate"]) == 1.3, (direct.status_code, direct.text, dict(direct.headers)))
rate(BASE, euro, 0.8, "2024-03-01")
newer = client.get(RATES + "/rate", params={"from": euro, "to": BASE, "on": "2024-04-01"})
check("an opposite pair row on the same day as the direct one does not win", newer.status_code == 200 and float(newer.json()["rate"]) == 1.2 and "X-Rate-Inverted" not in newer.headers, newer.text)
rate(BASE, euro, 0.5, "2024-03-15")
newest = client.get(RATES + "/rate", params={"from": euro, "to": BASE, "on": "2024-04-01"})
check("a newer opposite pair row wins over an older direct one", newest.status_code == 200 and newest.headers.get("X-Rate-Inverted") == "true" and float(newest.json()["rate"]) == 2.0, (newest.status_code, newest.text))
none = client.get(RATES + "/rate", params={"from": "XXQ", "to": BASE, "on": "2024-04-01"})
check("a pair with no rate in either direction has none", none.status_code == 404, none.status_code)

for key in invoices:
    client.delete(f"{INVOICES}/{key}")
for key in rates:
    client.delete(f"{RATES}/{key}")
print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
