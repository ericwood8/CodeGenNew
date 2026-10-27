# API_Queue_v1 and the QueueTables setting

`QueueTables=JobQueue,Mail:State:Attempts` names the tables that hold work for background workers: a table and, after colons, its status column and its retry column (default `Status` and `RetryCount`). `QueueMaxRetries` (default 3) and `QueueClaimMinutes` (default 15) are written into the routes. The rule is `QueuePlan` in Core.

A queue row is `pending`, `claimed`, `done` or `failed`. The table needs a text status column of at least seven characters, a whole-number retry column, a created stamp (the date-and-time column the log plan picks), `ClaimedOn` and `ProcessedOn` (date and time, both allowing NULL), and optionally `ClaimedBy` and `LastError` (text). A table that lacks one fails the run with the list of what it lacks.

## Routes

`API_Queue` writes `Apis/Queue.cs`, one static class per table (`JobQueueWork`), registered by `API_Registration` as `QueueApi.Register`. With `ApiAuth` every route needs a signed-in caller; the roles of `WriteRoles` do not apply (a worker is a caller, not a person at a screen).

| Route | What it does |
|---|---|
| `POST /api/jobqueues/claim?worker=name` | Takes the oldest pending row (by the created stamp, then the key) with one UPDATE that still tests `Status = pending`, so two workers never get the same row; 204 when there is none. A claim older than `QueueClaimMinutes` is released first. |
| `POST /api/jobqueues/{id}/renew` | The worker that holds the claim says it is still busy: `ClaimedOn` is set to now, so the claim is released only `QueueClaimMinutes` after the last renewal. 409 when the row is not claimed (a released or finished row has no claim to renew). |
| `POST /api/jobqueues/{id}/complete` | claimed to done, with the processed stamp. |
| `POST /api/jobqueues/{id}/fail` (body `{ "error": "..." }`) | One more retry. Back to pending until the retries reach `QueueMaxRetries`, then failed with the processed stamp. The error text is cut to the column's length. |
| `POST /api/jobqueues/{id}/retry` | failed to pending, retries back to 0. |
| `POST /api/jobqueues` (body: the row's own columns) | A producer puts a row in the queue (201 with the row). Status, retries, the created stamp, `ClaimedOn`, `ProcessedOn`, `ClaimedBy` and `LastError` are set by the route whatever the body holds, and a key the database numbers is left to it. 400 when the database refuses the row (a missing value, an unknown parent). |
| `GET /api/jobqueues/stats` | The count in each status and the oldest pending row (`oldestPendingId`, `oldestPendingOn`). |
| `GET /api/jobqueues/failed` | The last 200 failed rows. |

Each change is one `ExecuteUpdate` of the row in the status it expects. A row in another status answers 409 (`/problems/queue-wrong-state`), an absent row 404.

## The table itself

A queue table is also a read-only list (`LogTablePlan` includes it): no update, delete or clone route and no Add button (the enqueue `POST` is for a producer, not a person at a screen), newest first, `from` and `to` on the created stamp, and the screens of every stack without Add New, Edit, Clone, Delete or form. Rows are put in by the code that produces the work (the enqueue `POST`, or an insert of its own), not by a person. React, Angular and Blazor show the counts by status above the grid and a Retry button on a failed row; Uno and WinUI3 show the read-only list only.

## Python and Rust

`PY_Queue` (`app/routes/queue.py`) and `RS_Queue` (`src/routes/queue.rs`) write the same seven worker routes (no enqueue route: the table's own `POST` inserts a row, and the caller sets the pending status) with the same sentences (`QueueWords`), problem type and status codes; `PY_Mod` lists the router before the tables' routers and `RS_Mod` merges it. The Rust 409 is built in `queue.rs` itself (`ApiError` has no problem form without sign-in). With `PythonTests=true` and `RustTests=true`, `PY_TestQueue` (`tests/test_queue.py`, over the `conftest.py` the approval tests share; not for SQL Server) and `RS_TestQueue` (`tests/api/queue_routes.rs`) add three pending rows with fixed keys, the oldest of the table, before each test and play the cases of the ASP.NET tests. The tables' own routes of these two stacks still write rows: only the ASP.NET API makes a queue table read only.

## Generated tests and the http file

`API_Test` adds tests that put their own rows in the queue first (the enqueue `POST`, with the sample values of the other tests, so a parent row is found or made), then claim, renew, complete, fail and retry them, and settle a row they leave pending; nothing skips for want of a pending row. And the react, Angular and Blazor tests check the counts and the Retry button. `API_Http` adds the requests.

## Not covered

The Python and Rust APIs do not make the queue table read only (the ASP.NET API does), and have no enqueue route of their own. A worker that runs longer than `QueueClaimMinutes` must call `renew` in between, or it loses its row. A queue table whose other required columns the test values cannot fill (a parent with no list route) fails the enqueue test with the database's reason. Uno and WinUI3 have no counts or Retry button. `Apis/CrudApi.cs` is written once, so an older project refreshes it with the essentials replace option.

The retry count is the last assignment of the fail UPDATE in every stack, because MySQL reads a column that an earlier assignment of the same UPDATE already changed.

Checked with `Docs/Verification/Tools/Schemas/queue_archive.sql`: the ASP.NET, Python and Rust routes over a scratch SQLite copy and over a queue table on PostgreSQL and MySQL (the ASP.NET routes also on SQL Server), the generated Rust tests with `cargo test`, and on the SQLite copy (parallel claims over fewer pending rows than claims returned each row once and 204 for the rest; a stale claim went back to pending; failures up to `QueueMaxRetries` made a row failed; Retry made it pending), the React page in a browser, and the generated tests of the API, React, Angular and Blazor. The generated ASP.NET tests, including the enqueue and renew tests with no skipped test, ran against SQLite, SQL Server, PostgreSQL and MySQL; the Python tests (with the pytest stand-in) and the Rust tests (`cargo test`) with the renew case ran over the SQLite copy.
