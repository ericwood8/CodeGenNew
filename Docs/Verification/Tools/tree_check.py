"""Plays the TreeTables rules against a running API over a scratch copy of Schemas/row_shapes.sql.

python tree_check.py <base url> [route]

The API is a generated one (ASP.NET, Python or Rust) with TreeTables=Category:ParentCategoryId:3; with ApiAuth set API_USER and API_PASSWORD (the bootstrap administrator). When the table is also in SoftDeleteTables the
soft delete cases run too. The route defaults to /api/categories. The script adds Category rows and removes
them again (SoftDeleteTables, if set, only flags them), so use a scratch database."""
import os
import sys

import httpx

base = sys.argv[1]
route = sys.argv[2] if len(sys.argv) > 2 else "/api/categories"
client = httpx.Client(base_url=base, timeout=30)
if os.environ.get("API_USER"):
    # a project with ApiAuth: sign in as the user named by API_USER and API_PASSWORD (never written in a file)
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    token = next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
    client.headers["Authorization"] = "Bearer " + token
failures = []
made = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def make(name, parent=None):
    body = {"categoryId": 0, "name": name, "description": None, "sortOrder": 0, "isDeleted": False, "parentCategoryId": parent}
    r = client.post(route, json=body)
    if r.status_code == 201:
        made.append(r.json())
    return r


def put(row, parent):
    return client.put(f"{route}/{row['categoryId']}", json={**row, "parentCategoryId": parent})


a = make("tree a").json()
b = make("tree b", a["categoryId"]).json()
c = make("tree c", b["categoryId"]).json()

r = make("tree d", c["categoryId"])
check("a fourth level is refused", r.status_code == 400 and r.json()["type"].endswith("tree-too-deep"), (r.status_code, r.text))
r = put(a, a["categoryId"])
check("a row as its own parent is refused", r.status_code == 400 and r.json()["type"].endswith("tree-cycle"), (r.status_code, r.text))
r = put(a, c["categoryId"])
check("a row under its own descendant is refused", r.status_code == 400 and r.json()["type"].endswith("tree-cycle"), (r.status_code, r.text))
other = make("tree z").json()
r = put(other, c["categoryId"])
check("moving a root under the deepest row is too deep", r.status_code == 400 and r.json()["type"].endswith("tree-too-deep"), (r.status_code, r.text))
r = put(a, other["categoryId"])
check("a branch that would pass the limit is refused", r.status_code == 400 and r.json()["type"].endswith("tree-too-deep"), (r.status_code, r.text))

nodes = client.get(route + "/tree").json()
ids = [n["id"] for n in nodes]
at = {n["id"]: n for n in nodes}
check("the tree lists a parent before its children", ids.index(a["categoryId"]) < ids.index(b["categoryId"]) < ids.index(c["categoryId"]), ids)
check("the tree gives the level and whether there are children", at[b["categoryId"]]["depth"] == at[a["categoryId"]]["depth"] + 1 and at[a["categoryId"]]["hasChildren"] and not at[c["categoryId"]]["hasChildren"], at)

r = client.delete(f"{route}/{b['categoryId']}")
check("a row with children is not deleted", r.status_code == 409 and r.json()["type"].endswith("tree-has-children"), (r.status_code, r.text))
r = client.post(f"{route}/{b['categoryId']}/promote-children")
check("the children go to the row's parent", r.status_code == 200 and r.json()["moved"] == 1, (r.status_code, r.text))
check("the grandchild is now under the grandparent", client.get(f"{route}/{c['categoryId']}").json()["parentCategoryId"] == a["categoryId"])
r = client.delete(f"{route}/{b['categoryId']}")
check("the emptied row can be deleted", r.status_code == 200, (r.status_code, r.text))
check("promote on a missing row is a 404", client.post(f"{route}/999999/promote-children").status_code == 404)

if client.get(route + "/deleted").status_code == 200:
    print("soft delete:")
    leaf = make("tree leaf", other["categoryId"]).json()
    r = client.delete(f"{route}/{leaf['categoryId']}")
    check("a delete flags the row", r.status_code == 200, (r.status_code, r.text))
    check("a deleted row is a 404", client.get(f"{route}/{leaf['categoryId']}").status_code == 404)
    check("a deleted row is left out of the list", all(x["categoryId"] != leaf["categoryId"] for x in client.get(route).json()))
    check("a deleted row is left out of the tree", all(n["id"] != leaf["categoryId"] for n in client.get(route + "/tree").json()))
    check("a deleted row is listed as deleted", any(x["categoryId"] == leaf["categoryId"] for x in client.get(route + "/deleted").json()))
    check("a second delete is a 404", client.delete(f"{route}/{leaf['categoryId']}").status_code == 404)
    r = client.delete(f"{route}/{other['categoryId']}")
    check("deleted children do not block deleting the parent", r.status_code == 200, (r.status_code, r.text))
    r = client.post(f"{route}/{other['categoryId']}/restore")
    check("restore brings a row back", r.status_code == 200 and not r.json()["isDeleted"], (r.status_code, r.text))
    r = client.post(f"{route}/{other['categoryId']}/restore")
    check("restoring a row that is not deleted is a 409", r.status_code == 409 and r.json()["type"].endswith("not-deleted"), (r.status_code, r.text))
    check("restoring a missing row is a 404", client.post(f"{route}/999999/restore").status_code == 404)
    client.post(f"{route}/{leaf['categoryId']}/restore")
    check("a restored row is back in the tree", any(n["id"] == leaf["categoryId"] for n in client.get(route + "/tree").json()))
    made.append(leaf)

for row in (c, a, other):
    client.delete(f"{route}/{row['categoryId']}")
print("\nFAILED: " + ", ".join(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
