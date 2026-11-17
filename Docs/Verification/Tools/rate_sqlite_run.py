"""Plays the rate and rate document checks over a scratch SQLite copy of a schema for the ASP.NET, Python and Rust APIs.

  python rate_sqlite_run.py [--schema rate_documents] [--stack api,python,rust] [--set KEY=VALUE] [--check script[:argument]] [--port 5195] [--python <python with the generated requirements>]

Generates RateTables=ExchangeRate with RateDocuments=SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate, BaseCurrency=USD and RateInverse (the settings of Schemas/rate_documents.sql; --set adds or
replaces one) into a scratch folder, then for each stack empties the database, starts the API and runs the checks (default rate_check.py and rate_document_check.py) over HTTP. Schemas/rate_documents_float.sql
plays the floating point and date and time variants. A check can take arguments after colons, as in rate_check.py:ExchangeRateStamp or rate_document_check.py:CardInvoice:CardRate:EUR. Prints one line per check script and one line with the result."""
import argparse
import os
import socket
import sqlite3
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from common import cli_dir
from rate_auth_run import serve
from live_party_run import TOOLS

DOCUMENTS = ["RateTables=ExchangeRate", "RateDocuments=SalesInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate", "BaseCurrency=USD", "RateInverse=true"]


def play(stack, work, database, schema, port, python, checks):
    with sqlite3.connect(database) as db:
        for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'").fetchall():
            db.execute(f'DROP TABLE "{name}"')
        db.executescript(schema)
    command, folder, env = serve(stack, work, port, python, dict(os.environ))
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
            for check in checks:
                script, *arguments = check.split(":")
                result = subprocess.run([sys.executable, str(TOOLS / script), base, *arguments], capture_output=True, text=True)
                lines = result.stdout.splitlines()
                print(stack, check, "exit", result.returncode, "-", sum(1 for l in lines if l.startswith("ok ")), "checks ok")
                print("\n".join(l[:400] for l in lines if l.startswith("FAIL")), end="")
                if result.returncode != 0:
                    failed = True
                    print(result.stderr[-800:])
        finally:
            subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)
    return failed


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--schema", default="rate_documents")
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--check", action="append", default=[], help="a check script, optionally :table; repeat (default rate_check.py and rate_document_check.py)")
    parser.add_argument("--port", type=int, default=5195)
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "rate_sqlite"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "RateSqlite.db"
    schema = (TOOLS / "Schemas" / f"{args.schema}.sql").read_text(encoding="utf-8")
    checks = args.check or ["rate_check.py", "rate_document_check.py"]
    settings = {item.partition("=")[0]: item.partition("=")[2] for item in DOCUMENTS + args.set}
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "RateSqlite", "--schema", args.schema, "--stack", "api,python,rust", "--dir", str(work)]
        for key, value in settings.items():
            command += ["--set", f"{key}={value}"]
        result = subprocess.run(command, capture_output=True, text=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print(result.stderr[-1500:] or result.stdout[-1500:])
            return 1
    failed = False
    for stack in [s.strip() for s in args.stack.split(",") if s.strip()]:
        failed = play(stack, work / "out", database, schema, args.port, args.python, checks) or failed
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
