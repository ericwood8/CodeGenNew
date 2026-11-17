"""Generates the rate table (Schemas/rates_sqlserver.sql, rates_postgres.sql, rates_mysql.sql, already applied to the database) with RateTables=ExchangeRate for a live SQL Server, PostgreSQL or
MySQL database, starts each API stack on a port over it and plays rate_check.py and rate_race_check.py over HTTP (with --odata also rate_odata_check.py on the ASP.NET API).

  python live_rate_run.py mssql --host <server> --db InvoiceSystem_test
  python live_rate_run.py pg --host localhost --db <database> --user <login> --stack api,python,rust     (password in PGPASSWORD)
  python live_rate_run.py mysql --host 127.0.0.1 --db <database> --user <login> --stack api,python,rust  (password in MYSQL_PWD)

SQL Server uses Windows authentication and the rate schema. The check adds rates and removes them again, so give a test database. The password is read from the environment and only handed to the
generator and the servers of the run, never written to a file. Prints the failed checks of each stack and one line with the result."""
import argparse
import os
import secrets
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run
from live_inbound_run import KINDS, PASSWORDS, STACKS
from live_party_run import TOOLS, play, tests

PSQL = shutil.which("psql") or r"C:\Program Files\PostgreSQL\18\bin\psql.exe"
MYSQL = shutil.which("mysql") or r"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe"


def forget_user(args, login):
    """Removes the administrator the run added, so the user table is empty for the next run."""
    if args.kind == "mssql":
        command = ["sqlcmd", "-S", args.host, "-E", "-C", "-b", "-d", args.db, "-Q", f"DELETE FROM rate.RateUser WHERE Login = '{login}'"]
    elif args.kind == "pg":
        command = [PSQL, "-h", args.host, "-U", args.user or "", "-d", args.db, "-q", "-c", f"DELETE FROM \"RateUser\" WHERE \"Login\" = '{login}'"]
    else:
        command = [MYSQL, "-h", args.host, "-u", args.user or "", args.db, "-e", f"DELETE FROM rate_user WHERE login = '{login}'"]
    result = subprocess.run(command, capture_output=True, text=True)
    print("removed the made-up administrator" if result.returncode == 0 else "COULD NOT remove the made-up administrator: " + result.stderr[-300:])


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("kind", choices=list(KINDS))
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--user")
    parser.add_argument("--stack", default="api")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed (a virtual environment)")
    parser.add_argument("--port", type=int, default=5193)
    parser.add_argument("--requests", type=int, default=20)
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--tests", action="store_true", help="also run the generated tests of the rate table (ASP.NET, Python and Rust)")
    parser.add_argument("--odata", action="store_true", help="generate with OData=true and play rate_odata_check.py on the ASP.NET API as well")
    parser.add_argument("--documents", action="store_true", help="also generate RateDocuments=RatedInvoice with BaseCurrency=USD and RateInverse and play rate_document_check.py on every stack")
    parser.add_argument("--auth", action="store_true", help="generate with ApiAuth and the RateUser table as the user table, add a made-up administrator with first-person, and play the checks signed in (no generated tests)")
    parser.add_argument("--stamp", action="store_true", help="play the rate table whose day is a date and time (ExchangeRateStamp) instead of ExchangeRate: rate_check.py and the generated tests, no race, OData, document or sign-in")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()

    password = os.environ.get(PASSWORDS.get(args.kind, ""), "")
    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / f"live_rate_{args.kind}"
    name = "LiveRate" + args.kind.title()
    stacks = [s.strip() for s in args.stack.split(",") if s.strip()]
    if args.kind == "mssql" and "rust" in stacks:
        print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
        stacks.remove("rust")

    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS[args.kind], "DatabaseName": args.db,
                "RateTables": "ExchangeRate", "ApiTests": "true", "PythonTests": "true", "RustTests": "true"}
    if args.kind != "mssql":
        settings["DatabaseUser"] = args.user or ""
        settings["OnlyTables"] = "ExchangeRate"
    if args.kind == "mysql":
        settings["NamingStyle"] = "Pascal"   # rates_mysql.sql is snake case: exchange_rate is ExchangeRate in the generated code
    if args.odata:
        settings["OData"] = "true"
    if args.documents:
        settings.update({"RateDocuments": "RatedInvoice:CurrencyCode:FxRate:Total:BaseTotal:InvoiceDate", "BaseCurrency": "USD", "RateInverse": "true"})
        if args.kind != "mssql":
            settings["OnlyTables"] = "ExchangeRate,RatedInvoice"
    if args.auth:
        settings.update({"ApiAuth": "true", "UserTable": "RateUser", "UserNameColumn": "Login", "UserRoleColumn": "IsManager", "UserAdminColumn": "IsAdministrator"})
        if args.kind != "mssql":
            settings["OnlyTables"] += ",RateUser"
    table = "ExchangeRateStamp" if args.stamp else "ExchangeRate"
    if args.stamp:
        settings["RateTables"] = table
        if args.kind != "mssql":
            settings["OnlyTables"] = table
    for item in args.set:
        key, _, value = item.partition("=")
        settings[key] = value

    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        connect = ["--provider", KINDS[args.kind], "-S", args.host, "-d", args.db]
        if args.kind == "mssql":
            connect += ["-E", "--schema", "rate"]
        else:
            connect += ["-U", args.user or ""]
            if password:
                connect += ["-P", password]
        projects = home(cli) / "Projects"
        projects.mkdir(parents=True, exist_ok=True)
        config = projects / f"{name}.config"
        config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")
        try:
            result = run([cli_exe(cli), "generate", *connect, "--project", name, "-o", work, "--essentials", "--force"])
        finally:
            config.unlink(missing_ok=True)
        output = result.stdout + result.stderr
        print((result.stdout.strip().splitlines() or [""])[-1])
        problems = [line.strip() for line in output.splitlines() if line.strip().startswith(("Error", "Warning")) or "unique index" in line]
        print(len(problems), "errors and warnings", problems[:5])
        if any(p.startswith("Error") for p in problems) or result.returncode != 0:
            print(output[-1500:].replace(password, "***") if password else output[-1500:])
            return 1

    failed = False
    login = "zz_" + secrets.token_hex(3)
    sign_in = (login, secrets.token_urlsafe(18), "/api/exchangerates") if args.auth else None
    for stack in stacks:
        if args.tests and not args.auth:
            failed = tests(args, stack, work, name, password, api_filter=f"FullyQualifiedName~{table}|FullyQualifiedName~RatedInvoice", python_file="tests/test_rates.py tests/test_rate_documents.py" if args.documents else "tests/test_rates.py", rust_filter="rate", label="rate") or failed
        checks = [("rate_check.py", [table])] if args.stamp else [("rate_check.py", []), ("rate_race_check.py", [str(args.requests), str(args.rounds)])] + ([("rate_odata_check.py", [])] if args.odata and stack == "api" else []) + ([("rate_document_check.py", ["RatedInvoice"])] if args.documents else [])
        failed = play(args, stack, work, password, checks, sign_in) or failed
        if sign_in:
            forget_user(args, login)
    print("output:", work)
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
