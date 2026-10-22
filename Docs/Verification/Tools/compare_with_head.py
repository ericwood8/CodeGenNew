"""Shows what the uncommitted template changes do to the output of a project: generates with the working tree's templates (A),
puts the committed (HEAD) copy of every changed template back (B), generates again and lists the files that differ.

  python compare_with_head.py --project Shop --db shop.db --provider Sqlite --stack api,react [--diff] [-- <more generate arguments>]

The project file must already exist in the CLI's Projects folder (new_scratch_project.py writes one). Only template and
.tt.config files are put back: a change in Core code is in both runs, so compare that with a git worktree of HEAD instead.
Line endings are ignored. The seeded Templates folder is deleted at the end so the next run seeds the current templates again.
Exit code 0 when nothing differs, 1 when something does.
"""
import argparse
import difflib
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from common import REPO, build_cli, cli_dir, cli_exe, home, run

MAX_DIFF_LINES = 80
MANIFEST = ".codegen-manifest.json"


def git(*args, binary=False):
    return subprocess.run(["git", "-C", str(REPO), *args], capture_output=True, text=not binary)


def changed_templates():
    names = set(git("diff", "--name-only", "HEAD", "--", "Templates").stdout.split())
    names |= set(git("ls-files", "--others", "--exclude-standard", "--", "Templates").stdout.split())
    return sorted(Path(n).name for n in names)


def put_back_head_copies(templates, names):
    for name in names:
        target = templates / name
        shown = git("show", f"HEAD:Templates/{name}", binary=True)
        if shown.returncode == 0:
            target.write_bytes(shown.stdout)
        else:
            target.unlink(missing_ok=True)


def lines_of(path):
    return path.read_bytes().decode("utf-8", errors="replace").replace("\r\n", "\n").splitlines()


def compare(a, b, show_diff):
    files_a = {p.relative_to(a).as_posix() for p in a.rglob("*") if p.is_file() and p.name != MANIFEST}
    files_b = {p.relative_to(b).as_posix() for p in b.rglob("*") if p.is_file() and p.name != MANIFEST}
    differences = 0
    for name in sorted(files_a - files_b):
        print(f"only with the new templates: {name}")
        differences += 1
    for name in sorted(files_b - files_a):
        print(f"only with HEAD:              {name}")
        differences += 1
    for name in sorted(files_a & files_b):
        old, new = lines_of(b / name), lines_of(a / name)
        if old == new:
            continue
        differences += 1
        print(f"changed: {name}")
        if show_diff:
            diff = list(difflib.unified_diff(old, new, "HEAD", "new", lineterm="", n=1))
            for line in diff[:MAX_DIFF_LINES]:
                print("    " + line)
            if len(diff) > MAX_DIFF_LINES:
                print(f"    ... {len(diff) - MAX_DIFF_LINES} more lines")
    return differences


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--provider", default="Sqlite")
    parser.add_argument("--stack", required=True)
    parser.add_argument("--diff", action="store_true", help="print the changed lines of each differing file")
    parser.add_argument("--no-essentials", action="store_true")
    parser.add_argument("--cli-dir")
    parser.add_argument("--build-cli", action="store_true", help="build the CLI first so its embedded templates are the working tree's")
    parser.add_argument("--out", help="folder for the two outputs (default: a new temp folder, deleted at the end)")
    args, extra = parser.parse_known_args()

    if args.build_cli:
        build_cli()
    cli = cli_dir(args.cli_dir)
    exe = cli_exe(cli)
    if not exe.exists():
        sys.exit(f"{exe} does not exist: build the CLI first or pass --build-cli")
    templates = home(cli) / "Templates"
    names = changed_templates()
    if not names:
        sys.exit("no template or .tt.config file differs from HEAD")
    print("changed templates: " + ", ".join(names))

    work = Path(args.out) if args.out else Path(tempfile.mkdtemp(prefix="codegen_ab_"))
    out_a, out_b = work / "A", work / "B"

    def generate(out):
        shutil.rmtree(out, ignore_errors=True)
        command = [exe, "generate", "--provider", args.provider, "-d", args.db, "--project", args.project,
                   "--stack", args.stack, "-o", out, "--force", *extra]
        if not args.no_essentials:
            command.append("--essentials")
        result = run(command)
        if result.returncode != 0:
            sys.exit("generate failed:\n" + (result.stderr or result.stdout))
        print(result.stdout.strip().splitlines()[-1])

    try:
        shutil.rmtree(templates, ignore_errors=True)
        generate(out_a)
        put_back_head_copies(templates, names)
        generate(out_b)
        differences = compare(out_a, out_b, args.diff)
    finally:
        shutil.rmtree(templates, ignore_errors=True)
        if not args.out:
            shutil.rmtree(work, ignore_errors=True)
    print(f"{differences} file(s) differ" if differences else "no file differs")
    sys.exit(1 if differences else 0)


if __name__ == "__main__":
    main()
