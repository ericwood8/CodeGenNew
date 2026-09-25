# CodeGenNew

A C# code generator for a developer's own box: point it at a SQL Server database, pick a table, and generate code from **T4 templates** — SQL stored procedures, C# entities, enums, repositories and minimal-API classes, and the Angular TypeScript model, service and screen for the same table. It has a WinUI 3 desktop app (right-click a table in a TreeView) and a scriptable command-line tool, `codegen`, that does the same without the GUI.

It replaces a series of hand-rolled "write lines to a text file with substitutions and smart loops" generators with a real templating engine (T4 via `Mono.TextTemplating`), while staying simple and portable (unpackaged, no installer) and easy to extend: **a new template is just a new `.tt` file** — no code changes.

**CodeGenNew only reads.** It introspects schema (and, for two templates, the table's rows) and writes files to an output folder. It never creates, alters or runs anything in your database and never edits your project — you review the output and copy it in.

See **[Docs/specs.md](Docs/specs.md)** for the full specification: architecture, configuration file formats, the `TableModel`/`ColumnModel` schema, the special-logic column detection, every template's rules, and the CLI.

## What it generates

Twenty-two templates ship in `Templates\`. A table's right-click menu (or the CLI's `-T`) offers them grouped by the text before the first underscore.

| Group | Template | Writes |
|---|---|---|
| `SP` | `SP_Insert`, `SP_Update`, `SP_Delete`, `SP_Save` | `Table_Insert.sql` … — the classic CRUD stored procedures; `Save` is insert-or-update in one. Audit, active/inactive, soft-delete and date-range columns are handled by rule. |
| `SP` | `SP_Lookup` | ID + display columns of a row and of every table it points to, so a drop-down needs no joins. |
| `SP` | `SP_Clone` | Copies a row into a new one and returns the new key (a grid's "Clone" button). |
| `SP` | `SP_Load` | Reads the table's **rows** and writes a re-runnable procedure that loads the same rows into another database (seed data). |
| `SP` | `SP_Junction` | `Table_Junction.sql` — List/Link/Unlink for a many-to-many **junction table** (only offered when `TableModel.IsJunctionTable` is true). |
| `API` | `API_Crud` | `TableApi.cs` — a minimal-API class: get all, get by id, create, update, delete, each over the table's repository. |
| `API` | `API_Junction` | `TableJunctionApi.cs` — HTTP endpoints over `SP_Junction`'s three procedures, called straight through the `DbContext` (no repository) — the HTTP companion `TS_JunctionComponent` needs (also junction-only). |
| `CS` | `CS_Entity` | `Table.cs` — an EF Core entity: key, foreign keys, navigation properties, attributes. |
| `CS` | `CS_Enum` | `Table.cs` — a C# enum whose members are the **rows** of a small lookup table. |
| `CS` | `CS_Repo` | `TableRepo.cs` — the thin repository class over your shared generic repository. |
| `TS` | `TS_Model` | `models/table.ts` — an Angular interface matching the JSON the API really sends. |
| `TS` | `TS_Service` | `services/table.service.ts` — the `HttpClient` wrapper with the same method names on every table. |
| `TS` | `TS_Component` | Four files in `components/table/` — CSS, HTML, spec and TypeScript for a grid + add/edit form screen. |
| `TS` | `TS_JunctionComponent` | Four files in `components/table-junction/` — a two-`<select multiple>` shuttle-control screen calling `API_Junction` (also junction-only). |
| `TS` | `TS_DetailMasterComponent` | Four files in `components/table-detail-master/` — `TS_Component`'s own grid + form plus one read-only grid per child table (only offered when there's at least one). |
| `WinUI3` | `WinUI3_JunctionEditor` | Three files (View, code-behind, ViewModel) — the same shuttle-control idea as a desktop `ContentDialog`, calling `SP_Junction` directly through EF Core (also junction-only). |
| `WinUI3` | `WinUI3_MasterScreen` | Three files — a `Page` listing every row (grid + Add/Edit/Delete), calling `TableRepo` (`CS_Repo`) directly. |
| `WinUI3` | `WinUI3_DetailScreen` | Three files — a `ContentDialog` add/edit form, one field per column, drop-downs for foreign keys. |
| `WinUI3` | `WinUI3_DetailMasterScreen` | `WinUI3_DetailScreen`'s form plus one read-only child grid per table in `TableModel.ChildForeignKeys` (only offered when there's at least one). |

Templates carry a version in the file name (`SP_Save_v1.tt`). The menu shows only the newest version of each; older ones stay on disk. Files you customize are never overwritten by an update — the new shipped copy is written beside yours as `<name>.new`.

## Quick start

**Prerequisites:** the [.NET 10 SDK](https://dotnet.microsoft.com/) (the templates are compiled at run time, so a real SDK must be installed — not just the runtime) and a reachable SQL Server. The desktop app also needs Windows 10/11.

```
dotnet build CodeGenNew.slnx
```

**Command line** (the built exe is `CodeGenNew.Cli\bin\Debug\net10.0\codegen.exe`):

```
codegen -S MYSERVER -d MyDatabase -s dbo -t Holiday -T API_Crud.tt -E -o C:\Work\MyApp\Api\Apis
```

| Option | Meaning |
|---|---|
| `-S`, `-d`, `-s`, `-t` | server, database, schema (default `dbo`), table |
| `-T` | template file name; without a version (`SP_Save.tt`) means the latest, `SP_Save_v1.tt` pins that version |
| `-E` | Windows authentication (or `-U user` and `-P password`; the password is prompted for if omitted) |
| `-o` | output folder (default: `Output` from `Settings.json`) |

**Desktop app:** run `CodeGenNew.App`, choose *Connect*, expand the database, right-click a table and pick a template. *Output Location* sets the output folder; *Templates* adds, renames, edits and deletes templates.

The first run creates `Templates\`, `Output\` and `SpecialLogicColumns.config` next to the exe from copies embedded in it, so **copying the exe folder is all the deployment there is**.

### Templates that write several files

`TS_Component` writes four files and the `TS_` templates write into sub-folders. A template does that by starting each file with a marker line, `@@@FILE relative/path@@@` (see specs.md §8.2). For these templates point the output folder at the **root of the target project** — for Angular, its `src\app` folder — and the files land in the right places:

```
codegen -S MYSERVER -d MyDatabase -t Holiday -T TS_Component.tt -E -o C:\Work\MyApp\src\app
```

## Using the templates in your own project

The templates were written against the TimeEntry sample project (`TimeEntry.Common`, `TimeEntry.ApiService`, an Angular `TimeEntryUI`). **The names that belong to a project are a block at the very top of each template**, under a banner:

```
// ==========================================================================================
// PROJECT SETTINGS -- the lines to change to use this template in your own project ...
// ==========================================================================================
string apiNamespace = "TimeEntry.ApiService.Apis";
string contextType = "TimeEntryContext";
string[] usings = { };          // namespaces the generated file must "using"
string[] noRepositoryTables = { "ClearanceType", ... };
```

Open the `.tt` file (or the app's *Templates* button → Edit), change those lines, and save; the next generation uses them — there is nothing to rebuild.

| Setting | In | Tells the template |
|---|---|---|
| `apiNamespace`, `entityNamespace`, `enumNamespace`, `repositoryNamespace` | `API_Crud`, `CS_Entity`, `CS_Enum`, `CS_Repo` | the `namespace` the generated file declares |
| `usings` | `API_Crud`, `CS_Entity`, `CS_Repo` | extra `using` lines to write (leave empty if your project uses global usings) |
| `contextType` | `API_Crud`, `CS_Repo` | your `DbContext` class |
| `baseEntity`, `baseNameActiveEntity` | `CS_Entity` | the base classes your entities derive from |
| `modelsFolder`, `servicesFolder`, `componentsFolder`, `apiPrefix` | `TS_*` | where the Angular files go and the API URL prefix |
| `noNavigationTables`, `noRepositoryTables`, `noApiTables`, `noLookupParents` | several | tables that are enums, lookups or have no entity, and so get no navigation property, repository, API or screen |

The templates also assume the shape of the sample project's plumbing: a generic repository with `GetAll`, `GetByIdAsync`, `ExistsAsync` and `DeleteAsync`, and a `BaseApi<T>` that registers routes. Each template's header comment says what it assumes and what it deliberately does **not** write (collection navigations, hand-written business rules, detail grids), so you know what stays yours.

Column-name rules (which column means "created date", "is active", a display name, …) live in `SpecialLogicColumns.config`; edit it to match your naming.

## Solution structure

```
CodeGenNew.slnx
├── CodeGenNew.App                 WinUI 3 UI, MVVM via CommunityToolkit.Mvvm (TreeView, Connection/Location/Template Management dialogs)
├── CodeGenNew.Cli                 Scriptable console entry point (bypasses the WinUI 3 app)
├── CodeGenNew.Connections         Connect + test SQL Server connections (SQL Login & Windows Auth)
├── CodeGenNew.SchemaIntrospection Reads tables/columns/PK/FK from the database; builds TableModel
├── CodeGenNew.TemplateEngine      Wraps Mono.TextTemplating; discovers/runs .tt templates; writes output files; seeds defaults
├── CodeGenNew.Core                Shared model classes (TableModel, ColumnModel, ForeignKeyModel, settings)
├── CodeGenNew.Tests               MSTest suite (see below)
├── Templates\                     The shipped .tt templates and their .tt.config files
└── Docs\specs.md                  The specification
```

## Tests

```
dotnet test CodeGenNew.Tests\CodeGenNew.Tests.csproj
```

The suite needs **no database**. It covers the file-writing engine (`@@@FILE` splitting, unsafe paths, output naming), template discovery and versioning, the seeder (created / refreshed / kept-customized, and that **every file in `Templates\` is actually shipped**), the literal and display-column helpers, and it runs every shipped template against hand-built tables to check what it writes — the API, entity, enum, repository, model, service and component rules, each template's refusals (composite keys, enum tables), and that changing a project setting changes the output. It takes about a minute, mostly compiling templates.

## Known dependencies

| Dependency | Used by | Notes |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/) | all projects | Target framework, and required at run time to compile templates. |
| [Windows App SDK 2.4.0](https://github.com/microsoft/WindowsAppSDK) (WinUI 3) | `CodeGenNew.App` only | GUI framework, unpackaged (no MSIX); its native runtime is bundled. .NET itself is framework-dependent — see specs.md §3. |
| [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm) 8.4.0 | `CodeGenNew.App` only | MVVM helpers (`[ObservableProperty]`, `[RelayCommand]`), classic backing-field style — see specs.md §9. |
| [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient) | `Connections`, `SchemaIntrospection` | SQL Server ADO.NET provider. |
| [Mono.TextTemplating](https://github.com/mono/t4) | `CodeGenNew.TemplateEngine` | In-process T4 engine that runs outside Visual Studio (EF Core uses it for `dotnet ef dbcontext scaffold`). |
| [MSTest](https://www.nuget.org/packages/MSTest) 4 | `CodeGenNew.Tests` only | Test framework. |
| A reachable SQL Server | runtime | Read-only access is all the tool ever needs. Not bundled. |

CLI argument parsing is hand-rolled rather than pulling in a library, given the small number of flags (specs.md §10).

## Status

v1 is working: the desktop app, the CLI and fourteen of the twenty-two templates were each verified against a real database, and the generated code was compiled (and, for the Angular files, run) in the sample project. The junction-table family (`TableModel.IsJunctionTable`, added after the real `ProvidenceOgas.dbo.NameBaseGroupXref` table turned out to use a surrogate identity key rather than the composite-key shape first assumed) was verified per template: `SP_Junction` was deployed and exercised (List/Link/Unlink, including duplicate-link idempotency) against that real table via a disposable scratch copy; `API_Junction` was dropped into the real TimeEntryServer project and built there (0 errors) before being removed again; `TS_JunctionComponent` was dropped into the real TimeEntryUI project, where `ng test` compiled and ran it for real (13/13 passing) before being removed again. `WinUI3_JunctionEditor`'s output was rendered against the same real table and hand-reviewed, but not compiled in a live WinUI3 project — there is no existing WinUI3 desktop client in this ecosystem to drop it into.

The desktop CRUD-screen family (`TableModel.ChildForeignKeys`, `WinUI3_MasterScreen`, `WinUI3_DetailScreen`, `WinUI3_DetailMasterScreen`) was generated live against `ERICSMINIPC\ProvidenceOgas.dbo.Products` — a table whose `DOIProductTypeID`/`RDProductTypeID` columns each carry two separate foreign-key constraints to the same parent, which surfaced and fixed a duplicate-key crash in all three templates' column-lookup logic before this table was used as the test case — and against `Products`' 12 real child tables for `WinUI3_DetailMasterScreen`'s per-child grid (whose columns are discovered at run time via EF Core's own entity metadata, not known when the file is generated — see specs.md §11). Like `WinUI3_JunctionEditor`, none of the three were compiled in a live WinUI3 project.

`TS_DetailMasterComponent` (the Angular counterpart of `WinUI3_DetailMasterScreen`) was generated live against the real `TimeEntry` database's `E_TimeSheet`/`E_TimeSheetDetail` pair and dropped into the real TimeEntryUI project, where `ng test` compiled and ran it for real (12/12 passing) — but only after temporarily setting aside the hand-maintained `components/timesheet/` and swapping in a freshly-generated `timesheet.ts` model: the real, hand-written model types its date column as `Date`, not the `string` every `TS_*` template assumes (`Docs/specs.md` §11), a pre-existing mismatch this exercise surfaced rather than something the new template caused. Both were restored afterward and `git status` confirmed clean.

Every `TS_*` template was then run twice more against a fresh ~50 real tables total, sampled from 16 of the ~35 databases on the dev SQL Server (legacy schemas spanning `dbo` and non-`dbo` schemas, composite keys, natural `varchar`/`char` keys, reserved-word table names, several with junctions and child tables) — no crashes across either round, but several tables' menu options were only refused once generation was attempted (a composite or natural-key table offered `TS_Service`/`TS_Component`/etc. that could never work for it). Fixed by a new per-template `RequiredPrimaryKeyShape` restriction (`TableModel`/`TableSummary.PrimaryKeyShape`, `Docs/specs.md` §5.3) so the menu (and the CLI) now leave a wrongly-shaped table's key off the option list entirely, applied to every `TS_*`/`API_Crud`/WinUI3 template that needs a specific key shape.

A related mismatch was found by inspecting a real hand-maintained API rather than generating against one: `TS_Service`/`TS_Component`/`TS_DetailMasterComponent` always generate a `getAll()` call, but a "name/active" table's real backend (`API_Crud.tt` has always refused to generate one, since it needs duplicate-name checks) is hand-maintained and, confirmed against the real `TimeEntry` database's `DepartmentTeamApi.cs`, commonly has **no plain `getAll()` route at all** — only a parent-scoped one. Fixed the same way: a new `RequiresNotNameActiveTable` restriction (`TableModel`/`TableSummary.IsNameActiveTable`, one canonical rule replacing four identical inline copies across `API_Crud.tt`/`CS_Entity.tt`/`CS_Repo.tt`/the WinUI3 family), applied to every template that assumes a plain `getAll()`-shaped backend — `TS_Service`, `TS_Component`, `TS_DetailMasterComponent`, `API_Crud`, and the WinUI3 CRUD-screen family all now leave a name/active table's option off the menu, verified live against the real `DepartmentTeam` table.

Only SQL Server is supported; only tables (not views).

## License

[MIT](LICENSE)
