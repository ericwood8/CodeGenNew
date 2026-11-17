"""Plays the rate checks with a sign-in over a scratch SQLite copy of Schemas/rates_auth.sql for the ASP.NET, Python and Rust APIs, and the OData rule check on the ASP.NET API.

  python rate_auth_run.py [--stack api,python,rust] [--port 5194] [--python <python with the generated requirements>]

Generates RateTables=ExchangeRate with ApiAuth, OData and the Employee table as the user table into a scratch folder, then for each stack empties the database, starts the API with a random Jwt__Key,
adds the first administrator with POST /api/auth/first-person and runs rate_check.py (and rate_odata_check.py on the ASP.NET API) with API_USER and API_PASSWORD. The administrator's
name and password are made up for the run and only handed to the processes of the run through the environment; a request with no sign-in must be refused (401). Prints one line per check script and one
line with the result. The race check is not run here: SQLite takes one writer at a time and answers the others "database is locked", which says nothing of the rule."""
import argparse
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
from live_party_run import CARGO, TOOLS

SETTINGS = ["RateTables=ExchangeRate", "ApiAuth=true", "UserTable=Employee", "UserNameColumn=Login", "UserRoleColumn=IsManager", "UserAdminColumn=IsAdministrator", "OData=true"]


def serve(stack, work, port, python, environment):
    env = dict(environment, PORT=str(port))
    if stack == "api":
        folder = next(f for f in work.glob("*.Api") if f.is_dir())
        return ["dotnet", "run", "--project", str(folder), "--no-launch-profile", "--urls", f"http://localhost:{port}"], folder, env
    if stack == "python":
        return [python, "-m", "uvicorn", "app.main:app", "--port", str(port)], next(work.glob("*.Python")), env
    folder = next(work.glob("*.Rust"))
    return [sys.executable, CARGO, str(folder), "run"], folder, env


def play(stack, work, database, schema, port, python):
    with sqlite3.connect(database) as db:
        for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'").fetchall():
            db.execute(f'DROP TABLE "{name}"')
        db.executescript(schema)
    user = "zz_" + secrets.token_hex(3)
    password = secrets.token_urlsafe(18)
    environment = dict(os.environ, Jwt__Key=secrets.token_urlsafe(48), ASPNETCORE_ENVIRONMENT="Development")
    command, folder, env = serve(stack, work, port, python, environment)
    log = work / f"serve_{stack}.log"
    failed = False
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
                return True
            time.sleep(2)
            base = f"http://localhost:{port}"
            signed_out = httpx.get(base + "/api/exchangerates", timeout=30)
            print(stack, "no sign-in:", signed_out.status_code, "ok" if signed_out.status_code == 401 else "FAIL")
            failed = signed_out.status_code != 401
            httpx.post(base + "/api/auth/first-person", json={"userName": user, "password": password}, timeout=30).raise_for_status()
            scripts = [("rate_check.py", [])] + ([("rate_odata_check.py", [])] if stack == "api" else [])
            for script, extra in scripts:
                result = subprocess.run([sys.executable, str(TOOLS / script), base, *extra], capture_output=True, text=True, env=dict(os.environ, API_USER=user, API_PASSWORD=password))
                lines = result.stdout.splitlines()
                print(stack, script, "exit", result.returncode, "-", sum(1 for l in lines if l.startswith("ok ")), "checks ok")
                print("\n".join(l[:400] for l in lines if l.startswith("FAIL")), end="")
                if result.returncode != 0:
                    failed = True
                    print(result.stderr[-800:])
        finally:
            subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)
    return failed


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--port", type=int, default=5194)
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "rate_auth"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "RateAuth.db"
    schema = (TOOLS / "Schemas" / "rates_auth.sql").read_text(encoding="utf-8")
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "RateAuth", "--schema", "rates_auth", "--stack", "api,python,rust", "--dir", str(work)]
        for setting in SETTINGS:
            command += ["--set", setting]
        result = subprocess.run(command, capture_output=True, text=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print(result.stderr[-1500:] or result.stdout[-1500:])
            return 1
    failed = False
    for stack in [s.strip() for s in args.stack.split(",") if s.strip()]:
        failed = play(stack, work / "out", database, schema, args.port, args.python) or failed
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
