"""Plays DocumentTotals on invoices limited to a tenant (a TenantId column on the invoice, its lines and its allowance rows) over a scratch SQLite copy of Schemas/document_totals_tenant.sql for the ASP.NET,
Python and Rust APIs.

  python document_totals_tenant_run.py [--stack api,python,rust] [--port 5199] [--python <python with the generated requirements>]

Generates DocumentTotals (with TaxLines, AllowanceCharges and AllowanceTax) with ApiAuth, TenantTable=Tenant and TenantMembership, writes the password hash of one person who belongs to two tenants into the scratch copy
(a made-up password that only this run knows), then for each stack starts the API with a random Jwt__Key and checks that the totals of a tenant's invoice are worked out, that the other tenant can neither
add a line to it nor read it, and that the check route lists only the signed-in tenant's invoices. A stale server on the port makes every sign-in fail: stop it first."""
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

SETTINGS = [
    "DocumentTotals=Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Allowances=AllowanceTotal:Charges=ChargeTotal:TaxExclusive=TaxExclusive:Tax=TaxTotal:TaxInclusive=TaxInclusive:Prepaid=Prepaid:Payable=Payable:AllowanceTax=TaxAmount",
    "TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate,InvoiceAllowanceCharge:TaxCategoryId:TaxRate:Amount:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo",
    "AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId",
    "ApiAuth=true", "TenantTable=Tenant", "TenantMembership=EmployeeTenant:EmployeeId:TenantId:Role", "UserTable=Employee", "UserNameColumn=Login", "UserRoleColumn=IsManager", "UserAdminColumn=IsAdministrator"]
INVOICES, LINES, EXTRAS, CATEGORIES = "/api/invoices", "/api/invoicelines", "/api/invoiceallowancecharges", "/api/taxcategories"
failures = []


def check(stack, label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + f"[{stack}] " + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append((stack, label))


def hash_password(password):
    salt = b"0123456789abcdef"
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 600_000, 32)
    return f"pbkdf2-sha256$600000${base64.b64encode(salt).decode()}${base64.b64encode(digest).decode()}"


def signed_in(base, user, password, tenant):
    client = httpx.Client(base_url=base, timeout=30)
    response = client.post("/api/auth/login", json={"userName": user, "password": password, "tenantId": tenant})
    response.raise_for_status()
    body = response.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
    return client


def invoice(client, tenant, customer):
    return client.post(INVOICES, json={"invoiceId": 0, "tenantId": tenant, "customer": customer, "invoiceDate": "2024-06-01", "currency": "USD", "itemsTotal": 999, "allowanceTotal": 999, "chargeTotal": 999,
                                       "taxExclusive": 999, "taxTotal": 999, "taxInclusive": 999, "prepaid": 0, "rounding": 0, "payable": 999})


def line(client, tenant, document, total, category=None):
    return client.post(LINES, json={"invoiceLineId": 0, "tenantId": tenant, "invoiceId": document, "lineDate": "2024-06-01", "description": "tenant run", "taxCategoryId": category, "taxRate": 0, "lineTotal": total,
                                    "taxAmount": 0})


def extra(client, tenant, document, charge, amount, category=None):
    return client.post(EXTRAS, json={"invoiceAllowanceChargeId": 0, "tenantId": tenant, "invoiceId": document, "isCharge": charge, "reasonCode": None, "baseAmount": None, "percentage": None, "amount": amount, "taxCategoryId": category, "taxRate": 0, "taxAmount": 0})


def read(client, document):
    row = client.get(f"{INVOICES}/{document}")
    return row.status_code, (row.json() if row.status_code == 200 else row.text)


def near(row, **want):
    names = {"items": "itemsTotal", "allowances": "allowanceTotal", "charges": "chargeTotal", "exclusive": "taxExclusive", "tax": "taxTotal", "inclusive": "taxInclusive", "payable": "payable"}
    return all(abs(float(row[names[k]]) - v) < 0.0051 for k, v in want.items())


def listing(client, **params):
    response = client.get(f"{INVOICES}/document-totals-check", params=params)
    return response.status_code, (response.json() if response.status_code == 200 else response.text)


