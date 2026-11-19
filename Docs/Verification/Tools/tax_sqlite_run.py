"""Plays the tax line check over a scratch SQLite copy of Schemas/tax_lines.sql for the ASP.NET, Python and Rust APIs.

  python tax_sqlite_run.py [--stack api,python,rust] [--set KEY=VALUE] [--port 5196] [--python <python with the generated requirements>]

Generates TaxLines with the dating, the day, the document and the label (see tax_check.py) into a scratch folder, then for each stack empties the database, starts the API and runs tax_check.py --dated.
"""
import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir
from live_party_run import TOOLS
from rate_sqlite_run import play

TAX_LINES = "TaxLines=InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo:LineDate:InvoiceId:Code"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--schema", default="tax_lines")
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--check", action="append", default=[], help="a check script with colon arguments (default tax_check.py:--dated)")
    parser.add_argument("--port", type=int, default=5196)
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "tax_sqlite"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "TaxSqlite.db"
    schema = (TOOLS / "Schemas" / f"{args.schema}.sql").read_text(encoding="utf-8")
    checks = args.check or ["tax_check.py:--dated"]
    settings = {item.partition("=")[0]: item.partition("=")[2] for item in [TAX_LINES] + args.set}
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "TaxSqlite", "--schema", args.schema, "--stack", "api,python,rust", "--dir", str(work)]
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
