# CodeGenNew

A code generator built with **Claude Code** using **WinUI3** for a developer's own box: point it at a **SQL Server, PostgreSQL, MySQL or SQLite** DB, pick a table, and generate code from **T4 templates**. It writes SQL stored procedures and functions; **C#** entities, enums, repositories, validators and **ASP.NET** minimal-API classes on **Entity Framework Core**, with **OData** and **JWT** sign-in; a **Python** API on **FastAPI** and **SQLAlchemy**; a **Rust** API on **Axum** and **sqlx**; and the screens for the same table in **Angular**, **React**, **Blazor**, **WinUI 3** and **Uno Platform**. It has a WinUI 3 desktop app (right-click a table in a TreeView) and a scriptable command-line tool, `codegen`, that does the same without the GUI.

It replaces a series of hand-rolled "write lines to a text file with substitutions and smart loops" generators with a real templating engine (T4 via `Mono.TextTemplating`), while staying simple and portable (unpackaged, no installer) and easy to extend: a new template is just a new `.tt` file — no code changes.

**CodeGenNew only reads.** It introspects schema (and, for two templates, the table's rows) and writes files to an output folder. It never creates, alters or runs anything in your DB and never edits your project — you review the output and copy it in.

See **[Docs/specs.md](Docs/specs.md)** for what the product promises and decides, and **[Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md)** for the code layout, how a run flows, the `.tt.config` keys and where a reviewer should look first.

**[Docs/QuickStart.md](Docs/QuickStart.md)** covers installing, building and running the command-line tool and the desktop app: every option, generating a whole project, essentials, project settings and the screen settings. **[Docs/Details.md](Docs/Details.md)** covers the per-stack notes (Blazor, Uno, Python, Rust), the dashboard, access mode, SQLite, names, passwords, security and the drop-downs for listed values. **[Docs/OwnProject.md](Docs/OwnProject.md)** shows how to use the templates in your own project: the block of project settings at the top of each template, what each setting changes, and what the templates assume about your plumbing.

## Status
<img width="126" height="20" alt="image" src="https://github.com/user-attachments/assets/3ea8d6d1-74b0-4b49-9821-5daf70302241" />
Project is actively being worked on. Issues and Pull Requests are welcomed.  We welcome contributions.


## Supported operating systems

| Operating system | Generator and `codegen` command-line tool | Desktop app (`CodeGenNew.App`, WinUI 3) |
|---|---|---|
| Windows | Supported (built and tested in CI) | Supported |
| Linux | Supported (built and tested in CI) | Not available |
| macOS | Supported (built and tested in CI) | Not available |

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

| Database | Connect from | Schema read from | `SP_*` templates write | Search call in generated C# | `API_Junction`, `WinUI3_JunctionEditor` |
|---|---|---|---|---|---|
| SQL Server | app and CLI | `sys.*` | stored procedures | `EXEC` | yes |
| PostgreSQL | app and CLI | `information_schema` + `pg_catalog` | functions (`RETURNS SETOF`, `plpgsql`) | `SELECT * FROM "f"(...)` | yes (call the functions) |
| MySQL | app and CLI | `information_schema` | stored procedures (`DELIMITER $$`) | `CALL p(...)` | yes (call the procedures) |
| SQLite | app and CLI (a file DB, opened read-only) | `sqlite_master`, the `pragma_*` functions and the `CREATE TABLE` text (for CHECK constraints) | nothing: SQLite has no routines (see `AccessMode`) | LINQ over the context (`CS_SearchQuery`) | yes (LINQ over the context) |

## API protocols

| Protocol | Served by | What a client gets | Used by the generated screens | Switch |
|---|---|---|---|---|
| REST | the ASP.NET, Rust and Python APIs | one route per table (`/api/customers`) with list, paging, search, read, create, update, delete and clone; JSON in camelCase, the same on every stack | Angular, React and Blazor REST screens | always on |
| OData v4 | the ASP.NET API, next to the REST routes | one entity set per table under `/odata` with `$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$select`, `$expand` and writes, so Excel, Power BI and other OData tools can read the data; sign-in, read roles and owner scoping apply as on the REST routes | Angular, React and Blazor OData screens: the several-column sort (Shift-click), the filter row and a slim `$select` grid | `OData=true` |
| None | no API | nothing: the app talks to the DB itself | the WinUI3 app: it reads the DB directly, so it uses neither REST nor OData | the WinUI3 stack without an API |

## What it generates

The templates ship in `Templates\` (the no-DB essentials groups described in [Docs/QuickStart.md](Docs/QuickStart.md) come on top). A table's right-click menu (or the CLI's `-T`) offers them grouped by the text before the first underscore.

The templates are shown in 3 grids, one for each layer of an application.

### Database layer

Stored procedures and scripts, plus the documents that describe the schema.

| Group | Template | Writes |
|---|---|---|
| `MD` | `MD_DataDictionary` | `Table.md` - one Markdown page per table: every column with type, null, default, key role, limits and notes, the foreign keys out and tables that refer to it. |
| `MD` | `MD_Erd` | `ErDiagram.md` - whole-DB: a Mermaid entity-relationship diagram (GitHub and VS Code draw it); `ErdTables` picks the tables. |
| `SP` | `SP_Insert`, `SP_Update`, `SP_Delete`, `SP_Save` | `Table_Insert.sql` … — the classic CRUD stored procedures; `Save` is insert-or-update in one. Audit, active/inactive, soft-delete and date-range columns are handled by rule. |
| `SP` | `SP_Lookup` | ID + display columns of a row and of every table it points to, so a drop-down needs no joins. |
| `SP` | `SP_EnumCasts` | `EnumCasts.sql` - whole-DB, PostgreSQL only: one guarded `CREATE CAST (text AS <enum>) ... AS ASSIGNMENT` per enum type a column uses, so EF Core can store the drop-down's value. |
| `SP` | `SP_CreateSchema` | `CreateSchema.sql` - whole-DB, SQL Server, PostgreSQL and MySQL: a create script written from the catalog (tables, columns, keys, indexes, foreign keys with their delete rule; no checks, filtered indexes, triggers or routines). Written for a project with `ApiTests` and `ApiTestContainers` that names no `ApiTestSchema`, so the test container needs no script of the project's own. |
| `SP` | `SP_Clone` | Copies a row into a new one and returns the new key (a grid's "Clone" button). |
| `SP` | `SP_Load` | Reads the table's **rows** and writes a re-runnable procedure that loads the same rows into another DB (seed data). |
| `SP` | `SP_Search` | `Table_Search.sql` — one optional `LIKE '%...%'` parameter per string column (audit columns excluded), AND-ed together, for a list screen's search box. Refuses a table with no searchable columns. |
| `SP` | `SP_LogIndex` | `Sql/LogIndexes.sql` — an index on the timestamp of each table in `LogTables` (descending where the database has it), unless one that starts with it exists; you run it, codegen never does. |
| `SP` | `SP_Junction` | `Table_Junction.sql` — List/Link/Unlink for a many-to-many **junction table** (only offered when `TableModel.IsJunctionTable` is true). |
| `SP` | `SP_KeySequence` | `KeySequence.sql` - whole-DB, SQL Server only: a key-sequence table and a `GetNextID` routine as an alternative to `IDENTITY`, for the tables the project lists in `KeySequenceTables` (their `SP_Insert` then asks the sequence for the key); each counter starts from the table's highest key. |
| `SP` | `SP_ReplicationTriggers` | `Table_ReplicationTriggers.sql` - SQL Server only: insert, update and delete triggers that copy each change to the DBs named in `ReplicationTargets` (linked server, DB). A failing target rolls back the write: review it and try it on a copy. |
| `SP` | `SP_AuditTable` | `Table_Audit.sql` - an audit trail for a table with audit columns (a `Create...` column and a `Modified...` or `Updated...` column): a `Table_History` table (the old row, `U` or `D`, when, who) and the trigger that fills it on update and delete; SQL Server, PostgreSQL, MySQL and SQLite. Re-runnable. |
| `SP` | `SP_TemporalTable` | `Table_Temporal.sql` - SQL Server only: makes the tables named in `TemporalTables` system-versioned (period columns and `SYSTEM_VERSIONING` with a `Table_History` the DB keeps). |
| `SP` | `SP_BulkUpdate` | `BulkUpdate.sql` - whole-DB, SQL Server only: a procedure that rewrites the columns named in `BulkUpdateColumns` (for example `UPPER({column})`) in every table that has them, as plain `UPDATE` statements in one transaction. |
| `SP` | `SP_Dashboard` | `DashboardQueries.sql` - whole-DB, only with `Dashboard=true`: the dashboard's statements for the DB that was read, each under a comment saying why the widget exists; run it by hand to check a widget. |
| `SP` | `SP_ForeignKeyIndexes` | `ForeignKeyIndexes.sql` - whole-DB, all DBs: a `CREATE INDEX` for every foreign key that no index starts with, in the DB's own syntax (an advisory script: it is never applied). |

### Back-end layer

Entities, repositories, the HTTP APIs and the Rust and Python servers, plus the request collections and the F# and Protocol Buffers shapes.

| Group | Template | Writes |
|---|---|---|
| `API` | `API_Crud` | `TableApi.cs` — a minimal-API class: get all, get by id, create, update, delete, each over the table's repository. |
| `API` | `API_Junction` | `TableJunctionApi.cs` — HTTP endpoints over `SP_Junction`'s 3 procedures, called straight through the `DbContext` (no repository) — the HTTP companion `TS_JunctionComponent` needs (also junction-only). |
| `API` | `API_Search` | `TableSearchApi.cs` — a `/search?...&pageNumber=&pageSize=` endpoint over `SP_Search`/`SP_SearchCount`, called straight through the `DbContext` (no repository), returning `TableSearchResult` (`Items`/`Page`/`PageSize`/`TotalCount`/`TotalPages`) — the HTTP companion the Angular/React pagination wiring needs (only offered for a table with at least one searchable column). |
| `API` | `API_Registration` | `ApiRegistration.cs` — also whole-DB: `AddGeneratedDbContext(configuration)` and `RegisterGeneratedApis()`, so a sample's `Program.cs` lists no table. |
| `API` | `API_OpenApi` | `openapi.yaml` - whole-DB: an OpenAPI 3.0 document of the API the other `API` templates write (paths, search parameters, schemas with types, lengths, CHECK limits, enum lists, required and nullable), for Kiota, NSwag, openapi-generator or a Swagger UI. |
| `API` | `API_Http` | `Table.http` - a request file per table (all, one, search, create, update, delete, clone) with example bodies shaped by the columns, for Visual Studio, VS Code and Rider; also written for a Python or Rust stack (the same routes). |
| `API` | `API_Test` | `TableApiTests.cs` - an MSTest integration test class per table in its own test project (`ApiTests=true`): add, read, change and remove a row, a 404, the search page, and with `ApiValidation` the limits. Runs the API in process over a scratch DB (SQLite: a copy of the file; others: `<PROJECT>_TEST_CONNECTION`). |
| `API` | `API_Dashboard` | `DashboardApi.cs` - `GET /api/dashboard` (every widget) and `?table=Customer` (that table's cards), cached for a minute. |
| `API` | `API_Csv` | `Apis/Csv.cs` - whole-DB, `ApiCsv=true`: `GET /api/table/export.csv` and an all-or-nothing `POST /api/table/import` for every table, no package. |
| `API` | `API_OData` | `Apis/OData.cs` - whole-DB, `OData=true`: the tables `ODataPlan` selects served as OData v4 under `/odata` (`$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$select`, `$expand`, and writes), next to the REST routes; needs the `Microsoft.AspNetCore.OData` package. |
| `API` | `API_UserDirectory` | `Apis/TableUserDirectory.cs` - whole-DB, `ApiAuth=true` with `UserTable=Employee`: the sign-in looks people up in your own table (`UserNameColumn`, `PasswordHashColumn`, `UserOwnerColumn`, `UserRoleColumn`), and `dotnet run -- set-password <name>` writes a password hash from the environment variable `NEW_PASSWORD`. |
| `API` | `API_Gates` | `Apis/Gates.cs` - whole-DB, only with `ChangeGates` or `GuardedColumns` (and `ApiAuth=true`): rule may prevent status change (e.g. status that may not move while customer is in bad standing unless the customer is exempt: 409 `change-not-allowed`) and a column only some people may change (`column-not-yours`, stamped with who and when), checked in the table's API before a save, in a clone and, for the import, by an admin-only route; `GET <route>/guards` tells the Angular screen which fields to show as text. See `Docs/TemplateNotes/API_Gates_v1.md`. |
| `CS` | `CS_Entity` | `Table.cs` — an EF Core entity: key, foreign keys, navigation properties, attributes. |
| `CS` | `CS_DbContext` | `<Context>.cs` — **one file for the whole DB** (the CLI needs no `-t`; the app has a toolbar button, Alt+D): the EF Core context as a `partial` class with a `DbSet` per table, both constructors, `OnConfiguring` and the one place the provider is chosen (`UseSqlServer`, `UseNpgsql` or `UseMySQL`; PostgreSQL's `timestamp` convention; the MySQL password from `MYSQL_PWD`). |
| `CS` | `CS_Validation` | `TableValidation.cs` — a `[MetadataType]` buddy class adding `[Required]`/`[StringLength]`/`[DataType]`/`[Display]` to the entity (works for a hand-maintained entity too; the entity class must be `partial`). |
| `CS` | `CS_Enum` | `Table.cs` — a C# enum whose members are the **rows** of a small lookup table. |
| `CS` | `CS_Repo` | `TableRepo.cs` — thin repo class over your shared generic repo, plus a `HasDuplicate<Column>` check and (for a string column) a `SuggestUnique<Column>` name generator for every column in a unique index other than the primary key. |
| `CS` | `CS_Dashboard` | `DashboardData.cs` - whole-DB, only with `Dashboard=true`: chosen widgets with their fixed SQL and `LoadAsync`, which runs them through the context; web API and the desktop page both call it. |
| `CS` | `CS_SearchQuery` | `TableSearchQuery.cs` - search as LINQ (filter by contains, sort from a fixed list, default order), written when the project uses `AccessMode=Ef` (always for SQLite); `CS_Repo` and `API_Search` call it instead of the routines. |
| `CS` | `CS_Validator` | `TableValidator.cs` - a FluentValidation `AbstractValidator<Entity>`: required, length, ranges from the name rules and CHECK constraints, CHECK lists, email / phone / URL / latitude / longitude by column name. |
| `CS` | `CS_Faker` | `TableFaker.cs` - Bogus fake-data generator per table: text of the right kind and length, numbers inside their CHECK ranges, choices from CHECK lists, nullable columns sometimes NULL, a seed for repeatable rows. |
| `CS` | `CS_Seed` | `TableSeed.cs` - rows a small table holds when you generate, as a class that adds them when the table is empty (a lookup table that is not an enum); the project's `SeedTables` names the tables, a parent before its children. |
| `CS` | `CS_SeedData` | `SeedData.cs` - whole-DB: runs the seed class of every seeded table and `app.SeedGeneratedDataAsync()` for Program.cs. |
| `CS` | `CS_Dto` | `TableDto.cs` - plain transfer class filled from any `IDataRecord`: a `Populate` method (required columns, then optional ones), an `IsIdentical` comparer over the columns that matter, a `ToString` on the display column. |
| `CS` | `CS_DtoInput` | `TableInput.cs` - `Dtos=true`: the request body of a table's create and update (only the columns a caller may set) with `ToEntity()` and `ApplyTo(entity)`; the API answers the `CS_Dto` class. |
| `CS` | `CS_Mapper` | `TableMapping.cs` - static class copying a table between its entity (`CS_Entity`) and its transfer class (`CS_Dto`): `ToDto`, `ToEntity`, list versions and `FromReader`. |
| `CS` | `CS_RoundTripTest` | `TableRoundTripTests.cs` - an MSTest class per table in the API test project (`Dtos=true` and `ApiTests=true`) that needs no database: the transfer class through a JSON round trip (every column, and with every optional column NULL), the reader constructor over a `DataTable`, `IsIdentical`, and the `CS_Mapper` copy of the entity both ways. |
| `CS` | `CS_SerializationTest` | `TableSerializationTests.cs` - an MSTest class for the three classes `CS_SerializationDtos` writes: `Basic` and `Serializable` (the custom `ISerializable` contract) through the `DataContractSerializer`, `Xml` through the `XmlSerializer`, every column compared, once with the optional columns NULL. Run by hand beside `CS_SerializationDtos`. |
| `CS` | `CS_MapperlyTest` | `TableMapperlyTests.cs` - an MSTest class for the Mapperly mapper: an entity to the transfer class and back, and `ProjectToDto`, every column compared. Needs the `Riok.Mapperly` package; name it with `CS_MapperlyMapper` in `PlanAlso`. |
| `CS` | `CS_DataShapesTest` | `TableDataShapesTests.cs` - an MSTest class for `CS_DataContractDto` (the `DataContractSerializer` round trip and the `DataRow`, reader and copy constructors) and `CS_TypedDataRow` (typed properties, `Is<Column>Null`, the `StrongTypingException`, a cloned table). Run by hand beside those two. |
| `CS` | `CS_MapperlyMapper` | `TableMapper.cs` - Mapperly counterpart of `CS_Mapper`: a `[Mapper]` partial class (`ToDto`, `ToEntity`, `ProjectToDto`) whose bodies Mapperly writes at compile time. |
| `CS` | `CS_DataContractDto` | `TableContract.cs` - `[DataContract]` class with 4 constructors (empty, from a `DataRow`, from a reader row, copy), for a WCF-style service. |
| `CS` | `CS_TypedDataRow` | `TableDataTable.cs` - typed-DataSet shape of a table: a `DataTable` with its columns and a `DataRow` with one typed property per column and `IsXNull` / `SetXNull` pairs. |
| `CS` | `CS_SerializationDtos` | `TableSerializationDtos.cs` - same data class in 3 contracts side by side: plain `[Serializable]`, custom `ISerializable`, and `XmlSerializer` attributes. |
| `CS` | `CS_EfConfiguration` | `Data/Configurations/TableConfiguration.cs` - `IEntityTypeConfiguration` per table (table, key, column details, indexes); with `EfConfigurations=true` the context applies them with `ApplyConfigurationsFromAssembly`. |
| `CS` | `CS_CqrsHandlers` | `Application/` - whole-DB, only with `PlanAlso`: commands, queries and handlers per table (create, update, delete, get by id, paged list) behind two small interfaces, and `AddApplicationHandlers()`. No package. |
| `FS` | `FS_Entity` | `Table.fs` - immutable F# record (a nullable column is an `option`) and module with `validate` and `ofStrings`, both returning `Result` that lists every problem; limits include DB's CHECK ranges. |
| `FS` | `FS_Rop` | `Rop.fs` - no table, no DB: shared `Result` helpers, error-collecting `<*>` operator and text parsers the records use. |
| `MD` | `MD_Postman`, `MD_Bruno` | `collections/<Project>.postman_collection.json` and `collections/bruno/` - whole-DB, only with `PlanAlso`: Postman and Bruno API requests (folder per table: all, one, search, add, change, remove, copy) with sample bodies and `baseUrl` variable. |
| `MD` | `MD_Dashboard` | `Dashboard.md` - whole-DB, only with `Dashboard=true`: the widgets the dashboard shows, each with schema fact that chose it and statement that fills it, then what caps left out. |
| `PROTO` | `PROTO_Message` | `Table.proto` - proto3 message per table, its key message, list messages and a CRUD service (a decimal is a string, a date a Timestamp, a nullable column optional). |
| `PY` | `PY_Model`, `PY_Schema` | `app/models/table.py` and `app/schemas/table.py` - SQLAlchemy 2.0 class and Pydantic 2 table schema, for Python stack (all DBs). |
| `PY` | `PY_Routes` | `app/routes/table.py` - FastAPI router with contract of other stacks (201 and `Location`, 400, 404, clone, search with paging and sort). |
| `PY` | `PY_Validate` | `app/validation/table.py` - `validate`, schema's rules for a row (`ApiValidation=true`). |
| `PY` | `PY_Dashboard` | `app/routes/dashboard.py` - `GET /api/dashboard` for the Python stack: the widgets and statements of `DashboardPlan`, kept for a minute, without the widgets of tables the caller's role may not read (`Dashboard=true`). |
| `PY` | `PY_Mod` | package lists and list of routers that `main.py` includes: every table's, the junction tables' (`PY_Junction`), the dashboard's and, with `ApiCsv=true`, the CSV routes (listed first, so a table's `/{id}` route does not read `export.csv` as a key). |
| `PY` | `PY_Junction` | `app/routes/table_junction.py` for a many-to-many junction table - junction routes of `API_Junction` (list with `isSelected`, link, unlink) for the Python API, so the junction screens of every UI stack work against it. |
| `PY` | `PY_Csv` | `app/routes/csv_files.py` - `GET /api/table/export.csv` and `POST /api/table/import` for every table (`ApiCsv=true`); the import is all or nothing. |
| `PY` | `PY_Gates` | `app/gates.py` - checks of `ChangeGates` and `GuardedColumns` (409 `change-not-allowed`, 403 `column-not-yours`, stamps, the clone reset) that the routes call, and `GET /api/table/guards` (`ApiAuth=true`). |
| `PY` | `PY_TestApprovals` | `tests/conftest.py` and `tests/test_approvals.py` - pytest tests of the approval routes (`PythonTests=true` with `RowApprovals`) over a scratch database: the same cases as the Rust tests and the two hooks.
| `PY` | `PY_UserDirectory` | `app/users.py` - people who sign in, read from `UserTable` of project, and `python -m app.users set-password` (`ApiAuth=true` with `UserTable`). |
| `RS` | `RS_Struct` | `src/models/table.rs` - Rust struct of a table (serde and sqlx), with JSON names other stacks send; for the Rust stack (PostgreSQL, MySQL, SQLite). |
| `RS` | `RS_Repo` | `src/repos/table.rs` - table's fixed SQL for DB that was read and sqlx functions: get, insert, update, delete, clone, and search with filters, fixed sort list and paging; with `ApiCsv=true` also `insert_all`, that adds all the rows of CSV import in one transaction. |
| `RS` | `RS_Routes` | `src/routes/table.rs` - Axum handlers with contract of other stacks (201 and `Location`, 400, 404, `/search`, `/clone`). |
| `RS` | `RS_Validate` | `src/validation/table.rs` - `validate`, schema's rules for a row (`ApiValidation=true`); answer is ASP.NET validation problem. |
| `RS` | `RS_Dashboard` | `src/routes/dashboard.rs` - Rust dashboard: widgets and statements of `DashboardPlan`, kept for a minute (`Dashboard=true`). |
| `RS` | `RS_Mod` | module lists and router that merges every table's routes, junction routes (`RS_Junction`), dashboard, CSV routes (`ApiCsv=true`) and gates router (`RS_Gates`). |
| `RS` | `RS_Junction` | `src/routes/table_junction.rs` for a many-to-many junction table - junction routes of `API_Junction` for Rust API. |
| `RS` | `RS_Csv` | `src/routes/csv_files.rs` - CSV export and import routes for every table (`ApiCsv=true`); rows are inserted in one transaction. |
| `RS` | `RS_Gates` | `src/routes/gates.rs` - same checks for Rust API, and guards routes. |
| `RS` | `RS_Test` | `tests/api/table.rs` - integration tests of a table's routes that run the crate's router in process (`RustTests=true`): add, read, change and remove a row, a 404, the search and its sorts, a copy, a unique value, the validation limits, and with `ApiAuth` the 401 and 403 answers and the owner rule of `OwnedTables`. Run by `cargo test` over a scratch database (SQLite: a copy of the file; others: `<PROJECT>_TEST_CONNECTION`).
| `RS` | `RS_TestBodies` | `tests/api/main.rs` and `tests/api/test_bodies.rs` - the request bodies of the tests, with a row added to a parent that has none (`RustTests=true`).
| `RS` | `RS_TestCsv` | `tests/api/csv_routes.rs` - tests of the CSV export and import of every table (`RustTests=true` with `ApiCsv=true`): the header, an import, a bad row that adds none, a formula text, and the administrator-only routes.
| `RS` | `RS_TestDashboard` | `tests/api/dashboard_routes.rs` - tests of `GET /api/dashboard` against the plan's widgets and strips (`RustTests=true` with a dashboard).
| `RS` | `RS_TestGates` | `tests/api/gate_routes.rs` - tests of the guards route, a guarded column and the 409 of a change gate, with the condition set through the parent's API (`RustTests=true` with `ChangeGates` or `GuardedColumns`).
| `RS` | `RS_TestApprovals` | `tests/api/approval_routes.rs` - tests of the approval routes (`RustTests=true` with `RowApprovals`): the people and a row added with SQL, a stranger, a second request, a person who may not decide, withdrawal, the four eyes and the history.
| `RS` | `RS_TestJunction` | `tests/api/table_junction.rs` - tests of the junction list, link and unlink routes of a many-to-many table (`RustTests=true`).
| `RS` | `RS_UserDirectory` | `src/users.rs` - people who sign in, read from `UserTable` of project, and `set-password` command (`ApiAuth=true` with `UserTable`). |

### UI layer

Angular, React, Blazor, Uno and WinUI3 code: models, services, screens and dashboards.

| Group | Template | Writes |
|---|---|---|
| `BLZ` | `BLZ_Model`, `BLZ_Client` | `Models/Table.cs` and `Services/TableClient.cs` - class a table's JSON becomes in Blazor app and its `HttpClient` calls (get, search with paging and sort, create, update, delete, clone). |
| `BLZ` | `BLZ_GridColumns` | `Services/GridColumns.cs` - whole-DB support code for Columns list every Blazor grid has: saved choice of columns and cut of a long text. |
| `BLZ` | `BLZ_Page` | `Pages/TablePage.razor` - Blazor screen: grid, search, sortable headers, paging, add / edit panel with drop-downs for foreign keys. |
| `BLZ` | `BLZ_Dashboard` | Blazor dashboard page (`Services/DashboardClient.cs`, `Components/DashboardWidgetView.razor`, `Pages/Dashboard.razor`; money shown as currency; with `DashboardStrip=true`, `Components/DashboardStrip.razor` above each table page). |
| `BLZ` | `BLZ_ODataClient` | `Services/TableODataClient.cs` for each table the project serves as OData: a short class on `ODataClient` (`BLZ_EssentialOData`) with entity set, key, search boxes, header sorts and REST route a clone goes through (opt in with `OData=true`). |
| `BLZ` | `BLZ_Junction` | `Pages/TableJunctionPage.razor` and `Services/TableJunctionClient.cs` for a many-to-many junction table - a routed page (`/table-links`) that picks a row of the anchor table and moves rows of the other table between an Available and a Selected list over the junction routes; each move is saved at once. |
| `BLZ` | `BLZ_ODataPage` | `BLZ_Page` run as its OData form (`Base`/`Variant` in its config): `Pages/TableODataPage.razor`, same page over table's OData client with several-column sort (Shift-click) and filter row; `ODataScreens=Replace` gives it REST page's route. |
| `BLZ` | `BLZ_Screens` | `Layout/NavMenu.razor`, `Pages/Home.razor` and `Services/ApiClients.cs` - whole-DB: the menu in `Screens` order and the registration of every client. |
| `TS` | `TS_GridColumns` | `grid-columns.ts` - whole-DB support code for the Columns list every Angular grid has: the saved choice of columns, the rule that reads it back, the cut of a long text and the `GridColumns` object a screen holds. |
| `TS` | `TS_GridSort` | `grid-sort.ts` - Angular counterpart: how the sort is saved, loaded and flipped. |
| `TS` | `TS_Screens` | `app.routes.ts` - whole-DB: Angular app's `screens` and `routes`, from same list. |
| `TS` | `TS_Dashboard` | Angular dashboard: service, page, widget component, strip and host that puts it above a screen. |
| `TS` | `TS_Model` | `models/table.ts` — Angular interface matching JSON the API really sends. |
| `TS` | `TS_Validators` | `validators/table.validators.ts` - Angular `Validators` schema states for each field (required, length, allowed values, ranges, email, phone, URL), keyed by property name. |
| `TS` | `TS_Service` | `services/table.service.ts` — `HttpClient` wrapper with same table method names. |
| `TS` | `TS_Component` | 4 files in `components/table/` — CSS, HTML, spec and TypeScript for a grid + add/edit form screen. |
| `TS` | `TS_ODataComponent` | `TS_Component` run as its OData form (`Base`/`Variant` in its config): 4 files in `components/table-odata/`, same screen over the table's OData service, with its own route and menu entry; `OData=true`, tables served as OData. |
| `TS` | `TS_ODataDetailMasterComponent` | `TS_DetailMasterComponent` run as its OData form (`Base`/`Variant` in its config): master-detail screen over table's OData service in `components/table-odata/`, with filter row, several-column sort and slim grid; child grids read child's entity set filtered by  parent's key when it is served too. `OData=true`. |
| `TS` | `TS_ODataService` | `services/table-odata.service.ts` for each table served as OData - whole-DB: a short class on `ODataCrudService` (`odata/<set>`), `OData=true`. |
| `TS` | `TS_JunctionComponent` | 4 files in `components/table-junction/` — 2-`<select multiple>` shuttle-control screen calling `API_Junction` (also junction-only). |
| `TS` | `TS_DetailMasterComponent` | 4 files in `components/table-detail-master/` — `TS_Component`'s own grid + form plus one read-only grid per child table (only offered when there's at least one). |
| `TSX` | `TSX_Screens` | `screens.tsx` - also whole-DB: React app's screen list (route, menu text, page) in menu order; `App.tsx` imports it, so a new table is a regenerate, not an edit of shell. |
| `TSX` | `TSX_ODataApi` | `api/tableODataApi.ts` for each table served as OData - whole-DB: a short module on `createODataApi` (`/odata/<set>`) with same method names as `TSX_Api`; `OData=true`. |
| `TSX` | `TSX_ODataPage` | `TSX_Page` run as its OData form (`Base`/`Variant` in its config): `pages/TableODataPage.tsx` and its test, same page over table's OData api module with several-column sort (Shift-click), filter row and slim grid; `OData=true`. |
| `TSX` | `TSX_ODataDetailMasterPage` | `TSX_DetailMasterPage` run as its OData form: master-detail page over the table's OData api module; child grid whose table is served too reads child's entity set filtered by parent's key. `OData=true`. |
| `TSX` | `TSX_GridColumns` | `components/gridColumns.tsx` - whole-DB support code for Columns list every React grid has: Columns button, saved column choices, cut of a long text. |
| `TSX` | `TSX_GridSort` | `components/gridSort.tsx` - whole-DB support code every React grid imports: sortable header, the right-click **Clear sort** menu and saved sort. |
| `TSX` | `TSX_Dashboard` | React dashboard (cards, bar lists, a sparkline, recent rows; no chart package) and, with `DashboardStrip=true`, strip above each table's grid. |
| `TSX` | `TSX_Api` | `api/tableApi.ts` — React Service: plain object of `fetch`-based functions instead of an `HttpClient` class (reuses `TS_Model`'s own output for row type — no separate React model template exists). |
| `TSX` | `TSX_Schema` | `schemas/table.ts` - zod schema of editable fields with same limits, and form type inferred from it. |
| `TSX` | `TSX_Page` | `pages/TablePage.tsx` plus a colocated `pages/__tests__/TablePage.test.tsx` — React counterpart of `TS_Component`: one function component (grid + add/edit form) using `useState`/`useEffect` instead of a class. |
| `TSX` | `TSX_LogPage` | `pages/TablePage.tsx` plus its test for a table in `LogTables`: a read-only list that opens on a date range (From, To) and the search boxes, newest first, with no Add New, Edit, Clone, Delete or form; `TSX_Page` writes nothing for such a table. |
| `TSX` | `TSX_DetailMasterPage` | `pages/TableDetailMasterPage.tsx` plus its test — `TSX_Page`'s own grid + form plus one read-only grid per child table (only offered when there's at least one). |
| `TSX` | `TSX_JunctionPage` | `components/TableJunction.tsx` plus its test — 2-`<select multiple>` shuttle-control component (an `anchorId` prop, not routed on its own) calling `API_Junction` (also junction-only). |
| `UNO` | `UNO_Model`, `UNO_Client` | `Models/Table.cs` and `Services/TableClient.cs` - class a table's JSON becomes in Uno app and its `HttpClient` calls, Blazor client's. |
| `UNO` | `UNO_MasterScreen`, `UNO_DetailScreen`, `UNO_DetailMasterScreen` | UNO list page and add / edit dialog (`Views/TableListPage.xaml`, `Views/TableDetailDialog.xaml`, and for a table with child tables `Views/TableDetailMasterDialog.xaml` with a grid per child) and their view models, reading and writing through clients. |
| `UNO` | `UNO_Dashboard`, `UNO_JunctionEditor` | UNO dashboard page over `GET /api/dashboard` (`Dashboard=true`, with `DashboardStrip=true` a strip above each list page), and 2-list editor of a many-to-many junction table over junction routes. |
| `UNO` | `UNO_ODataClient`, `UNO_ODataPage` | OData client and list page per served table (`OData=true`), with a menu entry `Table (OData)`. |
| `UNO` | `UNO_GridColumns` | `ViewModels/GridColumnStore.cs` and `Views/GridCells.cs` - whole-DB support code for the Columns menu of every Uno list page: saved choice of columns, cut of a long text, the cells of a row. |
| `UNO` | `UNO_Screens` | `MainPage.Screens.cs` and `Services/ApiClients.cs` - menu, screen switch and one client per table. |
| `WinUI3` | `WinUI3_GridColumns` | `ViewModels/GridColumnStore.cs` and `Views/GridCells.cs` - whole-DB support code for the Columns menu of every WinUI3 list page: saved choice of columns, cut of a long text, the cells of a row. |
| `WinUI3` | `WinUI3_Screens` | `MainWindow.Screens.cs` - whole-DB: other half of `partial class MainWindow` (`AddScreens()` adds menu entries, `ShowPage(tag)` shows page). |
| `WinUI3` | `WinUI3_DashboardPage` | dashboard page for desktop, built in code, with strip above each list page. |
| `WinUI3` | `WinUI3_JunctionEditor` | 3 files (View, code-behind, ViewModel) — same shuttle-control idea as a desktop `ContentDialog`, calling `SP_Junction` directly through EF Core (also junction-only). |
| `WinUI3` | `WinUI3_DirectoryListing` | 4 files — **no table and no database**: a `Page` listing files in a folder (sortable Name / Size / Modified, a filter box, Add File..., Open, Rename, Delete, Open Folder). The project settings `ListingName`, `ListingFolder` and `ListingPattern` name it; run it with the CLI without `-S`, `-d` or `-t`. |
| `WinUI3` | `WinUI3_MasterScreen` | 3 files — a `Page` listing every row (grid + Add/Edit/Delete), calling `TableRepo` (`CS_Repo`) directly. |
| `WinUI3` | `WinUI3_DetailScreen` | 3 files — a `ContentDialog` add/edit form, one field per column, drop-downs for foreign keys. |
| `WinUI3` | `WinUI3_DetailMasterScreen` | `WinUI3_DetailScreen`'s form plus one read-only child grid per table in `TableModel.ChildForeignKeys` (only offered when there's at least one). |

### Screen-family mapping across platforms

WinUI3 (and Uno, which shares its XAML) needs 3 list/form templates where Angular, React and Blazor each need only 2 or 1, because they split the job differently rather than covering different ground:

| Concept | WinUI3 | Uno (`UNO`) | Angular (`TS`) | React (`TSX`) | Blazor (`BLZ`) |
|---|---|---|---|---|---|
| List grid | `WinUI3_MasterScreen` | `UNO_MasterScreen` | folded into `TS_Component` | folded into `TSX_Page` | folded into `BLZ_Page` |
| Add/edit form | `WinUI3_DetailScreen` | `UNO_DetailScreen` | folded into `TS_Component` | folded into `TSX_Page` | folded into `BLZ_Page` |
| Master-detail (parent + child grids; editable child rows with `EditableChildren`) | `WinUI3_DetailMasterScreen` | `UNO_DetailMasterScreen` | `TS_DetailMasterComponent` | `TSX_DetailMasterPage` | folded into `BLZ_Page` |
| Junction (many-to-many editor) | `WinUI3_JunctionEditor` | `UNO_JunctionEditor` | `TS_JunctionComponent` | `TSX_JunctionPage` | `BLZ_Junction` |

WinUI3's list page and its add/edit form are 2 separate templates because the idiomatic WinUI3 pattern is a `ContentDialog` opened from the grid page. `TS_Component` and `TSX_Page` each generate a single file that already contains both the grid *and* an inline add/edit form on one screen, matching how a hand-written CRUD screen in those frameworks is usually built. Uno runs the same XAML as WinUI3, so its templates mirror the WinUI3 ones but read and write through the generated API instead of the database. Blazor, like Angular and React, fits the list and the form into one page (`BLZ_Page`, which also draws the child grids of a master-detail table); its junction page is a template of its own, routed at `/<table>-links`. Every platform ends up with the same coverage — list, add/edit, master-detail, junction — the web stacks just fit 2 or 3 of WinUI3's roles into one generated file.

Templates carry a version in file name (`SP_Save_v1.tt`). The menu shows only newest version of each; older ones stay on disk. Files you customize are never overwritten by an update — new shipped copy is written beside yours as `<name>.new`.

## Solution structure

```
CodeGenNew.slnx
├── CodeGenNew.App                 WinUI 3 UI, MVVM via CommunityToolkit.Mvvm (TreeView, Connection/Location/Template Management dialogs)
├── CodeGenNew.Cli                 Scriptable console entry point (bypasses the WinUI 3 app)
├── CodeGenNew.Connections         Connect + test SQL Server, PostgreSQL and MySQL connections
├── CodeGenNew.SchemaIntrospection Reads tables/columns/PK/FK from DB; builds TableModel
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

The suite needs **no database** (live PostgreSQL and MySQL integration tests run only when `CODEGENNEW_PG_*` / `CODEGENNEW_MYSQL_*` environment variables name a server, and are skipped otherwise). It covers the file-writing engine (`@@@FILE` splitting, unsafe paths, output naming), template discovery and versioning, the seeder (created / refreshed / kept-customized, and that **every file in `Templates\` is actually shipped**), the literal and display-column helpers, and it runs every shipped template against hand-built tables to check what it writes — the API, entity, validation, enum, repository, model, service, component and React-page rules, each template's refusals (composite keys, enum tables), and that changing a project setting changes the output. Most of the time goes to compiling templates.

## Functionality Status

SQL Server, PostgreSQL and MySQL are supported, in the command-line tool and in the desktop app (choose the database type in the connection dialog; on the command line `--provider SqlServer|PostgreSql|MySql`, `-S host[:port]`, `-U` / `-P` for PostgreSQL and MySQL; the default schema is `public` for PostgreSQL and database name for MySQL). Every template works for all three, and every `SP_*` template writes what the database has: stored procedures for SQL Server and MySQL, functions for PostgreSQL; the C# that calls them (`CS_Repo`, `API_Search`, `API_Junction`, `WinUI3_JunctionEditor`) calls each database's own kind. A database whose names are `snake_case` (`customer_item`, `customer_id`) is read with the project setting `NamingStyle=Pascal` (or `--naming Pascal`): the generated code says `CustomerItem` / `CustomerId` and the SQL keeps the real names. Only tables (not views).

## License

[MIT](LICENSE)