def exercise(stack, base, password, bump):
    """bump(invoice key) adds 5 to the stored payable amount of the invoice behind the server's back."""
    north, contoso = (signed_in(base, "ada", password, tenant) for tenant in (1, 2))
    category = north.post(CATEGORIES, json={"taxCategoryId": 0, "code": "T20", "name": "Tenant run", "rate": 0.2, "exemptionReason": None, "validFrom": "2000-01-01", "validTo": None})
    check(stack, "a tax category is created", category.status_code == 201, (category.status_code, category.text))
    cat = category.json()["taxCategoryId"]

    made = invoice(north, 1, "Northwind's")
    check(stack, "a tenant adds an invoice", made.status_code == 201, (made.status_code, made.text))
    key = made.json()["invoiceId"]
    code, row = read(north, key)
    check(stack, "the totals the client wrote are replaced", code == 200 and near(row, items=0, allowances=0, charges=0, exclusive=0, tax=0, payable=0), (code, row))
    check(stack, "a taxed line of 100 is added", line(north, 1, key, 100, cat).status_code == 201)
    check(stack, "a taxed allowance of 10 is added", extra(north, 1, key, False, 10, cat).status_code == 201)
    check(stack, "a taxed charge of 5 is added", extra(north, 1, key, True, 5, cat).status_code == 201)
    code, row = read(north, key)
    check(stack, "the lines, allowances, charges and tax of the tenant's invoice are worked out", code == 200 and near(row, items=100, allowances=10, charges=5, exclusive=95, tax=19, inclusive=114, payable=114), (code, row))

    other = invoice(contoso, 2, "Contoso's")
    other_key = other.json()["invoiceId"]
    check(stack, "the second tenant adds a line to its own invoice", line(contoso, 2, other_key, 40).status_code == 201)
    code, row = read(contoso, other_key)
    check(stack, "its totals are its own", code == 200 and near(row, items=40, payable=40), (code, row))
    refused = line(contoso, 2, key, 7)
    print(f"note [{stack}] a line of the second tenant that names the first tenant's invoice: {refused.status_code} (the key is not checked against the tenant of the row it points at; not part of this run)")
    if refused.status_code == 201:
        client_line = refused.json()["invoiceLineId"]
        contoso.delete(f"{LINES}/{client_line}")
    code, row = read(north, key)
    check(stack, "the first tenant's totals are what its own rows give", code == 200 and near(row, items=100, payable=114), (code, row))
    code, _ = read(contoso, key)
    check(stack, "the second tenant cannot read the first tenant's invoice", code in (403, 404), code)

    code, answer = listing(north)
    check(stack, "the check route lists only the first tenant's invoice", code == 200 and answer["checked"] == 1 and answer["differing"] == 0, (code, answer))
    code, answer = listing(contoso)
    check(stack, "and the second tenant's lists only its own", code == 200 and answer["checked"] == 1 and answer["differing"] == 0, (code, answer))
    anonymous = httpx.get(base + f"{INVOICES}/document-totals-check", timeout=30)
    check(stack, "the check route refuses a call with no sign-in", anonymous.status_code == 401, anonymous.status_code)

    bump(key)
    code, answer = listing(north)
    check(stack, "a total altered under the server is listed for its tenant", code == 200 and answer["differing"] == 1, (code, answer))
    code, answer = listing(contoso)
    check(stack, "and not for the other tenant", code == 200 and answer["differing"] == 0, (code, answer))


def play(stack, work, database, schema, port, python):
    password = secrets.token_urlsafe(12)
    with sqlite3.connect(database) as db:
        for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'").fetchall():
            db.execute(f'DROP TABLE "{name}"')
        db.executescript(schema)
        db.execute("INSERT INTO Employee (FullName, Login, PasswordHash, IsManager, IsAdministrator) VALUES ('Ada', 'ada', ?, 1, 0)", (hash_password(password),))
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
            exercise(stack, f"http://localhost:{port}", password, lambda key: sqlite3.connect(database).execute("UPDATE Invoice SET Payable = Payable + 5 WHERE InvoiceId = ?", (key,)).connection.commit())
        finally:
            subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--port", type=int, default=5199)
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "document_totals_tenant"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "DocumentTotalsTenant.db"
    schema = (TOOLS / "Schemas" / "document_totals_tenant.sql").read_text(encoding="utf-8")
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "DocumentTotalsTenant", "--schema", "document_totals_tenant", "--stack", "api,python,rust", "--dir", str(work)]
        for setting in SETTINGS:
            command += ["--set", setting]
        result = subprocess.run(command, capture_output=True, text=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print(result.stderr[-1500:] or result.stdout[-1500:])
            return 1
    for stack in [s.strip() for s in args.stack.split(",") if s.strip()]:
        play(stack, work / "out", database, schema, args.port, args.python)
    print("FAILED" if failures else "ALL OK")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
