"""Generates the inbound landing tables (Schemas/inbound_mssql.sql, inbound_postgres.sql, inbound_mysql.sql, already applied to the database) with their generated tests and plays the tests of each API stack over that live database.

  python live_inbound_run.py mssql --host ERICSMINIPC --db InvoiceSystem_test
  python live_inbound_run.py pg --host localhost --db InvoiceSystem --user <login> --stack api,python,rust     (password in PGPASSWORD)
  python live_inbound_run.py mysql --host 127.0.0.1 --db invoicesystem --user <login> --stack api            (password in MYSQL_PWD)

SQL Server uses Windows authentication. The database is written to (the tests remove their rows again), so give a test database. The password is read from the environment and only handed to the generator, the test run and the connection strings of the run, never written to a file.
With --http each API is started on --port and inbound_check.py is played against it over HTTP (the status route, the collect route, a sender with its own secret) instead of the generated tests.
Prints one line per stack: the passed and failed counts and the first failures.
"""
import argparse
import os
import shutil
import socket
import time
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run

KINDS = {"mssql": "SqlServer", "pg": "PostgreSql", "mysql": "MySql"}
PASSWORDS = {"pg": "PGPASSWORD", "mysql": "MYSQL_PWD"}
STACKS = {"api": "Api", "python": "Python", "rust": "Rust", "react": "React"}


def connection_string(kind, host, db, user, password):
    if kind == "mssql":
        return f"Server={host};Database={db};Trusted_Connection=True;TrustServerCertificate=True"
    if kind == "pg":
        return f"Host={host};Database={db};Username={user};Password={password}"
    return f"Server={host};Database={db};User={user};Password={password};AllowUserVariables=true"


def url(kind, host, db, user, password, stack="python"):
    if kind == "mssql":
        return f"mssql+pyodbc://@{host}/{db}?driver=ODBC+Driver+17+for+SQL+Server&Trusted_Connection=yes&TrustServerCertificate=yes"
    if stack == "rust":
        scheme = "postgres" if kind == "pg" else "mysql"
    else:
        scheme = "postgresql+psycopg" if kind == "pg" else "mysql+pymysql"
    return f"{scheme}://{user}:{password}@{host}/{db}"


def http_check(args, stack, work, environment, password, port):
    """Starts the generated API of one stack on a port over the live database, plays inbound_check.py against it over HTTP and stops it. True when the check failed."""
    env = dict(environment)
    cargo = str(Path(__file__).resolve().parents[3].parent / "tools" / "cargo_vs.py")
    if stack == "api":
        env["ConnectionStrings__DbConnectionString"] = connection_string(args.kind, args.host, args.db, args.user, password)
        env["ASPNETCORE_ENVIRONMENT"] = "Development"
        folder = next(work.glob("*.Api"))
        command = ["dotnet", "run", "--project", str(folder), "--no-launch-profile", "--urls", f"http://localhost:{port}"]
    elif stack == "python":
        env["DATABASE_URL"] = url(args.kind, args.host, args.db, args.user, password)
        folder = next(work.glob("*.Python"))
        command = [args.python, "-m", "uvicorn", "app.main:app", "--port", str(port)]
    else:
        env["PORT"] = str(port)
        host = args.host.split(":")[0]
        if args.kind == "pg":
            env.update(PGHOST=host, PGUSER=args.user or "", PGDATABASE=args.db)
        else:
            env.update(MYSQL_HOST=host, MYSQL_USER=args.user or "", MYSQL_DATABASE=args.db)
        folder = next(work.glob("*.Rust"))
        command = [sys.executable, cargo, str(folder), "run"]
    log = work / f"serve_{stack}.log"
    with open(log, "w") as out:
        server = subprocess.Popen(command, cwd=folder, env=env, stdout=out, stderr=subprocess.STDOUT)
        try:
            deadline = time.time() + 420
            while time.time() < deadline:
                if server.poll() is not None:
                    break
                try:
                    socket.create_connection(("localhost", port), timeout=1).close()
                    break
                except OSError:
                    time.sleep(2)
            else:
                print(stack, "the server did not start in time")
            if server.poll() is not None:
                print(stack, "the server stopped:", "\n".join(log.read_text(errors="replace").splitlines()[-15:]))
                return True
            check = [sys.executable, str(Path(__file__).resolve().parent / "inbound_check.py"), f"http://localhost:{port}",
                     "--webhook-route", "/api/" + args.webhook_table.lower() + "s", "--error-route", "/api/" + args.error_table.lower() + "s", "--client-limit", "30", "--own", "stripe:STRIPE_WEBHOOK_SECRET"]
            result = subprocess.run(check, env=env, capture_output=True, text=True)
            lines = result.stdout.splitlines()
            print(stack, "http check exit", result.returncode, "-", sum(1 for l in lines if l.startswith("ok ")), "checks ok")
            print("\n".join(l[:300] for l in lines if l.startswith("FAIL") or l.startswith("ALL OK") or "FAILED" in l))
            if result.returncode != 0:
                print(result.stderr[-1500:])
            return result.returncode != 0
        finally:
            subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)


