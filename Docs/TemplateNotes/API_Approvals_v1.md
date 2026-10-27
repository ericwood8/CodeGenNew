# API_Approvals_v1

`Apis/Approvals.cs`: the approval of single rows, for the tables of `RowApprovals=TimeSheet,Expense`. Whole-database, written only when the project names a table. Needs `ApiAuth=true`, `UserTable` and either `UserRoleColumn` as a yes/no column or an `ApproverTable`. The ASP.NET API, the same routes in the Python and Rust APIs (below), and an approvals page in every front end.

## The side table

Each table needs a `<Table>Approval` table, one row per request cycle. `SP_ApprovalTable` writes its create script (`Sql/ApprovalTables.sql`, in the dialect of the source database) for every table that lacks one. Review it, run it, generate again: until the table exists the run stops with that sentence. The columns are fixed: `<Table>ApprovalId` (whole-number key), `<Table>Id`, `ApprovalStatusId` (1 Pending, 2 Approved, 3 Rejected, 4 Withdrawn), `Reason`, `RequestedBy`, `RequestedOn`, `DecidedBy`, `DecidedOn`, `DecisionNote`, `Revision`. The side table gets no CRUD API of its own, since that would let a caller skip the four eyes.

## Routes (all need a token)

- `POST <route>/{id}/request` with `{ "reason": "..." }`: a new cycle, `Revision` one more than the last. 409 while a request is already waiting. The requester must be a person of the user table (403 `approval-requester-unknown` otherwise).
- `POST <route>/{id}/approve` and `reject` with `{ "note": "..." }`: only an administrator, a person whose role flag is set or, with `ApproverTable`, a person it names for the table (403 `approval-not-approver`), never the person who made the request (403 `approval-four-eyes`). The decision is one guarded UPDATE of the Pending row inside a transaction; a second decision of the same request is a 409 `approval-wrong-state`.
- `POST <route>/{id}/withdraw`: only the requester, only while Pending.
- `GET <route>/toapprove`: the requests waiting for a decision, without the caller's own; approvers only.
- `GET <route>/{id}/approvals`: the history of a row, newest first.

## Who may approve

