"""Plays DocumentTotals on invoices limited to an owner (OwnedTables) over a scratch SQLite copy of Schemas/document_totals_owned.sql for the ASP.NET, Python and Rust APIs.

  python document_totals_owner_run.py [--stack api,python,rust] [--port 5199] [--python <python with the generated requirements>]

Generates DocumentTotals (with AllowanceCharges) with ApiAuth, OwnedTables=Invoice and the Employee table as the user table, writes the
password hashes of an administrator and two employees into the scratch copy (made-up passwords that only this run knows), then for each stack starts the API with a random Jwt__Key and checks that
the totals of an employee's invoice are worked out, that another employee can neither add a line to it nor see it, and that the check route lists
only the caller's invoices (the administrator's lists all of them)."""
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
    "DocumentTotals=Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Allowances=AllowanceTotal:Charges=ChargeTotal:TaxExclusive=TaxExclusive:Prepaid=Prepaid:Payable=Payable",
    "AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId",
    "ApiAuth=true", "OwnedTables=Invoice,InvoiceAllowanceCharge", "UserTable=Employee", "UserNameColumn=Login", "UserRoleColumn=IsManager", "UserAdminColumn=IsAdministrator",
    "UserOwnerColumn=EmployeeId"]
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


def signed_in(base, user, password):
    client = httpx.Client(base_url=base, timeout=30)
    response = client.post("/api/auth/login", json={"userName": user, "password": password})
    response.raise_for_status()
    body = response.json()
    client.headers["Authorization"] = "Bearer " + next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
    return client


def invoice(client, customer):
    return client.post(INVOICES, json={"invoiceId": 0, "employeeId": 0, "customer": customer, "invoiceDate": "2024-06-01", "currency": "USD", "itemsTotal": 999, "allowanceTotal": 999, "chargeTotal": 999,
                                       "taxExclusive": 999, "taxTotal": 999, "taxInclusive": 999, "prepaid": 0, "rounding": 999, "payable": 999})


def line(client, document, total, category=None):
    return client.post(LINES, json={"invoiceLineId": 0, "invoiceId": document, "lineDate": "2024-06-01", "description": "owner run", "taxCategoryId": category, "taxRate": 0, "lineTotal": total,
                                    "taxAmount": 0})


def extra(client, document, charge, amount, category=None):
    return client.post(EXTRAS, json={"invoiceAllowanceChargeId": 0, "employeeId": 0, "invoiceId": document, "isCharge": charge, "reasonCode": None, "baseAmount": None, "percentage": None, "amount": amount,
                                     "taxCategoryId": category})


def totals(client, document):
    row = client.get(f"{INVOICES}/{document}")
    return row.status_code, (row.json() if row.status_code == 200 else row.text)


def near(row, **want):
    names = {"items": "itemsTotal", "allowances": "allowanceTotal", "charges": "chargeTotal", "exclusive": "taxExclusive", "tax": "taxTotal", "inclusive": "taxInclusive", "payable": "payable"}
    return all(abs(float(row[names[k]]) - v) < 0.0051 for k, v in want.items())


def listing(client):
    response = client.get(f"{INVOICES}/document-totals-check")
    return response.status_code, (response.json() if response.status_code == 200 else response.text)


def exercise(stack, base, passwords, database):
    ada, grace, boss = (signed_in(base, n, passwords[n]) for n in ("ada", "grace", "boss"))
    made = invoice(ada, "Ada's")
    check(stack, "an employee adds an invoice", made.status_code == 201, (made.status_code, made.text))
    key = made.json()["invoiceId"]
    code, row = totals(ada, key)
    check(stack, "the totals the client wrote are replaced", code == 200 and near(row, items=0, allowances=0, charges=0, exclusive=0, payable=0), (code, row))
    check(stack, "a line of 100 is added", line(ada, key, 100).status_code == 201)
    check(stack, "an allowance of 10 is added", extra(ada, key, False, 10).status_code == 201)
    check(stack, "a charge of 5 is added", extra(ada, key, True, 5).status_code == 201)
    code, row = totals(ada, key)
    check(stack, "the lines, allowances and charges of the employee's invoice are worked out", code == 200 and near(row, items=100, allowances=10, charges=5, exclusive=95, payable=95), (code, row))

    other = invoice(grace, "Grace's")
    other_key = other.json()["invoiceId"]
    check(stack, "a second employee adds a line to her own invoice", line(grace, other_key, 40).status_code == 201)
    code, row = totals(grace, other_key)
    check(stack, "her totals are her own", code == 200 and near(row, items=40, payable=40), (code, row))
    refused = line(grace, key, 7)
    check(stack, "she cannot add a line to the first employee's invoice", refused.status_code in (403, 404), refused.status_code)
    code, row = totals(ada, key)
    check(stack, "and the first employee's totals are unchanged", code == 200 and near(row, items=100, payable=95), (code, row))
    code, _ = totals(grace, key)
    check(stack, "the second employee cannot read the first employee's invoice", code in (403, 404), code)

    code, answer = listing(ada)
    check(stack, "the check route lists only the first employee's invoice", code == 200 and answer["checked"] == 1 and answer["differing"] == 0, (code, answer))
    code, answer = listing(boss)
    check(stack, "the administrator's check route covers both", code == 200 and answer["checked"] == 2 and answer["differing"] == 0, (code, answer))
    anonymous = httpx.get(base + f"{INVOICES}/document-totals-check", timeout=30)
    check(stack, "the check route refuses a call with no sign-in", anonymous.status_code == 401, anonymous.status_code)

    with sqlite3.connect(database) as db:
        db.execute("UPDATE Invoice SET Payable = Payable + 5 WHERE InvoiceId = ?", (key,))
    code, answer = listing(ada)
    check(stack, "a total altered under the server is listed for its owner", code == 200 and answer["differing"] == 1, (code, answer))
    code, answer = listing(grace)
    check(stack, "and not for the other employee", code == 200 and answer["checked"] == 1 and answer["differing"] == 0, (code, answer))
    code, answer = listing(boss)
    check(stack, "but for the administrator", code == 200 and answer["differing"] == 1, (code, answer))
    one = boss.get(f"{INVOICES}/document-totals-check", params={"id": key})
    check(stack, "one invoice can be asked for", one.status_code == 200 and one.json()["checked"] == 1, one.text)


def play(stack, work, database, schema, port, python):
    passwords = {name: secrets.token_urlsafe(12) for name in ("boss", "ada", "grace")}
    with sqlite3.connect(database) as db:
        for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'").fetchall():
            db.execute(f'DROP TABLE "{name}"')
        db.executescript(schema)
        for name, admin in (("boss", 1), ("ada", 0), ("grace", 0)):
            db.execute("INSERT INTO Employee (Login, PasswordHash, IsManager, IsAdministrator) VALUES (?, ?, 0, ?)", (name, hash_password(passwords[name]), admin))
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
            exercise(stack, f"http://localhost:{port}", passwords, database)
        finally:
            subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--port", type=int, default=5199)
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "document_totals_owner"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "DocumentTotalsOwner.db"
    schema = (TOOLS / "Schemas" / "document_totals_owned.sql").read_text(encoding="utf-8")
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "DocumentTotalsOwner", "--schema", "document_totals_owned", "--stack", "api,python,rust", "--dir", str(work)]
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