def apply_sql(args, password, script):
    """Runs a SQL file with the client of the database (sqlcmd, psql or mysql); returns the exit code and the output."""
    if args.kind == "mssql":
        command = ["sqlcmd", "-S", args.host, "-E", "-I", "-b", "-d", args.db, "-i", str(script)]
    elif args.kind == "pg":
        command = [r"C:\Program Files\PostgreSQL\18\bin\psql.exe", "-h", args.host.split(":")[0], "-U", args.user or "", "-d", args.db, "-v", "ON_ERROR_STOP=1", "-f", str(script)]
    else:
        command = [r"C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe", "-h", args.host.split(":")[0], "-u", args.user or "", args.db, "--execute=source " + str(script).replace("\\", "/")]
    result = subprocess.run(command, capture_output=True, text=True)
    return result.returncode, (result.stdout + result.stderr)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("kind", choices=list(KINDS))
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--user")
    parser.add_argument("--stack", default="api")
    parser.add_argument("--set", action="append", default=[], help="KEY=VALUE project setting; repeat")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed (a virtual environment)")
    parser.add_argument("--http", action="store_true", help="start each API on a port and play inbound_check.py over HTTP instead of the generated tests")
    parser.add_argument("--webhook-table", default="InboundWebhook")
    parser.add_argument("--error-table", default="InboundErrorReport")
    parser.add_argument("--partition", action="store_true", help="also set PartitionTables for both tables and, after generating, run the partition script twice (the tables must exist and be unpartitioned)")
    parser.add_argument("--port", type=int, default=5191)
    parser.add_argument("--no-run", action="store_true", help="only generate (and run the partition script)")
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
                "InboundTables": f"{args.webhook_table},{args.error_table}:Payload",
                "ApiTests": "true", "PythonTests": "true", "RustTests": "true"}
    if args.partition:
        settings["PartitionTables"] = f"{args.webhook_table},{args.error_table}"
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
        if args.partition:
            script = next(work.rglob("PartitionJobs.sql"))
            for turn in ("first", "second"):
                code, output = apply_sql(args, password, script)
                print("partition script,", turn, "run: exit", code, output.strip()[-600:].replace(password, "***") if password else output.strip()[-600:])
                if code != 0:
                    return 1

    environment = dict(os.environ)
    environment["INBOUND_WEBHOOK_SECRET"] = "live-test-shared-secret"
    environment["INBOUND_SECRET"] = "live-test-shared-secret"
    environment["STRIPE_WEBHOOK_SECRET"] = "live-test-own-secret"
    variable = "".join(c if c.isalnum() else "_" for c in name.upper()) + "_TEST_CONNECTION"
    failed = False
    for stack in [] if args.no_run else stacks:
        if args.http:
            if stack == "rust" and args.kind == "mssql":
                print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
                continue
            failed = http_check(args, stack, work, environment, password, args.port) or failed
            continue
        if stack == "api":
            environment[variable] = connection_string(args.kind, args.host, args.db, args.user, password)
            folder = next(work.glob("*.Api.Tests"))
            command = ["dotnet", "test", "--filter", "FullyQualifiedName~InboundApiTests"]
        elif stack == "python":
            environment[variable] = url(args.kind, args.host, args.db, args.user, password)
            folder = next(work.glob("*Python*")) if list(work.glob("*Python*")) else work
            command = [args.python, "-m", "pytest", "-q", "tests"]
        else:
            if args.kind == "mssql":
                print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
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
