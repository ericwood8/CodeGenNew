"""Plays the facet routes over HTTP against a running generated API over a scratch copy of Schemas/catalog.sql.

python catalog_check.py <base url>

The API is generated with FacetTables=ProductAttribute (Product is the product table; NumberValue is the number column). CATALOG_SQLITE names the SQLite file (without it the check builds the same rows from Schemas/catalog.sql, for a live database): the check reads the products and attributes and works the expected answers out again in Python
(an independent oracle), then compares them with what GET facets and GET narrow answer: the counts of every value for the keyword, the values and the ranges, the order (most common first), several values of one attribute as alternatives,
different attributes all having to match, an attribute counted without its own filter, a selected value that no product carries (count 0, selected), the keyword (any case, in the product's text and in the attribute values), the lowest and highest number of each attribute,
ranges (both ends, one end), paging and the refusals.
With ApiAuth set API_USER and API_PASSWORD. CATALOG_ROUTE names the product route (default /api/products)."""
import os
import sqlite3
import sys

import httpx

base = sys.argv[1]
client = httpx.Client(base_url=base, timeout=60)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
PRODUCTS = os.environ.get("CATALOG_ROUTE", "/api/products")
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:400]))
    if not ok:
        failures.append(label)


if os.environ.get("CATALOG_SQLITE"):
    db = sqlite3.connect(os.environ["CATALOG_SQLITE"])
else:
    # a live SQL Server, PostgreSQL or MySQL database holds the same rows as Schemas/catalog.sql
    db = sqlite3.connect(":memory:")
    db.executescript(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "Schemas", "catalog.sql"), encoding="utf-8").read())
products = {row[0]: row for row in db.execute("SELECT ProductId, CatalogNumber, Name, Description, Manufacturer FROM Product")}
attributes = db.execute("SELECT ProductId, Name, Value, NumberValue FROM ProductAttribute").fetchall()


def carries(product, name, values):
    return any(p == product and n == name and v in values for p, n, v, _ in attributes if v is not None)


def within(product, name, low, high):
    return any(p == product and n == name and x is not None and (low is None or x >= low) and (high is None or x <= high) for p, n, _, x in attributes)


def text_hit(product, q):
    term = q.lower()
    row = products[product]
    return any(term in (c or "").lower() for c in row[1:5]) or any(p == product and v is not None and term in v.lower() for p, _, v, _ in attributes)


def matching(q, filters, ranges, except_name=None):
    found = []
    for pid in products:
        if q and not text_hit(pid, q):
            continue
        if all(carries(pid, n, vs) for n, vs in filters.items() if n != except_name) and all(within(pid, n, lo, hi) for n, (lo, hi) in ranges.items() if n != except_name):
            found.append(pid)
    return sorted(found)


def expected(q, filters, ranges):
    ids = set(matching(q, filters, ranges))
    per_name = {}
    for p, n, v, _ in attributes:
        if p in ids:
            per_name.setdefault(n, set()).add(p)
    names = sorted(per_name, key=lambda n: (-len(per_name[n]), n))[:12]
    for n in list(filters) + list(ranges):
        if n not in names:
            names.append(n)
    groups = []
    for n in names:
        scope = set(matching(q, filters, ranges, n))
        counts = {}
        for p, name, v, _ in attributes:
            if name == n and v is not None and p in scope:
                counts.setdefault(v, set()).add(p)
        values = sorted(counts, key=lambda v: (-len(counts[v]), v))[:12]
        for v in filters.get(n, []):
            if v not in values:
                values.append(v)
        numbers = [x for p, name, _, x in attributes if name == n and x is not None and p in scope]
        low, high = (min(numbers), max(numbers)) if numbers else (None, None)
        chosen = ranges.get(n, (None, None)) if n in ranges else (None, None)
        groups.append((n, [(v, len(counts.get(v, ())), v in filters.get(n, [])) for v in values], low, high, chosen[0], chosen[1]))
    return len(ids), groups


def ask(q=None, filters=None, ranges=None, route="facets", **more):
    params = [("filter", f"{n}:{v}") for n, vs in (filters or {}).items() for v in vs]
    params += [("range", f"{n}:{'' if lo is None else lo}:{'' if hi is None else hi}") for n, (lo, hi) in (ranges or {}).items()]
    if q:
        params.append(("q", q))
    params += list(more.items())
    return client.get(f"{PRODUCTS}/{route}", params=params)


def same_number(a, b):
    return (a is None and b is None) or (a is not None and b is not None and abs(float(a) - float(b)) < 1e-6)


def facets_match(label, q=None, filters=None, ranges=None):
    response = ask(q, filters, ranges)
    if response.status_code != 200:
        check(label, False, response.text)
        return
    body = response.json()
    total, groups = expected(q, filters or {}, ranges or {})
    got = {g["name"]: g for g in body["facets"]}
    want = {g[0]: g for g in groups}
    # the order inside a tie is the database's collation (not Python's byte order), so compare each tie as a set and the order by its counts
    same = body["total"] == total and sorted(got) == sorted(want)
    for name, g in got.items():
        if name not in want:
            continue
        w = want[name]
        values = sorted((v["value"], v["count"], v["selected"]) for v in g["values"])
        same = same and values == sorted(w[1]) and same_number(g.get("min"), w[2]) and same_number(g.get("max"), w[3]) and same_number(g.get("selectedMin"), w[4]) and same_number(g.get("selectedMax"), w[5])
    ordered = all(all(a["count"] >= b["count"] for a, b in zip(g["values"], g["values"][1:])) for g in got.values())
    check(label, same and ordered, f"expected {total} {groups}; got {body['total']} {body['facets']}")


