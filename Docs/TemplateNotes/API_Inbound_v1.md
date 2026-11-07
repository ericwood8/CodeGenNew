# API_Inbound_v1 and the InboundTables setting

`InboundTables=WebhookEvent,ErrorReport:Payload:ExternalId` names the tables that land events from a sender that is not a person (a webhook, a system error report, a search query, a page view): a table and, after colons, its body column and its event id column (found by name when left out: `Body`, `Payload`, `RawBody`, `Content`, `Json` or `Message`, and `EventId`, `ExternalId`, `SenderEventId` or `MessageId`).

The table needs a single whole-number key, a text body column, a received-on stamp (a date and time column; one that starts with `Received` is preferred), and either a yes / no `Processed` (or `IsProcessed`) column or a `ProcessedOn` date and time that allows NULL. A `Source` (or `Sender`, `Provider`, `Origin`) text column is filled from the route. The other columns must allow NULL or have a default, since the route cannot fill them; the template says which column is in the way. The table is also a read-only list, like a log table (`LogTables`), so nobody edits an event by hand. Add the table to `ArchiveTables` to purge old rows by date.

## Routes

`API_Inbound` writes `Apis/Inbound.cs`, one static class per table (`WebhookEventInbound`), registered by `API_Registration` as `InboundApi.Register`.

| Route | What it does |
|---|---|
| `POST /api/webhookevents/receive` and `POST /api/webhookevents/receive/{source}` | Open to anyone: the signature is the check. `X-Signature` is `t=<unix seconds>,v1=<hex of the HMAC-SHA256 of "t.body">` with the shared secret held by the environment variable (or configuration key) `InboundSecret` names (`INBOUND_WEBHOOK_SECRET` by default). 503 when the secret is not set, 401 for a missing or wrong signature, 400 for a signed time more than `InboundToleranceSeconds` (300) from the server's (a replay), 413 for a body larger than `InboundMaxBytes` (1048576) or than the body column, 400 for an empty body. The raw body is kept as text with the received-on stamp; the answer is `{ received, duplicate, id }`. |
| `GET /api/webhookevents/unprocessed` | The 100 oldest unprocessed events, for a worker. With `ApiAuth` it needs a signed-in caller. |
| `GET /api/webhookevents/status` | The events that wait, for a person. With `ApiAuth` it needs a signed-in caller. `{ summary, total, unprocessed, oldestUnprocessedId, oldestUnprocessedOn (ASP.NET and Python), senders, collect: { enabled, perMinute, maxBytes }, partitionScript }`. `summary` is the sentence the list screens show (`Unprocessed: 3 of 10. Oldest unprocessed: #4. Senders with a secret of their own: stripe. Collect route: on, 30 a minute. Partition script: written.`). `senders`, `collect` and `partitionScript` are what the generator was told (the script is run by hand, so `partitionScript` says it was written, not that the table is partitioned). |
| `POST /api/webhookevents/{id}/processed` | Marks one event processed (one guarded UPDATE, so two workers never both succeed): `{ processed, alreadyProcessed, row }`, 404 when there is no such event. |

## A secret for each sender

`InboundSourceSecrets=stripe:STRIPE_WEBHOOK_SECRET,github:GITHUB_WEBHOOK_SECRET` gives a sender its own secret: a message posted to `receive/stripe` (in any letter case) is checked against the variable `STRIPE_WEBHOOK_SECRET` alone, so a leak of the shared secret, or of another sender's, cannot forge a Stripe event. The source is stored as it is spelled in the setting. A sender whose variable is not set gets the `503` for an unset secret, never the shared secret. The route with no source and a source that is not listed keep checking the shared secret (`InboundSecret`). The names are validated when generating: a source may hold letters, digits, `-` and `_`, a variable letters, digits and `_`, and a source is named once.

## The collect route for a browser

`InboundClientTables=PageView,SearchQuery` (tables that are also in `InboundTables`) gives a table `POST /api/<table>/collect`, for what a browser sends and cannot sign. It is open to anyone and write only:

