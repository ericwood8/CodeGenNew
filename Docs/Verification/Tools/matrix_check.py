"""Generates a scratch project once and builds / tests each generated stack, one line per cell: PASS, FAIL (with the first error lines) or SKIP (with the reason).

  python matrix_check.py Shop --schema master_detail --stack api,python,rust,blazor --set ApiTests=true --set PythonTests=true --set RustTests=true

The cells: api (dotnet test of <name>.Api.Tests when it exists, else dotnet build of <name>.Api), blazor (dotnet build), python (compile of every module, then pytest when it is installed),
rust (cargo test), winui3 and uno (dotnet build of the app, Windows only), react / angular (npm test, only with --frontend, since it installs packages). A cell whose tool is not on the PATH is a SKIP, not a FAIL.
Exit code 1 when any cell fails.

--settings-file reads KEY=VALUE lines (blank lines and lines that start with # are ignored; @schema=name and @stack=a,b choose the schema and the stacks), so the settings of a feature are kept in a file and the same matrix is run again.
--container KIND runs the API tests of the generated project over a container (container_run.py: mssql, pg or mysql; needs Docker, --host, --db and --user, the password in PGPASSWORD or MYSQL_PWD),
one extra cell per kind, which is how a database other than SQLite becomes a cell."""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
TAIL_LINES = 6


def generate(args):
    command = [sys.executable, str(HERE / "new_scratch_project.py"), args.name, "--schema", args.schema, "--stack", args.stack]
    for setting in args.set:
        command += ["--set", setting]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit("generation failed:\n" + result.stdout + result.stderr)
    for line in result.stdout.splitlines():
        if line.startswith("output:"):
            return Path(line.split(":", 1)[1].strip())
    sys.exit("no output folder in:\n" + result.stdout)


def run(command, folder, timeout=900):
    try:
        result = subprocess.run(command, cwd=folder, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return False, ["timed out"]
    text = (result.stdout + "\n" + result.stderr).strip().splitlines()
    errors = [line for line in text if " error " in line or "FAILED" in line or "Error" in line][:TAIL_LINES] or text[-TAIL_LINES:]
    return result.returncode == 0, errors


def tool(name):
    return shutil.which(name) is not None


def cell_api(out, name):
    tests = out / f"{name}.Api.Tests"
    if not tool("dotnet"):
        return "SKIP", ["dotnet is not on the PATH"]
    if tests.exists():
        return run(["dotnet", "test", str(tests), "-v", "q"], out)
    return run(["dotnet", "build", str(out / f"{name}.Api"), "-v", "q"], out)


def cell_blazor(out, name):
    folder = out / f"{name}.Blazor"
    if not folder.exists():
        return "SKIP", ["not generated"]
    return run(["dotnet", "build", str(folder), "-v", "q"], out) if tool("dotnet") else ("SKIP", ["dotnet is not on the PATH"])


def cell_python(out, name):
    folder = out / f"{name}.Python"
    if not folder.exists():
        return "SKIP", ["not generated"]
    ok, errors = run([sys.executable, "-m", "compileall", "-q", "app"], folder)
    if not ok:
        return ok, errors
    if (folder / "tests").exists():
        probe = subprocess.run([sys.executable, "-c", "import pytest"], capture_output=True)
        if probe.returncode == 0:
            return run([sys.executable, "-m", "pytest", "-q"], folder)
        return "SKIP", ["compiled; pytest is not installed"]
    return ok, errors


def cell_rust(out, name):
    folder = out / f"{name}.Rust"
    if not folder.exists():
        return "SKIP", ["not generated"]
    if not tool("cargo"):
        return "SKIP", ["cargo is not on the PATH"]
    wrapper = next((c for c in (HERE.parents[3].parent / "tools" / "cargo_vs.py", Path("C:/Github/tools/cargo_vs.py")) if sys.platform == "win32" and c.exists()), None)
    if wrapper is not None:
        return run([sys.executable, str(wrapper), str(folder), "test", "--quiet"], folder, timeout=1800)
    return run(["cargo", "test", "--quiet"], folder, timeout=1800)


def cell_app(suffix, platform):
    def cell(out, name):
        folders = sorted(out.glob(f"*.{suffix}"))
        if not folders:
            return "SKIP", ["not generated"]
        if sys.platform != "win32":
            return "SKIP", ["the app is built on Windows"]
        command = ["dotnet", "build", str(folders[0]), "-v", "q"] + ([f"-p:Platform={platform}"] if platform else [])
        return run(command, out)
    return cell


def cell_container(args, kind):
    def cell(out, name):
        if not (args.host and args.db):
            return "SKIP", ["--container needs --host and --db"]
        if not tool("docker"):
            return "SKIP", ["docker is not on the PATH"]
        command = [sys.executable, str(HERE / "container_run.py"), kind, "--host", args.host, "--db", args.db]
        if args.user:
            command += ["--user", args.user]
        ok, errors = run(command, HERE, timeout=1800)
        return ok, errors
    return cell


def cell_frontend(args):
    def cell(out, name):
        folder = out / "frontend"
        if not folder.exists():
            return "SKIP", ["not generated"]
        if not args.frontend:
            return "SKIP", ["pass --frontend to install packages and run npm test"]
        npm = shutil.which("npm")
        if npm is None:
            return "SKIP", ["npm is not on the PATH"]
        ok, errors = run([npm, "install", "--no-audit", "--no-fund"], folder)
        return (ok, errors) if not ok else run([npm, "test", "--", "--run"], folder)
    return cell


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("name")
    parser.add_argument("--schema", default="plain")
    parser.add_argument("--stack", default="api")
    parser.add_argument("--set", action="append", default=[], metavar="KEY=VALUE")
    parser.add_argument("--frontend", action="store_true")
    parser.add_argument("--settings-file", metavar="FILE")
    parser.add_argument("--container", action="append", default=[], choices=["mssql", "pg", "mysql"])
    parser.add_argument("--host")
    parser.add_argument("--db")
    parser.add_argument("--user")
    args = parser.parse_args()
    if args.settings_file:
        for line in Path(args.settings_file).read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line.startswith("@schema="):
                args.schema = line.split("=", 1)[1]
            elif line.startswith("@stack="):
                args.stack = line.split("=", 1)[1]
            elif line and not line.startswith("#"):
                args.set.append(line)

    out = generate(args)
    stacks = {s.strip() for s in args.stack.split(",")}
    cells = []
    if "api" in stacks:
        cells.append(("api", cell_api))
    if "blazor" in stacks:
        cells.append(("blazor", cell_blazor))
    if "python" in stacks:
        cells.append(("python", cell_python))
    if "rust" in stacks:
        cells.append(("rust", cell_rust))
    if "winui3" in stacks:
        cells.append(("winui3", cell_app("WinUI3", "x64")))
    if "uno" in stacks:
        cells.append(("uno", cell_app("Uno", None)))
    for kind in args.container:
        cells.append((f"api@{kind}", cell_container(args, kind)))
    for web in ("react", "angular"):
        if web in stacks:
            cells.append((web, cell_frontend(args)))

    failed = False
    print(f"output: {out}")
    for label, cell in cells:
        ok, errors = cell(out, args.name)
        status = ok if isinstance(ok, str) else ("PASS" if ok else "FAIL")
        print(f"{label:8} {status}" + ("" if status == "PASS" else "  " + " | ".join(errors)))
        failed |= status == "FAIL"
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
