# Contributing

Issues and pull requests are welcome. A change is easiest to review when it is small and says what it generates differently.

## Build and test

```
dotnet build CodeGenNew.slnx          # the desktop app needs Windows; the rest builds anywhere .NET 10 does
dotnet test CodeGenNew.Tests          # about a minute without a database
```

The live schema tests (SQL Server, PostgreSQL, MySQL) report themselves as *inconclusive* unless their environment variables are set: `CODEGENNEW_SQLSERVER_HOST` / `_DATABASE`, `CODEGENNEW_PG_HOST` / `_DATABASE` / `_USER` / `_PASSWORD`, `CODEGENNEW_MYSQL_HOST` / `_DATABASE` / `_USER` / `_PASSWORD`. They create and drop their own scratch tables; point them at a throwaway database. Never put a password in a file, a script or a commit: pass it through the environment.

## Changing a template

- A template is `Templates/<Group>_<Name>_v1.tt` with a `.tt.config` beside it. The group prefix (`SP`, `API`, `CS`, `TS`, `TSX`, `WinUI3`) is its menu and its default file extension; a new template is added to the list in `DefaultAssetSeeder`.
- Add a test that renders it (`CodeGenNew.Tests/TemplateRenderingTests.cs` shows the pattern), and run the whole suite: some tests count the templates per group and forbid a double hyphen anywhere in a template.
- If a sample project's output would change, generate it with `codegen generate ... --dry-run` first: a template change that touches files it should not is a bug.
- Template output uses `\n` line endings; the templates are LF in the repository.

## Style

- Match the surrounding code: naming, comment density, file-scoped namespaces, collection expressions.
- Comments say what the code does and why, not where an idea came from.
- No names of private projects, machines or people anywhere in a file (`LeftoverNamesTests` checks the common ones).

## Checking a change in a running app

`Docs/Verification` has the scripts: a scratch copy of the database, a UI Automation module for the WinUI3 app, an API create/read/update/delete walk and the recipes per stack.
