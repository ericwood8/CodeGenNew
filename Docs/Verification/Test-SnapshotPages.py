"""Drives the order line screen of a generated React, Angular or Blazor WebAssembly app in a real browser (Playwright, the installed Chrome) over a throwaway SQLite project with SnapshotColumns.

  python Test-SnapshotPages.py --stack react
  python Test-SnapshotPages.py --stack angular
  python Test-SnapshotPages.py --stack blazor

It generates Tools/Schemas/snapshot.sql (API and the chosen front end), builds and starts the API (port 5080) and the dev server, adds a product, a tax category and an order through the API, then adds a line in
the browser: the form must have no box for a copied column or the total, the saved line must carry the product's name and price, the tax rate and the total the server worked out, and a new quantity must work
the total out again. Prints PASS / FAIL lines and exits 1 on a failure; what it started is stopped at the end. --build-cli builds the command-line tool first. --out <folder> uses a generated project.
--browser chrome|msedge picks the installed browser. Needs `pip install playwright`.
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

SNAPSHOT = "SnapshotColumns=OrderItem:ProductName<-Product.Name+UnitPrice<-Product.Price+TaxRate<-TaxCategory.Rate:LineTotal=Quantity*UnitPrice"
SETTINGS = [SNAPSHOT, "Screens=Product,TaxCategory,SalesOrder,OrderItem"]


def drive(page):
    tag = uuid.uuid4().hex[:6]
    product = pages.api_send("POST", "/api/products", {"productId": 0, "name": "Widget " + tag, "price": 10.5, "sku": None, "description": None, "unit": None})["productId"]
    tax = pages.api_send("POST", "/api/taxcategories", {"taxCategoryId": 0, "name": "Standard " + tag, "rate": 8.25, "code": None, "notes": None, "region": None})["taxCategoryId"]
    order = pages.api_send("POST", "/api/salesorders", {"salesOrderId": 0, "reference": "SO-" + tag, "customer": "Acme", "note": None, "channel": None, "remarks": None})["salesOrderId"]

    def lines():
        return [r for r in pages.api_rows("/api/orderitems") if r["salesOrderId"] == order]

    page.goto(f"{pages.DEV}/order-item")
    page.get_by_role("heading", name=re.compile("Order Item")).wait_for(timeout=90000)
    pages.check("the order line screen opens", True)
    page.get_by_role("button", name=re.compile("^(Add|New)")).first.click()
    dialog = page.locator("dialog[open], [role=\"dialog\"]").first
    dialog.wait_for(timeout=10000)
    pages.check("the form has no box for a copied column or the total", dialog.get_by_label("Product Name").count() == 0 or dialog.get_by_label("Product Name").evaluate("e => e.tagName") not in ("INPUT", "SELECT"))
    pages.check("the form has no box for the line total", dialog.get_by_label("Line Total").count() == 0 or dialog.get_by_label("Line Total").evaluate("e => e.tagName") not in ("INPUT", "SELECT"))
    for label, text in (("Sales Order", "SO-" + tag), ("Product", "Widget " + tag), ("Tax Category", "Standard " + tag)):
        box = dialog.get_by_label(re.compile("^" + label))
        box.select_option(label=text)
    dialog.get_by_label("Quantity").fill("3")
    dialog.get_by_role("button", name=re.compile("^(Save|Submit)")).click()
    page.wait_for_timeout(2000)
    saved = lines()
    pages.check("the line is saved", len(saved) == 1, str(saved))
    row = saved[0] if saved else {}
    pages.check("the server filled the product name and price", row.get("productName") == "Widget " + tag and row.get("unitPrice") == 10.5, str(row))
    pages.check("the server filled the tax rate and the total", row.get("taxRate") == 8.25 and row.get("lineTotal") == 31.5, str(row))
    pages.check("the grid shows the copied name", page.get_by_text("Widget " + tag).count() >= 1)
    page.get_by_role("row").filter(has_text="Widget " + tag).get_by_role("button", name="Edit").first.click()
    dialog = page.locator("dialog[open], [role=\"dialog\"]").first
    dialog.wait_for(timeout=10000)
    pages.check("the edit form shows the copied name as text", dialog.get_by_text("Widget " + tag).count() >= 1 and dialog.locator("input[value='Widget " + tag + "']").count() == 0)
    dialog.get_by_label("Quantity").fill("4")
    dialog.get_by_role("button", name=re.compile("^(Save|Submit)")).click()
    page.wait_for_timeout(2000)
    changed = lines()
    pages.check("a new quantity works the total out again", len(changed) == 1 and changed[0]["lineTotal"] == 42 and changed[0]["productName"] == "Widget " + tag, str(changed))
    for r in lines():
        pages.api_send("DELETE", f"/api/orderitems/{r['orderItemId']}")
    pages.api_send("DELETE", f"/api/salesorders/{order}")
    pages.api_send("DELETE", f"/api/products/{product}")
    pages.api_send("DELETE", f"/api/taxcategories/{tax}")


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
        result = subprocess.run([sys.executable, str(HERE / "Tools" / "new_scratch_project.py"), "PwSnap" + args.stack.title(), "--schema", "snapshot", "--stack", "api," + args.stack,
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
        pages.wait_for(f"{pages.API}/api/products", 60, "the API")
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
                    pages.check("the snapshot walk reached its end", False, str(e).splitlines()[0][:200])
                    page.screenshot(path=str(out / "snapshot_failure.png"))
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
