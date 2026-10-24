"""Generates the API and its tests for one existing database with ApiTestContainers and no ApiTestSchema, then runs the tests: a container is started and filled from the create script written from the catalog.

  python container_run.py mssql --host <server> --db <database> --trusted
  python container_run.py pg --host localhost --db <database> --user <login>        (password in PGPASSWORD)
  python container_run.py mysql --host 127.0.0.1:3306 --db <database> --user <login> (password in MYSQL_PWD)
  add --ef for AccessMode=Ef (SQLite-style routines are then not needed)

Needs Docker and a built CLI. The database is only read. The password is read from the environment and passed to the CLI as an argument for the run, never written to a file.
Prints the template errors, the create script written, the tests that failed and why the container could not be made, if it could not.
"""
import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run

PROVIDERS = {"mssql": ("SqlServer", None), "pg": ("PostgreSql", "PGPASSWORD"), "mysql": ("MySql", "MYSQL_PWD")}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("kind", choices=list(PROVIDERS))
    parser.add_argument("--host", required=True, help="server (host:port for PostgreSQL or MySQL on another port)")
    parser.add_argument("--db", required=True)
    parser.add_argument("--user")
    parser.add_argument("--trusted", action="store_true", help="SQL Server Windows authentication")
    parser.add_argument("--ef", action="store_true", help="AccessMode=Ef")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()

    provider, password_variable = PROVIDERS[args.kind]
    cli = cli_dir(args.cli_dir)
    exe = cli_exe(cli)
    work = Path(tempfile.gettempdir()) / f"crun_{args.kind}{'_ef' if args.ef else ''}"
    shutil.rmtree(work, ignore_errors=True)
    name = "Crun" + args.kind.title() + ("Ef" if args.ef else "")

    settings = {"ProjectName": name, "Stacks": "Api", "ApiTests": "true", "ApiTestContainers": "true", "EnumTables": "none",
                "DatabaseProvider": provider, "DatabaseName": args.db}
    if args.ef:
        settings["AccessMode"] = "Ef"
    connect = ["--provider", provider, "-S", args.host, "-d", args.db]
    if args.trusted:
        connect.append("-E")
    else:
        settings["DatabaseUser"] = args.user or ""
        connect += ["-U", args.user or ""]
        if password_variable and os.environ.get(password_variable):
            connect += ["-P", os.environ[password_variable]]

    projects = home(cli) / "Projects"
    projects.mkdir(parents=True, exist_ok=True)
    config = projects / f"{name}.config"
    config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")
    try:
        result = run([exe, "generate", *connect, "--project", name, "-o", work, "--essentials", "--force"])
    finally:
        config.unlink(missing_ok=True)
    print((result.stdout.strip().splitlines() or [""])[-1])
    errors = [line for line in result.stdout.splitlines() if line.startswith("Error")]
    print(len(errors), "template errors", errors[:3])
    create = list(work.rglob("CreateSchema.sql"))
    print("create script:", [str(p.relative_to(work)) for p in create], [p.stat().st_size for p in create])

    tests = next(work.glob("*.Api.Tests"))
    lines = subprocess.run(["dotnet", "test"], cwd=tests, capture_output=True, text=True).stdout.splitlines()
    print("\n".join(line[:400] for line in lines if line.startswith(("Passed!", "Failed!", "  Failed"))))
    if not any(line.startswith(("Passed!", "Failed!")) for line in lines):
        print("\n".join(lines[-15:]))
    problems = [line for line in lines if "container could not be made" in line]
    print(problems[0][:700] if problems else "no container problem")
    print("output:", work)


if __name__ == "__main__":
    sys.exit(main())
