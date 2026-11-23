"""Generates the payment tables (Schemas/payments_sqlserver.sql, payments_postgres.sql, payments_mysql.sql, already applied to the database) with BankAccountTables, InstallmentTables and PaymentTermsTables for a live SQL Server,
PostgreSQL or MySQL database, starts each API stack on a port over it and plays payment_check.py over HTTP.

  python live_payment_run.py mssql --host <server> --db InvoiceSystem_test
  python live_payment_run.py pg --host localhost --db <database> --user <login> --stack api,python,rust     (password in PGPASSWORD)
  python live_payment_run.py mysql --host 127.0.0.1 --db <database> --user <login> --stack api,python,rust  (password in MYSQL_PWD)

SQL Server uses Windows authentication and the pay schema. The checks add rows and remove them again, so give a test database. The password is read from the environment and only handed to the
generator and the servers of the run, never written to a file. Prints the failed checks of each stack and one line with the result."""
import argparse
import os
import shutil
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run
from live_inbound_run import KINDS, PASSWORDS, STACKS
from live_party_run import play

SETTINGS = {"BankAccountTables": "PartyBankAccount", "InstallmentTables": "PaymentInstallment:PayDocumentId:PayDocument:Total", "PaymentTermsTables": "PaymentMeans:PayDocumentId:PayDocument:OrderedOn", "LedgerTables": "PayTransaction:PayDocumentId:PayDocument:Total"}
TABLES = "PayDocument,PaymentTerms,PartyBankAccount,PaymentMeans,PaymentInstallment,PayTransaction"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("kind", choices=list(KINDS))
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--user")
    parser.add_argument("--stack", default="api")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed (a virtual environment)")
    parser.add_argument("--port", type=int, default=5192)
    parser.add_argument("--requests", type=int, default=20)
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()

    password = os.environ.get(PASSWORDS.get(args.kind, ""), "")
    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / f"live_payment_{args.kind}"
    name = "LivePay" + args.kind.title()
    stacks = [s.strip() for s in args.stack.split(",") if s.strip()]
    if args.kind == "mssql" and "rust" in stacks:
        print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
        stacks.remove("rust")

    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS[args.kind], "DatabaseName": args.db, **SETTINGS}
    if args.kind != "mssql":
        settings["DatabaseUser"] = args.user or ""
        settings["OnlyTables"] = "pay_document,payment_terms,party_bank_account,payment_means,payment_installment,pay_transaction" if args.kind == "mysql" else TABLES
    if args.kind == "mysql":
        settings["NamingStyle"] = "Pascal"   # payments_mysql.sql is snake case: payment_installment is PaymentInstallment in the generated code
    for item in args.set:
        key, _, value = item.partition("=")
        settings[key] = value

    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        connect = ["--provider", KINDS[args.kind], "-S", args.host, "-d", args.db]
        if args.kind == "mssql":
            connect += ["-E", "--schema", "pay"]
        else:
            connect += ["-U", args.user or ""]
            if args.kind == "pg":
                connect += ["--schema", "pay"]
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
    for stack in stacks:
        checks = [("payment_check.py", []), ("ledger_check.py", [])]
        failed = play(args, stack, work, password, checks) or failed
    print("output:", work)
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
