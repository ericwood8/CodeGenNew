"""Plays the ContentTables rules against a running API over a scratch copy of Schemas/content.sql.

python content_check.py <base url> [route]

The API is a generated one (ASP.NET, Python or Rust) with ContentTables=Post; with ApiAuth set API_USER and API_PASSWORD (the bootstrap administrator). The public reads are always called without the token.
With ContentPublicColumns=Post:Title+Slug the check that Body stays out of the public answer runs too. The route defaults to /api/posts. The script adds Post rows and removes them again, so use a scratch database."""
import os
import sys
import uuid

import httpx

base = sys.argv[1]
route = sys.argv[2] if len(sys.argv) > 2 else "/api/posts"
client = httpx.Client(base_url=base, timeout=30)
anonymous = httpx.Client(base_url=base, timeout=30)
if os.environ.get("API_USER"):
    login = client.post("/api/auth/login", json={"userName": os.environ["API_USER"], "password": os.environ["API_PASSWORD"]})
    login.raise_for_status()
    body = login.json()
    token = next(body[k] for k in ("token", "accessToken", "access_token") if k in body)
    client.headers["Authorization"] = "Bearer " + token
failures = []
made = []
token = uuid.uuid4().hex[:8]


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def body_of(**values):
    row = {"postId": 0, "title": "Sample " + token, "slug": "", "body": "Some text", "status": "draft", "publishedOn": None, "createdOn": None}
    row.update(values)
    return row


def make(**values):
    response = client.post(route, json=body_of(**values))
    if response.status_code == 201:
        made.append(response.json())
    return response


def put(row, **values):
    return client.put(f"{route}/{row['postId']}", json={**row, **values})


def stored(row):
    return client.get(f"{route}/{row['postId']}").json()


first = make(title="Wö " + token).json()
check("the slug is made from the title: lower case, no accent, a hyphen", first["slug"] == "wo-" + token, first["slug"])
second = make(title="Wö " + token).json()
check("a made slug that is taken gets a number", second["slug"] == "wo-" + token + "-2", second["slug"])
r = make(title="Named " + token, slug="WO-" + token)
check("a slug the caller names that is taken is a 409", r.status_code == 409 and r.json()["type"].endswith("content-slug-taken"), (r.status_code, r.text))
named = make(title="Other " + token, slug="My Page " + token).json()
check("a slug the caller names is made into the same form", named["slug"] == "my-page-" + token, named["slug"])
punctuation = make(title="?!").json()
check("a title with nothing to make a slug from gives untitled", punctuation["slug"].startswith("untitled"), punctuation["slug"])

renamed = put(first, title="New " + token)
check("a save with a new title is accepted", renamed.status_code == 200, (renamed.status_code, renamed.text))
blank = put(first, title="New " + token, slug="")
check("a save with a blank slug is accepted", blank.status_code == 200, (blank.status_code, blank.text))
now = stored(first)
check("a new title does not move the slug", now["title"] == "New " + token and now["slug"] == "wo-" + token, now)
r = put(first, slug=second["slug"])
check("a rename to a slug that is taken is a 409", r.status_code == 409 and r.json()["type"].endswith("content-slug-taken"), (r.status_code, r.text))
r = put(first, slug="renamed-" + token)
check("a rename to a free slug is a rename on purpose", r.status_code == 200 and stored(first)["slug"] == "renamed-" + token, (r.status_code, r.text))
first["slug"] = "renamed-" + token

draft = make(status="published", publishedOn="2026-01-01T09:00:00").json()
check("a new row is a draft whatever it says", draft["status"] == "draft" and draft["publishedOn"] is None, draft)
r = put(draft, status="archived", publishedOn="2026-02-02T09:00:00")
now = stored(draft)
check("a save does not change the status or the published time", r.status_code == 200 and now["status"] == "draft" and now["publishedOn"] is None, now)

address = draft["slug"]
check("a draft is not public", anonymous.get(f"{route}/public/{address}").status_code == 404)
r = client.post(f"{route}/{draft['postId']}/publish")
check("publish answers the published row", r.status_code == 200 and r.json()["status"] == "published" and r.json()["publishedOn"] is not None, (r.status_code, r.text))
stamp = r.json()["publishedOn"]
r = anonymous.get(f"{route}/public/{address}")
check("a published row is public without a sign-in", r.status_code == 200 and r.json()["slug"] == address, (r.status_code, r.text))
listed = anonymous.get(f"{route}/public?pageSize=100")
check("the public list holds the published row", listed.status_code == 200 and any(x["slug"] == address for x in listed.json()), (listed.status_code, listed.text[:200]))
check("the public list holds no draft", all(x["slug"] != first["slug"] for x in listed.json()))
check("the public list is the newest first", [x["slug"] for x in listed.json()][0] == address, [x["slug"] for x in listed.json()])
check("the page size is at most 100", len(anonymous.get(f"{route}/public?pageSize=1000").json()) <= 100)
check("a page size of 1 gives one row", len(anonymous.get(f"{route}/public?pageSize=1").json()) == 1)
if "body" not in anonymous.get(f"{route}/public/{address}").json():
    check("a column that is not in ContentPublicColumns stays out of the public list", all("body" not in x for x in listed.json()))
r = client.post(f"{route}/{draft['postId']}/publish")
check("publishing again keeps the first time", r.status_code == 200 and r.json()["publishedOn"] == stamp, (r.status_code, r.text))
r = client.post(f"{route}/{draft['postId']}/unpublish")
check("unpublish is back to a draft", r.status_code == 200 and r.json()["status"] == "draft", (r.status_code, r.text))
check("an unpublished row is not public", anonymous.get(f"{route}/public/{address}").status_code == 404)
client.post(f"{route}/{draft['postId']}/publish")
r = client.post(f"{route}/{draft['postId']}/archive")
check("archive sets the state", r.status_code == 200 and r.json()["status"] == "archived", (r.status_code, r.text))
check("an archived row is not public", anonymous.get(f"{route}/public/{address}").status_code == 404)
check("publish on a missing row is a 404", client.post(f"{route}/999999/publish").status_code == 404)
check("a missing slug is a 404", anonymous.get(f"{route}/public/no-such-page-" + token).status_code == 404)

if os.environ.get("API_USER"):
    check("publish needs the sign-in", anonymous.post(f"{route}/{draft['postId']}/publish").status_code == 401)
    check("the list needs the sign-in", anonymous.get(route).status_code == 401)

for row in made:
    client.delete(f"{route}/{row['postId']}")
print("\nFAILED: " + ", ".join(failures) if failures else "\nall passed")
sys.exit(1 if failures else 0)
