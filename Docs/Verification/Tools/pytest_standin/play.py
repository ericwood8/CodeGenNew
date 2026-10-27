"""python play.py <api folder> : plays tests/test_queue.py with the pytest stand-in."""
import importlib
import os
import sys

root = sys.argv[1]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, root)
sys.path.insert(0, os.path.join(root, "tests"))
os.chdir(root)
import pytest  # noqa: E402

importlib.import_module("conftest")
module = importlib.import_module("test_queue")
module.__dict__.update({k: v for k, v in vars(sys.modules["conftest"]).items() if k == "run"})
results = pytest.run_module(module)
for label, outcome, detail in results:
    print(f"{outcome:8} {label} {detail}")
sys.exit(0 if all(r[1] != "FAILED" for r in results) else 1)
