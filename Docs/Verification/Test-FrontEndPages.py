"""Drives the generated React, Angular, Blazor WebAssembly or Uno browser app of a throwaway SQLite project in a real browser (Playwright, the installed Chrome) and checks every step against the API.

  python Test-FrontEndPages.py --stack react
  python Test-FrontEndPages.py --stack blazor
  python Test-FrontEndPages.py --stack uno
  python Test-FrontEndPages.py --stack angular --prove-failure
  python Test-FrontEndPages.py --stack react --schema junction
  python Test-FrontEndPages.py --stack react --schema owner_scoped

Generates the master_detail sample schema (API and the chosen front end) with Tools/new_scratch_project.py, builds and starts the API (port 5080) and the
dev server (Vite on 5173, ng serve on 4200, dotnet run on 5190 for Blazor and on 5195 for Uno), then walks the Region screen: the empty form is refused by the browser, a row is added, found by the search, edited, cloned and
deleted; React, Angular and Blazor then walk a drop-down fed by another table (a customer order picks its customer), the child grid of a master-detail dialog (the customers of a region) and the paging bar. Uno also walks the child grid and the paging bar (by Next and Previous and the rows on show), and its drop-down only as far as listing and picking: its calendar date picker cannot be driven through the accessibility page, so the order is never saved there and the check is that a picked customer stops being reported as missing. After each step the API is asked what the database holds, so a screen that looks right but saved nothing fails. Prints PASS / FAIL lines, the elapsed time on the way out, and
exits 1 on a failure. Everything it started is stopped at the end; the generated folder is kept and printed.

--prove-failure aborts the browser's POST to the API, so the add and everything after it must fail: it shows that the checks can fail.
--stack react|angular|blazor|uno picks the front end (Angular is generated for version 22). The Uno app draws on a canvas, so it is driven through the semantic page that its accessibility switch builds, by focus and keys.
--schema junction generates the many-to-many sample (Region, Customer, Tag, CustomerTag) and walks the Customer dialog of React, Angular or Blazor: the junction table has no grid there and the page asks the API for nothing that is refused.
--schema owner_scoped generates an API with sign-in (ApiAuth, Auth, the Employee user table, OData screens) over Employee and Expense, an expense belonging to the employee in EmployeeId, sets passwords for three people (ann, bob and root, the administrator) and walks the Expense screen of React, Angular, Blazor or Uno signed in as each: the sign-in page, each person's own rows only, add (not on Uno, whose calendar date picker cannot be driven), edit and delete of one's own row, a row for someone else refused (not on Uno), and the API's refusals of another person's row.
--out <folder> uses an already generated project (the folder that holds the Api project and the frontend folder or Blazor project) instead of generating one.
--build-cli builds the command-line tool before generating (a fresh checkout, as in CI, has not built it).
--browser chrome|msedge picks the installed browser (default chrome). Needs `pip install playwright`; no browser is downloaded.
"""
import argparse
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
API = "http://localhost:5080"
DEV = "http://localhost:5173"
STACKS = {"react": 5173, "angular": 4200, "blazor": 5190, "uno": 5195}
failures = 0
SUBMIT = re.compile("^(Submit|Save)$")


def check(name, ok, detail=""):
    global failures
    print(("PASS " if ok else "FAIL ") + name + ("" if ok or not detail else f" ({detail})"))
    if not ok:
        failures += 1


def get_json(url):
    with urllib.request.urlopen(url, timeout=10) as response:
        return json.loads(response.read().decode())


def regions():
    data = get_json(f"{API}/api/regions")
    return data if isinstance(data, list) else next(v for v in data.values() if isinstance(v, list))


def wait_for(url, seconds, what):
    end = time.time() + seconds
    while time.time() < end:
        try:
            urllib.request.urlopen(url, timeout=3).close()
            return
        except urllib.error.HTTPError:
            return   # the server answered (a 401 from an API that wants a sign-in is an answer)
        except Exception:
            time.sleep(1)
    sys.exit(f"{what} did not answer at {url} within {seconds} s")


def port_free(port):
    import socket
    with socket.socket() as s:
        return s.connect_ex(("127.0.0.1", port)) != 0


def stop(process):
    if process and process.poll() is None:
        if os.name == "nt":
            subprocess.run(["taskkill", "/T", "/F", "/PID", str(process.pid)], capture_output=True)
        else:
            os.killpg(os.getpgid(process.pid), signal.SIGTERM)   # the process was started as the leader of its own group, so its children go too


OWNER_SETTINGS = ["ApiAuth=true", "Auth=true", "OData=true", "ODataScreens=Replace", "UserTable=Employee", "UserNameColumn=Login", "PasswordHashColumn=PasswordHash",
                  "UserOwnerColumn=EmployeeId", "UserAdminColumn=IsAdministrator", "OwnedTables=Expense"]
OWNER_KEY = "harness-signing-key-0123456789-0123456789-abcdef"
OWNER_PEOPLE = {"ann": ("Ann", False), "bob": ("Bob", False), "root": ("Root", True)}   # sign-in name: (name, administrator); they are employees 1, 2 and 3


def owner_password(login):
    return f"Pw-{login}-1234567"


def generate(stack, build_cli=False, schema="master_detail"):
    extra = ["--set", "AngularVersion=22"] if stack == "angular" else []
    # OData and Dtos exclude each other; the owner-scoped sample is served as OData screens
    settings = [x for pair in (("--set", s) for s in OWNER_SETTINGS) for x in pair] if schema == "owner_scoped" else ["--set", "Dtos=true"]
    suffix = {"junction": "Junction", "owner_scoped": "Owner"}.get(schema, "")
    result = subprocess.run([sys.executable, str(HERE / "Tools" / "new_scratch_project.py"), "PwShop" + stack.title() + suffix, "--schema", schema,
                             "--stack", "api," + stack, *settings, *extra, *(["--build-cli"] if build_cli else [])], capture_output=True, text=True, cwd=HERE / "Tools")
    if result.returncode != 0:
        sys.exit(result.stderr or result.stdout)
    return Path(re.search(r"^output:\s+(.+)$", result.stdout, re.M).group(1).strip())


def field(form, label):
    """The input of a form field: React labels it with aria-label, Angular with the id <table><Label>."""
    return form.locator(f'[aria-label="{label}"], #region{label}')


