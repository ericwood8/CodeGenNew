"""Generates the party tables (Schemas/parties_sqlserver.sql, parties_postgres.sql, parties_mysql.sql, already applied to the database) with PartyTables=Party:PartyIdentifier for a live SQL Server, PostgreSQL or
MySQL database, starts each API stack on a port over it and plays party_check.py and party_race_check.py over HTTP.

  python live_party_run.py mssql --host <server> --db InvoiceSystem_test
  python live_party_run.py pg --host localhost --db <database> --user <login> --stack api,python,rust     (password in PGPASSWORD)
  python live_party_run.py mysql --host 127.0.0.1 --db <database> --user <login> --stack api,python,rust  (password in MYSQL_PWD)

SQL Server uses Windows authentication and the party schema. The checks add parties and identifiers and remove them again, so give a test database. The password is read from the environment and only handed to
the generator and the servers of the run, never written to a file. With --tests the generated tests of the party tables are run as well (they add rows too).
Prints the failed checks of each stack and one line with the result."""
import argparse
import os
import secrets
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import httpx

from common import cli_dir, cli_exe, home, run
from live_inbound_run import KINDS, PASSWORDS, STACKS, connection_string, url

TOOLS = Path(__file__).resolve().parent
CARGO = str(Path(__file__).resolve().parents[3].parent / "tools" / "cargo_vs.py")


def serve(args, stack, work, password):
    """The command, folder and environment that start the generated API of one stack on the port."""
    env = dict(os.environ)
    if stack == "api":
        env["ConnectionStrings__DbConnectionString"] = connection_string(args.kind, args.host, args.db, args.user, password)
        env["ASPNETCORE_ENVIRONMENT"] = "Development"
        folder = next(f for f in work.glob("*.Api") if f.is_dir())
        return ["dotnet", "run", "--project", str(folder), "--no-launch-profile", "--urls", f"http://localhost:{args.port}"], folder, env
    if stack == "python":
        env["DATABASE_URL"] = url(args.kind, args.host, args.db, args.user, password)
        return [args.python, "-m", "uvicorn", "app.main:app", "--port", str(args.port)], next(work.glob("*.Python")), env
    env["PORT"] = str(args.port)
    host = args.host.split(":")[0]
    if args.kind == "pg":
        env.update(PGHOST=host, PGUSER=args.user or "", PGDATABASE=args.db)
    else:
        env.update(MYSQL_HOST=host, MYSQL_USER=args.user or "", MYSQL_DATABASE=args.db)
    folder = next(work.glob("*.Rust"))
    return [sys.executable, CARGO, str(folder), "run"], folder, env


def play(args, stack, work, password, checks=None, sign_in=None):
    """Starts one API, plays the checks (the party checks, or the (script, arguments) pairs given) and stops it. True when a check failed.
    sign_in is (user name, password, a path that needs a sign-in) of an administrator to add with POST /api/auth/first-person (the user table must be empty): a request with no sign-in must then be refused (401) and the checks run signed in."""
    command, folder, env = serve(args, stack, work, password)
    checked = dict(os.environ)
    if sign_in:
        env["Jwt__Key"] = secrets.token_urlsafe(48)
        checked.update(API_USER=sign_in[0], API_PASSWORD=sign_in[1])
    log = work / f"serve_{stack}.log"
    failed = False
    with open(log, "w") as out:
        server = subprocess.Popen(command, cwd=folder, env=env, stdout=out, stderr=subprocess.STDOUT)
        try:
            deadline = time.time() + 600
            while time.time() < deadline and server.poll() is None:
                try:
                    socket.create_connection(("localhost", args.port), timeout=1).close()
                    break
                except OSError:
                    time.sleep(2)
            if server.poll() is not None:
                print(stack, "the server stopped:", "\n".join(log.read_text(errors="replace").splitlines()[-15:]))
                return True
            time.sleep(2)
            base = f"http://localhost:{args.port}"
            if sign_in:
                signed_out = httpx.get(base + sign_in[2], timeout=30)
                print(stack, "no sign-in:", signed_out.status_code, "ok" if signed_out.status_code == 401 else "FAIL")
                failed = signed_out.status_code != 401
                first = httpx.post(base + "/api/auth/first-person", json={"userName": sign_in[0], "password": sign_in[1]}, timeout=30)
                print(stack, "first person:", first.status_code, "ok" if first.is_success else "FAIL")
                if not first.is_success:
                    return True
            for script, extra in checks or (("party_check.py", []), ("party_race_check.py", [str(args.requests), str(args.rounds)])):
                result = subprocess.run([sys.executable, str(TOOLS / script), base, *extra], capture_output=True, text=True, env=checked)
                lines = result.stdout.splitlines()
                print(stack, script, "exit", result.returncode, "-", sum(1 for l in lines if l.startswith("ok ")), "checks ok")
                print("\n".join(l[:400] for l in lines if l.startswith("FAIL")), end="")
                if result.returncode != 0:
                    failed = True
                    print(result.stderr[-800:])
        finally:
            subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)
    return failed


