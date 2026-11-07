"""Plays the inbound event landing tables against a running API (ASP.NET, Python or Rust) over a scratch copy of Schemas/inbound.sql.

python inbound_check.py <base url> [--auth --login <user>:<password>] [--client-limit <requests a minute>] [--own <source>:<VARIABLE>]

The API is a generated one with InboundTables=WebhookEvent,ErrorReport:Payload, started with the environment variable INBOUND_WEBHOOK_SECRET set to the same value this script has in its own
environment (default test-secret-123). It checks, for WebhookEvent (an event id, a source, a processed time) and ErrorReport (a processed flag, no event id): a message with no signature, a wrong one and an
old one are refused and store nothing; a body over the limit is a 413; a signed message is kept as it came and listed as unprocessed; marking it processed takes it out of the list, marking it again is harmless and
an unknown id is a 404; an event id is kept once (in the body and in the header); and with --auth (the API has ApiAuth) the worker routes are a 401 without a token and the script signs in with --login (the bootstrap administrator of the API) to play the rest.
With --client-limit N (the API has InboundClientTables=WebhookEvent,ErrorReport and InboundClientPerMinute=N) it also plays the write-only collect route of each table: a message with no signature is a 202 with no body and is listed,
an empty body is a 400, a body over the limit is a 413, and a caller that goes past N requests in a minute gets a 429 with Retry-After.
With --own stripe:STRIPE_SECRET (the API has InboundSourceSecrets=stripe:STRIPE_SECRET and both variables set; this script reads the sender's secret from its own environment variable of that name) it also plays a sender with a secret
of its own: the shared secret is a 401 for it, its own secret is kept (also when the route spells the sender in capitals, which is stored as configured), and another sender still signs with the shared secret."""
import hashlib
import hmac
import json
import os
import sys
import time
import uuid

import httpx

base = sys.argv[1]
auth = "--auth" in sys.argv
client_limit = int(sys.argv[sys.argv.index("--client-limit") + 1]) if "--client-limit" in sys.argv else 0
own_source, own_variable = sys.argv[sys.argv.index("--own") + 1].split(":", 1) if "--own" in sys.argv else (None, None)
shared_source = "stripe-x" if own_source and own_source.lower() == "stripe" else "stripe"
secret = os.environ.get("INBOUND_WEBHOOK_SECRET", "test-secret-123")
client = httpx.Client(base_url=base, timeout=30)
failures = []


def check(label, ok, detail=""):
    print(("ok   " if ok else "FAIL ") + label + ("" if ok else "  " + str(detail)[:300]))
    if not ok:
        failures.append(label)


def signed(path, text, key=None, at=None, event=None):
    stamp = int(time.time()) if at is None else at
    digest = hmac.new((key or secret).encode(), f"{stamp}.".encode() + text.encode(), hashlib.sha256).hexdigest()
    headers = {"X-Signature": f"t={stamp},v1={digest}", "Content-Type": "application/json"}
    if event:
        headers["X-Event-Id"] = event
    return client.post(path, content=text.encode(), headers=headers)


def message(marker, event=None):
    return json.dumps({"id": event, "marker": marker})


def column(row, *names):
    for name in names:
        if name in row:
            return row[name]
    return None


def play(route, body_name, limit, has_event, has_source):
    marker = "inbound-" + uuid.uuid4().hex
    label = route.rsplit("/", 1)[-1]
    unsigned = client.post(route + "/receive", content=message(marker), headers={"Content-Type": "application/json"})
    check(f"{label}: no signature is a 401", unsigned.status_code == 401, unsigned.text)
    check(f"{label}: a wrong secret is a 401", signed(route + "/receive", message(marker), key="not-the-secret").status_code == 401)
    old = signed(route + "/receive", message(marker), at=int(time.time()) - 3600)
    check(f"{label}: an old message is a 400 replay", old.status_code == 400 and "inbound-expired" in old.text, old.text)
    big = signed(route + "/receive", "x" * (limit + 1))
    check(f"{label}: a body over the limit is a 413", big.status_code == 413 and "inbound-too-large" in big.text, big.text)

    path = route + (f"/receive/{shared_source}" if has_source else "/receive")
    text = message(marker)
    received = signed(path, text)
    check(f"{label}: a signed message is kept", received.status_code == 200 and received.json().get("received") is True, received.text)
    if auth:
        denied = client.get(route + "/unprocessed")
        check(f"{label}: the worker route needs a sign-in", denied.status_code == 401, denied.status_code)
        denied = client.post(f"{route}/1/processed")
        check(f"{label}: marking processed needs a sign-in", denied.status_code == 401, denied.status_code)
    pending = client.get(route + "/unprocessed", headers=worker_headers())
    check(f"{label}: the unprocessed list answers", pending.status_code == 200, pending.text)
    rows = [r for r in pending.json() if marker in (column(r, body_name) or "")] if pending.status_code == 200 else []
    check(f"{label}: the event is listed once, with its raw body", len(rows) == 1 and column(rows[0], body_name) == text, rows)
    if has_source and rows:
        check(f"{label}: the source is kept", column(rows[0], "source") == shared_source, rows[0])
    key_name = next((k for k in (rows[0] if rows else {}) if k.lower().endswith("id") and k.lower().startswith(label[:-1].lower())), None)
    event_id = received.json().get("id") if received.status_code == 200 else None
    check(f"{label}: the answer names the new id", event_id is not None and rows and event_id == rows[0].get(key_name))
    done = client.post(f"{route}/{event_id}/processed", headers=worker_headers())
    check(f"{label}: marking it processed", done.status_code == 200 and done.json().get("processed") is True, done.text)
    again = client.post(f"{route}/{event_id}/processed", headers=worker_headers())
    check(f"{label}: marking it again is harmless", again.status_code == 200 and again.json().get("alreadyProcessed") is True, again.text)
    gone = client.get(route + "/unprocessed", headers=worker_headers())
    check(f"{label}: a processed event is no longer listed", gone.status_code == 200 and not [r for r in gone.json() if marker in (column(r, body_name) or "")])
    missing = client.post(f"{route}/2147483647/processed", headers=worker_headers())
    check(f"{label}: an unknown id is a 404", missing.status_code == 404, missing.text)

    if has_event:
        event = "evt-" + uuid.uuid4().hex
        first = signed(route + "/receive", message(marker, event))
        second = signed(route + "/receive", message(marker, event))
        third = signed(route + "/receive", message(marker), event=event)
        check(f"{label}: an event id is kept once", first.json().get("duplicate") is False and second.json().get("duplicate") is True and second.json().get("id") == first.json().get("id"), (first.text, second.text))
        check(f"{label}: the id in the header counts the same", third.status_code == 200 and third.json().get("duplicate") is True, third.text)


