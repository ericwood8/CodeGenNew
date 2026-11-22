"""Plays the allowance totals of a table limited to an owner or to a tenant over a live SQL Server, for the ASP.NET and Python APIs (the checks of allowance_owner_run.py and allowance_tenant_run.py).

  python live_allowance_scoped_run.py owner|tenant --host <server> --db InvoiceSystem_test [--stack api,python]

Windows authentication. Makes the tables again in the schema allowown or allowten (Schemas/allowance_charges_owned_sqlserver.sql, allowance_charges_tenant_sqlserver.sql, dropped first, so give a test
database), adds made-up users whose passwords only this run knows, generates, starts each API with a random Jwt__Key, plays the checks and leaves the tables in place for a look."""
import argparse
import os
import secrets
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from argparse import Namespace
from pathlib import Path

import allowance_owner_run as owner
import allowance_tenant_run as tenant
from common import cli_dir, cli_exe, home, run
from live_inbound_run import KINDS, STACKS
from live_party_run import TOOLS, serve

MODES = {
    "owner": (owner, "allowown", "allowance_charges_owned_sqlserver.sql"),
    "tenant": (tenant, "allowten", "allowance_charges_tenant_sqlserver.sql"),
}


def sql(host, db, text=None, file=None):
    command = ["sqlcmd", "-S", host, "-E", "-C", "-I", "-b", "-d", db, "-h", "-1", "-W"] + (["-i", str(file)] if file else ["-Q", "SET NOCOUNT ON; " + text])
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(result.stdout + result.stderr)
    return result.stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("mode", choices=list(MODES))
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--stack", default="api,python")
    parser.add_argument("--python", default=sys.executable)
    parser.add_argument("--port", type=int, default=5195)
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    module, schema, script = MODES[args.mode]
    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / f"live_allowance_{args.mode}_mssql"
    name = "LiveAllowance" + args.mode.title()
    stacks = [s.strip() for s in args.stack.split(",") if s.strip() and s.strip() != "rust"]

    sql(args.host, args.db, file=TOOLS / "Schemas" / script)
    if args.mode == "owner":
        passwords = {n: secrets.token_urlsafe(12) for n in ("boss", "ada", "grace")}
        for n, admin in (("boss", 1), ("ada", 0), ("grace", 0)):
            sql(args.host, args.db, f"INSERT INTO {schema}.Employee (Login, PasswordHash, IsManager, IsAdministrator) VALUES ('{n}', '{module.hash_password(passwords[n])}', 0, {admin})")
        secret = passwords
    else:
        secret = secrets.token_urlsafe(12)
        sql(args.host, args.db, f"INSERT INTO {schema}.Employee (FullName, Login, PasswordHash, IsManager, IsAdministrator) VALUES ('Ada', 'ada', '{module.hash_password(secret)}', 1, 0);"
            f" INSERT INTO {schema}.EmployeeTenant (EmployeeId, TenantId, Role) SELECT e.EmployeeId, t.TenantId, NULL FROM {schema}.Employee e CROSS JOIN {schema}.Tenant t")

    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS["mssql"], "DatabaseName": args.db}
    settings.update(item.partition("=")[::2] for item in module.SETTINGS)
    if not args.no_generate:
        shutil.rmtree(work, ignore_errors=True)
        projects = home(cli) / "Projects"
        projects.mkdir(parents=True, exist_ok=True)
        config = projects / f"{name}.config"
        config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")
        try:
            result = run([cli_exe(cli), "generate", "--provider", KINDS["mssql"], "-S", args.host, "-d", args.db, "-E", "--schema", schema, "--project", name, "-o", work, "--essentials", "--force"])
        finally:
            config.unlink(missing_ok=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print((result.stdout + result.stderr)[-1500:])
            return 1

    serve_args = Namespace(kind="mssql", host=args.host, db=args.db, user=None, port=args.port, python=args.python)
    for stack in stacks:
        command, folder, env = serve(serve_args, stack, work, "")
        env.update(Jwt__Key=secrets.token_urlsafe(48))
        with open(work / f"serve_{stack}.log", "w") as out:
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
                    print(stack, "the server stopped:", "\n".join((work / f"serve_{stack}.log").read_text(errors="replace").splitlines()[-15:]))
                    module.failures.append((stack, "server"))
                    continue
                time.sleep(2)
                module.exercise(stack, f"http://localhost:{args.port}", secret)
            finally:
                subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)
    print("FAILED" if module.failures else "ALL OK")
    return 1 if module.failures else 0


if __name__ == "__main__":
    sys.exit(main())
