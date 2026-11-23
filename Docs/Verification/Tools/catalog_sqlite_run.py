"""Plays the facet checks over a scratch SQLite copy of Schemas/catalog.sql for the ASP.NET, Python and Rust APIs.

  python catalog_sqlite_run.py [--stack api,python,rust] [--set KEY=VALUE] [--check script[:argument]] [--port 5197]

Generates FacetTables=ProductAttribute into a scratch folder and database, starts the API and runs catalog_check.py over HTTP. Prints one line per check script and one line with the result."""
import argparse
import os
import sys
import tempfile
import subprocess
from pathlib import Path

from common import cli_dir
from live_party_run import TOOLS
from rate_sqlite_run import play

SETTINGS = ["FacetTables=ProductAttribute"]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--schema", default="catalog")
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--check", action="append", default=[], help="a check script, optionally :argument; repeat (default catalog_check.py)")
    parser.add_argument("--port", type=int, default=5197)
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "catalog_sqlite"))
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    cli_dir(args.cli_dir)
    work = Path(args.dir)
    database = work / "CatalogSqlite.db"
    os.environ["CATALOG_SQLITE"] = str(database)   # catalog_check.py moves the end of a hold into the past through this file
    schema = (TOOLS / "Schemas" / f"{args.schema}.sql").read_text(encoding="utf-8")
    checks = args.check or ["catalog_check.py"]
    settings = {item.partition("=")[0]: item.partition("=")[2] for item in SETTINGS + args.set}
    if not args.no_generate:
        command = [sys.executable, str(TOOLS / "new_scratch_project.py"), "CatalogSqlite", "--schema", args.schema, "--stack", "api,python,rust", "--dir", str(work)]
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
