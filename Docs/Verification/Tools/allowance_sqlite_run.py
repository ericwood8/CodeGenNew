"""Plays the allowance and charge check over a scratch SQLite copy of Schemas/allowance_charges.sql for the ASP.NET API.

  python allowance_sqlite_run.py [--set KEY=VALUE] [--port 5197]

Generates AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId into a scratch folder, then empties the database, starts the API and runs allowance_check.py."""
import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir
from live_party_run import TOOLS
from rate_sqlite_run import play

ALLOWANCES = "AllowanceCharges=InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--schema", default="allowance_charges")
    parser.add_argument("--stack", default="api")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--port", type=int, default=5197)
    parser.add_argument("--python", default=sys.executable)
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "allowance_sqlite"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "AllowanceSqlite.db"
    schema = (TOOLS / "Schemas" / f"{args.schema}.sql").read_text(encoding="utf-8")
    settings = {item.partition("=")[0]: item.partition("=")[2] for item in [ALLOWANCES] + args.set}
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "AllowanceSqlite", "--schema", args.schema, "--stack", args.stack, "--dir", str(work)]
        for key, value in settings.items():
            command += ["--set", f"{key}={value}"]
        result = subprocess.run(command, capture_output=True, text=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print(result.stderr[-1500:] or result.stdout[-1500:])
            return 1
    failed = False
    for stack in [s.strip() for s in args.stack.split(",") if s.strip()]:
        failed = play(stack, work / "out", database, schema, args.port, args.python, ["allowance_check.py"]) or failed
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
