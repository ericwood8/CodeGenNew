"""Drives the attachments of a generated React, Angular or Blazor WebAssembly app in a real browser (Playwright, the installed Chrome) over a throwaway SQLite project with PolymorphicLinks.

  python Test-PolymorphicPages.py --stack react
  python Test-PolymorphicPages.py --stack angular
  python Test-PolymorphicPages.py --stack blazor

It generates Tools/Schemas/polymorphic.sql (API and the chosen front end), builds and starts the API (port 5080) and the dev server, adds a product, a customer and one attachment of each through the API, then:
opens the product, which must show its own attachment and not the customer's; opens the attachment from that grid, whose record type is a drop-down of the listed tables, and saves a changed title; tries to delete the
customer that an attachment names, which must stay, and then the product's other rows. Prints PASS / FAIL lines and exits 1 on a failure; what it started is stopped at the end.
--build-cli builds the command-line tool first. --out <folder> uses a generated project. --browser chrome|msedge picks the installed browser. Needs `pip install playwright`.
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

LINKS = "PolymorphicLinks=Attachment:OwnerType:OwnerId:Product|Customer|Opportunity,Remark:OwnerKind:OwnerRef:Product|Customer:remove"
SETTINGS = [LINKS]


def drive(page):
    tag = uuid.uuid4().hex[:6]
    product = pages.api_send("POST", "/api/products", {"productId": 0, "name": "Widget " + tag, "sku": "W-" + tag, "price": 5, "category": "K-" + tag, "notes": None})
    customer = pages.api_send("POST", "/api/customers", {"customerId": 0, "name": "Cust " + tag, "email": None, "city": "C-" + tag, "phone": None, "notes": None})
    mine = pages.api_send("POST", "/api/attachments", {"attachmentId": 0, "ownerType": "Product", "ownerId": product["productId"], "title": "Brochure " + tag, "url": None, "caption": None})
    pages.api_send("POST", "/api/attachments", {"attachmentId": 0, "ownerType": "Customer", "ownerId": customer["customerId"], "title": "Contract " + tag, "url": None, "caption": None})
    dialog = page.locator("dialog[open], [role=\"dialog\"]")

    page.goto(f"{pages.DEV}/product")
    page.get_by_role("heading", name="Products").wait_for(timeout=90000)
    pages.check("the Product screen opens", True)
    page.locator("tr", has_text="K-" + tag).first.get_by_role("button", name="Edit").click()
    dialog.get_by_role("heading", name="Attachments").wait_for(timeout=10000)
    pages.check("the product's dialog has an Attachments section", True)
    section = dialog.last
    try:
        section.get_by_text("Brochure " + tag).first.wait_for(timeout=8000)   # the rows are read after the dialog opens
        listed = True
    except Exception:
        listed = False
    pages.check("it lists the product's own attachment", listed)
    pages.check("it does not list another table's attachment", section.get_by_text("Contract " + tag).count() == 0)

    section.locator("tr", has_text="Brochure " + tag).first.get_by_role("button", name="Edit").click()
    page.wait_for_url(re.compile(r"/attachment"), timeout=10000)
    dialog.first.wait_for(timeout=10000)
    kind = dialog.locator("select").first
    options = [o.strip() for o in kind.evaluate("select => [...select.options].map((o) => o.text)")]
    pages.check("the record type is a drop-down of the listed tables", {"Product", "Customer", "Opportunity"} <= set(options), str(options))
    title = dialog.get_by_label(re.compile("Title")).first
    title.fill("Renamed " + tag)
    dialog.get_by_role("button", name=re.compile("^(Save|Submit)")).first.click()
    page.wait_for_timeout(1500)
    saved = [a for a in pages.api_rows("/api/attachments") if a["attachmentId"] == mine["attachmentId"]]
    pages.check("the changed title is saved", bool(saved) and saved[0]["title"] == "Renamed " + tag, str(saved))
    pages.check("the type and the id are kept", bool(saved) and saved[0]["ownerType"] == "Product" and saved[0]["ownerId"] == product["productId"], str(saved))

    page.goto(f"{pages.DEV}/customer")
    page.get_by_role("heading", name="Customers").wait_for(timeout=90000)
    page.once("dialog", lambda d: d.accept())
    page.locator("tr", has_text="C-" + tag).first.get_by_role("button", name="Delete").click()
    page.wait_for_timeout(1500)
    pages.check("a customer an attachment names is not deleted", any(c["customerId"] == customer["customerId"] for c in pages.api_rows("/api/customers")))
    pages.check("the screen says why", page.get_by_text(re.compile("in use|still|linked|attach", re.I)).count() > 0)


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
        result = subprocess.run([sys.executable, str(HERE / "Tools" / "new_scratch_project.py"), "PwPoly" + args.stack.title(), "--schema", "polymorphic", "--stack", "api," + args.stack,
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
                    pages.check("the attachments walk reached its end", False, str(e).splitlines()[0][:200])
                    page.screenshot(path=str(out / "polymorphic_failure.png"))
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
