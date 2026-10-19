# PY_Gates_v1

`app/gates.py` for a project with `ApiAuth=true` and a `ChangeGates` or `GuardedColumns` rule: the checks `API_Gates` writes for the ASP.NET API, for the Python API. The rules and their parser are `GatePlan` in Core, the same one `API_Gates` reads, so a rule that does not fit the schema stops the run with the same sentence; `GateCode` writes the conditions in Python.

Per gated table the module has:

- `check_<table>(session, user, row, current)`: called by `PY_Routes` on create (`current` is `None`) and on update (`current` is the stored row), after the validation. A **guard** lets only an administrator or a person whose user-table row has the `by` column set change the column (403 `/problems/column-not-yours`) and, with `stampBy` and `stampOn`, records who did it and when; when the column did not change the stamps keep their stored values. A **gate** refuses a column that moves to one of the `to` values while `when` holds unless `unless` holds (409 `/problems/change-not-allowed`, the rule's `message` or a generated sentence); a new row must start at `start` when the rule gives one (400 in the validation-problem form). A condition can read a column of the row or of a parent one foreign key away (read with a `select` of its own).
- `may_<table>_<column>(session, user)`: the guard's test, also used by the guards route.
- `after_clone_<table>(session, user, copy)`: called by the clone route; the copy starts as a new row does (the gated columns at their `start`, a guarded column reset unless the caller may change it, the stamps set or cleared) and a gate that holds a new row removes the copy.
- `GET <route>/guards` is written by `PY_Routes` for a table with a guarded column: `{ "<column>": true | false }` for the caller, so a screen shows the others as read-only.

The user table cannot carry a gate or a guard (as in `GatePlan`). The plan adds the template for a project that names a rule; `InPlan=false` otherwise.

Checked on a SQLite scratch project (the InvoiceSystem case: a customer in bad standing blocks an invoice's status move unless a manager set the exemption): 20 HTTP checks with three real sign-ins, all passed; see the proof in `spec_completed.md`.

The decisions are not made here: `GatePlan.Tables` lists each gated table with its checks (guards, then gates), the holder tables and the sentences, so `API_Gates`, `PY_Gates` and `RS_Gates` write only syntax and a change of wording is one change in Core.
