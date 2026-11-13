"""Plays the PolymorphicLinks rules against a running API over a scratch copy of Schemas/polymorphic.sql.

python polymorphic_check.py <base url>

The API is a generated one (ASP.NET, Python or Rust) with
PolymorphicLinks=Attachment:OwnerType:OwnerId:Product|Customer|Opportunity,Remark:OwnerKind:OwnerRef:Product|Customer:remove
Attachment refuses the delete of a row it names; Remark removes its rows with the row. With ApiAuth set API_USER and API_PASSWORD. The script adds rows and removes them again, so use a scratch database."""
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
tag = uuid.uuid4().hex[:8]


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def problem_type(response):
    try:
        return response.json().get("type", "")
    except Exception:
        return ""


def make(route, **values):
    response = client.post(route, json=values)
    assert response.status_code == 201, (route, response.status_code, response.text)
    return response.json()


def rows(route):
    data = client.get(route).json()
    return data if isinstance(data, list) else next(v for v in data.values() if isinstance(v, list))


product = make("/api/products", productId=0, name="Widget " + tag, sku="W-" + tag, price=5, category=None, notes=None)
customer = make("/api/customers", customerId=0, name="Cust " + tag, email=None, city=None, phone=None, notes=None)
quiet = make("/api/customers", customerId=0, name="Quiet " + tag, email=None, city=None, phone=None, notes=None)


def attach(kind, ref, **values):
    return client.post("/api/attachments", json={"attachmentId": 0, "ownerType": kind, "ownerId": ref, "title": "Brochure " + tag, "url": None, **values})


unknown = attach("Invoice", product["productId"])
check("a record type no table is listed for is refused (400)", unknown.status_code == 400 and problem_type(unknown).endswith("link-type-unknown"), unknown.text)
check("the refusal names the listed tables", "Product, Customer, Opportunity" in unknown.text, unknown.text)
missing = attach("Product", 987654)
check("an id that is no row of the named table is refused (400)", missing.status_code == 400 and problem_type(missing).endswith("link-target-missing"), missing.text)
blank = attach("", product["productId"])
check("a blank type is refused", blank.status_code == 400, blank.text)
wrong_table = attach("Customer", product["productId"] + 50000)
check("an id that exists only in another table is refused", wrong_table.status_code == 400, wrong_table.text)

saved = attach("product", product["productId"])
check("a link to an existing row is saved (201)", saved.status_code == 201, saved.text)
link = saved.json() if saved.status_code == 201 else {}
check("the type is written in the table's own spelling", link.get("ownerType") == "Product", link)

moved = client.put(f"/api/attachments/{link.get('attachmentId')}", json={**link, "ownerType": "CUSTOMER", "ownerId": customer["customerId"]})
check("an update may move the link to another table", moved.status_code in (200, 204), moved.text)
now = next((r for r in rows("/api/attachments") if r["attachmentId"] == link.get("attachmentId")), {})
check("the moved link holds the other table's spelling and id", now.get("ownerType") == "Customer" and now.get("ownerId") == customer["customerId"], now)
bad_move = client.put(f"/api/attachments/{link.get('attachmentId')}", json={**now, "ownerType": "Opportunity", "ownerId": 987654})
check("an update to a row that does not exist is refused", bad_move.status_code == 400, bad_move.text)

in_use = client.delete(f"/api/customers/{customer['customerId']}")
check("a row that links name is not deleted (409)", in_use.status_code == 409 and problem_type(in_use).endswith("link-target-in-use"), in_use.text)
check("the row is still there", any(r["customerId"] == customer["customerId"] for r in rows("/api/customers")))
other = client.delete(f"/api/products/{product['productId']}")
check("a row no link names is deleted", other.status_code in (200, 204), other.text)

removed = client.delete(f"/api/attachments/{link.get('attachmentId')}")
check("the link itself can be deleted", removed.status_code in (200, 204), removed.text)
again = client.delete(f"/api/customers/{customer['customerId']}")
check("with its links gone the row is deleted", again.status_code in (200, 204), again.text)

# Remark removes its rows with the row
remark = client.post("/api/remarks", json={"remarkId": 0, "ownerKind": "customer", "ownerRef": quiet["customerId"], "body": "Call back " + tag})
check("a remark of a customer is saved", remark.status_code == 201 and remark.json().get("ownerKind") == "Customer", remark.text)
unknown_remark = client.post("/api/remarks", json={"remarkId": 0, "ownerKind": "Opportunity", "ownerRef": 1, "body": "x"})
check("a table this link table does not list is refused", unknown_remark.status_code == 400, unknown_remark.text)
gone = client.delete(f"/api/customers/{quiet['customerId']}")
check("a row with remarks is deleted", gone.status_code in (200, 204), gone.text)
check("its remarks are removed with it", not any(r.get("body") == "Call back " + tag for r in rows("/api/remarks")), rows("/api/remarks"))

print("FAILED: " + ", ".join(failures) if failures else "OK")
sys.exit(1 if failures else 0)
