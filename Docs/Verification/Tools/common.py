"""Shared helpers of the verification tools: where the CLI is, where its Projects and Templates folders are, how to run it."""
import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CLI_PROJECT = REPO / "CodeGenNew.Cli"
DEFAULT_CLI_DIR = CLI_PROJECT / "bin" / "Debug" / "net10.0"


def cli_dir(override=None):
    return Path(override) if override else DEFAULT_CLI_DIR


def cli_exe(directory):
    return directory / ("codegen.exe" if os.name == "nt" else "codegen")


def home(directory):
    """The folder that holds Templates and Projects: CODEGENNEW_HOME, else beside the program."""
    return Path(os.environ["CODEGENNEW_HOME"]) if os.environ.get("CODEGENNEW_HOME") else directory


def build_cli(project=CLI_PROJECT):
    result = subprocess.run(["dotnet", "build", str(project), "-v", "q"], capture_output=True, text=True)
    errors = sorted({line for line in result.stdout.splitlines() if " error " in line})
    if result.returncode != 0:
        sys.exit("build of the CLI failed:\n" + "\n".join(errors))


def run(args, **kwargs):
    return subprocess.run([str(a) for a in args], capture_output=True, text=True, **kwargs)
