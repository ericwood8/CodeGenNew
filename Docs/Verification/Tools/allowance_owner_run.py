"""Plays the allowance totals of a table limited to an owner (OwnedTables) over a scratch SQLite copy of Schemas/allowance_charges_owned.sql for the ASP.NET, Python and Rust APIs.

  python allowance_owner_run.py [--stack api,python,rust] [--port 5199] [--python <python with the generated requirements>]

Generates AllowanceCharges with ApiAuth, OwnedTables=InvoiceAllowanceCharge and the Employee table as the user table, writes the password hashes of an administrator and two employees into the scratch
copy (made-up passwords that only this run knows), then for each stack starts the API with a random Jwt__Key and checks that each employee's totals count only that employee's rows (the amount is
still worked out), that a second employee's rows stay out of the first one's answer and that the administrator's answer counts both."""
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

SETTINGS = ["AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId", "ApiAuth=true", "OwnedTables=InvoiceAllowanceCharge", "UserTable=Employee", "UserNameColumn=Login",
            "UserRoleColumn=IsManager", "UserAdminColumn=IsAdministrator", "UserOwnerColumn=EmployeeId"]
ROUTE = "/api/invoiceallowancecharges"
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


def totals(client, document):
    response = client.get(f"{ROUTE}/allowance-charge-totals", params={"document": document})
    return response.status_code, (response.json() if response.status_code == 200 else response.text)


def add(client, document, charge, base, percentage, amount):
    response = client.post(ROUTE, json={"invoiceAllowanceChargeId": 0, "employeeId": 0, "invoiceId": document, "isCharge": charge, "reasonCode": None, "baseAmount": base, "percentage": percentage, "amount": amount})
    return response


def exercise(stack, base, passwords):
    """The checks of this run over the API at <base>, whatever database it runs on."""
    document = 910000 + secrets.randbelow(90000)
    ada, grace, boss = (signed_in(base, n, passwords[n]) for n in ("ada", "grace", "boss"))
    r = add(ada, document, False, 200, 10, 1)
    check(stack, "an employee adds an allowance and the amount is worked out", r.status_code == 201 and abs(r.json()["amount"] - 20) < 0.001, (r.status_code, r.text))
    r = add(grace, document, True, None, None, 60)
    check(stack, "a second employee adds a charge on the same document", r.status_code == 201, (r.status_code, r.text))
    code, t = totals(ada, document)
    check(stack, "the first employee's totals count only that employee's rows", code == 200 and t["rows"] == 1 and abs(t["allowances"] - 20) < 0.001 and t["charges"] == 0, (code, t))
    code, t = totals(grace, document)
    check(stack, "the second employee's totals count only that employee's rows", code == 200 and t["rows"] == 1 and abs(t["charges"] - 60) < 0.001 and t["allowances"] == 0, (code, t))
    code, t = totals(boss, document)
    check(stack, "the administrator's totals count both", code == 200 and t["rows"] == 2 and abs(t["net"] - 40) < 0.001, (code, t))
    anonymous = httpx.get(base + f"{ROUTE}/allowance-charge-totals", params={"document": document}, timeout=30)
    check(stack, "no sign-in is refused", anonymous.status_code == 401, anonymous.status_code)


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
            base = f"http://localhost:{port}"
            exercise(stack, base, passwords)
        finally:
            subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--port", type=int, default=5199)
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "allowance_owner"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "AllowanceOwner.db"
    schema = (TOOLS / "Schemas" / "allowance_charges_owned.sql").read_text(encoding="utf-8")
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "AllowanceOwner", "--schema", "allowance_charges_owned", "--stack", "api,python,rust", "--dir", str(work)]
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
