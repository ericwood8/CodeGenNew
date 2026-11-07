"""Generates the inbound landing tables (Schemas/inbound_mssql.sql, inbound_postgres.sql, inbound_mysql.sql, already applied to the database) with their generated tests and plays the tests of each API stack over that live database.

  python live_inbound_run.py mssql --host ERICSMINIPC --db InvoiceSystem_test
  python live_inbound_run.py pg --host localhost --db InvoiceSystem --user <login> --stack api,python,rust     (password in PGPASSWORD)
  python live_inbound_run.py mysql --host 127.0.0.1 --db invoicesystem --user <login> --stack api            (password in MYSQL_PWD)

SQL Server uses Windows authentication. The database is written to (the tests remove their rows again), so give a test database. The password is read from the environment and only handed to the generator, the test run and the connection strings of the run, never written to a file.
Prints one line per stack: the passed and failed counts and the first failures.
"""
import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run

KINDS = {"mssql": "SqlServer", "pg": "PostgreSql", "mysql": "MySql"}
PASSWORDS = {"pg": "PGPASSWORD", "mysql": "MYSQL_PWD"}
STACKS = {"api": "Api", "python": "Python", "rust": "Rust"}


def connection_string(kind, host, db, user, password):
    if kind == "mssql":
        return f"Server={host};Database={db};Trusted_Connection=True;TrustServerCertificate=True"
    if kind == "pg":
        return f"Host={host};Database={db};Username={user};Password={password}"
    return f"Server={host};Database={db};User={user};Password={password};AllowUserVariables=true"


def url(kind, host, db, user, password, stack="python"):
    if stack == "rust":
        scheme = "postgres" if kind == "pg" else "mysql"
    else:
        scheme = "postgresql+psycopg" if kind == "pg" else "mysql+pymysql"
    return f"{scheme}://{user}:{password}@{host}/{db}"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("kind", choices=list(KINDS))
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--user")
    parser.add_argument("--stack", default="api")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed (a virtual environment)")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()

    password = os.environ.get(PASSWORDS.get(args.kind, ""), "")
    cli = cli_dir(args.cli_dir)
    exe = cli_exe(cli)
    work = Path(tempfile.gettempdir()) / f"live_inbound_{args.kind}"
    name = "LiveInbound" + args.kind.title()
    stacks = [s.strip() for s in args.stack.split(",") if s.strip()]

    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef",
                "DatabaseProvider": KINDS[args.kind], "DatabaseName": args.db,
                "InboundTables": "InboundWebhook,InboundErrorReport:Payload",
                "ApiTests": "true", "PythonTests": "true", "RustTests": "true"}
    for item in args.set:
        key, _, value = item.partition("=")
        settings[key] = value
    if args.kind != "mssql":
        settings["DatabaseUser"] = args.user or ""

    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        connect = ["--provider", KINDS[args.kind], "-S", args.host, "-d", args.db]
        if args.kind == "mssql":
            connect.append("-E")
        else:
            connect += ["-U", args.user or ""]
            if password:
                connect += ["-P", password]
        projects = home(cli) / "Projects"
        projects.mkdir(parents=True, exist_ok=True)
        config = projects / f"{name}.config"
        config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")
        try:
            result = run([exe, "generate", *connect, "--project", name, "-o", work, "--essentials", "--force"])
        finally:
            config.unlink(missing_ok=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        errors = [line.strip() for line in (result.stdout + result.stderr).splitlines() if line.strip().startswith("Error")]
        print(len(errors), "template errors", errors[:5])
        if errors:
            return 1

    environment = dict(os.environ)
    environment["INBOUND_WEBHOOK_SECRET"] = "live-test-shared-secret"
    environment["INBOUND_SECRET"] = "live-test-shared-secret"
    environment["STRIPE_WEBHOOK_SECRET"] = "live-test-own-secret"
    variable = "".join(c if c.isalnum() else "_" for c in name.upper()) + "_TEST_CONNECTION"
    failed = False
    for stack in stacks:
        if stack == "api":
            environment[variable] = connection_string(args.kind, args.host, args.db, args.user, password)
            folder = next(work.glob("*.Api.Tests"))
            command = ["dotnet", "test", "--filter", "FullyQualifiedName~InboundApiTests"]
        elif stack == "python":
            if args.kind == "mssql":
                print("python: SQL Server is skipped by the generated tests")
                continue
            environment[variable] = url(args.kind, args.host, args.db, args.user, password)
            folder = next(work.glob("*Python*")) if list(work.glob("*Python*")) else work
            command = [args.python, "-m", "pytest", "-q", "tests"]
        else:
            if args.kind == "mssql":
                print("rust: SQL Server is not supported by the Rust stack")
                continue
            environment[variable] = url(args.kind, args.host, args.db, args.user, password, "rust")
            folder = next(work.glob("*Rust*")) if list(work.glob("*Rust*")) else work
            command = [sys.executable, str(Path(__file__).resolve().parents[3].parent / "tools" / "cargo_vs.py"), str(folder), "test", "--test", "api", "inbound"]
        result = subprocess.run(command, cwd=folder, env=environment, capture_output=True, text=True)
        lines = (result.stdout + result.stderr).splitlines()
        interesting = [l for l in lines if l.startswith(("Passed!", "Failed!", "  Failed", "test result", "FAILED", "failed"))
                       or " passed" in l or " failed" in l]
        print(stack, "exit", result.returncode)
        print("\n".join(l[:300].replace(password, "***") if password else l[:300] for l in interesting[:20]))
        if result.returncode != 0:
            failed = True
            print("\n".join(l[:300].replace(password, "***") if password else l[:300] for l in lines[-25:]))
    print("output:", work)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