def search_box(page, label):
    return page.locator(f'[aria-label="Search by {label}"], [placeholder="Search by {label}"]')


def drive(page, prove_failure):
    page.on("dialog", lambda d: d.accept())  # window.confirm of the delete
    if prove_failure:
        page.route("**/api/regions", lambda route: route.abort() if route.request.method == "POST" else route.continue_())

    for leftover in [r for r in regions() if r["name"].startswith("Harness")]:
        urllib.request.urlopen(urllib.request.Request(f"{API}/api/regions/{leftover['regionId']}", method="DELETE"), timeout=10).close()
    page.goto(f"{DEV}/region")
    page.get_by_role("heading", name="Regions").wait_for(timeout=90000)  # a Blazor app downloads its runtime first
    check("the Region screen opens", True)

    form = page.locator('dialog[open], [role="dialog"]')
    page.get_by_role("button", name="Add New").click()
    form.wait_for()
    form.get_by_role("button", name=SUBMIT).click()
    page.wait_for_timeout(500)
    check("an empty form is refused and nothing is saved", form.is_visible() and not any(r["name"] == "" for r in regions()))

    before = len(regions())
    field(form, "Name").fill("Harness North")
    field(form, "Code").fill("HN")
    field(form, "Population").fill("1234")
    form.get_by_role("button", name=SUBMIT).click()
    try:
        form.wait_for(state="hidden", timeout=4000)
        closed = True
    except Exception:
        closed = False
    saved = [r for r in regions() if r["name"] == "Harness North"]
    check("Submit closes the form", closed)
    check("the new row is in the database with what was typed", len(regions()) == before + 1 and len(saved) == 1 and saved[0]["code"] == "HN" and saved[0]["population"] == 1234)
    row = page.locator("table").first.locator("tbody tr", has_text="Harness North")
    check("the new row is in the grid", row.count() == 1)

    search_box(page, "Name").fill("Harness")
    page.get_by_role("button", name="Search").click()
    page.wait_for_timeout(700)
    check("the search finds the row and only it", page.locator("table").first.locator("tbody tr").count() == 1 and row.count() == 1)

    row.get_by_role("button", name="Edit").click()
    form.wait_for()
    check("Edit opens the form with the saved name", field(form, "Name").input_value() == "Harness North")
    field(form, "Name").fill("Harness South")
    form.get_by_role("button", name=SUBMIT).click()
    try:
        form.wait_for(state="hidden", timeout=4000)
    except Exception:
        pass
    check("the edit reaches the database", any(r["name"] == "Harness South" for r in regions()) and not any(r["name"] == "Harness North" for r in regions()))

    search_box(page, "Name").fill("Harness South")
    page.get_by_role("button", name="Search").click()
    page.wait_for_timeout(700)
    page.locator("table").first.locator("tbody tr", has_text="Harness South").get_by_role("button", name="Clone").click()
    form.wait_for()
    check("Clone saves a copy and opens it", sum(r["name"].startswith("Harness South") for r in regions()) == 2, str([r["name"] for r in regions()]))
    form.get_by_role("button", name="Cancel").click()
    form.wait_for(state="hidden")

    for _ in range(2):
        rows = page.locator("table").first.locator("tbody tr", has_text="Harness")
        if rows.count() == 0:
            break
        rows.first.get_by_role("button", name="Delete").click()
        page.wait_for_timeout(900)
    check("Delete removes the rows from the database", not any(r["name"].startswith("Harness") for r in regions()))
    check("the grid shows no such row", page.locator("table").first.locator("tbody tr", has_text="Harness").count() == 0)


def api_send(method, path, body=None):
    request = urllib.request.Request(f"{API}{path}", data=None if body is None else json.dumps(body).encode(), method=method, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=10) as response:
        text = response.read().decode()
    return json.loads(text) if text else None


def api_rows(path):
    data = get_json(f"{API}{path}")
    return data if isinstance(data, list) else next(v for v in data.values() if isinstance(v, list))