facets_match("no filter: every attribute with its counts, most common first")
facets_match("one value chosen: the other attributes narrow, its own attribute stays whole", filters={"Material": ["Copper"]})
facets_match("two values of one attribute are alternatives", filters={"Material": ["Copper", "Steel"]})
facets_match("two attributes must both match", filters={"Voltage": ["600 V"], "Material": ["Aluminum"]})
facets_match("a chosen value no product has (count 0, selected)", filters={"Material": ["Copper"], "Voltage": ["120 V"]})
facets_match("the keyword narrows the facets", q="romex")
facets_match("the keyword in capitals finds the same", q="ROMEX")
facets_match("keyword and filter together", q="wire", filters={"Color": ["Blue"]})
facets_match("a value with a NULL value row does not count", filters={"Amperage": ["15 A"]})
facets_match("a keyword that matches nothing", q="zzzz-no-such-thing")
facets_match("the keyword reads the attribute values too", q="1-5/8")
facets_match("the keyword reads a number's text in the attribute values", q="600 v")
facets_match("a range keeps the products with a number inside it", ranges={"Voltage": (100, 300)})
facets_match("a range with only a low end", ranges={"Length": (200, None)})
facets_match("a range with only a high end", ranges={"Length": (None, 100)})
facets_match("a range and a value of another attribute", filters={"Material": ["Steel"]}, ranges={"Length": (5, 50)})
facets_match("a range and the keyword", q="wire", ranges={"Voltage": (500, 700)})
facets_match("a range that no product reaches", ranges={"Voltage": (1000, 2000)})

zinc = ask(q="1-5/8", route="narrow").json()
check("the keyword finds a product by an attribute value that is not in its text", zinc.get("total") == 1 and zinc["items"][0]["id"] == 9, zinc)
voltage = ask(ranges={"Voltage": (100, 300)}, route="narrow").json()
check("narrow applies a range", voltage.get("total") == len(matching(None, {}, {"Voltage": (100, 300)})) and voltage.get("total") > 0, voltage)

everything = ask(route="narrow", pageSize=5)
body = everything.json() if everything.status_code == 200 else {}
check("narrow answers the total and a page of the products", everything.status_code == 200 and body.get("total") == len(products) and len(body.get("items", [])) == 5 and body.get("page") == 1, everything.text)
first = (body.get("items") or [{}])[0]
check("an item carries the key, the title and the code", set(first) >= {"id", "title", "code", "detail"} and first.get("code") == "NM-B-14/2" and first.get("title", "").startswith("Building Wire"), first)
check("items come in key order", [i["id"] for i in body.get("items", [])] == sorted(products)[:5], body.get("items"))
last = ask(route="narrow", pageSize=5, page=3).json()
check("the last page holds the rest", len(last.get("items", [])) == len(products) - 10 and last.get("page") == 3, last)
filtered = ask(filters={"Material": ["Copper"]}, route="narrow").json()
check("narrow applies the filters", filtered.get("total") == len(matching(None, {"Material": ["Copper"]}, {})) and all(i["id"] in matching(None, {"Material": ["Copper"]}, {}) for i in filtered["items"]), filtered)
none = ask(q="zzzz-no-such-thing", route="narrow").json()
check("no match is an empty page", none.get("total") == 0 and none.get("items") == [], none)
bad = client.get(f"{PRODUCTS}/facets", params=[("filter", "Voltage")])
check("a filter with no value is refused", bad.status_code == 400 and bad.json().get("type", "").endswith("facet-filter-invalid"), bad.text)
for label, value in (("a range with no numbers", "Voltage::"), ("a range with a word for a number", "Voltage:abc:5"), ("a range with no name", ":1:5"), ("a range with one colon", "Voltage:100")):
    bad = client.get(f"{PRODUCTS}/narrow", params=[("range", value)])
    check(label + " is refused", bad.status_code == 400 and bad.json().get("type", "").endswith("facet-filter-invalid"), bad.text)
bad = client.get(f"{PRODUCTS}/narrow", params={"page": 0})
check("page 0 is refused", bad.status_code == 400 and bad.json().get("type", "").endswith("facet-page-invalid"), bad.text)
many = ask(route="narrow", pageSize=100000).json()
check("the page size is capped", many.get("pageSize") == 200, many)
limited = ask(maxFacets=2, maxValues=1).json()
check("maxFacets and maxValues limit the answer", len(limited["facets"]) == 2 and all(len(g["values"]) == 1 for g in limited["facets"]), limited)
check("the product route itself still lists", client.get(PRODUCTS).status_code == 200)

print("FAILED: " + ", ".join(failures) if failures else "ALL OK")
sys.exit(1 if failures else 0)
