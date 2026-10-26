# API_Approvals_v1

`Apis/Approvals.cs`: the approval of single rows, for the tables of `RowApprovals=TimeSheet,Expense`. Whole-database, written only when the project names a table. Needs `ApiAuth=true`, `UserTable` and `UserRoleColumn` as a yes/no column. The ASP.NET API, the same routes in the Python and Rust APIs (below), and an approvals page in every front end.

## The side table

Each table needs a `<Table>Approval` table, one row per request cycle. `SP_ApprovalTable` writes its create script (`Sql/ApprovalTables.sql`, in the dialect of the source database) for every table that lacks one. Review it, run it, generate again: until the table exists the run stops with that sentence. The columns are fixed: `<Table>ApprovalId` (whole-number key), `<Table>Id`, `ApprovalStatusId` (1 Pending, 2 Approved, 3 Rejected, 4 Withdrawn), `Reason`, `RequestedBy`, `RequestedOn`, `DecidedBy`, `DecidedOn`, `DecisionNote`, `Revision`. The side table gets no CRUD API of its own, since that would let a caller skip the four eyes.

## Routes (all need a token)

- `POST <route>/{id}/request` with `{ "reason": "..." }`: a new cycle, `Revision` one more than the last. 409 while a request is already waiting. The requester must be a person of the user table (403 `approval-requester-unknown` otherwise).
- `POST <route>/{id}/approve` and `reject` with `{ "note": "..." }`: only an administrator or a person whose role flag is set (403 `approval-not-approver`), never the person who made the request (403 `approval-four-eyes`). The decision is one guarded UPDATE of the Pending row inside a transaction; a second decision of the same request is a 409 `approval-wrong-state`.
- `POST <route>/{id}/withdraw`: only the requester, only while Pending.
- `GET <route>/toapprove`: the requests waiting for a decision, without the caller's own; approvers only.
- `GET <route>/{id}/approvals`: the history of a row, newest first.

## The project's own rules

`ApprovalHooks.Validate` runs before every action and must have no side effects: a sentence refuses it (409 `approval-refused`). `ApprovalHooks.OnDecided` runs after an approval or rejection is written and before it commits, with the same context, so what it changes is saved in the decision's transaction. Set them at start-up.

## The gate

A `ChangeGates` rule may end with the clause `approval`: the change that the rule holds is allowed once the row's latest request is Approved. A new request starts a new cycle, so a row that must be approved again asks again. A row edited after its approval is not asked again on its own.

## The screens

Every front end gets one **Approvals** page and a menu entry, written when the project has `RowApprovals` and the side tables exist: `TSX_Approvals` (React), `TS_Approvals` (Angular), `BLZ_Approvals` (Blazor), `UNO_Approvals` and `WinUI3_Approvals`. For each table it shows the requests waiting for the signed-in approver with a note and Approve / Reject, then a row's request, withdrawal and history by the row's number. A refusal shows the API's own sentence; a person who may not decide is told so. The words are `ApprovalWords` in Core, so all five say the same.

- **Uno** reads the API (`ApprovalsClient`). **WinUI3** reads the database itself, so `ApprovalService` runs the same rules in process, in the same words; the person is `DesktopCaller.Name` (the Windows sign-in unless the project sets it), and, like the gates, this is a check in the client, not enforcement. The two desktop apps draw the page from one text (`DesktopApprovalsCode.Page`).
- A gate with the clause `approval` works in WinUI3 too (`WinUI3_Gates`).
- Tests: Vitest (React), the Angular spec, bUnit with `BlazorTests=true`, `ApprovalsClientTests` (Uno) and `ApprovalServiceTests` (WinUI3, over SQLite) with `DesktopTests=true`.

## Python and Rust

`PY_Approvals` writes `app/routes/approvals.py` and `RS_Approvals` writes `src/routes/approvals.rs`: the same routes, status codes, problem types and sentences as the ASP.NET API (`ApprovalWords` and `ApprovalWriter` in Core). Both read the user table for the requester and the approver, decide with one guarded UPDATE of the Pending row, and answer the same JSON (`id`, `rowId`, `revision`, `status`, `reason`, `requestedBy`, `requestedOn`, `decidedBy`, `decidedOn`, `decisionNote`).

- **Python:** `PY_Mod` lists the approvals router before the tables' routers (their `/{id}` would read `toapprove` as a key). The hooks are `hooks.validate(event)` (no side effects; a sentence refuses) and `hooks.on_decided(event)`; the event carries the session, so what `on_decided` changes is committed with the decision.
- **Rust:** axum prefers the fixed path `toapprove` to a table's `/{id}`. The side table has no model of its own (it has no CRUD API), so `approvals.rs` reads it into a struct written there. The hooks are `HOOKS.write().unwrap().validate = Some(f)` (a sync function returning a fixed sentence) and `on_decided` (an async function that receives the transaction, so its changes commit with the decision). The Rust stack reads PostgreSQL, MySQL and SQLite only.
- **The gate clause `approval`:** `PY_Gates` and `RS_Gates` check it the same way (`row_approved_<table>`).
- The Python and Rust APIs have no generated tests for approvals yet; the routes were checked with a scratch harness over SQLite (a stranger, a second request, a non-approver, the four eyes, withdrawal, deciding twice, the history, the gate holding until the latest request is approved, and the hooks), the same cases as the ASP.NET API.

## Not built

A panel on each table's edit form (request and withdraw next to the row, its status in the grid): the page asks for the row by its number. More than one level, delegation, time limits and notification; an alternative that uses the table's own status column with a move table; generated tests for the Python and Rust routes.

## Checked

`RowApprovalsTests` (the plan, the script, the generated routes, the gate clause) and, over a scratch SQLite copy, the generated routes called with real principals: a stranger refused, a second request refused while waiting, a non-approver refused, a requester who is also an approver refused (four eyes), withdraw, decide twice, the history, the gate holding until the latest request is approved, and both hooks.