def drive_more(page, prove_failure):
    """The screens the Region walk does not reach: a drop-down fed by another table (a customer order picks its customer), the child grid of a master-detail
    dialog (the customers of a region) and the paging bar (more rows than a page). The rows they need are added through the API and removed again."""
    if prove_failure:
        page.route("**/api/customerorders", lambda route: route.abort() if route.request.method == "POST" else route.continue_())
    form = page.locator('dialog[open], [role="dialog"]')
    table = lambda: page.locator("table").first

    def clean():
        for order in api_rows("/api/customerorders"):
            if order["reference"].startswith("Harness"):
                api_send("DELETE", f"/api/customerorders/{order['customerOrderId']}")
        for customer in api_rows("/api/customers"):
            if customer["name"].startswith("Harness"):
                api_send("DELETE", f"/api/customers/{customer['customerId']}")
        for region in api_rows("/api/regions"):
            if region["name"].startswith("Harness"):
                api_send("DELETE", f"/api/regions/{region['regionId']}")

    clean()
    try:
        region = api_send("POST", "/api/regions", {"regionId": 0, "name": "Harness Region", "code": "HR", "population": 1})
        for name in ("Harness Alpha", "Harness Beta"):
            api_send("POST", "/api/customers", {"customerId": 0, "regionId": region["regionId"], "name": name, "email": "a@example.com", "creditLimit": 1, "isActive2": True})
        customers = {c["name"]: c["customerId"] for c in api_rows("/api/customers") if c["name"].startswith("Harness")}

        # the lookup drop-down
        page.goto(f"{DEV}/customer-order")
        page.get_by_role("heading", name="Customer Orders").wait_for(timeout=90000)
        page.get_by_role("button", name="Add New").click()
        form.wait_for()
        customer = form.locator('select[aria-label="Customer"], #customerOrderCustomerId').first
        labels = [t.strip() for t in customer.locator("option").all_inner_texts()]
        check("the Customer drop-down lists the customers of the other table", "Harness Alpha" in labels and "Harness Beta" in labels, str(labels[:6]))
        customer.select_option(label="Harness Beta")
        field_ = lambda label, ident: form.locator(f'[aria-label="{label}"], #{ident}').first
        field_("Reference", "customerOrderReference").fill("Harness Order")
        field_("Order Date", "customerOrderOrderDate").fill("2026-04-01")
        field_("Total", "customerOrderTotal").fill("12.5")
        form.get_by_role("button", name=SUBMIT).click()
        try:
            form.wait_for(state="hidden", timeout=4000)
        except Exception:
            pass
        orders = [o for o in api_rows("/api/customerorders") if o["reference"] == "Harness Order"]
        check("the order is saved with the customer that was picked", len(orders) == 1 and orders[0]["customerId"] == customers["Harness Beta"], str(orders))
        table().locator("tbody tr", has_text="Harness Order").get_by_role("button", name="Edit").click()
        form.wait_for()
        selected = customer.evaluate("e => e.options[e.selectedIndex]?.text.trim()")
        check("Edit shows the saved customer in the drop-down", selected == "Harness Beta", str(selected))
        form.get_by_role("button", name="Cancel").click()

        # the child grid of a master-detail dialog
        page.goto(f"{DEV}/region")
        page.get_by_role("heading", name="Regions").wait_for(timeout=90000)
        search_box(page, "Name").fill("Harness Region")
        page.get_by_role("button", name="Search").click()
        page.wait_for_timeout(700)
        table().locator("tbody tr", has_text="Harness Region").get_by_role("button", name="Edit").click()
        form.wait_for()
        page.wait_for_timeout(1200)
        children = form.locator("table tbody tr")
        shown = [t for t in children.all_inner_texts() if "Harness" in t]
        check("the child grid lists the customers of the region", len(shown) == 2 and any("Alpha" in t for t in shown) and any("Beta" in t for t in shown), str(shown))
        form.get_by_role("button", name="Cancel").click()

        # the paging bar: more rows than one page
        for number in range(1, 26):
            api_send("POST", "/api/regions", {"regionId": 0, "name": f"Harness Page {number:02d}", "code": "HP", "population": number})
        search_box(page, "Name").fill("Harness Page")
        page.get_by_role("button", name="Search").click()
        page.wait_for_timeout(900)
        grid_rows = lambda: table().locator("tbody tr").count()
        first_page = grid_rows()
        previous = page.get_by_role("button", name=re.compile("^Previous( page)?$"))
        following = page.get_by_role("button", name=re.compile("^Next( page)?$"))
        check("the first page holds a full page and Previous is off", 10 <= first_page < 25 and previous.is_disabled(), f"{first_page} rows")
        following.click()
        page.wait_for_timeout(900)
        second_page = grid_rows()
        check("Next shows the rest of the rows", first_page + second_page == 25, f"{first_page} + {second_page}")
        check("the last page has no Next", following.is_disabled())
        previous.click()
        page.wait_for_timeout(900)
        check("Previous goes back to the first page", grid_rows() == first_page)
    finally:
        clean()


def drive_uno(page, prove_failure):
    """The Uno browser head draws on a canvas: its screens are reached through the semantic DOM that "Enable accessibility" adds. The semantic
    elements sit at the wrong coordinates, so a click lands on nothing; focusing an element and pressing a key works."""
    if prove_failure:
        page.route("**/api/regions", lambda route: route.abort() if route.request.method == "POST" else route.continue_())

    def press(locator):
        locator.focus()
        page.wait_for_timeout(300)
        page.keyboard.press("Enter")
        page.wait_for_timeout(1200)

    def type_into(locator, text, replace=False):
        locator.focus()
        page.wait_for_timeout(300)
        if replace:
            page.keyboard.press("Control+a")
        page.keyboard.type(text)
        page.wait_for_timeout(300)

    def button(name):
        return page.get_by_role("button", name=name, exact=True)

    def name_box():
        return page.locator('input[aria-label="Name"]').last

    for leftover in [r for r in regions() if r["name"].startswith("Harness") or r["name"] == "Other Region"]:
        urllib.request.urlopen(urllib.request.Request(f"{API}/api/regions/{leftover['regionId']}", method="DELETE"), timeout=10).close()
    post(f"{API}/api/regions", {"regionId": 0, "name": "Other Region", "code": "OR", "population": 5})

    page.goto(DEV)
    page.wait_for_selector("#uno-enable-accessibility", state="attached", timeout=120000)
    page.wait_for_timeout(3000)
    page.evaluate("document.getElementById('uno-enable-accessibility').focus()")
    page.keyboard.press("Enter")
    page.get_by_role("option", name="Region", exact=True).wait_for(timeout=30000)
    page.get_by_role("option", name="Region", exact=True).focus()
    page.keyboard.press("Enter")
    button("Add New").wait_for(timeout=30000)
    check("the Region screen opens", True)

    press(button("Add New"))
    page.get_by_role("tab", name="Main").wait_for()
    press(button("Save"))
    check("an empty form is refused and nothing is saved", page.get_by_role("tab", name="Main").count() == 1 and not any(r["name"] == "" for r in regions()))

    before = len(regions())
    type_into(name_box(), "Harness North")
    type_into(page.locator('input[aria-label="Code"]').last, "HN")
    type_into(page.locator('input[aria-label^="Population"]'), "1234")
    press(button("Save"))
    page.wait_for_timeout(1000)
    saved = [r for r in regions() if r["name"] == "Harness North"]
    check("Save closes the form", page.get_by_role("tab", name="Main").count() == 0)
    check("the new row is in the database with what was typed", len(regions()) == before + 1 and len(saved) == 1 and saved[0]["code"] == "HN" and saved[0]["population"] == 1234)
    check("the new row is in the grid", page.get_by_text("Harness North", exact=True).count() >= 1)

    type_into(page.locator('input[aria-label="Name"]').first, "Harness")
    press(button("Search"))
    page.wait_for_timeout(800)
    check("the search finds the row and leaves the other out", page.get_by_text("Harness North", exact=True).count() >= 1 and page.get_by_text("Other Region", exact=True).count() == 0)

    press(button("Edit").last)
    check("Edit opens the form with the saved name", name_box().input_value() == "Harness North")
    type_into(name_box(), "Harness South", replace=True)
    press(button("Save"))
    page.wait_for_timeout(1000)
    check("the edit reaches the database", any(r["name"] == "Harness South" for r in regions()) and not any(r["name"] == "Harness North" for r in regions()))

    press(button("Clone").last)
    page.get_by_role("tab", name="Main").wait_for()
    check("Clone saves a copy and opens it", sum(r["name"].startswith("Harness South") for r in regions()) == 2, str([r["name"] for r in regions()]))
    press(button("Cancel"))

    for _ in range(2):
        press(button("Delete").last)
        press(button("Delete").last)
        page.wait_for_timeout(800)
    check("Delete removes the rows from the database", not any(r["name"].startswith("Harness") for r in regions()) and any(r["name"] == "Other Region" for r in regions()))
    check("the grid shows no such row", page.get_by_text("Harness South", exact=False).count() == 0)
    for leftover in [r for r in regions() if r["name"] == "Other Region"]:
        urllib.request.urlopen(urllib.request.Request(f"{API}/api/regions/{leftover['regionId']}", method="DELETE"), timeout=10).close()