def tests(args, stack, work, name, password, api_filter="FullyQualifiedName~PartyIdentifierApiTests|FullyQualifiedName~PartyApiTests", python_file="tests/test_parties.py", rust_filter="party", label="party"):
    """Runs the generated tests of the party tables (or the filters, file and name given) for one stack. True when they failed."""
    env = dict(os.environ)
    variable = "".join(c if c.isalnum() else "_" for c in name.upper()) + "_TEST_CONNECTION"
    if stack == "api":
        env[variable] = connection_string(args.kind, args.host, args.db, args.user, password)
        folder, command = next(work.glob("*.Api.Tests")), ["dotnet", "test", "--filter", api_filter]
    elif stack == "python":
        if args.kind == "mssql":
            print(f"python tests: the generated {label} tests are not written for SQL Server")
            return False
        env[variable] = url(args.kind, args.host, args.db, args.user, password)
        folder, command = next(work.glob("*.Python")), [args.python, "-m", "pytest", "-q", *python_file.split()]
    else:
        env[variable] = url(args.kind, args.host, args.db, args.user, password, "rust")
        folder = next(work.glob("*.Rust"))
        command = [sys.executable, CARGO, str(folder), "test", "--test", "api", rust_filter]
    result = subprocess.run(command, cwd=folder, env=env, capture_output=True, text=True)
    lines = (result.stdout + result.stderr).splitlines()
    shown = [l for l in lines if l.startswith(("Passed!", "Failed!", "  Failed", "test result", "FAILED")) or " passed" in l or " failed" in l]
    print(stack, "tests exit", result.returncode)
    print("\n".join((l[:300].replace(password, "***") if password else l[:300]) for l in (shown[:12] if result.returncode == 0 else lines[-25:])))
    return result.returncode != 0


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
    parser.add_argument("--tests", action="store_true", help="also run the generated tests of the party tables")
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()

    password = os.environ.get(PASSWORDS.get(args.kind, ""), "")
    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / f"live_party_{args.kind}"
    name = "LiveParty" + args.kind.title()
    stacks = [s.strip() for s in args.stack.split(",") if s.strip()]
    if args.kind == "mssql" and "rust" in stacks:
        print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
        stacks.remove("rust")

    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS[args.kind], "DatabaseName": args.db,
                "PartyTables": "Party:PartyIdentifier", "ApiTests": "true", "PythonTests": "true", "RustTests": "true"}
    if args.kind != "mssql":
        settings["DatabaseUser"] = args.user or ""
        settings["OnlyTables"] = "Party,PartyIdentifier"
    if args.kind == "mysql":
        settings["NamingStyle"] = "Pascal"   # parties_mysql.sql is snake case: party_identifier is PartyIdentifier in the generated code
    for item in args.set:
        key, _, value = item.partition("=")
        settings[key] = value

    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        connect = ["--provider", KINDS[args.kind], "-S", args.host, "-d", args.db]
        if args.kind == "mssql":
            connect += ["-E", "--schema", "party"]
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
        problems = [line.strip() for line in output.splitlines() if line.strip().startswith(("Error", "Warning")) or "unique index" in line]
        print(len(problems), "errors and warnings", problems[:5])
        if any(p.startswith("Error") for p in problems) or result.returncode != 0:
            print(output[-1500:].replace(password, "***") if password else output[-1500:])
            return 1

    failed = False
    for stack in stacks:
        if args.tests:
            failed = tests(args, stack, work, name, password) or failed
        failed = play(args, stack, work, password) or failed
    print("output:", work)
    print("FAILED" if failed else "ALL OK")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
