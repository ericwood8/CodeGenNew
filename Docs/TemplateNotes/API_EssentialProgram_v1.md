# API_EssentialProgram_v1

The `Program` group of the API essentials: the project file, `Program.cs`, `appsettings.json`, `GlobalUsings.cs` and, by setting, `ProductionProfile.cs`, `Telemetry.cs`, `OpenApiDocs.cs`, `ValidationFilter.cs`, the Dockerfile and `Problems.cs`.

## Problems.cs

Every project answers errors as problem details (RFC 9457), so the Angular, React and Blazor screens can show the `detail`.

- `AddApiProblems()` and `UseApiProblems()` (called from `Program.cs`) register `AddProblemDetails`, the exception handler and the status-code pages.
- `ApiProblems.Describe(DbUpdateException)` reads the provider's own error number and says what the database refused, naming the table in words (`sales invoice line`): a duplicate value is **409** "Duplicate value"; removing a row that other rows use is **409** "In use"; saving a row that names a parent that is not there is **400** "Related row missing"; a value too long is **400** "Value too long"; a missing required value is **400** "Value required"; anything else the database refuses is **500** "The database refused the change" without its text.
- The numbers are the project's database only (`ConstraintErrors` in Core: SQL Server error numbers, PostgreSQL SQLSTATE, MySQL error numbers, SQLite extended result codes) and are named constants in the generated file.
- `ApiExceptionHandler` also answers a stale write (`DbUpdateConcurrencyException`) as **409**, a body ASP.NET could not read as **400** (Development throws instead of answering), and any other exception as **500** "Something went wrong" with a fixed sentence. In Development only, the answer carries the message of the inner exception (`cause`) and the stack trace (`trace`); the log always has the exception.
- `CrudApi` uses it for the child rows of an editable list (`ChildRowsSupport.SaveAsync`), and its bare 400s (an id that disagrees with the body, a delete of a row in use) now carry a `detail`. A delete of a row in use stays **400**: every client reads that status as "in use".
- Each kind of problem has its own `type`, a relative path such as `/problems/value-too-long` (the names are `ProblemTypes` in Core). The status says how serious it is, the `type` says which failure it is, so a caller can tell "too long" from "required" while all three stay 400; the `title` and `detail` stay for people. Every `Results.Problem` in the CRUD, search and sign-in templates names one, and a test fails if one does not. "In use" shares one type whether the database (409) or the delete check (400) found it. The path is an identifier, not a page.
- `ProductionProfile.cs` no longer registers problem details; this file does, with or without `ApiProduction`.
- **Checked:** a scratch API over SQLite, PostgreSQL and SQL Server (MySQL compiled, not run) answered 409 for a duplicate, 400 for a too-long value, a missing value and a missing parent, 400 for an unreadable body, and a 500 whose body held neither the message nor the stack outside Development.