def drive_uno_dropdown(page, prove_failure):
    """The lookup drop-down on the Uno head: a customer order picks its customer from a ComboBox fed by the other table. Runs after drive_uno in the
    same page; the rows it needs are added through the API and removed again."""
    if prove_failure:
        page.route("**/api/customerorders", lambda route: route.abort() if route.request.method == "POST" else route.continue_())

    def press(locator):
        locator.focus()
        page.wait_for_timeout(300)
        page.keyboard.press("Enter")
        page.wait_for_timeout(1200)

    def clean():
        for order in api_rows("/api/customerorders"):
            if order["reference"].startswith("Harness"):
                api_send("DELETE", f"/api/customerorders/{order['customerOrderId']}")
        for customer in api_rows("/api/customers"):
            if customer["name"].startswith("Harness"):
                api_send("DELETE", f"/api/customers/{customer['customerId']}")
        for region in api_rows("/api/regions"):
            if region["name"].startswith("Harness"):
                api_send("DELETE", f"/api/regions/{region['regionId']}")

    clean()
    try:
        region = api_send("POST", "/api/regions", {"regionId": 0, "name": "Harness Region", "code": "HR", "population": 1})
        for name in ("Harness Alpha", "Harness Beta"):
            api_send("POST", "/api/customers", {"customerId": 0, "regionId": region["regionId"], "name": name, "email": "a@example.com", "creditLimit": 1, "isActive2": True})
        customers = {c["name"]: c["customerId"] for c in api_rows("/api/customers") if c["name"].startswith("Harness")}

        page.goto(DEV)
        page.wait_for_selector("#uno-enable-accessibility", state="attached", timeout=120000)
        page.wait_for_timeout(3000)
        page.evaluate("document.getElementById('uno-enable-accessibility').focus()")
        page.keyboard.press("Enter")
        page.get_by_role("option", name="Customer Order", exact=True).wait_for(timeout=30000)
        page.get_by_role("option", name="Customer Order", exact=True).focus()
        page.keyboard.press("Enter")
        add = page.get_by_role("button", name="Add New", exact=True)
        add.wait_for(timeout=30000)
        press(add)
        page.get_by_role("tab", name="Main").wait_for()
        combo = page.get_by_role("combobox", name="Customer", exact=True).last
        combo.wait_for()
        press(combo)
        choices = page.evaluate("[...document.querySelectorAll('[role=option]')].map(e => e.getAttribute('aria-label') || e.textContent.trim())")
        check("the Customer drop-down lists the customers of the other table", "Harness Alpha" in choices and "Harness Beta" in choices, str(choices))
        press(page.get_by_role("option", name="Harness Beta", exact=True))
        check("picking a customer closes the drop-down", page.get_by_role("option", name="Harness Beta", exact=True).count() == 0)
        page.locator('input[aria-label="Reference"]').last.focus()
        page.wait_for_timeout(300)
        page.keyboard.type("Harness Order")
        press(page.get_by_role("button", name="Save", exact=True))
        problems =page.evaluate("[...document.querySelectorAll('[role=status]')].map(e => e.textContent.trim()).filter(t => t)")
        check("a picked customer is no longer reported as missing", "Order Date is required." in problems and "Customer is required." not in problems, str(problems))
        check("nothing is saved while the order date is missing", not any(o["reference"].startswith("Harness") for o in api_rows("/api/customerorders")))
        press(page.get_by_role("button", name="Cancel", exact=True))
    finally:
        clean()


def drive_uno_grids(page, prove_failure):
    """The child grid of the Region dialog (the customers of a region) and the paging bar of the Region list on the Uno head, reached through the
    semantic page by focus and keys."""
    def press(locator):
        locator.focus()
        page.wait_for_timeout(300)
        page.keyboard.press("Enter")
        page.wait_for_timeout(1200)

    def clean():
        for customer in api_rows("/api/customers"):
            if customer["name"].startswith("Harness"):
                api_send("DELETE", f"/api/customers/{customer['customerId']}")
        for region in api_rows("/api/regions"):
            if region["name"].startswith("Harness"):
                api_send("DELETE", f"/api/regions/{region['regionId']}")

    ascii_ = lambda text: str(text).encode("ascii", "replace").decode()
    texts = "[...document.querySelectorAll('[aria-label],button')].map(e => e.getAttribute('role') + '/' + (e.getAttribute('aria-label') || e.textContent)).filter(t => t.length > 6).join(' | ')"
    clean()
    try:
        region = api_send("POST", "/api/regions", {"regionId": 0, "name": "Harness Region", "code": "HR", "population": 1})
        for name in ("Harness Alpha", "Harness Beta"):
            api_send("POST", "/api/customers", {"customerId": 0, "regionId": region["regionId"], "name": name, "email": "a@example.com", "creditLimit": 1, "isActive2": True})
        for number in range(1, 26):
            api_send("POST", "/api/regions", {"regionId": 0, "name": f"Harness Page {number:02d}", "code": "HP", "population": number})

        page.goto(DEV)
        page.wait_for_selector("#uno-enable-accessibility", state="attached", timeout=120000)
        page.wait_for_timeout(3000)
        page.evaluate("document.getElementById('uno-enable-accessibility').focus()")
        page.keyboard.press("Enter")
        page.get_by_role("option", name="Region", exact=True).wait_for(timeout=30000)
        page.get_by_role("option", name="Region", exact=True).focus()
        page.keyboard.press("Enter")
        page.get_by_role("button", name="Add New", exact=True).wait_for(timeout=30000)

        page.locator('input[aria-label="Name"]').first.focus()
        page.wait_for_timeout(300)
        page.keyboard.type("Harness Page")
        press(page.get_by_role("button", name="Search", exact=True))
        page.wait_for_timeout(800)
        shown = lambda: re.findall(r"Harness Page \d\d", page.evaluate(texts))
        previous = page.get_by_role("button", name="Previous", exact=True)
        following = page.get_by_role("button", name="Next", exact=True)
        check("the first page starts at the first row", "Harness Page 01" in shown() and "Harness Page 25" not in shown(), str(shown()[:3]))
        press(following)
        check("page 2 holds the last row and not the first", "Harness Page 25" in shown() and "Harness Page 01" not in shown(), str(shown()[:3]))
        press(previous)
        check("Previous goes back to page 1", "Harness Page 01" in shown() and "Harness Page 25" not in shown(), str(shown()[:3]))

        page.locator('input[aria-label="Name"]').first.focus()
        page.keyboard.press("Control+a")
        page.keyboard.type("Harness Region")
        press(page.get_by_role("button", name="Search", exact=True))
        page.wait_for_timeout(800)
        press(page.get_by_role("button", name="Edit", exact=True).last)
        page.get_by_role("tab", name="Main").wait_for()
        page.wait_for_timeout(1500)
        children = lambda: re.findall(r"Harness (?:Alpha|Beta)", page.evaluate(texts))
        check("the child grid lists the customers of the region", sorted(set(children())) == ["Harness Alpha", "Harness Beta"], ascii_(page.evaluate(texts))[-500:])
    finally:
        clean()


