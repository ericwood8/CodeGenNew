"""Plays the price list rules for price rows limited to an owner or a tenant over a scratch SQLite copy of Schemas/price_lists_owned.sql or price_lists_tenant.sql for the ASP.NET, Python and Rust APIs.

  python price_scoped_run.py [--kind owner|tenant] [--stack api,python,rust] [--port 5198] [--python <python with the generated requirements>]

Generates PriceListTables with ApiAuth (owner: OwnedTables=PriceList, the price rows are scoped through their list, and the Employee table as the user table; tenant: TenantTable=Tenant and TenantMembership, a TenantId on
the item, the list and the rows), writes made-up password hashes into the scratch copy, then for each stack starts the API with a random Jwt__Key and checks that two owners (or tenants) each have a list of the same
currency and days with a price for the same item, that each lookup answers the caller's own price, that the other's list cannot be read or given a row, and that the administrator of the owner run reads either. A stale server
on the port makes every sign-in fail: stop it first."""
import argparse
import base64
import hashlib
import os
import secrets
import socket
import sqlite3
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import httpx

from common import cli_dir
from live_party_run import TOOLS
from rate_auth_run import serve

PRICES = "PriceListTables=PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList:CurrencyCode:ValidFrom:ValidTo:CustomerGroup"
OWNER = [PRICES, "ApiAuth=true", "OwnedTables=PriceList", "UserTable=Employee", "UserNameColumn=Login", "UserRoleColumn=IsManager", "UserAdminColumn=IsAdministrator", "UserOwnerColumn=EmployeeId"]
OWNERCOL = [PRICES, "ApiAuth=true", "OwnedTables=PriceList,PriceListItem", "UserTable=Employee", "UserNameColumn=Login", "UserRoleColumn=IsManager", "UserAdminColumn=IsAdministrator", "UserOwnerColumn=EmployeeId"]
TENANT = [PRICES, "ApiAuth=true", "TenantTable=Tenant", "TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role", "UserTable=Employee", "UserNameColumn=Login", "UserRoleColumn=IsManager", "UserAdminColumn=IsAdministrator"]
failures = []


