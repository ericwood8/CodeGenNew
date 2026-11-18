"""Runs the unit tests on a CRLF copy of the working tree, the way the Windows CI runner checks the repository out.

python crlf_test_run.py [--filter <dotnet test filter>] [--keep] [--dir <copy folder>]

Copies every tracked and untracked (not ignored) file to a scratch folder, turns the line endings of the text files into CRLF and runs
`dotnet test CodeGenNew.Tests -c Release` there. A test that looks for "\\n" in generated text passes in a checkout with LF endings and fails on the
runner; this shows it before the push. Prints the failed tests with the first line of their message and one line with the result. The copy is deleted
unless --keep is given."""
import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from common import REPO


def is_text(data: bytes) -> bool:
    return b"\0" not in data[:8000]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--filter")
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--dir", default=str(Path(tempfile.gettempdir()) / "crlf_copy"))
    args = parser.parse_args()
    target = Path(args.dir)
    shutil.rmtree(target, ignore_errors=True)
    listed = subprocess.run(["git", "ls-files", "-co", "--exclude-standard", "-z"], cwd=REPO, capture_output=True, check=True).stdout
    names = [n for n in listed.decode("utf-8").split("\0") if n]
    converted = 0
    for name in names:
        source = REPO / name
        if not source.is_file():
            continue
        destination = target / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        data = source.read_bytes()
        if is_text(data):
            fixed = data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
            converted += fixed != data
            data = fixed
        destination.write_bytes(data)
    print(f"copied {len(names)} files to {target}, {converted} changed to CRLF")
    command = ["dotnet", "test", "CodeGenNew.Tests", "-c", "Release"] + (["--filter", args.filter] if args.filter else [])
    result = subprocess.run(command, cwd=target, capture_output=True, text=True)
    lines = (result.stdout + result.stderr).splitlines()
    for index, line in enumerate(lines):
        if line.strip().startswith("Failed ") and not line.strip().startswith("Failed!"):
            message = next((l.strip() for l in lines[index + 1:index + 6] if l.strip() and not l.strip().startswith("Error Message")), "")
            print(line.strip()[:200], "|", message[:200])
    print(next((l for l in lines if l.startswith(("Passed!", "Failed!"))), "no test summary: " + "\n".join(lines[-8:])))
    if not args.keep:
        shutil.rmtree(target, ignore_errors=True)
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