def drive_junction(page, prove_failure):
    """The Customer dialog of a database with a many-to-many table (CustomerTag): the junction table has no API list of its own, so the dialog shows no grid of it
    and asks the API for nothing that is refused. The tags are linked through the junction route first, so a grid that read the table would have rows to show."""
    form = page.locator('dialog[open], [role="dialog"]')
    refused = []
    page.on("response", lambda r: refused.append(f"{r.status} {r.url}") if "/api/" in r.url and r.status >= 400 else None)

    def clean():
        for customer_ in api_rows("/api/customers"):
            if customer_["name"].startswith("Harness"):
                api_send("DELETE", f"/api/customers/{customer_['customerId']}")
        for tag in api_rows("/api/tags"):
            if tag["name"].startswith("Harness"):
                api_send("DELETE", f"/api/tags/{tag['tagId']}")
        for region in api_rows("/api/regions"):
            if region["name"].startswith("Harness"):
                api_send("DELETE", f"/api/regions/{region['regionId']}")

    clean()
    try:
        region = api_send("POST", "/api/regions", {"regionId": 0, "name": "Harness Region", "code": "HR", "population": 1})
        customer = api_send("POST", "/api/customers", {"customerId": 0, "regionId": region["regionId"], "name": "Harness Customer", "email": "a@example.com", "creditLimit": 1, "isActive2": True})
        for name in ("Harness Red", "Harness Green"):
            tag = api_send("POST", "/api/tags", {"tagId": 0, "name": name, "colour": "x", "priority": 1, "created": "2026-04-01T00:00:00"})
            api_send("POST", "/api/customertags/junction/link", {"anchorId": customer["customerId"], "targetId": tag["tagId"]})
        linked = [r["name"] for r in api_send("GET", f"/api/customertags/junction/{customer['customerId']}") if r["isSelected"]]
        check("the tags are linked through the junction route", sorted(linked) == ["Harness Green", "Harness Red"], str(linked))

        page.goto(f"{DEV}/customer")
        page.get_by_role("heading", name="Customers").wait_for(timeout=90000)
        search_box(page, "Name").fill("Harness Customer")
        page.get_by_role("button", name="Search").click()
        page.wait_for_timeout(800)
        page.locator("table").first.locator("tbody tr", has_text="Harness Customer").get_by_role("button", name="Edit").click()
        form.wait_for()
        page.wait_for_timeout(1500)
        check("the Customer dialog opens on the saved row", form.locator('input[aria-label="Name"], #customerName').first.input_value() == "Harness Customer")
        check("the dialog shows no grid of the junction table", form.get_by_text("Customer Tag", exact=True).count() == 0)
        check("the page asked the API for nothing that was refused", not refused, "; ".join(refused)[:200])
        form.get_by_role("button", name="Cancel").click()
    finally:
        clean()