def check(stack, label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + f"[{stack}] " + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append((stack, label))


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


def signed_in(base, user, password, tenant=None):
    client = httpx.Client(base_url=base, timeout=30)
    body = {"userName": user, "password": password}
    if tenant is not None:
        body["tenantId"] = tenant
    response = client.post("/api/auth/login", json=body)
    response.raise_for_status()
    token = response.json()
    client.headers["Authorization"] = "Bearer " + next(token[k] for k in ("token", "accessToken", "access_token") if k in token)
    return client


def amount(response):
    return float(response.json()["price"]) if response.status_code == 200 else None


def exercise(stack, base, kind, clients):
    """clients: name -> (client, scope) where scope is the owner or tenant id the rows are written with."""
    (ada, ada_scope), (grace, grace_scope) = clients["ada"], clients["grace"]
    scope = (lambda s: {"tenantId": s}) if kind == "tenant" else (lambda s: {})
    owned = (lambda s: {"employeeId": s}) if kind in ("owner", "ownercol") else (lambda s: {})
    owned_rows = (lambda s: {"employeeId": s}) if kind == "ownercol" else (lambda s: {})

    def item(client, s):
        return client.post("/api/priceitems", json={"priceItemId": 0, **scope(s), "name": "Scoped " + kind, "sku": None, "unitName": None, "notes": None})

    def price_list(client, s, name):
        return client.post("/api/pricelists", json={"priceListId": 0, **scope(s), **owned(s), "name": name, "currencyCode": "USD", "validFrom": "2024-01-01", "validTo": "2024-12-31", "customerGroup": None})

    def price_row(client, s, list_id, item_id, minimum, price):
        return client.post("/api/pricelistitems", json={"priceListItemId": 0, **scope(s), **owned_rows(s), "priceListId": list_id, "itemId": item_id, "minQuantity": minimum, "price": price})

    def lookup(client, item_id, quantity=1):
        return client.get("/api/pricelistitems/price", params={"item": item_id, "quantity": quantity, "currency": "USD", "on": "2024-06-01"})

    a_item = item(ada, ada_scope)
    check(stack, "the first caller adds an item", a_item.status_code == 201, (a_item.status_code, a_item.text))
    g_item = item(grace, grace_scope)
    check(stack, "the second caller adds an item", g_item.status_code == 201, (g_item.status_code, g_item.text))
    a_list = price_list(ada, ada_scope, "Ada standard")
    check(stack, "the first caller adds a list", a_list.status_code == 201, (a_list.status_code, a_list.text))
    g_list = price_list(grace, grace_scope, "Grace standard")
    check(stack, "the second caller adds a list of the same currency and days (no overlap across owners or tenants)", g_list.status_code == 201, (g_list.status_code, g_list.text))
    a_list_id, g_list_id = a_list.json()["priceListId"], g_list.json()["priceListId"]
    a_item_id, g_item_id = a_item.json()["priceItemId"], g_item.json()["priceItemId"]

    if kind != "owner":
        again = price_list(ada, ada_scope, "Ada overlapping")
        check(stack, "the same caller's second list of the same currency and days overlaps (409 effective-overlap)", again.status_code == 409 and "effective-overlap" in again.text, (again.status_code, again.text))
    check(stack, "the first caller adds a break", price_row(ada, ada_scope, a_list_id, a_item_id, 1, 10).status_code == 201)
    check(stack, "and a second break", price_row(ada, ada_scope, a_list_id, a_item_id, 10, 9).status_code == 201)
    taken = price_row(ada, ada_scope, a_list_id, a_item_id, 10, 8)
    check(stack, "a break the list already holds is refused (409 price-taken)", taken.status_code == 409 and "price-taken" in taken.text, (taken.status_code, taken.text))
    check(stack, "the second caller adds a break to her own list", price_row(grace, grace_scope, g_list_id, g_item_id, 1, 20).status_code == 201)
    refused = price_row(grace, grace_scope, a_list_id, g_item_id, 1, 5)
    check(stack, "she cannot add a break to the first caller's list", refused.status_code in (400, 403, 404), (refused.status_code, refused.text))
    if kind == "ownercol":
        foreign = price_row(ada, grace_scope, a_list_id, a_item_id, 99, 1)
        check(stack, "a row with another owner than its list's is refused (400 price-invalid)", foreign.status_code in (400, 403) , (foreign.status_code, foreign.text))

    check(stack, "the first caller's lookup answers her own price", amount(lookup(ada, a_item_id, 1)) == 10.0 and amount(lookup(ada, a_item_id, 10)) == 9.0, lookup(ada, a_item_id).text)
    check(stack, "the second caller's lookup answers her own price", amount(lookup(grace, g_item_id, 1)) == 20.0, lookup(grace, g_item_id).text)
    check(stack, "the second caller does not see the first caller's price (404 price-missing)", lookup(grace, a_item_id, 1).status_code == 404)
    check(stack, "the first caller does not see the second caller's price", lookup(ada, g_item_id, 1).status_code == 404)
    anonymous = httpx.get(base + "/api/pricelistitems/price", params={"item": a_item_id, "currency": "USD"}, timeout=30)
    check(stack, "a lookup with no sign-in is refused", anonymous.status_code == 401, anonymous.status_code)
    if "boss" in clients:
        boss = clients["boss"][0]
        check(stack, "the administrator reads the first caller's price", amount(lookup(boss, a_item_id, 1)) == 10.0, lookup(boss, a_item_id).text)
        check(stack, "and the second caller's", amount(lookup(boss, g_item_id, 1)) == 20.0, lookup(boss, g_item_id).text)


def play(stack, work, database, schema, port, python, kind):
    passwords = {name: secrets.token_urlsafe(12) for name in ("boss", "ada", "grace")}
    with sqlite3.connect(database) as db:
        for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'").fetchall():
            db.execute(f'DROP TABLE "{name}"')
        db.executescript(schema)
        if kind in ("owner", "ownercol"):
            for name, admin in (("boss", 1), ("ada", 0), ("grace", 0)):
                db.execute("INSERT INTO Employee (Login, PasswordHash, IsManager, IsAdministrator) VALUES (?, ?, 0, ?)", (name, hash_password(passwords[name]), admin))
        else:
            db.executemany("INSERT INTO Tenant (Name, Slug, CreatedOn) VALUES (?, ?, '2024-01-01')", [("One", "one"), ("Two", "two")])
            db.execute("INSERT INTO Employee (FullName, Login, PasswordHash, IsManager, IsAdministrator) VALUES ('Ada', 'ada', ?, 1, 0)", (hash_password(passwords["ada"]),))
            db.executemany("INSERT INTO EmployeeTenant (EmployeeId, TenantId, Role) VALUES (1, ?, NULL)", [(1,), (2,)])
    environment = dict(os.environ, Jwt__Key=secrets.token_urlsafe(48), ASPNETCORE_ENVIRONMENT="Development")
    command, folder, env = serve(stack, work, port, python, environment)
    log = work / f"serve_{stack}.log"
    with open(log, "w") as out:
        server = subprocess.Popen(command, cwd=folder, env=env, stdout=out, stderr=subprocess.STDOUT)
        try:
            deadline = time.time() + 600
            while time.time() < deadline and server.poll() is None:
                try:
                    socket.create_connection(("localhost", port), timeout=1).close()
                    break
                except OSError:
                    time.sleep(2)
            if server.poll() is not None:
                print(stack, "the server stopped:", "\n".join(log.read_text(errors="replace").splitlines()[-15:]))
                failures.append((stack, "server"))
                return
            time.sleep(2)
            base = f"http://localhost:{port}"
            if kind in ("owner", "ownercol"):
                clients = {"boss": (signed_in(base, "boss", passwords["boss"]), None), "ada": (signed_in(base, "ada", passwords["ada"]), 2), "grace": (signed_in(base, "grace", passwords["grace"]), 3)}
            else:
                clients = {"ada": (signed_in(base, "ada", passwords["ada"], 1), 1), "grace": (signed_in(base, "ada", passwords["ada"], 2), 2)}
            exercise(stack, base, kind, clients)
        finally:
            subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--kind", default="owner", choices=["owner", "ownercol", "tenant"])
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--port", type=int, default=5198)
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--dir")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir or Path(tempfile.gettempdir()) / f"price_{args.kind}")
    name = f"Price{args.kind.title()}"
    schema_name = {"owner": "price_lists_owned", "ownercol": "price_lists_owncol", "tenant": "price_lists_tenant"}[args.kind]
    schema = (TOOLS / "Schemas" / f"{schema_name}.sql").read_text(encoding="utf-8")
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), name, "--schema", schema_name, "--stack", "api,python,rust", "--dir", str(work)]
        for setting in {"owner": OWNER, "ownercol": OWNERCOL, "tenant": TENANT}[args.kind]:
            command += ["--set", setting]
        result = subprocess.run(command, capture_output=True, text=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print(result.stderr[-1500:] or result.stdout[-1500:])
            return 1
    for stack in [s.strip() for s in args.stack.split(",") if s.strip()]:
        play(stack, work / "out", work / f"{name}.db", schema, args.port, args.python, args.kind)
    print("FAILED" if failures else "ALL OK")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