def play_client(route, body_name, has_source):
    label = route.rsplit("/", 1)[-1]
    marker = "inbound-" + uuid.uuid4().hex
    collect = route + "/collect"
    sent = client.post(collect, content=message(marker).encode(), headers={"Content-Type": "application/json"})
    check(f"{label}: a client message with no signature is a 202 with no body", sent.status_code == 202 and sent.content == b"", (sent.status_code, sent.text))
    rows = [r for r in client.get(route + "/unprocessed", headers=worker_headers()).json() if marker in (column(r, body_name) or "")]
    check(f"{label}: the client message is listed", len(rows) == 1, rows)
    if has_source and rows:
        check(f"{label}: the client source is kept", column(rows[0], "source") == "client", rows[0])
    check(f"{label}: an empty client body is a 400", client.post(collect, content=b"").status_code == 400)
    big = client.post(collect, content=b"x" * 5000)
    check(f"{label}: a large client body is a 413", big.status_code == 413 and "inbound-too-large" in big.text, big.text)
    refused = None
    for _ in range(client_limit + 5):
        answer = client.post(collect, content=message(marker).encode())
        if answer.status_code == 429:
            refused = answer
            break
    check(f"{label}: a caller past the limit gets a 429 with Retry-After", refused is not None and "retry-after" in refused.headers and "inbound-rate-limited" in refused.text, refused.text if refused is not None else "no 429")


def play_own(route, body_name, has_source):
    label = route.rsplit("/", 1)[-1]
    marker = "inbound-" + uuid.uuid4().hex
    own_secret = os.environ.get(own_variable, "")
    check(f"{label}: the sender's own secret is set in this script's environment", bool(own_secret) and own_secret != secret)
    path = f"{route}/receive/{own_source.upper()}"
    shared = signed(path, message(marker))
    check(f"{label}: the shared secret is a 401 for a sender with its own", shared.status_code == 401 and "inbound-signature" in shared.text, shared.text)
    sent = signed(path, message(marker), key=own_secret)
    check(f"{label}: the sender's own secret is kept", sent.status_code == 200 and sent.json().get("received") is True, sent.text)
    other = signed(route + "/receive/someone-else", message(marker))
    check(f"{label}: another sender still signs with the shared secret", other.status_code == 200 and other.json().get("received") is True, other.text)
    rows = [r for r in client.get(route + "/unprocessed", headers=worker_headers()).json() if marker in (column(r, body_name) or "")]
    check(f"{label}: both messages are listed", len(rows) == 2, rows)
    if has_source and rows:
        check(f"{label}: the sender is stored as configured", sorted(column(r, "source") for r in rows) == sorted([own_source, "someone-else"]), [column(r, "source") for r in rows])


def worker_headers():
    return token_headers


token_headers = {}
if "--login" in sys.argv:
    user, password = sys.argv[sys.argv.index("--login") + 1].split(":", 1)
    response = client.post("/api/auth/login", json={"userName": user, "password": password})
    check("the worker signs in", response.status_code == 200, response.text)
    token_headers = {"Authorization": "Bearer " + response.json()["token"]} if response.status_code == 200 else {}


play("/api/webhookevents", "body", 4000, True, True)
play("/api/errorreports", "payload", 1048576, False, False)
if client_limit:
    play_client("/api/webhookevents", "body", True)
    play_client("/api/errorreports", "payload", False)
if own_source:
    play_own("/api/webhookevents", "body", True)
    play_own("/api/errorreports", "payload", False)
print("\n" + ("ALL OK" if not failures else f"{len(failures)} FAILED: " + "; ".join(failures)))
sys.exit(1 if failures else 0)