def call(token, method, path, body=None):
    """One API call with a bearer token; answers (status, json or None) and never raises on a refusal."""
    headers = {"Content-Type": "application/json", **({"Authorization": f"Bearer {token}"} if token else {})}
    request = urllib.request.Request(f"{API}{path}", data=None if body is None else json.dumps(body).encode(), method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            status, text = response.status, response.read().decode()
    except urllib.error.HTTPError as error:
        status, text = error.code, error.read().decode()
    try:
        return status, json.loads(text) if text else None
    except ValueError:
        return status, None


def drive_owner(page, prove_failure):
    """A table whose rows belong to a person (Expense, owned through EmployeeId) served as an OData screen behind a sign-in: a visitor who is not signed in is sent to
    the sign-in page, a wrong password gets nowhere, each person's grid lists only their own rows (the administrator's lists all), a person adds, edits and deletes their
    own row, cannot save a row for someone else, and the API refuses another person's row by key and the unscoped REST search. The rows are made through the API with each person's token."""
    form = page.locator('dialog[open], [role="dialog"]')
    refused = []
    page.on("response", lambda r: refused.append(f"{r.status} {r.url}") if "/odata/" in r.url and r.status >= 400 and r.request.method == "GET" else None)
    if prove_failure:
        page.route(re.compile(r".*/(odata|api)/expenses.*"), lambda route: route.abort() if route.request.method == "POST" else route.continue_())

    tokens = {login: call(None, "POST", "/api/auth/login", {"userName": login, "password": owner_password(login)})[1]["token"] for login in OWNER_PEOPLE}
    employee_ids = {login: number for number, login in enumerate(OWNER_PEOPLE, start=1)}

    def expenses(token="root"):
        status, data = call(tokens[token], "GET", "/odata/expenses?$top=100")
        return data["value"] if status == 200 else []

    def clean():
        for row in [r for r in expenses() if r["description"].startswith("Harness")]:
            call(tokens["root"], "DELETE", f"/api/expenses/{row['expenseId']}")

    def make(login, description, amount):
        return call(tokens[login], "POST", "/api/expenses", {"expenseId": 0, "employeeId": employee_ids[login], "description": description, "amount": amount, "expenseDate": "2026-04-01T00:00:00"})

    def sign_in(login, password=None):
        page.goto(f"{DEV}/login")
        page.get_by_role("heading", name="Sign in").wait_for(timeout=90000)
        page.locator("#userName").fill(login)
        page.locator("#password").fill(password or owner_password(login))
        page.get_by_role("button", name="Sign in").click()

    def sign_out():
        page.get_by_role("button", name="Sign out").click()
        page.wait_for_timeout(500)

    def open_expenses():
        page.wait_for_timeout(1000)
        page.goto(f"{DEV}/expense")
        page.get_by_role("heading", name="Expenses").wait_for(timeout=90000)
        page.wait_for_timeout(900)

    def grid():
        return page.locator("table").first.locator("tbody tr", has_text="Harness")

    def grid_text():
        return " | ".join(grid().all_inner_texts())

    clean()
    try:
        make("ann", "Harness Ann Taxi", 12.5)
        make("ann", "Harness Ann Lunch", 8)
        make("bob", "Harness Bob Hotel", 99)
        ids = {r["description"]: r["expenseId"] for r in expenses() if r["description"].startswith("Harness")}
        check("the rows were made, each for its person", len(ids) == 3 and all(r["employeeId"] == employee_ids[l] for r in expenses() for l in ("ann", "bob") if r["description"].startswith(f"Harness {l.title()}")), str(ids))

        page.goto(f"{DEV}/expense")
        page.get_by_role("heading", name="Sign in").wait_for(timeout=90000)
        check("a visitor who is not signed in is sent to the sign-in page", "login" in page.url)
        sign_in("ann", "not-the-password")
        page.wait_for_timeout(1200)
        check("a wrong password gets nowhere", "login" in page.url and page.get_by_role("heading", name="Sign in").is_visible())

        sign_in("ann")
        open_expenses()
        text = grid_text()
        check("the grid lists the two rows of the person who signed in", grid().count() == 2 and "Taxi" in text and "Lunch" in text, text)
        check("it does not list another person's row", "Hotel" not in text)

        page.get_by_role("button", name="Add New").click()
        form.wait_for()
        employee = form.locator('select[aria-label="Employee"], #expenseEmployeeId').first
        field_ = lambda label, ident: form.locator(f'[aria-label="{label}"], #{ident}').first
        employee.select_option(label="Ann")
        field_("Description", "expenseDescription").fill("Harness Ann Train")
        field_("Amount", "expenseAmount").fill("30")
        field_("Expense Date", "expenseExpenseDate").fill("2026-04-02")
        form.get_by_role("button", name=SUBMIT).click()
        try:
            form.wait_for(state="hidden", timeout=4000)
        except Exception:
            pass
        saved = [r for r in expenses() if r["description"] == "Harness Ann Train"]
        check("a row added on the screen is saved for the person who signed in", len(saved) == 1 and saved[0]["employeeId"] == employee_ids["ann"] and saved[0]["amount"] == 30, str(saved))
        page.wait_for_timeout(900)
        check("it shows in the grid", grid().count() == 3 and "Train" in grid_text())

        page.get_by_role("button", name="Add New").click()
        form.wait_for()
        employee.select_option(label="Bob")
        field_("Description", "expenseDescription").fill("Harness Ann For Bob")
        field_("Amount", "expenseAmount").fill("5")
        field_("Expense Date", "expenseExpenseDate").fill("2026-04-02")
        form.get_by_role("button", name=SUBMIT).click()
        page.wait_for_timeout(1200)
        check("a row for someone else is refused: nothing is saved", not any(r["description"] == "Harness Ann For Bob" for r in expenses()))
        form.get_by_role("button", name="Cancel").click()

        grid().filter(has_text="Train").get_by_role("button", name="Edit").click()
        form.wait_for()
        page.wait_for_timeout(800)
        field_("Description", "expenseDescription").fill("Harness Ann Train Home")
        form.get_by_role("button", name=SUBMIT).click()
        try:
            form.wait_for(state="hidden", timeout=4000)
        except Exception:
            pass
        edited = [r for r in expenses() if r["description"] == "Harness Ann Train Home"]
        check("Edit saves a change to the person's own row", len(edited) == 1 and edited[0]["expenseId"] == saved[0]["expenseId"] if saved else False)

        page.on("dialog", lambda d: d.accept())
        grid().filter(has_text="Train Home").get_by_role("button", name="Delete").click()
        page.wait_for_timeout(1200)
        check("Delete removes the person's own row", not any(r["description"].startswith("Harness Ann Train") for r in expenses()) and grid().count() == 2)

        sign_out()
        sign_in("bob")
        open_expenses()
        text = grid_text()
        check("another person's grid lists only their own row", grid().count() == 1 and "Hotel" in text and "Taxi" not in text and "Lunch" not in text, text)

        sign_out()
        sign_in("root")
        open_expenses()
        check("the administrator's grid lists everyone's rows", grid().count() == 3 and "Hotel" in grid_text() and "Taxi" in grid_text(), grid_text())

        status, _ = call(tokens["bob"], "GET", f"/odata/expenses/{ids['Harness Ann Taxi']}")
        check("the API refuses another person's row by key", status == 403, str(status))
        status, _ = call(tokens["bob"], "PUT", f"/api/expenses/{ids['Harness Ann Taxi']}", {"expenseId": ids["Harness Ann Taxi"], "employeeId": employee_ids["ann"], "description": "Harness changed", "amount": 1, "expenseDate": "2026-04-01T00:00:00"})
        check("the API refuses a change to another person's row", status in (403, 404) and any(r["description"] == "Harness Ann Taxi" for r in expenses()), str(status))
        status, found = call(tokens["bob"], "GET", "/api/expenses/search?pageNumber=1&pageSize=100")
        listed = found if isinstance(found, list) else next((v for v in (found or {}).values() if isinstance(v, list)), [])
        check("the REST search lists only the caller's rows", status == 200 and len(listed) > 0 and all(r["employeeId"] == employee_ids["bob"] for r in listed), f"{status} {len(listed)} rows")
        check("the screen asked the OData routes for nothing that was refused", not refused, "; ".join(refused)[:200])
    finally:
        clean()


def seed_people(out, api_dir, env):
    """The people who sign in: three employees in the project's database, each with a password set through the generated API's own set-password command."""
    import sqlite3
    database = next(out.parent.glob("*.db"))
    connection = sqlite3.connect(database)
    if not connection.execute("SELECT COUNT(*) FROM Employee").fetchone()[0]:
        connection.executemany("INSERT INTO Employee (Login, Name, IsAdministrator) VALUES (?, ?, ?)", [(login, name, int(admin)) for login, (name, admin) in OWNER_PEOPLE.items()])
        connection.commit()
    connection.close()
    for login in OWNER_PEOPLE:
        result = subprocess.run(["dotnet", "run", "--no-build", "--", "set-password", login], cwd=api_dir, env=dict(env, NEW_PASSWORD=owner_password(login)), capture_output=True, text=True)
        if result.returncode != 0 or f"Password set for {login}" not in result.stdout:
            sys.exit(f"set-password failed for {login}: " + (result.stdout + result.stderr)[-600:])


def drive_owner_uno(page, prove_failure):
    """The Expense screen of the owner-scoped sample on the Uno head, through its accessibility page: the sign-in, each person's own rows only (the administrator's all),
    Edit and Delete of one's own row, and the API's refusals of another person's row. A row cannot be added there: the calendar date picker cannot be driven."""
    if prove_failure:
        page.route(re.compile(r".*/(odata|api)/expenses.*"), lambda route: route.abort() if route.request.method in ("PUT", "DELETE") else route.continue_())

    tokens = {login: call(None, "POST", "/api/auth/login", {"userName": login, "password": owner_password(login)})[1]["token"] for login in OWNER_PEOPLE}
    employee_ids = {login: number for number, login in enumerate(OWNER_PEOPLE, start=1)}

    def expenses():
        status, data = call(tokens["root"], "GET", "/odata/expenses?$top=100")
        return data["value"] if status == 200 else []

    def clean():
        for row in [r for r in expenses() if r["description"].startswith("Harness")]:
            call(tokens["root"], "DELETE", f"/api/expenses/{row['expenseId']}")

    def make(login, description, amount):
        return call(tokens[login], "POST", "/api/expenses", {"expenseId": 0, "employeeId": employee_ids[login], "description": description, "amount": amount, "expenseDate": "2026-04-01T00:00:00"})

    def press(locator):
        locator.focus()
        page.wait_for_timeout(300)
        page.keyboard.press("Enter")
        page.wait_for_timeout(1200)

    def type_into(locator, text, replace=False):
        locator.focus()
        page.wait_for_timeout(300)
        if replace:
            page.keyboard.press("Control+a")
        page.keyboard.type(text)
        page.wait_for_timeout(300)

    def button(name):
        return page.get_by_role("button", name=name, exact=True)

    def enable_accessibility():
        page.wait_for_selector("#uno-enable-accessibility", state="attached", timeout=120000)
        page.wait_for_timeout(3000)
        page.evaluate("document.getElementById('uno-enable-accessibility').focus()")
        page.keyboard.press("Enter")

    def sign_in(login, password=None):
        type_into(page.locator('input[aria-label="User name"]').last, login, replace=True)
        type_into(page.locator('input[type="password"]').last, password or owner_password(login), replace=True)
        press(button("Sign in"))

    def open_expenses():
        page.get_by_role("option", name="Expense", exact=True).wait_for(timeout=30000)
        press(page.get_by_role("option", name="Expense", exact=True))
        button("Add New").wait_for(timeout=30000)
        page.wait_for_timeout(1000)

    def shown(text):
        return page.get_by_text(text, exact=True).count() >= 1

    clean()
    try:
        make("ann", "Harness Ann Taxi", 12.5)
        make("ann", "Harness Ann Lunch", 8)
        make("bob", "Harness Bob Hotel", 99)
        ids = {r["description"]: r["expenseId"] for r in expenses() if r["description"].startswith("Harness")}
        check("the rows were made, each for its person", len(ids) == 3, str(ids))

        page.goto(DEV)
        enable_accessibility()
        page.locator('input[aria-label="User name"]').last.wait_for(timeout=30000)
        check("the app opens on the sign-in page", True)
        sign_in("ann", "not-the-password")
        page.wait_for_timeout(1000)
        check("a wrong password gets nowhere", page.locator('input[aria-label="User name"]').count() >= 1)

        sign_in("ann")
        open_expenses()
        check("the grid lists the two rows of the person who signed in", shown("Harness Ann Taxi") and shown("Harness Ann Lunch"))
        check("it does not list another person's row", not shown("Harness Bob Hotel"))

        press(button("Edit").last)
        description = page.locator('input[aria-label="Description"]').last
        check("Edit opens the form with the saved description", description.input_value() in ("Harness Ann Taxi", "Harness Ann Lunch"), description.input_value())
        opened = description.input_value()
        type_into(description, opened + " Home", replace=True)
        press(button("Save"))
        page.wait_for_timeout(1000)
        check("the edit reaches the database for the person's own row", any(r["description"] == opened + " Home" and r["employeeId"] == employee_ids["ann"] for r in expenses()))

        for _ in range(2):
            press(button("Delete").last)
            press(button("Delete").last)
            page.wait_for_timeout(800)
        check("Delete removes the person's own rows and no one else's", [r["description"] for r in expenses() if r["description"].startswith("Harness")] == ["Harness Bob Hotel"], str([r["description"] for r in expenses()]))
        make("ann", "Harness Ann Taxi", 12.5)
        make("ann", "Harness Ann Lunch", 8)

        press(button("Sign out"))
        page.wait_for_timeout(1000)
        sign_in("bob")
        open_expenses()
        check("another person's grid lists only their own row", shown("Harness Bob Hotel") and not shown("Harness Ann Taxi") and not shown("Harness Ann Lunch"))

        press(button("Sign out"))
        page.wait_for_timeout(1000)
        sign_in("root")
        open_expenses()
        check("the administrator's grid lists everyone's rows", shown("Harness Bob Hotel") and shown("Harness Ann Taxi") and shown("Harness Ann Lunch"))

        ids = {r["description"]: r["expenseId"] for r in expenses() if r["description"].startswith("Harness")}
        status, _ = call(tokens["bob"], "GET", f"/odata/expenses/{ids['Harness Ann Taxi']}")
        check("the API refuses another person's row by key", status == 403, str(status))
        status, _ = call(tokens["bob"], "PUT", f"/api/expenses/{ids['Harness Ann Taxi']}", {"expenseId": ids["Harness Ann Taxi"], "employeeId": employee_ids["ann"], "description": "Harness changed", "amount": 1, "expenseDate": "2026-04-01T00:00:00"})
        check("the API refuses a change to another person's row", status in (403, 404) and any(r["description"] == "Harness Ann Taxi" for r in expenses()), str(status))
    finally:
        clean()


def uno_profile(project):
    profiles = json.loads((project / "Properties" / "launchSettings.json").read_text(encoding="utf-8"))["profiles"]
    return next(name for name in profiles if "WebAssembly" in name)


def post(url, body):
    request = urllib.request.Request(url, data=json.dumps(body).encode(), method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(request, timeout=10).close()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stack", choices=list(STACKS), default="react")
    parser.add_argument("--out")
    parser.add_argument("--schema", choices=["master_detail", "junction", "owner_scoped"], default="master_detail", help="master_detail: the Region walks; junction: the Customer dialog of a table with a many-to-many link to Tag; owner_scoped: the Expense screen of a table whose rows belong to a signed-in person")
    parser.add_argument("--prove-failure", action="store_true")
    parser.add_argument("--build-cli", action="store_true", help="build the command-line tool first (a fresh checkout has not)")
    parser.add_argument("--browser", default="chrome")
    args = parser.parse_args()
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        sys.exit("pip install playwright")
    global DEV
    DEV = f"http://localhost:{STACKS[args.stack]}"
    for port in (5080, STACKS[args.stack]):
        if not port_free(port):
            sys.exit(f"port {port} is in use: stop what is listening there")

    out = Path(args.out) if args.out else generate(args.stack, args.build_cli, args.schema)
    print(f"project: {out}")
    api_dir = next(out.glob("*.Api"))
    front = next(out.glob("*.Blazor" if args.stack == "blazor" else "*.Uno")) if args.stack in ("blazor", "uno") else out / "frontend"
    npm = shutil.which("npm") or "npm.cmd"
    build = subprocess.run(["dotnet", "build", str(api_dir), "-v", "q"], capture_output=True, text=True)
    if build.returncode != 0:
        sys.exit("API build failed:\n" + "\n".join(l for l in build.stdout.splitlines() if " error " in l))
    if args.stack in ("blazor", "uno"):
        build = subprocess.run(["dotnet", "build", str(front), "-v", "q", *(["-f", "net10.0-browserwasm"] if args.stack == "uno" else [])], capture_output=True, text=True)
        if build.returncode != 0:
            sys.exit("front end build failed:\n" + "\n".join(l for l in build.stdout.splitlines() if " error " in l))
    elif not (front / "node_modules").exists():
        install = subprocess.run([npm, "install", "--no-audit", "--no-fund"], cwd=front, capture_output=True, text=True)
        if install.returncode != 0:
            sys.exit("npm install failed:\n" + (install.stderr + install.stdout)[-800:])

    api = dev = None
    api_env = dict(os.environ)
    if args.schema == "owner_scoped":
        api_env["Jwt__Key"] = OWNER_KEY
        seed_people(out, api_dir, api_env)
    try:
        api = subprocess.Popen(["dotnet", "run", "--no-build"], cwd=api_dir, env=api_env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=os.name != "nt")
        wait_for(f"{API}/api/{'expenses' if args.schema == 'owner_scoped' else 'regions'}", 60, "the API")
        command = {"react": [npm, "run", "dev", "--", "--strictPort"], "angular": [npm, "start", "--", "--port", "4200"],
                   "blazor": ["dotnet", "run", "--no-build", "--launch-profile", "http"],
                   "uno": ["dotnet", "run", "-f", "net10.0-browserwasm", "--no-build", "--launch-profile", uno_profile(front) if args.stack == "uno" else ""]}[args.stack]
        dev = subprocess.Popen(command, cwd=front, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=dict(os.environ, NG_CLI_ANALYTICS="false"), start_new_session=os.name != "nt")
        wait_for(DEV, 240, "the dev server")
        with sync_playwright() as p:
            browser = p.chromium.launch(channel=args.browser, headless=True)
            try:
                page = browser.new_page()
                errors = []
                page.on("pageerror", lambda e: errors.append(str(e)))
                page.set_default_timeout(5000)
                walks = [drive_junction] if args.schema == "junction" else [drive_owner_uno if args.stack == "uno" else drive_owner] if args.schema == "owner_scoped" else [drive_uno, drive_uno_dropdown, drive_uno_grids] if args.stack == "uno" else [drive, drive_more]
                for walk in walks:
                    try:
                        walk(page, args.prove_failure)
                    except Exception as e:
                        check(f"the {walk.__name__.replace('_', ' ')} walk reached its end", False, str(e).splitlines()[0][:160])
                check("the page raised no script error", not errors, "; ".join(errors)[:200])
            finally:
                browser.close()
    finally:
        stop(dev)
        stop(api)
    print(f"{'FAILED' if failures else 'OK'}: {failures} failing check(s)")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    started = time.perf_counter()

    def elapsed():
        seconds = time.perf_counter() - started
        print(f"Elapsed: {int(seconds // 60)} min {seconds % 60:.1f} s ({seconds:.1f} s)", flush=True)

    # a stop by the system (a cancelled CI run, a kill) raises SystemExit too, so the processes are stopped and the time is printed
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(143))
    try:
        main()
    except SystemExit as stop:
        # a message given to sys.exit is printed by Python after this block, so it is printed here first, then the time comes last
        if isinstance(stop.code, str):
            print(stop.code, file=sys.stderr, flush=True)
            stop = SystemExit(1)
        elapsed()
        raise stop
    except BaseException:
        elapsed()
        raise
    else:
        elapsed()
