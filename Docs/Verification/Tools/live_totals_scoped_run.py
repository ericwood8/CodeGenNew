"""Plays DocumentTotals on owner- or tenant-limited invoices over a live SQL Server, PostgreSQL or MySQL database for the ASP.NET, Python and Rust APIs: the checks of document_totals_owner_run.py (--scope owner) or
document_totals_tenant_run.py (--scope tenant), with the user, owner and tenant tables of Schemas/document_totals_owned_<db>.sql or document_totals_tenant_<db>.sql.

  python live_totals_scoped_run.py --scope owner --kind pg --host localhost --db zz_totals --user <login> [--stack api,python,rust]     (password in PGPASSWORD)
  python live_totals_scoped_run.py --scope tenant --kind mysql --host 127.0.0.1 --db zz_totals --user <login>                      (password in MYSQL_PWD)
  python live_totals_scoped_run.py --scope owner --kind mssql --host <server> --db InvoiceSystem_test                               (Windows authentication, schema totals, Rust refused)

The tables are dropped and made again on every run (SQL Server: in the schema totals, so give a test database), the people are added with made-up passwords that only the run knows (a hash written by SQL, the
passwords never written to a file) and --cleanup drops the tables again (SQL Server: the schema too). A stale server on the port makes every sign-in fail: stop it first. Prints one line with the result."""
import argparse
import os
import secrets
import subprocess
import sys
import tempfile
from pathlib import Path

from common import cli_dir, cli_exe, home, run
from document_totals_owner_run import SETTINGS as OWNER_SETTINGS, exercise as owner_exercise, failures as owner_failures, hash_password
from document_totals_tenant_run import SETTINGS as TENANT_SETTINGS, exercise as tenant_exercise, failures as tenant_failures
from live_allowance_scoped_run import sql
from live_inbound_run import KINDS, PASSWORDS, STACKS, apply_sql
from live_party_run import TOOLS, serve
import socket
import time

SCHEMAS = {"mssql": "sqlserver", "pg": "postgres", "mysql": "mysql"}
OWNER_NAMES = ("boss", "ada", "grace")


def run_sql(args, password, text):
    """One piece of SQL against the database of the run."""
    if args.kind == "mssql":
        return sql(args.host, args.db, text)
    path = Path(tempfile.gettempdir()) / f"scoped_run_{os.getpid()}.sql"
    path.write_text(text, encoding="utf-8")
    try:
        code, output = apply_sql(args, password, path)
    finally:
        path.unlink(missing_ok=True)
    if code != 0:
        raise RuntimeError(output.replace(password, "***") if password else output)
    return output


def tables(kind):
    """Quoted names per database: (employee, tenant, employee_tenant, invoice, columns)."""
    if kind == "pg":
        return '"Employee"', '"Tenant"', '"EmployeeTenant"', '"Invoice"', dict(login='"Login"', hash='"PasswordHash"', manager='"IsManager"', admin='"IsAdministrator"', full='"FullName"', payable='"Payable"', key='"InvoiceId"',
                                                                                  tenant_name='"Name"', slug='"Slug"', active='"IsActive"', created='"CreatedOn"', employee='"EmployeeId"', tenant='"TenantId"', role='"Role"', true="true", false="false")
    if kind == "mysql":
        return "employee", "tenant", "employee_tenant", "invoice", dict(login="login", hash="password_hash", manager="is_manager", admin="is_administrator", full="full_name", payable="payable", key="invoice_id",
                                                                         tenant_name="name", slug="slug", active="IsActive", created="created_on", employee="employee_id", tenant="tenant_id", role="role", true="1", false="0")
    return "totals.Employee", "totals.Tenant", "totals.EmployeeTenant", "totals.Invoice", dict(login="Login", hash="PasswordHash", manager="IsManager", admin="IsAdministrator", full="FullName", payable="Payable", key="InvoiceId",
                                                                                                tenant_name="Name", slug="Slug", active="IsActive", created="CreatedOn", employee="EmployeeId", tenant="TenantId", role="Role", true="1", false="0")


