"""Generates the allowance and charge tables (Schemas/allowance_charges_sqlserver.sql, already applied to the database) with AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId
over a live SQL Server, starts each API stack on a port over it and plays allowance_check.py over HTTP (with --tests also the generated ASP.NET tests).

  python live_allowance_run.py --host <server> --db InvoiceSystem_test [--stack api,python] [--tests]

Windows authentication, the schema allow. The run adds one invoice for the rows to belong to and removes it and its rows at the end, so give a test database. Prints the failed checks of each stack and
one line with the result."""
import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run
from live_inbound_run import KINDS, STACKS
from live_party_run import play, tests

SETTINGS = "AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId"


def sql(host, db, text):
    result = subprocess.run(["sqlcmd", "-S", host, "-E", "-C", "-b", "-d", db, "-h", "-1", "-W", "-Q", "SET NOCOUNT ON; " + text], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(result.stdout + result.stderr)
    return result.stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--stack", default="api")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed (a virtual environment)")
    parser.add_argument("--port", type=int, default=5194)
    parser.add_argument("--tests", action="store_true", help="also run the generated tests of the table (ASP.NET)")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    args.kind, args.user = "mssql", None

    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / "live_allowance_mssql"
    name = "LiveAllowanceMssql"
    stacks = [s.strip() for s in args.stack.split(",") if s.strip()]
    if "rust" in stacks:
        print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
        stacks.remove("rust")
    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS["mssql"], "DatabaseName": args.db,
                "AllowanceCharges": SETTINGS.partition("=")[2], "ApiTests": "true", "PythonTests": "true"}
    for item in args.set:
        key, _, value = item.partition("=")
        settings[key] = value

    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        projects = home(cli) / "Projects"
        projects.mkdir(parents=True, exist_ok=True)
        config = projects / f"{name}.config"
        config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")
        try:
            result = run([cli_exe(cli), "generate", "--provider", KINDS["mssql"], "-S", args.host, "-d", args.db, "-E", "--schema", "allow", "--project", name, "-o", work, "--essentials", "--force"])
        finally:
            config.unlink(missing_ok=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print((result.stdout + result.stderr)[-1500:])
            return 1

    failed = False
    document = sql(args.host, args.db, "INSERT INTO allow.Invoice (Customer) OUTPUT INSERTED.InvoiceId VALUES ('zz_allowance')").splitlines()[0]
    try:
        for stack in stacks:
            if args.tests and stack == "api":
                failed = tests(args, stack, work, name, "", api_filter="FullyQualifiedName~InvoiceAllowanceCharge", label="allowance") or failed
            failed = play(args, stack, work, "", [("allowance_check.py", ["invoiceId", document])]) or failed
    finally:
        sql(args.host, args.db, f"DELETE FROM allow.InvoiceAllowanceCharge WHERE InvoiceId = {int(document)}; DELETE FROM allow.Invoice WHERE InvoiceId = {int(document)}")
    print("output:", work)
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
