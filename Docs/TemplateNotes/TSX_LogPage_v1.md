# TSX_LogPage_v1 and the LogTables setting

`LogTables=ErrorLog,UserSession:StartedOn` names the insert-only tables the application only reads: an error log, a session list, a data-change ledger. A table and, after a colon, the column it is ordered by (without one the plan picks the date-and-time column whose name says when something happened: `LoggedOn`, `OccurredOn`, `CreatedOn`, ...). The rule is `LogTablePlan` in Core; every layer asks it, so they agree.

A log table is never guessed from its shape. A table that looks like one (a date and time column, nothing that edits it, a log-like name or a level and a message) is only mentioned in the run notes: "list it in LogTables".

## What a log table gets

- **API (ASP.NET).** `API_Crud` writes the class with `ReadOnly => true`: `CrudApi` then registers no create, update, delete or clone route (a POST, PUT or DELETE answers 405), and `NewestFirst` is the timestamp. The search takes `from` and `to` (the first and the last day, both included) on the timestamp and lists newest first when no sort is chosen (`SearchPlan.Range`, `DefaultDescending`; `CS_SearchQuery` and `API_Search` write them, `CS_Repo` passes them to WinUI3). A date that is not a date is a 400. The CSV export takes the same two parameters, lists newest first and stops at the first 10000 rows; there is no import. The `.http` file has no write request, and `API_Test` writes a read-only class (`ApiLogTests`: no route changes a row, the range, a bad date, the sorts, the export).
- **`SP_LogIndex`** writes `Sql/LogIndexes.sql`: an index on the timestamp (descending where the database has it) unless the table already has an index that starts with it. Review it and run it yourself; the generator never applies it.
- **React.** `TSX_LogPage` writes the page (`pages/<Table>Page.tsx`, the name `TSX_Page` would give it, so the menu needs no change) and its test; `TSX_Page` writes nothing for the table. It opens on a filter row (From, To, a box per text column, a drop-down per parent), pages newest first, sorts by a header and chooses columns. No Add New, Edit, Clone, Delete or form.
- **Angular, Blazor.** `TS_Component` and `BLZ_Page` write the same screen as for any table with the Add New button, the Actions column and the form left out, the date boxes added and a spec or tests of their own (`AngularLogSpec`, `BlazorLogTests`).
- **Uno, WinUI3.** `UNO_MasterScreen` and `WinUI3_MasterScreen` leave out Add New and the Actions column and add two boxes, From and To (`yyyy-MM-dd`; WinUI3 ignores text that is not a day, Uno sends it and the API refuses it). The detail dialogs are still written for the table but nothing opens them.
- A log table is a plain list, never a master-detail screen (`DatabaseModel.IsDetailMaster`), and the timestamp column shows its time.

## Refused with a reason

A listed table that has no API, a project with `AccessMode=Routines` (the date range is part of the EF search), `ListProtocol=Search` (one box), a table served as OData (its routes also write rows) or a table with no date-and-time column.

## Not covered

The Python and Rust APIs still write the write routes and take no date range for such a table; OData is refused; there is no read-only view of one row (the message of a long row is cut in the grid, the Columns list shows more); the API essentials file `Apis/CrudApi.cs` is written once, so an older project refreshes it with the replace option before it regenerates.

Checked with `Docs/Verification/Tools/Schemas/log_table.sql`: the API over a scratch SQLite copy (newest first, the range, 405, the export), React, Angular and Blazor in a browser and WinUI3 with UI Automation (a range of two days over 60 rows lists 7), and the generated tests of each.
