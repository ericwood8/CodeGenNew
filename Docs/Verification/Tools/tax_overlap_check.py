"""Plays the validity periods of tax categories (the group columns of TaxLines) over HTTP against a running generated API over a scratch copy of Schemas/tax_lines.sql.

python tax_overlap_check.py <base url>

The API is generated with TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code:Name, so categories with the same Name are one group. It plays
a period that overlaps one of the group (409 effective-overlap), a period of another group (another name) over the same days, periods that follow each other, a backwards period, an open period that overlaps a later one, and a change
of a category that moves its period onto another. The script adds rows and removes them again, so use a scratch database. API_USER and API_PASSWORD sign in when the API needs it; TAX_CATEGORIES is the route."""
import os
import sys
import uuid

import httpx

client = httpx.Client(base_url=sys.argv[1], timeout=30)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
CATEGORIES = os.environ.get("TAX_CATEGORIES", "/api/taxcategories")
tag = uuid.uuid4().hex[:6].upper()
failures = []
made_keys = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def body(code, name, valid_from, valid_to, key=0):
    return {"taxCategoryId": key, "code": code + tag, "name": name + tag, "rate": 0.1, "exemptionReason": None, "validFrom": valid_from, "validTo": valid_to}


def add(code, name, valid_from, valid_to):
    made = client.post(CATEGORIES, json=body(code, name, valid_from, valid_to))
    if made.status_code == 201:
        made_keys.append(made.json()["taxCategoryId"])
    return made


def refused(made):
    return made.status_code == 400


def overlaps(made):
    return made.status_code == 409 and made.json().get("type", "").endswith("effective-overlap")


try:
    first = add("A1", "Standard", "2024-01-01", "2024-06-30")
    check("the first period of a group is saved", first.status_code == 201, first.text)
    check("a period that overlaps the group is a 409 effective-overlap", overlaps(add("A2", "Standard", "2024-06-30", "2024-12-31")), "saved")
    other = add("B1", "Reduced", "2024-01-01", "2024-06-30")
    check("the same days of another group are saved", other.status_code == 201, other.text)
    follow = add("A3", "Standard", "2024-07-01", "2024-12-31")
    check("a period that follows the last day is saved", follow.status_code == 201, follow.text)
    check("a period that runs backwards is a 400", refused(add("A4", "Backwards", "2024-12-31", "2024-01-01")), "saved")
    opened = add("A5", "Standard", "2025-01-01", None)
    check("an open period after the group is saved", opened.status_code == 201, opened.text)
    check("a period inside the open one is a 409 effective-overlap", overlaps(add("A6", "Standard", "2030-01-01", "2030-12-31")), "saved")
    if follow.status_code == 201:
        key = follow.json()["taxCategoryId"]
        moved = client.put(f"{CATEGORIES}/{key}", json=body("A3", "Standard", "2024-03-01", "2024-12-31", key))
        check("a change that moves the period onto another is a 409 effective-overlap", overlaps(moved), moved.text)
        same = client.put(f"{CATEGORIES}/{key}", json=body("A3", "Standard", "2024-07-01", "2024-12-31", key))
        check("a change that keeps the period is saved (a row does not overlap itself)", same.status_code in (200, 204), same.text)
finally:
    for key in made_keys:
        client.delete(f"{CATEGORIES}/{key}")

print("FAILED: " + ", ".join(failures) if failures else "all passed")
sys.exit(1 if failures else 0)
