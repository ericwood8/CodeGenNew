"""Drives the Asset screen of a generated React, Angular or Blazor WebAssembly app in a real browser (Playwright, the installed Chrome) over a throwaway SQLite project with AssetTables.

  python Test-AssetPages.py --stack react
  python Test-AssetPages.py --stack angular
  python Test-AssetPages.py --stack blazor

It generates Tools/Schemas/assets.sql (API and the chosen front end), builds and starts the API (port 5080) and the dev server, then: sets a PNG on the file box of the list page (the box is driven with
Playwright's set_input_files, as a person would pick a file), checks the row and the stored file through the API, clicks Download on the row and compares the bytes the browser saved with the file that was sent
(for Blazor this runs the codegenSaveFile script of wwwroot/index.html), opens the row in the edit dialog and checks its Download button, the picture and the bytes again, adds a row from the dialog's own Upload
button (the dialog goes on with the saved row), and sends a file of a type the project refuses, which must save nothing. Prints PASS / FAIL lines and exits 1 on a failure; what it started is stopped at the end.
--build-cli builds the command-line tool first. --out <folder> uses a generated project. --browser chrome|msedge picks the installed browser. Needs `pip install playwright`.
"""
import argparse
import base64
import importlib.util
import os
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("front_end_pages", HERE / "Test-FrontEndPages.py")
pages = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pages)

PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")
SETTINGS = ["AssetTables=Asset:StorageKey:FileName:MimeType:SizeBytes:PublicUrl", "AssetAllowedTypes=image/png,text/plain", "Screens=Asset"]


def assets():
    return pages.api_rows("/api/assets")


def has_upload(dialog):
    """An Upload button (React, Angular) or the labelled file box of Blazor's InputFile."""
    return dialog.get_by_role("button", name="Upload", exact=True).count() + dialog.locator("label:has(input[type=file])").count() > 0


