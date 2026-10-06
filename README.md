# CodeGenNew

A C# code generator for a developer's own box: point it at a SQL Server, PostgreSQL, MySQL or SQLite database, pick a table, and generate code from **T4 templates** — SQL stored procedures, C# entities, enums, repositories and minimal-API classes, the Angular TypeScript model, service and screen for the same table, and a React counterpart of that same screen family. It has a WinUI 3 desktop app (right-click a table in a TreeView) and a scriptable command-line tool, `codegen`, that does the same without the GUI.

It replaces a series of hand-rolled "write lines to a text file with substitutions and smart loops" generators with a real templating engine (T4 via `Mono.TextTemplating`), while staying simple and portable (unpackaged, no installer) and easy to extend: **a new template is just a new `.tt` file** — no code changes.

**CodeGenNew only reads.** It introspects schema (and, for two templates, the table's rows) and writes files to an output folder. It never creates, alters or runs anything in your database and never edits your project — you review the output and copy it in.

See **[Docs/specs.md](Docs/specs.md)** for what the product promises and decides, and **[Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md)** for the code layout, how a run flows, the `.tt.config` keys and where a reviewer should look first.

## Status
<img width="126" height="20" alt="image" src="https://github.com/user-attachments/assets/3ea8d6d1-74b0-4b49-9821-5daf70302241" />
Project is actively being worked on. Issues and Pull Requests are welcomed.  We welcome contributions.


## Known dependencies

| Dependency | Used by | Notes |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/) | all projects | Target framework, and required at run time to compile templates. |
| [Windows App SDK 2.4.0](https://github.com/microsoft/WindowsAppSDK) (WinUI 3) | `CodeGenNew.App` only | GUI framework, unpackaged (no MSIX); its native runtime is bundled. .NET itself is framework-dependent — see specs.md. |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm) 8.4.0 (WinUI 3) | `CodeGenNew.App` only | MVVM helpers (`[ObservableProperty]`, `[RelayCommand]`), classic backing-field style — see ARCHITECTURE.md. |
| [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient), [Npgsql](https://www.nuget.org/packages/Npgsql), [MySqlConnector](https://www.nuget.org/packages/MySqlConnector) | `Connections`, `SchemaIntrospection` | The ADO.NET providers for SQL Server, PostgreSQL and MySQL. |
| [Mono.TextTemplating](https://github.com/mono/t4) | `CodeGenNew.TemplateEngine` | In-process T4 engine that runs outside Visual Studio (EF Core uses it for `dotnet ef dbcontext scaffold`). |
| [MSTest](https://www.nuget.org/packages/MSTest) 4 | `CodeGenNew.Tests` only | Test framework. |
| A reachable SQL Server, PostgreSQL or MySQL | runtime | Read-only access is all the tool ever needs. Not bundled. |

CLI argument parsing is hand-rolled rather than pulling in a library, given the small number of flags (see ARCHITECTURE.md).

## Databases

| | SQL Server | PostgreSQL | MySQL | SQLite |
|---|---|---|---|---|
| Connect from | app and CLI | app and CLI | app and CLI | app and CLI (the database is a file, opened read-only) |
| Schema read from | `sys.*` | `information_schema` + `pg_catalog` | `information_schema` | `sqlite_master`, the `pragma_*` functions and the `CREATE TABLE` text (for CHECK constraints) |
| `SP_*` templates write | stored procedures | functions (`RETURNS SETOF`, `plpgsql`) | stored procedures (`DELIMITER $$`) | nothing: SQLite has no routines (see `AccessMode`) |
| Search call in generated C# | `EXEC` | `SELECT * FROM "f"(...)` | `CALL p(...)` | LINQ over the context (`CS_SearchQuery`) |
| `API_Junction`, `WinUI3_JunctionEditor` | yes | yes (call the functions) | yes (call the procedures) | yes (LINQ over the context) |

**Blazor.** `Stacks=Blazor` writes a standalone Blazor WebAssembly app (.NET 10) that calls the generated API: a model and an `HttpClient` client per table, a page with grid, search, sort, paging and an add / edit panel (foreign keys as drop-downs), the menu, and the essentials (project file, layout, styles, `.gitignore`). The API allows the app's origin (`DevPort`, default 5190). No tabs, master-detail grids or dashboard yet; see `Docs/TemplateNotes/BLZ_Screens_v1.md`.

**Rust.** `Stacks=Rust` writes an Axum and sqlx API crate from a PostgreSQL, MySQL or SQLite database (not SQL Server: sqlx has no driver for it, and a run that names it stops with that reason): a struct, a repository of fixed SQL, the handlers and the validators per table, the module lists and router, and the essentials (`Cargo.toml` with pinned versions, `src/lib.rs`, `src/main.rs`, `src/support.rs`, `.env.example`). It keeps the contract of the other stacks (the same routes, JSON names, status codes, search and clone), so the unchanged React and Angular front ends and the verification scripts work against it. There are no routines in the database: the SQL is written for the database that was read (quotes, placeholders, PostgreSQL's casts for money and enums, `RETURNING` or a second read). The crate is also a library, `FRONTEND_DIR` makes it serve a built front end beside the API on one port, and `RustPort`, `RustCrateName` and `OutputRust` set the port, the package name and the folder. Compiling it needs a Rust toolchain; `Docs/Verification/Test-RustBuild.ps1` generates, builds and walks one route. The optional `Tauri` essentials group adds `desktop/`, a Tauri 2 shell that runs the API inside the app and shows the built React or Angular front end in a window on Windows, macOS or Linux. Not written: the many-to-many (junction) editors and the dashboard.

**Dashboard.** With `Dashboard=true` a whole-project run also writes a dashboard, derived from what the schema says and not from anything you list: a table's row count, a card for a money column (total, average, largest) and the same sum per foreign key's display name (the ten largest), a count per value of a CHECK-list column or a small lookup table, a monthly trend over a date column, active against inactive rows, rows by start and end dates, a master's count beside its child's, and the latest rows. `DashboardPlan` in Core picks the widgets (at most four cards, two top lists, two breakdowns, two trends and one recent list; a money column called a limit or a rate is not a measure) and every stack renders the same list, so the three front ends show the same page. Each widget is one fixed SQL statement with no parameter, run through EF Core (`SqlQueryRaw`), so it works in either access mode on all four databases; `Dashboard.md` and `DashboardQueries.sql` list the choice and the statements to review before any screen exists. `DashboardMeasures` adds or replaces a widget (`SalesInvoice.TotalAmount:sum:InvoiceDate:month`), `NoDashboardTables` leaves a table out, and `DashboardStrip=true` puts a table's two or three cards above its own grid. The pages draw their own cards, bars and sparkline (no chart package), show every number as text beside the chart and never rely on colour alone. The dashboard is the first menu entry; the aggregates are those of every row, so a project with row-level rules must put `/api/dashboard` behind the same policy as the tables.

**Access mode.** Search, sort, paging, clone and the many-to-many editors reach the database in one of two ways, chosen by the project setting `AccessMode`: `Routines` (the default where routines exist: the generated code calls the procedures or functions the `SP_` templates write) or `Ef` (LINQ over the EF Core context; no routine has to be deployed). SQLite always uses `Ef`; any other database can too, for owners who do not want routines in their database. A plan in `Ef` mode leaves out the routine templates and adds `CS_SearchQuery`, which holds the filter and the sort that `CS_Repo` and `API_Search` share. The filter matches text ignoring case and treats `%` and `_` in the value as ordinary characters.

**SQLite.** Point the CLI at the file (`--provider Sqlite -d path/to/shop.db`, no server, login or password) and set `DatabaseProvider=Sqlite` and `DatabaseName=<file>` in the project file so the generated project names the SQLite provider and `Data Source=<file>`. A declared type is read the way ORMs read it (`INTEGER` is an `int`, `BIGINT` a `long`, `BOOLEAN`, `DATETIME`, `DECIMAL(10,2)`, `VARCHAR(50)`); an integer named `Is...` or limited to 0 and 1 by a CHECK is a flag; unbounded `TEXT` is long text, as in PostgreSQL, so declare `VARCHAR(n)` for a column that should appear in grids and searches. The generated context stores `decimal` as a double and `DateTimeOffset` and `TimeSpan` as numbers, because SQLite cannot order the forms EF would otherwise use.

Everything else (entities, repositories, API, Angular, React, Blazor, WinUI3) is generated from the same model, so the screens look and behave the same whichever database sits underneath. The sample apps in the sibling repositories reuse one React or Angular front end over any of the three APIs.

**Names.** PostgreSQL and MySQL databases usually use `snake_case`. Set `NamingStyle=Pascal` in the project file (or `--naming Pascal`) and the generated C# and TypeScript say `CustomerItem` / `CustomerId` while the SQL text and the `[Table]` / `[Column]` attributes keep the real names. MySQL on Windows stores table names in lower case (`lower_case_table_names=1`), so use `snake_case` table names there.

**Passwords.** The app and CLI take the password from the connection dialog or `-P`; never commit one. For your own samples, read it from an environment variable (`PGPASSWORD`, `MYSQL_PWD`) at start-up.

**Junction tables and DbSet names.** A table with a composite primary key (a junction table) gets an entity (`CS_Entity` writes its key columns without `[Key]`), a `DbSet`, and `HasKey` in the generated `OnModelCreating`, which calls a partial `OnModelCreatingPartial` that a hand-written part of the context can implement. The `DbSet` properties are named like their tables; `DbSetNames=Plural` in the project file names them in the plural (`Customers`).

**Template notes.** Each template starts with a short summary (what it generates, its restrictions, what it needs); the long design notes for each one are in [Docs/TemplateNotes](Docs/TemplateNotes).

**Types with no mapping.** A column of a type CodeGenNew has no mapping for (a PostgreSQL array, `geometry`, a range, `inet`) cannot be used by an EF Core context, so the CLI warns about it (`Warning: column t.c has the type ..., which CodeGenNew does not map`) and the project setting `IgnoredColumns=Tags,Place.Location` (a column name for every table, or `Table.Column`; the database name or the generated one; a primary key is never left out) leaves it out so the rest of the table generates. A PostgreSQL `numeric` without a precision reads as 38 digits with 4 places (the SQL keeps `numeric` and the entity writes no `[Precision]`), and a money-named one is shown with 2.

**Provider wiring.** `CS_DbContext` and `API_Registration` write the `DbContext` and the API registration for the database that was read, so a web API's `Program.cs` is `AddGeneratedDbContext(...)` and `RegisterGeneratedApis()`; the NuGet package the provider needs is named in the generated context's comment (`Npgsql.EntityFrameworkCore.PostgreSQL`, `MySql.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer`).

**Drop-downs for listed values.** A MySQL `enum('a','b')` column is a drop-down of its values on every screen (Angular and React `<select>`, WinUI3 `ComboBox`); a `set` stays a text box. PostgreSQL does the same for a native enum type (`CREATE TYPE status AS ENUM (...)`) and for a text column limited by a single-column CHECK list (`CHECK (priority IN ('Low','High'))`). An enum column needs one extra step: EF Core sends text, which PostgreSQL will not store in an enum column, so run the `SP_EnumCasts` script (a whole-database template; it creates assignment casts from text and from varchar to each enum type, and a value that is not one of the labels is still refused). **Still open.** Views (tables only), and a CHECK that is not a plain list (a range, an OR).

## What it generates

Eighty-two templates ship in `Templates\` (the no-database essentials groups described below come on top). A table's right-click menu (or the CLI's `-T`) offers them grouped by the text before the first underscore.

| Group | Template | Writes |
|---|---|---|
| `SP` | `SP_Insert`, `SP_Update`, `SP_Delete`, `SP_Save` | `Table_Insert.sql` … — the classic CRUD stored procedures; `Save` is insert-or-update in one. Audit, active/inactive, soft-delete and date-range columns are handled by rule. |
| `SP` | `SP_Lookup` | ID + display columns of a row and of every table it points to, so a drop-down needs no joins. |
| `SP` | `SP_EnumCasts` | `EnumCasts.sql` - whole-database, PostgreSQL only: one guarded `CREATE CAST (text AS <enum>) ... AS ASSIGNMENT` per enum type a column uses, so EF Core can store the drop-down's value. |
| `SP` | `SP_Clone` | Copies a row into a new one and returns the new key (a grid's "Clone" button). |
| `SP` | `SP_Load` | Reads the table's **rows** and writes a re-runnable procedure that loads the same rows into another database (seed data). |
| `SP` | `SP_Search` | `Table_Search.sql` — one optional `LIKE '%...%'` parameter per string column (audit columns excluded), AND-ed together, for a list screen's search box. Refuses a table with no searchable columns. |
| `SP` | `SP_Junction` | `Table_Junction.sql` — List/Link/Unlink for a many-to-many **junction table** (only offered when `TableModel.IsJunctionTable` is true). |
| `SP` | `SP_KeySequence` | `KeySequence.sql` - whole-database, SQL Server only: a key-sequence table and a `GetNextID` routine as an alternative to `IDENTITY`, for the tables the project lists in `KeySequenceTables` (their `SP_Insert` then asks the sequence for the key); each counter starts from the table's highest key. |
| `SP` | `SP_ReplicationTriggers` | `Table_ReplicationTriggers.sql` - SQL Server only: insert, update and delete triggers that copy each change to the databases named in `ReplicationTargets` (linked server, database). A failing target rolls back the write: review it and try it on a copy. |
| `SP` | `SP_AuditTable` | `Table_Audit.sql` - an audit trail for a table with audit columns (a `Create...` column and a `Modified...` or `Updated...` column): a `Table_History` table (the old row, `U` or `D`, when, who) and the trigger that fills it on update and delete; SQL Server, PostgreSQL, MySQL and SQLite. Re-runnable. |
| `SP` | `SP_TemporalTable` | `Table_Temporal.sql` - SQL Server only: makes the tables named in `TemporalTables` system-versioned (period columns and `SYSTEM_VERSIONING` with a `Table_History` the database keeps). |
| `SP` | `SP_BulkUpdate` | `BulkUpdate.sql` - whole-database, SQL Server only: a procedure that rewrites the columns named in `BulkUpdateColumns` (for example `UPPER({column})`) in every table that has them, as plain `UPDATE` statements in one transaction. |
| `SP` | `SP_Dashboard` | `DashboardQueries.sql` - whole-database, only with `Dashboard=true`: the dashboard's statements for the database that was read, each under a comment saying why the widget exists; run it by hand to check a widget. |
| `SP` | `SP_ForeignKeyIndexes` | `ForeignKeyIndexes.sql` - whole-database, all three databases: a `CREATE INDEX` for every foreign key that no index starts with, in the database's own syntax (an advisory script: it is never applied). |
| `API` | `API_Crud` | `TableApi.cs` — a minimal-API class: get all, get by id, create, update, delete, each over the table's repository. |
| `API` | `API_Junction` | `TableJunctionApi.cs` — HTTP endpoints over `SP_Junction`'s three procedures, called straight through the `DbContext` (no repository) — the HTTP companion `TS_JunctionComponent` needs (also junction-only). |
| `API` | `API_Search` | `TableSearchApi.cs` — a `/search?...&pageNumber=&pageSize=` endpoint over `SP_Search`/`SP_SearchCount`, called straight through the `DbContext` (no repository), returning `TableSearchResult` (`Items`/`Page`/`PageSize`/`TotalCount`/`TotalPages`) — the HTTP companion the Angular/React pagination wiring needs (only offered for a table with at least one searchable column). |
| `CS` | `CS_Entity` | `Table.cs` — an EF Core entity: key, foreign keys, navigation properties, attributes. |
| `CS` | `CS_DbContext` | `<Context>.cs` — **one file for the whole database** (the CLI needs no `-t`; the app has a toolbar button, Alt+D): the EF Core context as a `partial` class with a `DbSet` per table, both constructors, `OnConfiguring` and the one place the provider is chosen (`UseSqlServer`, `UseNpgsql` or `UseMySQL`; PostgreSQL's `timestamp` convention; the MySQL password from `MYSQL_PWD`). |
| `TSX` | `TSX_Screens` | `screens.tsx` - also whole-database: the React app's screen list (route, menu text, page) in menu order; `App.tsx` imports it, so a new table is a regenerate, not an edit of the shell. |
| `TSX` | `TSX_GridSort` | `components/gridSort.tsx` - whole-database support code every React grid imports: the sortable header, the right-click **Clear sort** menu and the saved sort. |
| `TS` | `TS_GridSort` | `grid-sort.ts` - the Angular counterpart: how the sort is saved, loaded and flipped. |
| `TS` | `TS_Screens` | `app.routes.ts` - whole-database: the Angular app's `screens` and `routes`, from the same list. |
| `WinUI3` | `WinUI3_Screens` | `MainWindow.Screens.cs` - whole-database: the other half of `partial class MainWindow` (`AddScreens()` adds the menu entries, `ShowPage(tag)` shows the page). |
| `API` | `API_Registration` | `ApiRegistration.cs` — also whole-database: `AddGeneratedDbContext(configuration)` and `RegisterGeneratedApis()`, so a sample's `Program.cs` lists no table. |
| `API` | `API_OpenApi` | `openapi.yaml` - whole-database: an OpenAPI 3.0 document of the API the other `API` templates write (paths, search parameters, schemas with types, lengths, CHECK limits, enum lists, required and nullable), for Kiota, NSwag, openapi-generator or a Swagger UI. |
| `API` | `API_Http` | `Table.http` - a request file per table (all, one, search, create, update, delete, clone) with example bodies shaped by the columns, for Visual Studio, VS Code and Rider. |
| `API` | `API_Test` | `TableApiTests.cs` - an MSTest integration test class per table in its own test project (`ApiTests=true`): add, read, change and remove a row, a 404, the search page, and with `ApiValidation` the limits. Runs the API in process over a scratch database (SQLite: a copy of the file; others: `<PROJECT>_TEST_CONNECTION`). |
| `CS` | `CS_Validation` | `TableValidation.cs` — a `[MetadataType]` buddy class adding `[Required]`/`[StringLength]`/`[DataType]`/`[Display]` to the entity (works for a hand-maintained entity too; the entity class must be `partial`). |
| `CS` | `CS_Enum` | `Table.cs` — a C# enum whose members are the **rows** of a small lookup table. |
| `CS` | `CS_Repo` | `TableRepo.cs` — the thin repository class over your shared generic repository, plus a `HasDuplicate<Column>` check and (for a string column) a `SuggestUnique<Column>` name generator for every column in a unique index other than the primary key. |
| `CS` | `CS_Dashboard` | `DashboardData.cs` - whole-database, only with `Dashboard=true`: the chosen widgets with their fixed SQL and `LoadAsync`, which runs them through the context; the web API and the desktop page both call it. |
| `API` | `API_Dashboard` | `DashboardApi.cs` - `GET /api/dashboard` (every widget) and `?table=Customer` (that table's cards), cached for a minute. |
| `TSX` | `TSX_Dashboard` | the dashboard page for React (cards, bar lists, a sparkline, recent rows; no chart package) and, with `DashboardStrip=true`, the strip above each table's grid. |
| `TS` | `TS_Dashboard` | the same for Angular: a service, the page, a widget component, the strip and the host that puts it above a screen. |
| `WinUI3` | `WinUI3_DashboardPage` | the same for the desktop app, built in code, with the strip above each list page. |
| `CS` | `CS_SearchQuery` | `TableSearchQuery.cs` - the search as LINQ (filter by contains, sort from a fixed list, default order), written when the project uses `AccessMode=Ef` (always for SQLite); `CS_Repo` and `API_Search` call it instead of the routines. |
| `CS` | `CS_Validator` | `TableValidator.cs` - a FluentValidation `AbstractValidator<Entity>`: required, length, ranges from the name rules and CHECK constraints, CHECK lists, email / phone / URL / latitude / longitude by column name. |
| `CS` | `CS_Faker` | `TableFaker.cs` - a Bogus fake-data generator per table: text of the right kind and length, numbers inside their CHECK ranges, choices from CHECK lists, nullable columns sometimes NULL, a seed for repeatable rows. |
| `CS` | `CS_Dto` | `TableDto.cs` - a plain transfer class filled from any `IDataRecord`: a `Populate` method (required columns, then optional ones), an `IsIdentical` comparer over the columns that matter, a `ToString` on the display column. |
| `CS` | `CS_Mapper` | `TableMapping.cs` - a static class copying a table between its entity (`CS_Entity`) and its transfer class (`CS_Dto`): `ToDto`, `ToEntity`, list versions and `FromReader`. |
| `CS` | `CS_MapperlyMapper` | `TableMapper.cs` - the Mapperly counterpart of `CS_Mapper`: a `[Mapper]` partial class (`ToDto`, `ToEntity`, `ProjectToDto`) whose bodies Mapperly writes at compile time. |
| `CS` | `CS_DataContractDto` | `TableContract.cs` - a `[DataContract]` class with four constructors (empty, from a `DataRow`, from a reader row, copy), for a WCF-style service. |
| `CS` | `CS_TypedDataRow` | `TableDataTable.cs` - the typed-DataSet shape of a table: a `DataTable` with its columns and a `DataRow` with one typed property per column and `IsXNull` / `SetXNull` pairs. |
| `CS` | `CS_SerializationDtos` | `TableSerializationDtos.cs` - the same data class in three contracts side by side: plain `[Serializable]`, custom `ISerializable`, and `XmlSerializer` attributes. |
| `FS` | `FS_Entity` | `Table.fs` - an immutable F# record (a nullable column is an `option`) and a module with `validate` and `ofStrings`, both returning a `Result` that lists every problem; the limits include the database's CHECK ranges. |
| `FS` | `FS_Rop` | `Rop.fs` - no table, no database: the shared `Result` helpers, the error-collecting `<*>` operator and the text parsers the records use. |
| `MD` | `MD_DataDictionary` | `Table.md` - one Markdown page per table: every column with type, null, default, key role, limits and notes, the foreign keys out and the tables that refer to it. |
| `RS` | `RS_Struct` | `src/models/table.rs` - the Rust struct of a table (serde and sqlx), with the JSON names the other stacks send; for the Rust stack (PostgreSQL, MySQL, SQLite). |
| `RS` | `RS_Repo` | `src/repos/table.rs` - the table's fixed SQL for the database that was read and the sqlx functions that run it: get, insert, update, delete, clone, and a search with filters, a fixed sort list and paging. |
| `RS` | `RS_Routes` | `src/routes/table.rs` - the Axum handlers with the contract of the other stacks (201 and `Location`, 400, 404, `/search`, `/clone`). |
| `RS` | `RS_Validate` | `src/validation/table.rs` - `validate`, the schema's rules for a row (`ApiValidation=true`); the answer is the ASP.NET validation problem. |
| `RS` | `RS_Mod` | the module lists and the router that merges every table's routes. |
| `BLZ` | `BLZ_Model`, `BLZ_Client` | `Models/Table.cs` and `Services/TableClient.cs` - the class a table's JSON becomes in the Blazor app and its `HttpClient` calls (get, search with paging and sort, create, update, delete, clone). |
| `BLZ` | `BLZ_Page` | `Pages/TablePage.razor` - the Blazor screen: grid, search, sortable headers, paging, add / edit panel with drop-downs for foreign keys. |
| `BLZ` | `BLZ_Screens` | `Layout/NavMenu.razor`, `Pages/Home.razor` and `Services/ApiClients.cs` - whole-database: the menu in `Screens` order and the registration of every client. |
| `MD` | `MD_Postman`, `MD_Bruno` | `collections/<Project>.postman_collection.json` and `collections/bruno/` - whole-database, only with `PlanAlso`: a Postman and a Bruno collection of the API requests (a folder per table: all, one, search, add, change, remove, copy) with sample bodies and a `baseUrl` variable. |
| `CS` | `CS_CqrsHandlers` | `Application/` - whole-database, only with `PlanAlso`: commands, queries and handlers per table (create, update, delete, get by id, paged list) behind two small interfaces, and `AddApplicationHandlers()`. No package. |
| `MD` | `MD_Dashboard` | `Dashboard.md` - whole-database, only with `Dashboard=true`: the widgets the dashboard shows, each with the schema fact that chose it and the statement that fills it, then what the caps left out. |
| `MD` | `MD_Erd` | `ErDiagram.md` - whole-database: a Mermaid entity-relationship diagram (GitHub and VS Code draw it); `ErdTables` picks the tables. |
| `TS` | `TS_Model` | `models/table.ts` — an Angular interface matching the JSON the API really sends. |
| `TS` | `TS_Validators` | `validators/table.validators.ts` - the Angular `Validators` the schema states for each field (required, length, allowed values, ranges, email, phone, URL), keyed by property name. |
| `TS` | `TS_Service` | `services/table.service.ts` — the `HttpClient` wrapper with the same method names on every table. |
| `TS` | `TS_Component` | Four files in `components/table/` — CSS, HTML, spec and TypeScript for a grid + add/edit form screen. |
| `TS` | `TS_JunctionComponent` | Four files in `components/table-junction/` — a two-`<select multiple>` shuttle-control screen calling `API_Junction` (also junction-only). |
| `TS` | `TS_DetailMasterComponent` | Four files in `components/table-detail-master/` — `TS_Component`'s own grid + form plus one read-only grid per child table (only offered when there's at least one). |
| `TSX` | `TSX_Api` | `api/tableApi.ts` — the React counterpart of `TS_Service`: a plain object of `fetch`-based functions instead of an `HttpClient` class (reuses `TS_Model`'s own output for the row type — no separate React model template exists). |
| `TSX` | `TSX_Schema` | `schemas/table.ts` - a zod schema of the editable fields with the same limits, and the form type inferred from it. |
| `PROTO` | `PROTO_Message` | `Table.proto` - a proto3 message per table, its key message, list messages and a CRUD service (a decimal is a string, a date a Timestamp, a nullable column optional). |
| `TSX` | `TSX_Page` | `pages/TablePage.tsx` plus a colocated `pages/__tests__/TablePage.test.tsx` — the React counterpart of `TS_Component`: one function component (grid + add/edit form) using `useState`/`useEffect` instead of a class. |
| `TSX` | `TSX_DetailMasterPage` | `pages/TableDetailMasterPage.tsx` plus its test — `TSX_Page`'s own grid + form plus one read-only grid per child table (only offered when there's at least one). |
| `TSX` | `TSX_JunctionPage` | `components/TableJunction.tsx` plus its test — a two-`<select multiple>` shuttle-control component (an `anchorId` prop, not routed on its own) calling `API_Junction` (also junction-only). |
| `WinUI3` | `WinUI3_JunctionEditor` | Three files (View, code-behind, ViewModel) — the same shuttle-control idea as a desktop `ContentDialog`, calling `SP_Junction` directly through EF Core (also junction-only). |
| `WinUI3` | `WinUI3_DirectoryListing` | Four files — **no table and no database**: a `Page` listing the files of a folder (sortable Name / Size / Modified, a filter box, Add File..., Open, Rename, Delete, Open Folder). The project settings `ListingName`, `ListingFolder` and `ListingPattern` name it; run it with the CLI without `-S`, `-d` or `-t`. |
| `WinUI3` | `WinUI3_MasterScreen` | Three files — a `Page` listing every row (grid + Add/Edit/Delete), calling `TableRepo` (`CS_Repo`) directly. |
| `WinUI3` | `WinUI3_DetailScreen` | Three files — a `ContentDialog` add/edit form, one field per column, drop-downs for foreign keys. |
| `WinUI3` | `WinUI3_DetailMasterScreen` | `WinUI3_DetailScreen`'s form plus one read-only child grid per table in `TableModel.ChildForeignKeys` (only offered when there's at least one). |

### Screen-family mapping across platforms

WinUI3 needs three list/form templates where Angular and React each need only two, because they split the job differently rather than covering different ground:

| Concept | WinUI3 | Angular (`TS`) | React (`TSX`) |
|---|---|---|---|
| List grid | `WinUI3_MasterScreen` | folded into `TS_Component` | folded into `TSX_Page` |
| Add/edit form | `WinUI3_DetailScreen` | folded into `TS_Component` | folded into `TSX_Page` |
| Master-detail (parent + read-only child grids) | `WinUI3_DetailMasterScreen` | `TS_DetailMasterComponent` | `TSX_DetailMasterPage` |
| Junction (many-to-many editor) | `WinUI3_JunctionEditor` | `TS_JunctionComponent` | `TSX_JunctionPage` |

WinUI3's list page and its add/edit form are two separate templates because the idiomatic WinUI3 pattern is a `ContentDialog` opened from the grid page. `TS_Component` and `TSX_Page` each generate a single file that already contains both the grid *and* an inline add/edit form on one screen, matching how a hand-written CRUD screen in those frameworks is usually built. All three platforms end up with the same coverage — list, add/edit, master-detail, junction — Angular and React just fit two of WinUI3's roles into one generated file.

Templates carry a version in the file name (`SP_Save_v1.tt`). The menu shows only the newest version of each; older ones stay on disk. Files you customize are never overwritten by an update — the new shipped copy is written beside yours as `<name>.new`.

## Quick start

New here? **[Docs/GettingStarted.md](Docs/GettingStarted.md)** walks one small database from an empty folder to a running API. To install the command-line tool: `dotnet tool install -g CodeGenNew.Cli` (it keeps its templates, projects and output in `%APPDATA%\CodeGenNew`, or the folder in `CODEGENNEW_HOME`). Contributing: [CONTRIBUTING.md](CONTRIBUTING.md); checking a generated app in a running copy: [Docs/Verification](Docs/Verification).

**Prerequisites:** the [.NET 10 SDK](https://dotnet.microsoft.com/) (the templates are compiled at run time, so a real SDK must be installed — not just the runtime) and a reachable SQL Server, PostgreSQL or MySQL database. The desktop app also needs Windows 10/11.

```
dotnet build CodeGenNew.slnx
```

**Command line** (the built exe is `CodeGenNew.Cli\bin\Debug\net10.0\codegen.exe`):

```
codegen -S MYSERVER -d MyDatabase -s dbo -t Holiday -T API_Crud.tt -E -o C:\Work\MyApp\Api\Apis
```

| Option | Meaning |
|---|---|
| `--provider` | `SqlServer` (default), `PostgreSql` or `MySql` |
| `-S`, `-d`, `-s`, `-t` | server (`host` or `host:port`), database, schema, table (`-t` is not needed for `CS_DbContext` / `API_Registration`, which cover every table of the schema). The default schema is `dbo` for SQL Server, `public` for PostgreSQL and the database name for MySQL |
| `-T` | template file name; without a version (`SP_Save.tt`) means the latest, `SP_Save_v1.tt` pins that version |
| `-E` | Windows authentication, SQL Server only (or `-U user` and `-P password`; the password is prompted for if omitted — PostgreSQL and MySQL always use a user name and password) |
| `--naming` | `AsIs` (default) or `Pascal`: turn `snake_case` database names into PascalCase code names (see *Databases* above) |
| `-o` | output folder (default: `Output` from `Settings.json`) |
| `--project` | a project settings file, `Projects\<name>.config` next to the exe (see below) |
| `--projects-dir` | the folder of project files instead of `Projects` next to the exe; point both the app's `Settings.json` (`ProjectsDirectory`, an absolute path works) and the CLI at one folder to share projects |
| `--set Key=Value` | override any project setting by its name for this run (repeatable), e.g. `--set CurrencyCode=EUR --set "NonNegativeColumns=CreditLimit,Item.Cost"` |

**Generate everything for a project.** `codegen generate -S <server> -d <database> -E --project <name> -o <folder> [--stack api,winui3,react,angular]` reads the schema once and runs every template of the chosen stacks for every table it applies to, in one process (templates are compiled once, so a sample that took seven minutes of per-file runs takes about ten seconds). Which template runs for which tables is not a list you keep: it comes from each template's config (`Stacks`, `PlanTables`, `OutputFolder`) and the project's settings (`Screens`, `DetailMasterTables`, the enum rules, `PlanAlso` for the routines nothing calls by default). The stacks are `Api` (entities, repositories, CRUD and search APIs, the context, the registration), `WinUI3` (the same data classes inside the app, the list pages and dialogs, `MainWindow.Screens.cs`), `React` and `Angular` (models, services / api modules, pages / components, the screens list) and the SQL for the database that was read (written once). Each stack goes to its own folder under `-o`: `OutputApi`, `OutputWinUI3`, `OutputReact`, `OutputAngular` and `OutputSql` in the project file (defaults `<ProjectName>.Api`, `<ProjectName>.App`, `frontend`, `sql`). A file whose text did not change is not touched (line endings ignored), a template that declines a table (a name/active table has no plain CRUD API) is listed, not failed, and `--dry-run` writes nothing and reports what would change. `--only SP_Search,CS_Entity` runs part of the plan. `--table Customer,Item` regenerates only those tables' files (the whole-database files and the essentials are left out), `--diff` prints what differs in every file that exists with other content, and `--build` / `--test` build and test the stacks afterwards (`npm run build` / `npm test`, `dotnet build`; override per stack with `BuildApi`, `TestReact` ... in the project file, `none` skips a step). The run keeps `.codegen-manifest.json` in the output folder, the list of files it wrote: a file an earlier run wrote that no table or template produces any more is reported as **stale** (a dropped table), and `--delete-stale` deletes the ones nobody edited since (an edited one is only reported). In the desktop app the **Generate All** button (Alt+G, next to the database-templates button) does the same for the connected database: project, output folder, the stacks (ticked from the project), essentials, dry run, delete stale, build and test, with the list of files and the diff of the selected one. The samples' `Regenerate.sh` scripts are now one `codegen generate` call.

**Essentials.** The files every app of a kind needs and that no table drives are no-database templates, grouped per stack: *WinUI* (`App.xaml` and `.xaml.cs`; `MainWindow.xaml` and `.xaml.cs`; the `.csproj`, `app.manifest`, `appsettings.json` and `GlobalUsings.cs`; the base classes; an optional directory-listing page), *React* (`index.html`, `main.tsx`, `App.tsx`; `index.css`; `api/client.ts`, `PaginationBar.tsx`, `setupTests.ts`; `package.json`, `vite.config.ts`, `tsconfig.json`, `src/vite-env.d.ts`), *Angular* (`index.html`, `main.ts`, `app.ts` / `.html` / `.css`; `app.config.ts`, `proxy.conf.json`; `styles.css`) and *API* (the `.csproj`, `Program.cs`, `appsettings.json`, `GlobalUsings.cs`; `BaseApi`; the base classes; `launchSettings.json` and a `.http` file). Every stack also has a small ignore group (`.gitignore`), the C# stacks an `.editorconfig` that makes the generated style (collection expressions, file-scoped namespaces) an IDE warning, React and Angular an optional `Docker` group (a `Dockerfile` that builds with Node and serves with nginx, an `nginx.conf.template` that forwards `/api/` to the container in `API_UPSTREAM`, a `.dockerignore`; ticked by name, not by default), and Angular a `Build` group (`package.json`, `angular.json`, the `tsconfig` files as `ng new` writes them, with the `@angular` ranges following `AngularVersion`). They are written from the project's settings (`ProjectName`, `AppNamespace`, `DatabaseProvider`, `DatabaseServer`, `DatabaseName`, `DatabaseUser`, `ApiPort`, `DevPort`, `ProjectTitle`, `AngularVersion` and the namespaces; a password is never written). In the desktop app use the **Essentials** button (Alt+E): WinUI / React / Angular / API essentials open a checklist of the groups, the project and the output folder. On the command line: `codegen essentials --stack winui3 --project <name> -o <folder> [--groups app,mainwindow] [--replace]` (`--list` shows the groups; no database or table is needed), or add `--essentials` to `generate`. Existing files are kept unless `--replace` / "Replace files that already exist" is set, because these files are edited by hand after the first generation. Select a kept file in the dialog (or pass `--diff`) to see how it differs from what would be written. A group whose partner file is missing (the main window needs `MainWindow.Screens.cs` and the context; `Program.cs` needs `ApiRegistration.cs`) gets a warning. A template of your own with `NoDatabase=true`, `Stacks=WinUI3`, `EssentialsGroup=<name>` and `Description=...` in its config joins the menu.

**Project settings.** Templates carry their own namespaces and context name, so a project of your own would otherwise need each template edited. Put `ProjectName=InvoiceSystem` in `Projects\InvoiceSystem.config` (one `key=value` per line, `#` comments, comma-separated lists) and pass `--project InvoiceSystem`: every namespace you don't list is derived from the name (`InvoiceSystem.App.Views`, `InvoiceSystemContext`, ...). The keys are `ProjectName`, `ViewNamespace`, `ViewModelNamespace`, `ContextName`, `ContextNamespace`, `ApiNamespace`, `EnumNamespace`, `RepoNamespace`, `EntityNamespace`, `MinYear`, `MaxYear`, `ViewsFolder`, `ViewModelsFolder`, `Usings`, `NoLookupParents`, `NoRepositoryTables`, `NoApiTables`, `NoNavigationTables` and `NoCloneTables` (tables with no Clone button), `DtoNamespace`, `FSharpNamespace` and `ValidatorNamespace` (where the transfer classes, the F# records and the validators go), `ErdTables` (the tables the diagram draws). **Switches that add generated extras to a whole-project run** (all off by default): `ApiDocs=true` (`openapi.yaml`, served at `/openapi.yaml` with a Swagger UI page at `/docs`), `ApiHttp=true` (`.http` files), `ApiFakers=true` (Bogus fakers), `ProjectDocs=true` (data dictionary pages and the ER diagram under `docs`) and `ApiValidation=true` (FluentValidation validators that the create and update endpoints run). The database's own descriptions (SQL Server `MS_Description`, PostgreSQL `COMMENT ON`, MySQL `COMMENT`) are read and shown in the data dictionary and the OpenAPI document, `ReplicationTargets`, `KeySequenceTables` / `KeySequenceTable` and `BulkUpdateColumns` / `BulkUpdateExpression` (the SQL Server scripts above), `Screens` (the tables that get a menu entry, in menu order; default: every table with an API and a search, alphabetically) and `NamingStyle` (`Pascal` turns a database's `customer_item` / `customer_id` into `CustomerItem` / `CustomerId`; the default `AsIs` keeps its names) and `Acronyms` (`PO,UPC,MSRP`: with `Pascal`, those words stay upper-case, so `require_customer_po` becomes `RequireCustomerPO`, the name a SQL Server copy of the database already uses). A flag (`--view-ns`, `--viewmodel-ns`, `--context`, `--context-ns`, `--api-ns`, `--enum-ns`, `--repo-ns`, `--entity-ns`, `--project-name`, `--naming`, `--acronyms`, `--min-year`, `--max-year`) overrides the file for one run. Precedence: flag, then file, then the name-derived default, then the template's built-in value; with no `--project` a template generates exactly as before. **Enum tables.** Which tables are C# enums (no entity, repository or API of their own) is decided per project, not hard-coded: list them in `EnumTables`, or leave it out and the schema decides. A table is treated as an enum when it has at most `EnumMaxRows` rows (default 25) and either looks like a bare lookup table (one integer key, no foreign keys, a text column) or its name ends in one of `EnumNameSuffixes` (default `Type, Types, Code, Codes, Status, Kind`, so `LeaveType` and `MeterTypeCodes` count) and it has an integer key and a text column. `NoLookupParents`, `NoRepositoryTables`, `NoApiTables` and `NoNavigationTables` still work, each overriding `EnumTables` for its own question only. With no project chosen the templates keep their built-in lists.

**Front-end folders and money limits.** `ApiFolder`, `ModelsFolder`, `ServicesFolder`, `ComponentsFolder` and `PagesFolder` (defaults `api`, `models`, `services`, `components`, `pages`) are the folders the React and Angular templates write into and import from, relative to the output folder (`src` for React, `srcpp` for Angular). `NonNegativeColumns=CreditLimit,Item.Cost` lists money columns that can never be negative (a column name for every table, or `Table.Column`): their number box gets a minimum of 0 (WinUI3 `Minimum`, `min` in Angular and React). A database that states it itself needs no list: a single-column `CHECK` range (`>=`, `<=`, `>`, `<`, `BETWEEN`, on any of the three databases) gives the number boxes their `Minimum` / `Maximum` (WinUI3), `min` / `max` (Angular and React) and the entity a `[Range]`, and a text column with `CHECK (col IN (...))` (SQL Server stores it as an OR chain) becomes a drop-down as in PostgreSQL and MySQL. A check with a function, an OR of ranges or more than one column is ignored; the project list still applies for what the schema does not say.

**Angular version.** `AngularVersion=22` tells the Angular templates which version the project runs: from 19 the `standalone: true` flag is left out, from 18 the screens use the built-in `@if` / `@for` / `@else` instead of `*ngIf` / `*ngFor`, and from 22 the eager change-detection strategy is spelled `Eager` and the specs carry no animation providers. Leave it out and the output is what every version from 18 accepts.

**Other TypeScript and entity settings.** `HiddenParents` lists the tables whose foreign key columns the TypeScript forms hide (empty for a project that doesn't list any); `ModelFileOverrides=Table=file,Table2=file2` names model files that don't follow the usual convention (the table's base name, lower-cased, which is also what `TS_Model` writes); `BaseEntity` and `BaseNameActiveEntity` name the base classes of generated entities.

**Cloning a row.** A grid row has a **Clone** button (between Edit and Delete) for every table whose key the database assigns (an identity int) and whose unique columns are text: the repository's `CloneAsync` calls the table's `<Table>_Clone` routine (`SP_Clone`: audit columns, the active flag and the key are handled by rule), a unique text value such as an account number gets a free one (`A100` becomes `A1002`), the copy comes back through `POST /<route>/{id}/clone` and opens in the edit form at once so the person changes what differs. The copy is a shallow one (the row, not its child rows). The project setting `NoCloneTables=Customer,SalesInvoice` takes tables out.

**Sorting a grid.** Click a column header to sort by it, click it again to flip the direction, right-click the grid for **Clear sort**. The sort is applied by the database together with any search filters (the search routine takes `SortColumn` / `SortDescending`; the column name is compared with a fixed list of the table's own columns, never pasted into SQL; a foreign key column sorts by the parent's name), is kept when paging, and is remembered: React and Angular keep it in the browser's local storage, the WinUI3 app in `GridSorts.json` in its local application data folder. Long text columns are never sorted. The read-only child grids inside a master dialog sort the same way, but in the browser or the app (they already hold every row), comparing real values (numbers as numbers, dates as dates) and the names a column shows in place of an id; each child grid remembers its own sort.

**Whole-number inputs.** Every whole-number column that is not a foreign key is edited in a number box (WinUI3 `NumberBox`) or a number input with `min`/`max` (Angular, React), limited to the range its name and SQL type imply: `Year`/`FiscalYear` and similar run from `MinYear` to `MaxYear` (default 2000-2100), `Month`/`Mth` 1-12, `Day` 1-31, `Quarter` 1-4, `Week` 1-53, `*Percent`/`*Pct` 0-100, `*Qty`/`*Count` and `SortOrder` from 0, and any other integer the limits of its SQL type. The name list is deliberately narrow (a `BirthYear` is not limited to the year range). `CS_Validation` writes the same limits as `[Range]` for the named kinds. In the WinUI3 ViewModels such a field is a `double` (`NaN` = blank) that is checked for being whole and in range before it is converted back.

**Currency inputs.** A `money`/`smallmoney` column, or a `decimal` column whose name says money (`Amount`, `Amt`, `Price`, `Cost`, `Fee`, `Charge`, `Salary`, `Wage`, `Payment`, `Balance`, or `Total...` unless it names another unit such as `TotalHours`), is edited in a WinUI3 `NumberBox` with a `CurrencyFormatter` set in the dialog's code-behind, using the project's `CurrencyCode` (default `USD`) and the column's own scale for the decimals (two for `money`). The ViewModel holds it as a `double` and the save converts it to a `decimal` rounded to that scale. A `Quantity` stays a plain text box. The WinUI3 list and child grids show money in the project's `CurrencyCode` too; the Angular and React grids use the currency pipe and `toLocaleString` with the project's code.

In the desktop app the *Project* button (Alt+P) edits these files and picks the one to generate with.

**Desktop app:** run `CodeGenNew.App`, choose *Connect*, expand the database, right-click a table and pick a template. *Output Location* sets the output folder; *Templates* adds, renames, edits and deletes templates.

The first run creates `Templates\`, `Output\` and `SpecialLogicColumns.config` next to the exe from copies embedded in it, so **copying the exe folder is all the deployment there is**.

### Templates that write several files

`TS_Component` writes four files and the `TS_`/`TSX_` templates write into sub-folders. A template does that by starting each file with a marker line, `@@@FILE relative/path@@@` (see ARCHITECTURE.md). For these templates point the output folder at the **root of the target project** — for Angular, its `src\app` folder; for React, its `src` folder — and the files land in the right places:

```
codegen -S MYSERVER -d MyDatabase -t Holiday -T TS_Component.tt -E -o C:\Work\MyApp\src\app
```

## Using the templates in your own project

The `SP`/`API`/`CS`/`TS`/`WinUI3` templates were written against a layered sample (`MyApp.Common`, `MyApp.ApiService`, an Angular front end); the `TSX` family against a React front end with a small `request` helper. **The names that belong to a project are a block at the very top of each template**, under a banner:

```
// ==========================================================================================
// PROJECT SETTINGS -- the lines to change to use this template in your own project ...
// ==========================================================================================
string apiNamespace = "MyApp.ApiService.Apis";
string contextType = "MyAppContext";
string[] usings = { };          // namespaces the generated file must "using"
string[] noRepositoryTables = { "ClearanceType", ... };
```

Open the `.tt` file (or the app's *Templates* button → Edit), change those lines, and save; the next generation uses them — there is nothing to rebuild.

| Setting | In | Tells the template |
|---|---|---|
| `apiNamespace`, `entityNamespace`, `enumNamespace`, `repositoryNamespace` | `API_Crud`, `API_Search`, `CS_Entity`, `CS_Validation`, `CS_Enum`, `CS_Repo` | the `namespace` the generated file declares |
| `usings` | `API_Crud`, `API_Search`, `CS_Entity`, `CS_Validation`, `CS_Repo` | extra `using` lines to write (leave empty if your project uses global usings) |
| `contextType` | `API_Crud`, `API_Search`, `CS_Repo` | your `DbContext` class |
| `baseEntity`, `baseNameActiveEntity` | `CS_Entity` | the base classes your entities derive from |
| `modelsFolder`, `servicesFolder`, `componentsFolder`, `apiPrefix` | `TS_*` | where the Angular files go and the API URL prefix |
| `pagesFolder`, `apiFolder`, `componentsFolder`, `apiPrefix` | `TSX_*` | where the React files go and the API URL prefix |
| `noNavigationTables`, `noRepositoryTables`, `noApiTables`, `noLookupParents` | several | tables that are enums, lookups or have no entity, and so get no navigation property, repository, API or screen |

The templates also assume the shape of the sample project's plumbing: a generic repository with `GetAll`, `GetByIdAsync`, `ExistsAsync` and `DeleteAsync`, a `BaseApi<T>` that registers routes, Angular's own `HttpClient`, and — for `TSX_*` only, since React has no framework-supplied HTTP client — a shared `request<T>(path, init)`/`ApiError` pair the target project must already have (`import { request } from './client'`). Each template's header comment says what it assumes and what it deliberately does **not** write (collection navigations, hand-written business rules, detail grids), so you know what stays yours.

Column-name rules (which column means "created date", "is active", a display name, …) live in `SpecialLogicColumns.config`; edit it to match your naming.

## Solution structure

```
CodeGenNew.slnx
├── CodeGenNew.App                 WinUI 3 UI, MVVM via CommunityToolkit.Mvvm (TreeView, Connection/Location/Template Management dialogs)
├── CodeGenNew.Cli                 Scriptable console entry point (bypasses the WinUI 3 app)
├── CodeGenNew.Connections         Connect + test SQL Server, PostgreSQL and MySQL connections
├── CodeGenNew.SchemaIntrospection Reads tables/columns/PK/FK from the database; builds TableModel
├── CodeGenNew.TemplateEngine      Wraps Mono.TextTemplating; discovers/runs .tt templates; writes output files; seeds defaults
├── CodeGenNew.Core                Shared model classes (TableModel, ColumnModel, ForeignKeyModel, settings)
├── CodeGenNew.Tests               MSTest suite (see below)
├── Templates\                     The shipped .tt templates and their .tt.config files
└── Docs\                          specs.md (the specification), ARCHITECTURE.md (the reviewer guide), template notes, verification scripts
```

## Tests

```
dotnet test CodeGenNew.Tests\CodeGenNew.Tests.csproj
```

The suite needs **no database** (live PostgreSQL and MySQL integration tests run only when `CODEGENNEW_PG_*` / `CODEGENNEW_MYSQL_*` environment variables name a server, and are skipped otherwise). It covers the file-writing engine (`@@@FILE` splitting, unsafe paths, output naming), template discovery and versioning, the seeder (created / refreshed / kept-customized, and that **every file in `Templates\` is actually shipped**), the literal and display-column helpers, and it runs every shipped template against hand-built tables to check what it writes — the API, entity, validation, enum, repository, model, service, component and React-page rules, each template's refusals (composite keys, enum tables), and that changing a project setting changes the output. It takes about a minute, mostly compiling templates.

## Functionality Status

SQL Server, PostgreSQL and MySQL are supported, in the command-line tool and in the desktop app (choose the database type in the connection dialog; on the command line `--provider SqlServer|PostgreSql|MySql`, `-S host[:port]`, `-U` / `-P` for PostgreSQL and MySQL; the default schema is `public` for PostgreSQL and the database name for MySQL). Every template works for all three, and every `SP_*` template writes what the database has: stored procedures for SQL Server and MySQL, functions for PostgreSQL; the C# that calls them (`CS_Repo`, `API_Search`, `API_Junction`, `WinUI3_JunctionEditor`) calls each database's own kind. A database whose names are `snake_case` (`customer_item`, `customer_id`) is read with the project setting `NamingStyle=Pascal` (or `--naming Pascal`): the generated code says `CustomerItem` / `CustomerId` and the SQL keeps the real names. Only tables (not views).

## License

[MIT](LICENSE)
