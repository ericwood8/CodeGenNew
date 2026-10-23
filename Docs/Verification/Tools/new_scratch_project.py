"""Builds a throwaway SQLite database from a schema file, writes a project file for it and generates into a scratch folder.

  python new_scratch_project.py Shop --schema plain --stack api,react --set Dtos=true --set ApiTests=true

--schema is a name in Tools/Schemas (plain, master_detail, junction, date_pairs) or a path to a .sql file. Settings are the project file's keys
(see Docs/Reference.md); EnumTables=none and AccessMode=Ef are the defaults, since SQLite has no routines. Tables need five or
more columns to get a screen. Prints the database, project file and output folder. The project file is written to the CLI's
Projects folder as <name>.config, so a later `codegen generate --project <name>` finds it.
"""
import argparse
import sqlite3
import sys
import tempfile
from pathlib import Path

from common import build_cli, cli_dir, cli_exe, home, run

SCHEMAS = Path(__file__).resolve().parent / "Schemas"


def schema_text(schema):
    path = Path(schema)
    if not path.exists():
        path = SCHEMAS / (schema + ".sql")
    if not path.exists():
        sys.exit(f"schema {schema!r} is not a file or one of: " + ", ".join(p.stem for p in sorted(SCHEMAS.glob("*.sql"))))
    return path.read_text(encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("name", help="project name; also the file names")
    parser.add_argument("--schema", default="plain")
    parser.add_argument("--stack", default="api", help="comma list, for example api,react,angular")
    parser.add_argument("--set", action="append", default=[], metavar="KEY=VALUE", help="a project setting; repeat")
    parser.add_argument("--dir", help="working folder (default: a new folder under the system temp folder)")
    parser.add_argument("--cli-dir", help="folder of codegen.exe (default: the repository's Debug build)")
    parser.add_argument("--build-cli", action="store_true", help="build the CLI first")
    parser.add_argument("--no-generate", action="store_true", help="only write the database and the project file")
    parser.add_argument("--no-essentials", action="store_true", help="skip the essentials groups")
    args = parser.parse_args()

    if args.build_cli:
        build_cli()
    cli = cli_dir(args.cli_dir)
    exe = cli_exe(cli)
    if not exe.exists():
        sys.exit(f"{exe} does not exist: build the CLI (dotnet build CodeGenNew.Cli) or pass --build-cli")

    work = Path(args.dir) if args.dir else Path(tempfile.mkdtemp(prefix=f"codegen_{args.name}_"))
    work.mkdir(parents=True, exist_ok=True)
    db = work / f"{args.name}.db"
    db.unlink(missing_ok=True)
    connection = sqlite3.connect(db)
    connection.executescript(schema_text(args.schema))
    connection.commit()
    connection.close()

    settings = {"ProjectName": args.name, "Stacks": args.stack, "EnumTables": "none", "AccessMode": "Ef",
                "DatabaseProvider": "Sqlite", "DatabaseName": db.as_posix()}
    for item in args.set:
        key, _, value = item.partition("=")
        settings[key.strip()] = value.strip()

    projects = home(cli) / "Projects"
    projects.mkdir(parents=True, exist_ok=True)
    config = projects / f"{args.name}.config"
    config.write_text("".join(f"{k}={v}\n" for k, v in settings.items()), encoding="utf-8")

    out = work / "out"
    print(f"database: {db}\nproject:  {config}\noutput:   {out}")
    if args.no_generate:
        return
    command = [exe, "generate", "--provider", "Sqlite", "-d", db, "--project", args.name, "-o", out, "--force"]
    if not args.no_essentials:
        command.append("--essentials")
    result = run(command)
    print(result.stdout.strip().splitlines()[-1] if result.stdout.strip() else "")
    if result.returncode != 0:
        sys.exit(result.stderr.strip() or result.stdout.strip() or "generate failed")


if __name__ == "__main__":
    main()
