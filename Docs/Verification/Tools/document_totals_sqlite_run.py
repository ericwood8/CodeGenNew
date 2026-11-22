"""Plays the document totals check over a scratch SQLite copy of Schemas/document_totals.sql for the ASP.NET, Python and Rust APIs.

  python document_totals_sqlite_run.py [--stack api,python,rust] [--port 5198]

Generates DocumentTotals (with TaxLines and AllowanceCharges, the settings of Matrix/document_totals.settings) into a scratch folder, empties the database, starts each API and runs document_totals_check.py."""
import argparse
import os
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir
from live_party_run import TOOLS
from rate_sqlite_run import play

SETTINGS = {
    "DocumentTotals": "Invoice:InvoiceLine:InvoiceId:LineTotal:Lines=ItemsTotal:Allowances=AllowanceTotal:Charges=ChargeTotal:TaxExclusive=TaxExclusive:Tax=TaxTotal:TaxInclusive=TaxInclusive:Prepaid=Prepaid:Rounding=Rounding:Payable=Payable:Currency=Currency:Round=CHF/0.05:Decimals=JPY/0:AllowanceTax=TaxAmount",
    "TaxLines": "InvoiceLine:TaxCategoryId:TaxRate:LineTotal:TaxAmount:TaxCategory:Rate,InvoiceAllowanceCharge:TaxCategoryId:TaxRate:Amount:TaxAmount:TaxCategory:Rate:ValidFrom:ValidTo",
    "AllowanceCharges": "InvoiceAllowanceCharge:IsCharge:BaseAmount:Percentage:Amount:InvoiceId",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--port", type=int, default=5198)
    parser.add_argument("--python", default=sys.executable)
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "document_totals_sqlite"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "DocumentTotals.db"
    schema = (TOOLS / "Schemas" / "document_totals.sql").read_text(encoding="utf-8")
    settings = dict(SETTINGS)
    settings.update(item.partition("=")[::2] for item in args.set)
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "DocumentTotals", "--schema", "document_totals", "--stack", args.stack, "--dir", str(work)]
        for key, value in settings.items():
            command += ["--set", f"{key}={value}"]
        result = subprocess.run(command, capture_output=True, text=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print(result.stderr[-1500:] or result.stdout[-1500:])
            return 1
    os.environ["TOTALS_DB"] = str(database)
    failed = False
    for stack in [s.strip() for s in args.stack.split(",") if s.strip()]:
        failed = play(stack, work / "out", database, schema, args.port, args.python, ["document_totals_check.py"]) or failed
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
