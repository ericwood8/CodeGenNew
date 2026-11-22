"""Plays the document totals over a live SQL Server: generates DocumentTotals, TaxLines and AllowanceCharges (the settings of Matrix/document_totals.settings) over the schema totals, starts each API stack on a port and
plays document_totals_check.py over HTTP (with --tests also the generated ASP.NET tests).

  python live_totals_run.py --host <server> --db InvoiceSystem_test [--stack api,python] [--tests]

Windows authentication. Makes the tables again in the schema totals (Schemas/document_totals_sqlserver.sql, dropped first, so give a test database); the check removes its rows and the tables stay for a look, so drop the
schema afterwards. Rust is refused over SQL Server. Prints the failed checks of each stack and one line with the result."""
import argparse
import shutil
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run
from document_totals_sqlite_run import SETTINGS
from live_allowance_scoped_run import sql
from live_inbound_run import KINDS, STACKS
from live_party_run import TOOLS, play, tests


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--stack", default="api")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--port", type=int, default=5196)
    parser.add_argument("--tests", action="store_true", help="also run the generated tests of the header (ASP.NET)")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    args.kind, args.user = "mssql", None

    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / "live_totals_mssql"
    name = "LiveTotalsMssql"
    stacks = [s.strip() for s in args.stack.split(",") if s.strip() and s.strip() != "rust"]
    sql(args.host, args.db, file=TOOLS / "Schemas" / "document_totals_sqlserver.sql")
    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS["mssql"], "DatabaseName": args.db, "ApiTests": "true", **SETTINGS}
    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        projects = home(cli) / "Projects"
        projects.mkdir(parents=True, exist_ok=True)
        config = projects / f"{name}.config"
        config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")
        try:
            result = run([cli_exe(cli), "generate", "--provider", KINDS["mssql"], "-S", args.host, "-d", args.db, "-E", "--schema", "totals", "--project", name, "-o", work, "--essentials", "--force"])
        finally:
            config.unlink(missing_ok=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print((result.stdout + result.stderr)[-1500:])
            return 1

    failed = False
    args.port = args.port
    for stack in stacks:
        if args.tests and stack == "api":
            failed = tests(args, stack, work, name, "", api_filter="FullyQualifiedName~InvoiceApiTests", label="totals") or failed
        failed = play(args, stack, work, "", [("document_totals_check.py", []), ("document_totals_race_check.py", ["20", "3"])]) or failed
    print("output:", work)
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