By default an approver is an administrator or a person whose user-table role flag is set, the same for every table. `ApproverTable=Approver:EmployeeId,TableName` moves that into rows: the table, the column that holds the person (a whole-number column with the user table's key), and optionally a text column naming the `RowApprovals` table the person may decide (empty there: every table). An administrator always may. With the setting the role flag no longer decides, so `UserRoleColumn` may be left out. `ApprovalPlan.Resolve` checks the table and its columns, and refuses a run in which anyone signed in could change the approver table, since that would let a person make themselves an approver: name it in `TableWriteRoles` (`Approver:Administrator`). The approver table is an ordinary table with the usual list and edit screens in every front end.

The one lookup is `ApprovalWriter.MayDecideMethod` (C#, used by the API and the WinUI3 rules); the Python and Rust APIs write the same query for their own database layer. A refused person gets the same 403 `approval-not-approver` and sentence on `approve`, `reject` and `toapprove`. No front end decides from the role flag: each page shows the Approve and Reject buttons only on the requests the API lists, and shows the API's sentence when `toapprove` is refused, so the pages need no change.

## The project's own rules

`ApprovalHooks.Validate` runs before every action and must have no side effects: a sentence refuses it (409 `approval-refused`). `ApprovalHooks.OnDecided` runs after an approval or rejection is written and before it commits, with the same context, so what it changes is saved in the decision's transaction. Set them at start-up.

## The gate

A `ChangeGates` rule may end with the clause `approval`: the change that the rule holds is allowed once the row's latest request is Approved. A new request starts a new cycle, so a row that must be approved again asks again. A row edited after its approval is not asked again on its own.

## The screens

Every front end gets one **Approvals** page and a menu entry, written when the project has `RowApprovals` and the side tables exist: `TSX_Approvals` (React), `TS_Approvals` (Angular), `BLZ_Approvals` (Blazor), `UNO_Approvals` and `WinUI3_Approvals`. For each table it shows the requests waiting for the signed-in approver with a note and Approve / Reject, then a row's request, withdrawal and history by the row's number. A refusal shows the API's own sentence; a person who may not decide is told so. The words are `ApprovalWords` in Core, so all five say the same.

- **Uno** reads the API (`ApprovalsClient`). **WinUI3** reads the database itself, so `ApprovalService` runs the same rules in process, in the same words; the person is `DesktopCaller.Name` (the Windows sign-in unless the project sets it), and, like the gates, this is a check in the client, not enforcement. The two desktop apps draw the page from one text (`DesktopApprovalsCode.Page`).
- A gate with the clause `approval` works in WinUI3 too (`WinUI3_Gates`).
- Tests: Vitest (React), the Angular spec, bUnit with `BlazorTests=true`, `ApprovalsClientTests` (Uno) and `ApprovalServiceTests` (WinUI3, over SQLite) with `DesktopTests=true`.

### The panel on a row's edit form

The form of a saved row of a `RowApprovals` table carries an **Approval** panel under its fields, so a person requests and withdraws from the row they are editing instead of finding its number on the Approvals page. It shows the latest request of the row in one line (`Approval status: Pending, requested by ann`; once decided, who decided and the note), a Reason box and Request approval while no request waits, and Withdraw while one does. A refusal shows the API's own sentence. Approve and Reject stay on the Approvals page, which lists what waits for the signed-in approver.

- One small component per stack, written by that stack's approvals template: `ApprovalPanel.tsx` (React), `approval-panel.component` (Angular), `Components/ApprovalPanel.razor` (Blazor), and `ApprovalPanel` in `Views/ApprovalsPage.cs` (Uno and WinUI3, from the text `DesktopApprovalsCode.Page`). The page templates only place it: `TSX_Page`, `TSX_DetailMasterPage`, `TS_Component`, `TS_DetailMasterComponent`, `BLZ_Page`, and the detail and master-detail dialogs of both desktop apps. The OData variants share those templates.
- A page template sees one table, so it decides from the settings (`ApprovalScreens.PanelSegment`: `ApiAuth`, `UserTable`, the table in `RowApprovals`, a whole-number key). A table without approvals gets no panel, and a new row has none until it is saved.
- The sentence of the status line is `ApprovalWords.PanelStatus`; the web stacks write its TypeScript twin and the C# stacks its expression from Core (`PanelStatusTypeScript`, `PanelStatusCSharp`).
- WinUI3 shares one context between the dialog and the panel, so the panel's calls take turns with the dialog's (`ViewModelBase.InTurnAsync`), and the dialog's `ShowAndWaitAsync` waits for the panel's last call before the caller reuses the context.
- Not covered: the single-row desktop screen (`WinUI3_SingleRowScreen`, `UNO_SingleRowScreen`) has no panel; the panel's own desktop controls are compiled but not driven by a generated test.

## Python and Rust

`PY_Approvals` writes `app/routes/approvals.py` and `RS_Approvals` writes `src/routes/approvals.rs`: the same routes, status codes, problem types and sentences as the ASP.NET API (`ApprovalWords` and `ApprovalWriter` in Core). Both read the user table for the requester and the approver, decide with one guarded UPDATE of the Pending row, and answer the same JSON (`id`, `rowId`, `revision`, `status`, `reason`, `requestedBy`, `requestedOn`, `decidedBy`, `decidedOn`, `decisionNote`).

- **Python:** `PY_Approvals` also writes the SQLAlchemy model of each side table (`app/models/<table>Approval.py`, the text `PY_Model` writes for any table, from `PythonModelFile`), since the side table has no CRUD API and so no model of its own. `PY_Mod` lists the approvals router before the tables' routers (their `/{id}` would read `toapprove` as a key). The hooks are `hooks.validate(event)` (no side effects; a sentence refuses) and `hooks.on_decided(event)`; the event carries the session, so what `on_decided` changes is committed with the decision.
- **Rust:** axum prefers the fixed path `toapprove` to a table's `/{id}`. The side table has no model of its own (it has no CRUD API), so `approvals.rs` reads it into a struct written there. The hooks are `HOOKS.write().unwrap().validate = Some(f)` (a sync function returning a fixed sentence) and `on_decided` (an async function that receives the transaction, so its changes commit with the decision). The Rust stack reads PostgreSQL, MySQL and SQLite only.
- **The gate clause `approval`:** `PY_Gates` and `RS_Gates` check it the same way (`row_approved_<table>`).
- **Tests:** `PythonTests=true` adds `PY_TestApprovals` (`tests/conftest.py`, `tests/test_approvals.py`, run with `pytest` from the API's folder) and `RustTests=true` adds `RS_TestApprovals` (`tests/api/approval_routes.rs`, run with `cargo test`). Both open the application over a scratch database (a copy of the SQLite file; `<PROJECT>_TEST_CONNECTION` for PostgreSQL and MySQL; Python skips SQL Server), add the people of the user table, the approver rows and a row of the first `RowApprovals` table with plain SQL before each test, and remove them after it. `ApprovalTestSeed` in Core writes those statements from the schema, with fixed keys far above the usual ones; when a required column has no example value (a binary column, a key that is not a whole number) no test file is written. The cases are the ASP.NET and WinUI3 ones in the same words (a stranger, a missing row, a second request, a person who may not decide, the waiting list, withdrawal by the requester only, the four eyes, a decision written once, the history), plus, with `ApproverTable` naming a table column, a person approved for another table only. Python also tests the two hooks; Rust does not, since the crate keeps its routes module private. Rust needs the `Client.start_signed_out`, `as_person` and `pool` of `test_support.rs`: an older copy is refreshed with `essentials --replace`.

## Not built

A panel on each table's edit form (request and withdraw next to the row, its status in the grid): the page asks for the row by its number. More than one level, delegation, time limits and notification; an alternative that uses the table's own status column with a move table.

## Checked

`RowApprovalsTests` (the plan, the script, the generated routes, the gate clause) and, over a scratch SQLite copy, the generated routes called with real principals: a stranger refused, a second request refused while waiting, a non-approver refused, a requester who is also an approver refused (four eyes), withdraw, decide twice, the history, the gate holding until the latest request is approved, and both hooks.
