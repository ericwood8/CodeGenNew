# Verification harness

A build and a unit test cannot see a blank drop-down, a number box that takes a value the database refuses, or a dialog that never closes. This folder holds the tooling for checking a change in a **running** app, so a check recorded as done can be run again.

| File | What it does |
|---|---|
| `ScratchDatabase.ps1` | Makes or drops `<name>_scratch`, a throwaway copy of a database: SQL Server by backup and restore (Windows authentication), PostgreSQL by `CREATE DATABASE ... TEMPLATE`, MySQL by copying the tables. Passwords come from the environment (`PGPASSWORD`, `MYSQL_PWD`), never a parameter. The login needs the right to create a database (MySQL: a login limited to one database cannot). |
| `UiAutomation.psm1` | A small Windows UI Automation module for WinUI3 apps: `Start-App`, `Find-Name`, `Find-Like`, `Find-Type`, `Wait-Name`, `Invoke-El`, `Select-El`, `Set-Text`, `List-Names`, `Assert-That`, `Stop-App`. |
| `Test-AppDialogs.ps1` | A worked click-through of the CodeGenNew app (the Project settings dialog and the Essentials dialog): PASS / FAIL lines and an exit code. |
| `Test-ApiCrud.ps1` | Starts a generated API on a spare port against a scratch database and walks one route through POST, GET, PUT, list and DELETE. |
| `Test-OpenApiRoutes.ps1` | Checks the generated `openapi.yaml` against the running API: every documented GET answers 200, every `PUT /{id}` 400, and the schema's properties are in a real row's JSON. Apply the generated SQL to the scratch database first. |
| `Test-RustBuild.ps1` | The Rust stack: generates the crate of a PostgreSQL, MySQL or SQLite database with `codegen generate --essentials`, compiles it with `cargo build` (in a Visual Studio developer environment that has the C++ libraries) and runs `Test-ApiCrud.ps1` against the binary (`-Executable`). Needs a Rust toolchain. `Test-OpenApiRoutes.ps1` takes `-Executable` too. |
| `Test-SqliteStack.ps1` | The SQLite counterpart of the API check: generates the API of a SQLite file with `codegen generate`, builds it and runs `Test-ApiCrud.ps1` against a scratch copy of the file. No server is needed. |
| `Browser.md` | Snippets for driving and reading a React / Angular page from the browser's console. |
| `Recipes/` | The steps per stack: `Api.md`, `React.md`, `Angular.md`, `WinUI3.md`. |

## A typical run

```powershell
$env:PGPASSWORD = '<your password>'          # in your own shell only
.\ScratchDatabase.ps1 -Provider PostgreSql -Source MyDatabase -User dev_login
.\Test-ApiCrud.ps1 -ApiProject C:\MySample\My.Api -Route /api/customers `
    -ConnectionString 'Host=localhost;Port=5432;Database=MyDatabase_scratch;Username=dev_login' `
    -Body '{"accountNumber":"A-1","customerName":"Test","customerStatusId":1,"isTaxable":false}' -IdProperty customerId
.\Test-AppDialogs.ps1
.\ScratchDatabase.ps1 -Provider PostgreSql -Source MyDatabase -User dev_login -Drop
```

Every script stops what it started in a `finally` block and exits with 1 when a check fails, so they can be chained. Checks that need the app connected to a database (the Generate All dialog, the screens of a generated app) follow the same pattern: copy the database, start the app with the copy's connection, drive it with `UiAutomation.psm1`, drop the copy.
