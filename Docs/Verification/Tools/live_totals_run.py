"""Plays the document totals over a live SQL Server, PostgreSQL or MySQL: generates DocumentTotals, TaxLines and AllowanceCharges (the settings of Matrix/document_totals.settings), starts each API stack on a port and
plays document_totals_check.py and document_totals_race_check.py over HTTP (with --tests also the generated ASP.NET tests).

  python live_totals_run.py --host <server> --db InvoiceSystem_test [--stack api,python] [--tests]
  python live_totals_run.py --kind pg --host localhost --db zz_totals --user <login> [--stack api,python,rust]
  python live_totals_run.py --kind mysql --host 127.0.0.1 --db zz_totals --user <login> [--stack api,python,rust]

SQL Server: Windows authentication, the tables are made again in the schema totals (Schemas/document_totals_sqlserver.sql, dropped first, so give a test database); Rust is refused over SQL Server. PostgreSQL and
MySQL: an empty database of its own (Schemas/document_totals_postgres.sql and document_totals_mysql.sql drop and make the four tables again) and the password in PGPASSWORD or MYSQL_PWD. The check removes its rows and
the tables stay for a look, so drop them afterwards. Prints the failed checks of each stack and one line with the result."""
import argparse
import os
import shutil
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run
from document_totals_sqlite_run import SETTINGS
from live_allowance_scoped_run import sql
from live_inbound_run import KINDS, PASSWORDS, STACKS, apply_sql
from live_party_run import TOOLS, play, tests

SCHEMAS = {"mssql": "document_totals_sqlserver.sql", "pg": "document_totals_postgres.sql", "mysql": "document_totals_mysql.sql"}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--kind", choices=list(KINDS), default="mssql")
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--user")
    parser.add_argument("--stack", default="api")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--port", type=int, default=5196)
    parser.add_argument("--tests", action="store_true", help="also run the generated tests of the header (ASP.NET)")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    if args.kind != "mssql" and not args.user:
        parser.error("--user is needed over PostgreSQL and MySQL")
    if args.kind == "mssql":
        args.user = None
    password = os.environ.get(PASSWORDS.get(args.kind, ""), "")

    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / f"live_totals_{args.kind}"
    name = "LiveTotals" + args.kind.title()
    stacks = [s.strip() for s in args.stack.split(",") if s.strip()]
    if args.kind == "mssql":
        if "rust" in stacks:
            print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
            stacks.remove("rust")
        sql(args.host, args.db, file=TOOLS / "Schemas" / SCHEMAS["mssql"])
    else:
        code, output = apply_sql(args, password, TOOLS / "Schemas" / SCHEMAS[args.kind])
        if code != 0:
            print(output[-1500:].replace(password, "***") if password else output[-1500:])
            return 1
    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS[args.kind], "DatabaseName": args.db, "ApiTests": "true", **SETTINGS}
    if args.kind != "mssql":
        settings["DatabaseUser"] = args.user
    if args.kind == "mysql":
        settings["NamingStyle"] = "Pascal"   # document_totals_mysql.sql is snake case: invoice_line is InvoiceLine in the generated code
    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        projects = home(cli) / "Projects"
        projects.mkdir(parents=True, exist_ok=True)
        config = projects / f"{name}.config"
        config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")
        connect = ["--provider", KINDS[args.kind], "-S", args.host, "-d", args.db]
        if args.kind == "mssql":
            connect += ["-E", "--schema", "totals"]
        else:
            connect += ["-U", args.user] + (["-P", password] if password else [])
        try:
            result = run([cli_exe(cli), "generate", *connect, "--project", name, "-o", work, "--essentials", "--force"])
        finally:
            config.unlink(missing_ok=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print((result.stdout + result.stderr)[-1500:])
            return 1

    failed = False
    for stack in stacks:
        if args.tests and stack == "api":
            failed = tests(args, stack, work, name, password, api_filter="FullyQualifiedName~InvoiceApiTests", label="totals") or failed
        failed = play(args, stack, work, password, [("document_totals_check.py", []), ("document_totals_race_check.py", ["20", "3"])]) or failed
    print("output:", work)
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
