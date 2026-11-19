"""Generates TaxLines over the tables of Schemas/tax_lines_postgres.sql, tax_lines_mysql.sql or tax_lines_sqlserver.sql (already applied to the database) for a live PostgreSQL, MySQL or SQL Server database, starts each API stack on a port over it
and plays tax_check.py --dated over HTTP, with --tests also the generated ASP.NET tests of the rule.

  python live_tax_run.py pg --host localhost --db <database> --user <login> --stack api,python,rust     (password in PGPASSWORD)
  python live_tax_run.py mssql --host <server> --db InvoiceSystem_test --stack api,python     (Windows authentication, the tax schema; Rust is not offered over SQL Server)
  python live_tax_run.py mysql --host 127.0.0.1 --db <database> --user <login> --stack api,python,rust  (password in MYSQL_PWD)

The check adds categories and lines and removes them again, so give a test database. The password is read from the environment and only handed to the generator and the servers of the run, never written
to a file."""
import argparse
import os
import shutil
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run
from live_inbound_run import KINDS, PASSWORDS, STACKS
from live_party_run import play, tests

TAX_LINES = "LevyLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:LevyCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("kind", choices=["pg", "mysql", "mssql"])
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--user")
    parser.add_argument("--stack", default="api")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--port", type=int, default=5194)
    parser.add_argument("--requests", type=int, default=20)
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--tests", action="store_true", help="also run the generated ASP.NET tests of the tax lines")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()

    password = os.environ.get(PASSWORDS.get(args.kind, ""), "")
    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / f"live_tax_{args.kind}"
    name = "LiveTax" + args.kind.title()
    stacks = [s.strip() for s in args.stack.split(",") if s.strip()]
    if args.kind == "mssql" and "rust" in stacks:
        print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
        stacks.remove("rust")
    tax_lines = TAX_LINES if args.kind != "mssql" else "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code"
    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS[args.kind], "DatabaseName": args.db,
                "TaxLines": tax_lines, "ApiTests": "true"}
    if args.kind != "mssql":
        settings.update({"DatabaseUser": args.user or "", "OnlyTables": "LevyCategory,LevyLine"})
    if args.kind == "mysql":
        settings["NamingStyle"] = "Pascal"   # tax_lines_mysql.sql is snake case: levy_line is LevyLine in the generated code
    for item in args.set:
        key, _, value = item.partition("=")
        settings[key] = value

    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        connect = ["--provider", KINDS[args.kind], "-S", args.host, "-d", args.db]
        if args.kind == "mssql":
            connect += ["-E", "--schema", "tax"]
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
        problems = [line.strip() for line in output.splitlines() if line.strip().startswith(("Error", "Warning"))]
        print(len(problems), "errors and warnings", problems[:5])
        if any(p.startswith("Error") for p in problems) or result.returncode != 0:
            print(output[-1500:].replace(password, "***") if password else output[-1500:])
            return 1

    if args.kind != "mssql":
        os.environ["TAX_CATEGORIES"] = "/api/levycategories"
        os.environ["TAX_LINES"] = "/api/levylines"
    failed = False
    for stack in stacks:
        if args.tests and stack == "api":
            failed = tests(args, stack, work, name, password, api_filter="FullyQualifiedName~LevyLine|FullyQualifiedName~InvoiceLineApiTests") or failed
        failed = play(args, stack, work, password, [("tax_check.py", ["--dated"])]) or failed
    print("output:", work)
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
