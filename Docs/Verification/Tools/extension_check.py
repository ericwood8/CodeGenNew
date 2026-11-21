"""Plays the one-to-one extension save (Customer.CustomerProfile in EditableChildren) over HTTP against a running ASP.NET API over a scratch or test copy of InvoiceSystem.

python extension_check.py <base url>

The customer's create and update take the profile as one object under customerProfile: an object adds or changes the row in the same transaction, null removes it, a body without the property
leaves it alone, and a bad object rolls the customer's own change back. With ApiAuth set API_USER and API_PASSWORD. The script adds a customer and removes it again."""
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
tag = uuid.uuid4().hex[:6].upper()


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def profile_of(customer_id):
    response = client.get(f"/api/customerprofiles/{customer_id}")
    return response.json() if response.status_code == 200 else None


sample = client.get("/api/customers", params={"pageSize": 1}).json()
rows = sample["items"] if isinstance(sample, dict) and "items" in sample else sample
template = {k: v for k, v in rows[0].items() if k != "customerProfile"}
template["customerId"] = 0
template["accountNumber"] = "EXT" + tag
template["customerName"] = "Extension check " + tag

created = client.post("/api/customers", json={**template, "customerProfile": {"preferredLanguage": "fr", "preferredCurrency": "EUR", "loyaltyPoints": 5, "marketingOptIn": True}})
check("create with the profile object", created.status_code == 201, created.text)
customer_id = created.json()["customerId"] if created.status_code == 201 else None
try:
    if customer_id is not None:
        profile = profile_of(customer_id)
        check("the profile row was written with the customer", bool(profile) and profile["preferredLanguage"] == "fr" and profile["loyaltyPoints"] == 5, profile)

        changed = client.put(f"/api/customers/{customer_id}", json={**template, "customerId": customer_id, "customerProfile": {"preferredLanguage": "de", "preferredCurrency": "EUR", "loyaltyPoints": 9, "marketingOptIn": False}})
        profile = profile_of(customer_id)
        check("an object changes the profile row", changed.is_success and profile and profile["preferredLanguage"] == "de" and profile["loyaltyPoints"] == 9, (changed.status_code, changed.text, profile))

        left = client.put(f"/api/customers/{customer_id}", json={**template, "customerId": customer_id, "customerName": "Left alone " + tag})
        profile = profile_of(customer_id)
        check("a body without the property leaves the profile alone", left.is_success and profile and profile["preferredLanguage"] == "de", (left.status_code, left.text, profile))

        bad = client.put(f"/api/customers/{customer_id}", json={**template, "customerId": customer_id, "customerName": "Must roll back " + tag, "customerProfile": {"preferredLanguage": "x" * 50, "preferredCurrency": "EUR", "loyaltyPoints": 1, "marketingOptIn": False}})
        name = client.get(f"/api/customers/{customer_id}").json()["customerName"]
        check("a bad profile object is refused and the customer's own change rolls back", not bad.is_success and name == "Left alone " + tag, (bad.status_code, name))

        removed = client.put(f"/api/customers/{customer_id}", json={**template, "customerId": customer_id, "customerProfile": None})
        check("null removes the profile row", removed.is_success and profile_of(customer_id) is None, (removed.status_code, removed.text))

        again = client.put(f"/api/customers/{customer_id}", json={**template, "customerId": customer_id, "customerProfile": {"preferredLanguage": "en", "preferredCurrency": "USD", "loyaltyPoints": 0, "marketingOptIn": False}})
        check("an object adds the row again", again.is_success and profile_of(customer_id) is not None, (again.status_code, again.text))
finally:
    if customer_id is not None:
        if profile_of(customer_id) is not None:
            refused = client.delete(f"/api/customers/{customer_id}")
            check("delete of a customer that has a profile follows the foreign key's rule (refused with no cascade, removed with one)", refused.status_code == 400 or (refused.status_code in (200, 204) and profile_of(customer_id) is None), refused.text)
            if refused.status_code == 400:
                client.put(f"/api/customers/{customer_id}", json={**template, "customerId": customer_id, "customerProfile": None})
        if client.get(f"/api/customers/{customer_id}").status_code == 200:
            gone = client.delete(f"/api/customers/{customer_id}")
            check("the customer is deleted once it has no profile", gone.status_code in (200, 204) and client.get(f"/api/customers/{customer_id}").status_code == 404, gone.text)

print(len(failures), "failed" if failures else "all ok")
sys.exit(1 if failures else 0)
