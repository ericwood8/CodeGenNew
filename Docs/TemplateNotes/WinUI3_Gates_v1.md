# WinUI3_Gates_v1

`Repositories/Gates.cs`: the checks of the project's `ChangeGates` and `GuardedColumns` for the WinUI3 app, which reads the database itself and so never meets the API's checks. Whole-database, written only when the project names a rule (the plan adds it). The settings and their language are those of `API_Gates` (`API_Gates_v1.md`), including `ApiAuth=true` and `UserTable` for guards.

- One static class per table (`CustomerGates`), written by the same Core code as the API (`GateWriter`). The difference is the flavor: a refusal is a sentence instead of a problem result, and there is no caller parameter.
- `CheckAsync(context, row, isNew)` runs in the detail, master-detail and single-row dialogs before the row is saved. A refusal shows as the dialog's error and an edited row is read again so the refused value does not stay in the tracked row. It also sets the guard's stamp columns.
- `AfterCloneAsync` runs after the list page's Clone: the copy starts as a new row does, and a gate that holds it removes the copy.
- `May<Column>Async(context)` says whether the person may change a guarded column; the dialog loads it and the field sits in a `ContentControl` that is enabled only when it is true, with the sentence `Only a manager can change this.` under it otherwise. The stamp columns show as text.
- **Who the person is:** `DesktopCaller.Name`, which starts as the Windows sign-in name and is looked up in the user table's name column. An app with its own sign-in sets it. A person found there with the administrator column set, or the `by` column true, may change a guarded column.
- **Limits:** this is a check in the client. Anyone who can write to the database directly can bypass it; the API is where it is enforced. The junction editor and editable child rows save through their own path and are not gated.

Checked by `WinUI3GatesTests` and, with a generated test project over a scratch SQLite database, by running the view models: a non-manager refused, a manager stamped, an administrator allowed, a status change refused in bad standing and allowed once exempt, a new invoice that must start at 1, a clone resetting the guarded value. The generated WinUI3 app built for x64.