def drive(page, work):
    dialog = page.locator("dialog[open], [role=\"dialog\"]")
    page.goto(f"{pages.DEV}/asset")
    page.get_by_role("heading", name="Assets").wait_for(timeout=90000)
    pages.check("the Asset screen opens", True)
    grid = page.locator("table").first

    png = work / "pixel.png"
    png.write_bytes(PNG)
    before = len(assets())
    page.locator("input[type=file]").first.set_input_files(str(png))
    page.wait_for_timeout(1500)
    rows = [a for a in assets() if a["fileName"] == "pixel.png"]
    pages.check("the file box of the list uploads the picture as a row", len(assets()) == before + 1 and len(rows) == 1 and rows[0]["mimeType"] == "image/png" and rows[0]["sizeBytes"] == len(PNG), str(rows))
    row_id = rows[0]["assetId"] if rows else 0
    with urllib.request.urlopen(f"{pages.API}/api/assets/{row_id}/download", timeout=10) as response:
        pages.check("the server holds the bytes that were sent", response.read() == PNG)
    line = grid.locator("tbody tr", has_text="pixel.png")
    pages.check("the new row is in the grid", line.count() == 1)

    box = line.get_by_role("button", name="Delete").bounding_box()
    width = page.viewport_size["width"]
    pages.check("the Delete button of the row is fully on the screen", box is not None and box["x"] >= 0 and box["x"] + box["width"] <= width, f"{box} in {width}")
    with page.expect_download(timeout=15000) as download:
        line.get_by_role("button", name="Download").click()
    saved = work / "from-row.png"
    download.value.save_as(str(saved))
    pages.check("Download on the row saves the file under its name", download.value.suggested_filename == "pixel.png", download.value.suggested_filename)
    pages.check("the saved file holds the bytes that were sent", saved.read_bytes() == PNG)

    line.get_by_role("button", name="Edit").click()
    dialog.wait_for()
    pages.check("the edit dialog has a Download button", dialog.get_by_role("button", name="Download").count() == 1)
    pages.check("a saved row has no Upload button in its dialog", not has_upload(dialog))
    picture = dialog.locator("img")
    try:
        picture.first.wait_for(timeout=5000)
        drawn = page.wait_for_function("() => { const i = document.querySelector('dialog[open] img, [role=\"dialog\"] img'); return i && i.complete && i.naturalWidth > 0; }", timeout=5000).json_value()
    except Exception:
        drawn = False
    pages.check("the dialog draws the picture", bool(drawn))
    with page.expect_download(timeout=15000) as download:
        dialog.get_by_role("button", name="Download").click()
    dialog_saved = work / "from-dialog.png"
    download.value.save_as(str(dialog_saved))
    pages.check("Download in the dialog saves the same bytes under the stored name", dialog_saved.read_bytes() == PNG and download.value.suggested_filename == "pixel.png", download.value.suggested_filename)
    dialog.get_by_role("button", name="Cancel").click()
    dialog.wait_for(state="hidden")

    note = work / "note.txt"
    note.write_bytes(b"hello from the dialog")
    before = len(assets())
    page.get_by_role("button", name="Add New").click()
    dialog.wait_for()
    pages.check("a new row's dialog has an Upload and no Download button", dialog.get_by_role("button", name="Download").count() == 0 and has_upload(dialog))
    dialog.locator("input[type=file]").set_input_files(str(note))
    page.wait_for_timeout(1500)
    made = [a for a in assets() if a["fileName"] == "note.txt"]
    pages.check("Upload in the dialog makes the row", len(assets()) == before + 1 and len(made) == 1 and made[0]["mimeType"].startswith("text/plain"), str(made))
    pages.check("the dialog goes on with the saved row", dialog.get_by_role("heading", name=re.compile("Edit")).count() == 1 and dialog.get_by_role("button", name="Download").count() == 1)
    with page.expect_download(timeout=15000) as download:
        dialog.get_by_role("button", name="Download").click()
    text_saved = work / "from-note.txt"
    download.value.save_as(str(text_saved))
    pages.check("the row made in the dialog downloads what was sent", text_saved.read_bytes() == b"hello from the dialog")
    dialog.get_by_role("button", name="Cancel").click()
    dialog.wait_for(state="hidden")

    refused = work / "tool.bin"
    refused.write_bytes(b"\x00\x01\x02binary")
    before = len(assets())
    page.locator("input[type=file]").first.set_input_files(str(refused))
    page.wait_for_timeout(1500)
    pages.check("a file of a type the project refuses saves nothing", len(assets()) == before)

    for leftover in [a for a in assets() if a["fileName"] in ("pixel.png", "note.txt")]:
        urllib.request.urlopen(urllib.request.Request(f"{pages.API}/api/assets/{leftover['assetId']}", method="DELETE"), timeout=10).close()
    pages.check("the rows are removed again", not any(a["fileName"] in ("pixel.png", "note.txt") for a in assets()))


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
        result = subprocess.run([sys.executable, str(HERE / "Tools" / "new_scratch_project.py"), "PwAsset" + args.stack.title(), "--schema", "assets", "--stack", "api," + args.stack,
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
    work = Path(tempfile.mkdtemp(prefix="asset_pages_"))
    try:
        api = subprocess.Popen(["dotnet", "run", "--no-build"], cwd=api_dir, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=os.name != "nt")
        pages.wait_for(f"{pages.API}/api/assets", 60, "the API")
        command = {"react": [npm, "run", "dev", "--", "--strictPort"], "angular": [npm, "start", "--", "--port", "4200"], "blazor": ["dotnet", "run", "--no-build", "--launch-profile", "http"]}[args.stack]
        dev = subprocess.Popen(command, cwd=front, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=dict(os.environ, NG_CLI_ANALYTICS="false"), start_new_session=os.name != "nt")
        pages.wait_for(pages.DEV, 240, "the dev server")
        with sync_playwright() as p:
            browser = p.chromium.launch(channel=args.browser, headless=True)
            try:
                page = browser.new_page(accept_downloads=True)
                errors = []
                page.on("pageerror", lambda e: errors.append(str(e)))
                page.set_default_timeout(5000)
                try:
                    drive(page, work)
                except Exception as e:
                    pages.check("the asset walk reached its end", False, str(e).splitlines()[0][:200])
                pages.check("the page raised no script error", not errors, "; ".join(errors)[:200])
            finally:
                browser.close()
    finally:
        pages.stop(dev)
        pages.stop(api)
        shutil.rmtree(work, ignore_errors=True)
    print(f"{'FAILED' if pages.failures else 'OK'}: {pages.failures} failing check(s)")
    sys.exit(1 if pages.failures else 0)


if __name__ == "__main__":
    main()