def seed(args, password, passwords):
    employee, tenant, link, _, c = tables(args.kind)
    if args.scope == "owner":
        rows = ", ".join(f"('{n}', '{hash_password(passwords[n])}', {c['false']}, {c['true'] if n == 'boss' else c['false']})" for n in OWNER_NAMES)
        run_sql(args, password, f"INSERT INTO {employee} ({c['login']}, {c['hash']}, {c['manager']}, {c['admin']}) VALUES {rows};\n")
    else:
        text = (f"INSERT INTO {tenant} ({c['tenant_name']}, {c['slug']}, {c['active']}, {c['created']}) VALUES ('Northwind Traders', 'northwind', {c['true']}, '2026-01-05 09:00:00'), ('Contoso Services', 'contoso', {c['true']}, '2026-02-10 09:00:00');\n"
                f"INSERT INTO {employee} ({c['full']}, {c['login']}, {c['hash']}, {c['manager']}, {c['admin']}) VALUES ('Ada', 'ada', '{hash_password(passwords['ada'])}', {c['true']}, {c['false']});\n"
                f"INSERT INTO {link} ({c['employee']}, {c['tenant']}, {c['role']}) VALUES (1, 1, NULL), (1, 2, NULL);\n")
        run_sql(args, password, text)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--scope", choices=["owner", "tenant"], required=True)
    parser.add_argument("--kind", choices=list(KINDS), default="mssql")
    parser.add_argument("--host", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--user")
    parser.add_argument("--stack", default="api,python,rust")
    parser.add_argument("--python", default=sys.executable, help="the Python that has the generated requirements.txt installed")
    parser.add_argument("--port", type=int, default=5199)
    parser.add_argument("--no-generate", action="store_true")
    parser.add_argument("--cleanup", action="store_true", help="drop the tables afterwards")
    parser.add_argument("--cli-dir")
    args = parser.parse_args()
    if args.kind != "mssql" and not args.user:
        parser.error("--user is needed over PostgreSQL and MySQL")
    if args.kind == "mssql":
        args.user = None
    password = os.environ.get(PASSWORDS.get(args.kind, ""), "")
    cli = cli_dir(args.cli_dir)
    work = Path(tempfile.gettempdir()) / f"live_totals_scoped_{args.scope}_{args.kind}"
    name = f"LiveTotals{args.scope.title()}{args.kind.title()}"
    stacks = [s.strip() for s in args.stack.split(",") if s.strip()]
    if args.kind == "mssql" and "rust" in stacks:
        print("rust: the generator refuses Rust over SQL Server (sqlx cannot read it)")
        stacks.remove("rust")
    schema_file = TOOLS / "Schemas" / f"document_totals_{'owned' if args.scope == 'owner' else 'tenant'}_{SCHEMAS[args.kind]}.sql"
    def reset():
        """The tables made again and the people added, so every stack starts from the same rows."""
        if args.kind == "mssql":
            sql(args.host, args.db, file=schema_file)
            return True
        code, output = apply_sql(args, password, schema_file)
        if code != 0:
            print(output[-1500:].replace(password, "***") if password else output[-1500:])
        return code == 0

    if not reset():
        return 1

    base =OWNER_SETTINGS if args.scope == "owner" else TENANT_SETTINGS
    settings = {"ProjectName": name, "Stacks": ",".join(STACKS[s] for s in stacks), "EnumTables": "none", "AccessMode": "Ef", "DatabaseProvider": KINDS[args.kind], "DatabaseName": args.db}
    for item in base:
        key, _, value = item.partition("=")
        settings[key] = value
    if args.kind != "mssql":
        settings["DatabaseUser"] = args.user
    if args.kind == "mysql":
        settings["NamingStyle"] = "Pascal"
    if not args.no_generate:
        import shutil
        shutil.rmtree(work, ignore_errors=True)
        projects = home(cli) / "Projects"
        projects.mkdir(parents=True, exist_ok=True)
        config = projects / f"{name}.config"
        config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")
        connect = ["--provider", KINDS[args.kind], "-S", args.host, "-d", args.db]
        connect += ["-E", "--schema", "totals"] if args.kind == "mssql" else ["-U", args.user] + (["-P", password] if password else [])
        try:
            result = run([cli_exe(cli), "generate", *connect, "--project", name, "-o", work, "--essentials", "--force"])
        finally:
            config.unlink(missing_ok=True)
        print((result.stdout.strip().splitlines() or [""])[-1])
        if result.returncode != 0:
            print((result.stdout + result.stderr)[-1500:])
            return 1

    passwords = {n: secrets.token_urlsafe(12) for n in OWNER_NAMES}
    _, _, _, invoice_table, c = tables(args.kind)
    bump = lambda key: run_sql(args, password, f"UPDATE {invoice_table} SET {c['payable']} = {c['payable']} + 5 WHERE {c['key']} = {key};")
    failures = owner_failures if args.scope == "owner" else tenant_failures
    for stack in stacks:
        if not reset():
            return 1
        seed(args, password, passwords)
        command, folder, env = serve(args, stack, work, password)
        env["Jwt__Key"] = secrets.token_urlsafe(48)
        log = work / f"serve_{stack}.log"
        with open(log, "w") as out:
            server = subprocess.Popen(command, cwd=folder, env=env, stdout=out, stderr=subprocess.STDOUT)
            try:
                deadline = time.time() + 900
                while time.time() < deadline and server.poll() is None:
                    try:
                        socket.create_connection(("localhost", args.port), timeout=1).close()
                        break
                    except OSError:
                        time.sleep(2)
                if server.poll() is not None:
                    print(stack, "the server stopped:", "\n".join(log.read_text(errors="replace").splitlines()[-15:]))
                    failures.append((stack, "server"))
                    continue
                time.sleep(2)
                base_url = f"http://localhost:{args.port}"
                if args.scope == "owner":
                    owner_exercise(stack, base_url, passwords, bump)
                else:
                    tenant_exercise(stack, base_url, passwords["ada"], bump)
            finally:
                subprocess.run(["taskkill", "/PID", str(server.pid), "/T", "/F"], capture_output=True)
    if args.cleanup:
        drops = {"pg": 'DROP TABLE "InvoiceAllowanceCharge", "InvoiceLine", "TaxCategory", "Invoice", "EmployeeTenant", "Employee", "Tenant";',
                 "mysql": "SET FOREIGN_KEY_CHECKS = 0; DROP TABLE IF EXISTS invoice_allowance_charge, invoice_line, tax_category, invoice, employee_tenant, employee, tenant; SET FOREIGN_KEY_CHECKS = 1;"}
        if args.kind == "mssql":
            for table in ("InvoiceAllowanceCharge", "InvoiceLine", "TaxCategory", "Invoice", "EmployeeTenant", "Employee", "Tenant"):
                sql(args.host, args.db, f"DROP TABLE IF EXISTS totals.{table}")
            sql(args.host, args.db, "DROP SCHEMA IF EXISTS totals")
        else:
            run_sql(args, password, drops[args.kind] + "\n")
    print("FAILED" if failures else "ALL OK")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
