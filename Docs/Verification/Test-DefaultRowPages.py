"""Drives the Make default button of a generated React, Angular or Blazor WebAssembly app in a real browser (Playwright, the installed Chrome) over a throwaway SQLite project with DefaultRowTables.

  python Test-DefaultRowPages.py --stack react
  python Test-DefaultRowPages.py --stack angular
  python Test-DefaultRowPages.py --stack blazor

It generates Tools/Schemas/default_rows.sql (API and the chosen front end), builds and starts the API (port 5080) and the dev server, adds a customer with three addresses through the API, then opens the address
list: the default row of a group must show no Make default button and the others must show one; a click must move the mark in the database and the grid must show it. Prints PASS / FAIL lines and exits 1 on a
failure; what it started is stopped at the end. --build-cli builds the command-line tool first. --out <folder> uses a generated project. --browser chrome|msedge picks the installed browser. Needs `pip install playwright`.
"""
import argparse
import importlib.util
import os
import re
import shutil
import subprocess
import sys
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("front_end_pages", HERE / "Test-FrontEndPages.py")
pages = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pages)

DEFAULTS = "DefaultRowTables=CustomerAddress:IsDefault:CustomerId+AddressType"
SETTINGS = [DEFAULTS, "Screens=Customer,CustomerAddress"]