- the body (any text; a page view or a search query is usually JSON) is kept as it came with the received-on stamp and, when the table has a source column, the source `client`; no event id is read, so nothing is applied once;
- the answer is `202` with no body (no id, no echo), `400` for an empty body, `413` for a body over `InboundClientMaxBytes` (default 4096) or the body column;
- one caller address may make `InboundClientPerMinute` requests (default 30) in a fixed minute for each table; the next gets `429` with `Retry-After` and the problem type `/problems/inbound-rate-limited`. A refused or oversized request counts too.

The limit is counted in the memory of the API process, so several instances multiply it, and the address is the one the server sees: behind a proxy turn forwarded headers on (the generated Dockerfile does) or every visitor shares the proxy's address. The routes do not add CORS; allow the site's origin in the API's CORS setting. The Rust API serves with the caller's address when a client table is named.

## The Processed button

The read-only list of an inbound table (React, Angular, Blazor, WinUI3 and Uno) shows a Processed button on a row whose processed flag is not true, or whose processed time is empty. It marks that event processed (the API's `POST {id}/processed`; the WinUI3 app runs the same guarded UPDATE on its database) and reads the page again. A row that is processed shows no button, and marking an event twice changes nothing.

The same sentence is shown above the grid of the list screens (React, Angular, Blazor and Uno read it from `status`; the WinUI3 app counts in its database). It is read again after Processed.

## Editing the events

An inbound table is a read-only list by default: events are what a sender sent, and editing one breaks a replay and the event-id check. `InboundEditTables=WebhookEvent` turns that off for a table that must be corrected by hand: it gets the ordinary CRUD screens and routes in every stack, keeps the inbound routes, and loses the Processed button (the processed column is an ordinary field then). A table that is not also in `InboundTables` is refused when generating.

## Event ids

When the table has an event id column the id comes from the `X-Event-Id` header, else from the `id` property of a JSON body. An event whose id (and source) is already in the table is not stored again: the answer is `{ received: false, duplicate: true, id }` with the id of the row that is there. Two requests at the same moment are settled by a unique index on the event id: the one that loses answers the same duplicate.

## Python and Rust

`PY_Inbound` writes `app/routes/inbound.py` and `RS_Inbound` writes `src/routes/inbound.rs`: the same four routes, sentences, problem types and status codes. The receive routes are in a `public_router` that `main.py` and `main.rs` include without the sign-in check; the worker routes are in the ordinary router (listed before the tables' own routers, since their `/{id}` would read `unprocessed` as a key). The shared secret is read from the environment at each request (503 when it is not set). With `PythonTests=true` and `RustTests=true`, `PY_TestInbound` (`tests/test_inbound_<table>.py`) and `RS_TestInbound` (`tests/api/inbound_<table>_routes.rs`) play the cases of the ASP.NET tests; they put the secret in the environment themselves. `Docs/Verification/Tools/inbound_check.py` plays the same cases over HTTP against a running ASP.NET, Python or Rust API.

## Access mode

The list and the status are read through the EF search, so an inbound table needs `AccessMode=Ef`: the generator refuses a routines project (the default for SQL Server) with the message that says so. Set `AccessMode=Ef` for SQL Server.

## Tests

With `ApiTests=true` the `API_TestInbound` template writes `<Table>InboundApiTests.cs`: no signature, a wrong one and an old one are refused and store nothing; a signed message is kept as it came, listed as unprocessed and marked processed (and again is harmless; an unknown id is a 404); an event id is kept once (in the body or in the header); a body over the limit is a 413; for a client table a message is kept with no signature and the answer holds nothing, an empty or large body is refused, and the caller past the limit gets a 429 with `Retry-After`; for a sender with its own secret the shared secret is refused and its own accepted (the test is inconclusive until that variable is set to a value other than the shared secret); with `ApiAuth` the worker routes answer 401 without a sign-in. The tests need the shared secret in the environment (or the configuration of the test host) and are inconclusive without it. Each test removes the events it wrote.

## Not covered

There is no worker that processes the events on its own: the owner's code reads `unprocessed` and marks each event processed (or lists the table in `QueueTables` for a claim, complete and fail cycle). The shared secret is one for every table; a sender that needs its own is named in `InboundSourceSecrets`, and a secret has no second value for a change over, so changing it means a moment when the sender and the server disagree. A big table can be split by month with `PartitionTables` (`Docs/TemplateNotes/SP_PartitionJob_v1.md`); the script turns the `(Source, EventId)` unique index into a plain one.
