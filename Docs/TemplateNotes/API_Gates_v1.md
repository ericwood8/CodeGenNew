# API_Gates_v1

`Apis/Gates.cs`: the checks of the project's `ChangeGates` and `GuardedColumns`, one static class per table (`SalesInvoiceGates`, `CustomerGates`), called from that table's API before a row is saved. Whole-database, written only when the project names a rule (the plan adds it). Needs `ApiAuth=true`; `GuardedColumns` also needs `UserTable`.

```
ChangeGates=SalesInvoice.InvoiceStatusId; to 2,3,4,5,6; when Customer.CustomerStatusId=2; unless Customer.IgnoreBadStatus=1; start 1; message "Customer is in Bad Standing; a manager must set Ignore Bad Status on the customer first."
GuardedColumns=Customer.IgnoreBadStatus; by Employee.IsManager; stampBy IgnoreBadStatusBy; stampOn IgnoreBadStatusOn
```

Several rules of one setting are separated by `|`; the parts of a rule by `;`. Both are read outside double or single quotes, so a message may hold them.

## A gate: a change that a rule may refuse

- `Table.Column` is the column that is watched. `to` says the new values that are held: a list (`2,3,4,5,6`) or a comparison (`> 1`, `<> 1`). `when` is the condition that holds the change and `unless` the exemption. Both compare a column of the row (`IsPrinted=0`) or of a parent one foreign key away (`Customer.CustomerStatusId=2`) with `=`, `<>`, `<`, `>`, `<=`, `>=` or `in`, joined by `and` and `or` (`and` binds tighter). It is a small checked language, not SQL: a table, column or foreign key that does not exist, a value the column cannot hold or two foreign keys to the same parent stop the run with the reason.
- The gate is checked **at the change**: the API reads the stored row, and a PUT that carries the whole row cannot slip a changed value past it. A new customer status set after the invoice was saved holds the next move. An edit that leaves the column as it is (the notes, the dates) goes ahead, and so does a save at the status the row already has. Reads are never gated.
- A refused change is a 409 problem of type `change-not-allowed` whose `detail` is `message`, or by default: `Invoice Status cannot change to 2, 3, 4, 5, 6 while Customer Status = 2 unless Customer Ignore Bad Status = true.` The default is accurate but plain; a `message` in the project's own words reads better where the values are ids.
- `start` says what a new row begins as: a create that asks for another value is a 400, a clone starts there too. Without `start` a create is checked like a change from nothing.
- Nobody is exempt by role: a manager is blocked like anyone else until the exemption (here the customer's flag) is set, so every exception is a stamped change on a row.

## A guard: a column only some people may change

- `by Employee.IsManager` names a yes/no column of the user table; the caller is found there by the token's name (`UserNameColumn`). An administrator may always change a guarded column.
- Anyone else may save the row while the guarded value is unchanged (editing a customer's address works). A changed value, or a non-default value on a create, is a 403 problem of type `column-not-yours`.
- `stampBy` and `stampOn` name columns the server fills with the caller's name and the time when the value changes. A stamp sent in a body is replaced by the stored one, so it cannot be forged; a row from before the stamp columns existed keeps them empty and the screens say "not recorded".
- `GET <route>/guards` answers `{ "ignoreBadStatus": true }`: which guarded columns the caller may change. It needs a token like every route.

## Where else a row is made

- **Clone** (`POST <route>/{id}/clone`): `CrudApi` calls `AfterCloneAsync` after the copy is made. The copy starts as a new row does (the gated column at its `start`; the guarded column reset, or stamped with the caller when the caller may change it), and a gate that holds a new row removes the copy and answers the problem.
- **CSV import** (`ApiCsv=true`): an import saves rows without the API's checks, so the import into a table that has a rule is for the administrator role only (the export stays open).
- **A hand-written route** that saves rows itself is outside the gate: call `<Table>Gates.CheckAsync` from it.

## What the generator does with it

- `GatePlan` (Core) reads and checks the settings once; `GateCode` writes the conditions as C#; `ProblemTypes.ChangeNotAllowed` and `ColumnNotYours` name the answers. `API_Crud` writes `BeforeSaveAsync` (and `AfterCloneAsync` for a table that clones) on a table with a rule; a rule on the user table itself is refused (its own rules are in `API_Crud_v1.md`).
- `API_Registration` maps `GatesApi.Register(app)` when a guard exists.
- **Angular** (the front end with a sign-in): the table's service gets `getGuards()`; the screen asks once when it opens, shows a guarded field as the control for a person who may change it and as text with the sentence for everyone else (`Only a manager can change this.`, from the name of the `by` column), and shows the stamp columns as text (`not recorded` when empty). A refusal is shown in the API's own words (a toast or an alert). Not colour alone.
- **React, Blazor, WinUI3:** none of them signs in (the API refuses them without a token, and WinUI3 reads the database directly), so a gate or guard does not reach them; the guarded and stamp columns show as ordinary fields there.
- **Python and Rust APIs** do not read these settings: they have their own sign-in and the same two problem types, but no gate. Do not put a rule on a project that generates them unless the .NET API is the one in use.

## Checked

`GatedChangesTests` (the language, the refusals, the C#, the screens) and, live over HTTP with real sign-ins of three people on a scratch copy of InvoiceSystem, `Docs/Verification/Test-GatedChanges.py` (33 checks: who may change the column, the move refused and allowed, the whole-row PUT, the stamp, a manager blocked until the flag is set, an administrator clearing it, a customer put into Bad Standing after the invoice was saved, clone, CSV import). Against an API generated without the rules the same script fails 20 of 33. The Angular Edit screen was opened as a non-manager (text, sentence, "not recorded") and as a manager (a checkbox, saved and stamped), and a refused status change showed the API's words.