def drive(page):
    tag = uuid.uuid4().hex[:6]
    owner = pages.api_send("POST", "/api/customers", {"customerId": 0, "name": "Home " + tag, "email": None, "city": None, "phone": None, "notes": None})["customerId"]

    def address(kind, line, default):
        return pages.api_send("POST", "/api/customeraddresses", {"customerAddressId": 0, "customerId": owner, "addressType": kind, "line1": line, "line2": None, "city": "Town " + line,
                                                                 "region": None, "postalCode": None, "country": "US", "isDefault": default})

    def marked():
        return {r["line1"]: r["isDefault"] for r in pages.api_rows("/api/customeraddresses") if r["customerId"] == owner}

    first = address("billing", "A-" + tag, False)
    second = address("billing", "B-" + tag, False)
    address("shipping", "C-" + tag, False)
    pages.check("the first address of a group is the default", marked() == {"A-" + tag: True, "B-" + tag: False, "C-" + tag: True}, str(marked()))

    page.goto(f"{pages.DEV}/customer-address")
    page.get_by_role("heading", name=re.compile("Customer Address")).wait_for(timeout=90000)
    pages.check("the address screen opens", True)
    grid = page.locator("table").first
    row_a = grid.locator("tbody tr").nth(0)
    row_b = grid.locator("tbody tr").nth(1)
    row_b.get_by_role("button", name="Make default").wait_for(timeout=15000)
    pages.check("the default row has no Make default button", row_a.get_by_role("button", name="Make default").count() == 0)
    pages.check("a row that is not the default has one", row_b.get_by_role("button", name="Make default").count() == 1)
    row_b.get_by_role("button", name="Make default").click()
    page.wait_for_timeout(1500)
    pages.check("the click moves the mark in the database", marked() == {"A-" + tag: False, "B-" + tag: True, "C-" + tag: True}, str(marked()))
    row_a = grid.locator("tbody tr").nth(0)
    row_b = grid.locator("tbody tr").nth(1)
    pages.check("the grid shows the new default without the button", row_b.get_by_role("button", name="Make default").count() == 0 and row_a.get_by_role("button", name="Make default").count() == 1)
    row_a.get_by_role("button", name="Edit").click()
    dialog = page.locator("dialog[open], [role=\"dialog\"]").first
    dialog.wait_for(timeout=10000)
    dialog.get_by_role("button", name="Make default").wait_for(timeout=10000)
    pages.check("the edit form shows the flag as text, not a check box", dialog.locator("input[type=checkbox]").count() == 0)
    pages.check("the edit form of a row that is not the default has a Make default button", dialog.get_by_role("button", name="Make default").count() == 1)
    dialog.get_by_role("button", name="Make default").click()
    page.wait_for_timeout(1500)
    pages.check("the button in the edit form moves the mark in the database", marked() == {"A-" + tag: True, "B-" + tag: False, "C-" + tag: True}, str(marked()))
    pages.check("the form then shows no Make default button", dialog.get_by_role("button", name="Make default").count() == 0)
    for key in (first["customerAddressId"], second["customerAddressId"]):
        pages.api_send("DELETE", f"/api/customeraddresses/{key}")
    for r in pages.api_rows("/api/customeraddresses"):
        if r["customerId"] == owner:
            pages.api_send("DELETE", f"/api/customeraddresses/{r['customerAddressId']}")
    pages.api_send("DELETE", f"/api/customers/{owner}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stack", choices=["react", "angular", "blazor"], default="react")
    parser.add_argument("--out")
    parser.add_argument("--build-cli", action="store_true")
    parser.add_argument("--browser", default="chrome")
    args = parser.parse_args()
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        sys.exit("pip install playwright")
    pages.DEV = f"http://localhost:{pages.STACKS[args.stack]}"
    for port in (5080, pages.STACKS[args.stack]):
        if not pages.port_free(port):
            sys.exit(f"port {port} is in use: stop what is listening there")

    if args.out:
        out = Path(args.out)
    else:
        extra = ["--set", "AngularVersion=22"] if args.stack == "angular" else []
        result = subprocess.run([sys.executable, str(HERE / "Tools" / "new_scratch_project.py"), "PwDflt" + args.stack.title(), "--schema", "default_rows", "--stack", "api," + args.stack,
                                 *[x for s in SETTINGS for x in ("--set", s)], *extra, *(["--build-cli"] if args.build_cli else [])], capture_output=True, text=True, cwd=HERE / "Tools")
        if result.returncode != 0:
            sys.exit(result.stderr or result.stdout)
        out = Path(re.search(r"^output:\s+(.+)$", result.stdout, re.M).group(1).strip())
    print(f"project: {out}")
    api_dir = next(out.glob("*.Api"))
    front = next(out.glob("*.Blazor")) if args.stack == "blazor" else out / "frontend"
    npm = shutil.which("npm") or "npm.cmd"
    build = subprocess.run(["dotnet", "build", str(api_dir), "-v", "q"], capture_output=True, text=True)
    if build.returncode != 0:
        sys.exit("API build failed:\n" + "\n".join(l for l in build.stdout.splitlines() if " error " in l))
    if args.stack == "blazor":
        build = subprocess.run(["dotnet", "build", str(front), "-v", "q"], capture_output=True, text=True)
        if build.returncode != 0:
            sys.exit("front end build failed:\n" + "\n".join(l for l in build.stdout.splitlines() if " error " in l))
    elif not (front / "node_modules").exists():
        install = subprocess.run([npm, "install", "--no-audit", "--no-fund"], cwd=front, capture_output=True, text=True)
        if install.returncode != 0:
            sys.exit("npm install failed:\n" + (install.stderr + install.stdout)[-800:])

    api = dev = None
    try:
        api = subprocess.Popen(["dotnet", "run", "--no-build"], cwd=api_dir, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=os.name != "nt")
        pages.wait_for(f"{pages.API}/api/customers", 60, "the API")
        command = {"react": [npm, "run", "dev", "--", "--strictPort"], "angular": [npm, "start", "--", "--port", "4200"], "blazor": ["dotnet", "run", "--no-build", "--launch-profile", "http"]}[args.stack]
        dev = subprocess.Popen(command, cwd=front, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=dict(os.environ, NG_CLI_ANALYTICS="false"), start_new_session=os.name != "nt")
        pages.wait_for(pages.DEV, 240, "the dev server")
        with sync_playwright() as p:
            browser = p.chromium.launch(channel=args.browser, headless=True)
            try:
                page = browser.new_page()
                errors = []
                page.on("pageerror", lambda e: errors.append(str(e)))
                page.set_default_timeout(5000)
                try:
                    drive(page)
                except Exception as e:
                    pages.check("the default row walk reached its end", False, str(e).splitlines()[0][:200])
                    page.screenshot(path=str(out / "default_failure.png"))
                pages.check("the page raised no script error", not errors, "; ".join(errors)[:200])
            finally:
                browser.close()
    finally:
        pages.stop(dev)
        pages.stop(api)
    print(f"{'FAILED' if pages.failures else 'OK'}: {pages.failures} failing check(s)")
    sys.exit(1 if pages.failures else 0)


if __name__ == "__main__":
    main()
