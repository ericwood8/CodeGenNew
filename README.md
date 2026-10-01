# CodeGenNew

A C# code generator for a developer's own box: point it at a SQL Server database, pick a table, and generate code from **T4 templates** — SQL stored procedures, C# entities, enums, repositories and minimal-API classes, the Angular TypeScript model, service and screen for the same table, and a React counterpart of that same screen family. It has a WinUI 3 desktop app (right-click a table in a TreeView) and a scriptable command-line tool, `codegen`, that does the same without the GUI.

It replaces a series of hand-rolled "write lines to a text file with substitutions and smart loops" generators with a real templating engine (T4 via `Mono.TextTemplating`), while staying simple and portable (unpackaged, no installer) and easy to extend: **a new template is just a new `.tt` file** — no code changes.

**CodeGenNew only reads.** It introspects schema (and, for two templates, the table's rows) and writes files to an output folder. It never creates, alters or runs anything in your database and never edits your project — you review the output and copy it in.

See **[Docs/specs.md](Docs/specs.md)** for the full specification: architecture, configuration file formats, the `TableModel`/`ColumnModel` schema, the special-logic column detection, every template's rules, and the CLI.

## Status
<img width="126" height="20" alt="image" src="https://github.com/user-attachments/assets/3ea8d6d1-74b0-4b49-9821-5daf70302241" />
Project is actively being worked on. Issues and Pull Requests are welcomed.  We welcome contributions.

## What it generates

Twenty-nine templates ship in `Templates\`. A table's right-click menu (or the CLI's `-T`) offers them grouped by the text before the first underscore.

| Group | Template | Writes |
|---|---|---|
| `SP` | `SP_Insert`, `SP_Update`, `SP_Delete`, `SP_Save` | `Table_Insert.sql` … — the classic CRUD stored procedures; `Save` is insert-or-update in one. Audit, active/inactive, soft-delete and date-range columns are handled by rule. |
| `SP` | `SP_Lookup` | ID + display columns of a row and of every table it points to, so a drop-down needs no joins. |
| `SP` | `SP_Clone` | Copies a row into a new one and returns the new key (a grid's "Clone" button). |
| `SP` | `SP_Load` | Reads the table's **rows** and writes a re-runnable procedure that loads the same rows into another database (seed data). |
| `SP` | `SP_Search` | `Table_Search.sql` — one optional `LIKE '%...%'` parameter per string column (audit columns excluded), AND-ed together, for a list screen's search box. Refuses a table with no searchable columns. |
| `SP` | `SP_Junction` | `Table_Junction.sql` — List/Link/Unlink for a many-to-many **junction table** (only offered when `TableModel.IsJunctionTable` is true). |
| `API` | `API_Crud` | `TableApi.cs` — a minimal-API class: get all, get by id, create, update, delete, each over the table's repository. |
| `API` | `API_Junction` | `TableJunctionApi.cs` — HTTP endpoints over `SP_Junction`'s three procedures, called straight through the `DbContext` (no repository) — the HTTP companion `TS_JunctionComponent` needs (also junction-only). |
| `API` | `API_Search` | `TableSearchApi.cs` — a `/search?...&pageNumber=&pageSize=` endpoint over `SP_Search`/`SP_SearchCount`, called straight through the `DbContext` (no repository), returning `TableSearchResult` (`Items`/`Page`/`PageSize`/`TotalCount`/`TotalPages`) — the HTTP companion the Angular/React pagination wiring needs (only offered for a table with at least one searchable column). |
| `CS` | `CS_Entity` | `Table.cs` — an EF Core entity: key, foreign keys, navigation properties, attributes. |
| `CS` | `CS_Validation` | `TableValidation.cs` — a `[MetadataType]` buddy class adding `[Required]`/`[StringLength]`/`[DataType]`/`[Display]` to the entity (works for a hand-maintained entity too; the entity class must be `partial`). |
| `CS` | `CS_Enum` | `Table.cs` — a C# enum whose members are the **rows** of a small lookup table. |
| `CS` | `CS_Repo` | `TableRepo.cs` — the thin repository class over your shared generic repository, plus a `HasDuplicate<Column>` check and (for a string column) a `SuggestUnique<Column>` name generator for every column in a unique index other than the primary key. |
| `TS` | `TS_Model` | `models/table.ts` — an Angular interface matching the JSON the API really sends. |
| `TS` | `TS_Service` | `services/table.service.ts` — the `HttpClient` wrapper with the same method names on every table. |
| `TS` | `TS_Component` | Four files in `components/table/` — CSS, HTML, spec and TypeScript for a grid + add/edit form screen. |
| `TS` | `TS_JunctionComponent` | Four files in `components/table-junction/` — a two-`<select multiple>` shuttle-control screen calling `API_Junction` (also junction-only). |
| `TS` | `TS_DetailMasterComponent` | Four files in `components/table-detail-master/` — `TS_Component`'s own grid + form plus one read-only grid per child table (only offered when there's at least one). |
| `TSX` | `TSX_Api` | `api/tableApi.ts` — the React counterpart of `TS_Service`: a plain object of `fetch`-based functions instead of an `HttpClient` class (reuses `TS_Model`'s own output for the row type — no separate React model template exists). |
| `TSX` | `TSX_Page` | `pages/TablePage.tsx` plus a colocated `pages/__tests__/TablePage.test.tsx` — the React counterpart of `TS_Component`: one function component (grid + add/edit form) using `useState`/`useEffect` instead of a class. |
| `TSX` | `TSX_DetailMasterPage` | `pages/TableDetailMasterPage.tsx` plus its test — `TSX_Page`'s own grid + form plus one read-only grid per child table (only offered when there's at least one). |
| `TSX` | `TSX_JunctionPage` | `components/TableJunction.tsx` plus its test — a two-`<select multiple>` shuttle-control component (an `anchorId` prop, not routed on its own) calling `API_Junction` (also junction-only). |
| `WinUI3` | `WinUI3_JunctionEditor` | Three files (View, code-behind, ViewModel) — the same shuttle-control idea as a desktop `ContentDialog`, calling `SP_Junction` directly through EF Core (also junction-only). |
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

WinUI3's list page and its add/edit form are two separate templates because the idiomatic WinUI3 pattern is a `ContentDialog` opened from the grid page. `TS_Component` and `TSX_Page` each generate a single file that already contains both the grid *and* an inline add/edit form on one screen, matching how the real reference projects (`TimeEntryUI`'s `holiday.component.*`, `CriticalViewer`'s own pages) actually build a CRUD screen. All three platforms end up with the same coverage — list, add/edit, master-detail, junction — Angular and React just fit two of WinUI3's roles into one generated file.

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
| `--project` | a project settings file, `Projects\<name>.config` next to the exe (see below) |

**Project settings.** Templates carry their own namespaces and context name, so a project of your own would otherwise need each template edited. Put `ProjectName=InvoiceSystem` in `Projects\InvoiceSystem.config` (one `key=value` per line, `#` comments, comma-separated lists) and pass `--project InvoiceSystem`: every namespace you don't list is derived from the name (`InvoiceSystem.App.Views`, `InvoiceSystemContext`, ...). The keys are `ProjectName`, `ViewNamespace`, `ViewModelNamespace`, `ContextName`, `ContextNamespace`, `ApiNamespace`, `EnumNamespace`, `RepoNamespace`, `EntityNamespace`, `MinYear`, `MaxYear`, `ViewsFolder`, `ViewModelsFolder`, `Usings`, `NoLookupParents`, `NoRepositoryTables`, `NoApiTables` and `NoNavigationTables`. A flag (`--view-ns`, `--viewmodel-ns`, `--context`, `--context-ns`, `--api-ns`, `--enum-ns`, `--repo-ns`, `--entity-ns`, `--project-name`, `--min-year`, `--max-year`) overrides the file for one run. Precedence: flag, then file, then the name-derived default, then the template's built-in value; with no `--project` a template generates exactly as before. **Enum tables.** Which tables are C# enums (no entity, repository or API of their own) is decided per project, not hard-coded: list them in `EnumTables`, or leave it out and the schema decides. A table is treated as an enum when it has at most `EnumMaxRows` rows (default 25) and either looks like a bare lookup table (one integer key, no foreign keys, a text column) or its name ends in one of `EnumNameSuffixes` (default `Type, Types, Code, Codes, Status, Kind`, so `LeaveType` and `MeterTypeCodes` count) and it has an integer key and a text column. `NoLookupParents`, `NoRepositoryTables`, `NoApiTables` and `NoNavigationTables` still work, each overriding `EnumTables` for its own question only. With no project chosen the templates keep their built-in lists.

**Other TypeScript and entity settings.** `HiddenParents` lists the tables whose foreign key columns the TypeScript forms hide (empty for a project that doesn't list any); `ModelFileOverrides=Table=file,Table2=file2` names model files that don't follow the usual convention (the table's base name, lower-cased, which is also what `TS_Model` writes); `BaseEntity` and `BaseNameActiveEntity` name the base classes of generated entities.

**Whole-number inputs.** Every whole-number column that is not a foreign key is edited in a number box (WinUI3 `NumberBox`) or a number input with `min`/`max` (Angular, React), limited to the range its name and SQL type imply: `Year`/`FiscalYear` and similar run from `MinYear` to `MaxYear` (default 2000-2100), `Month`/`Mth` 1-12, `Day` 1-31, `Quarter` 1-4, `Week` 1-53, `*Percent`/`*Pct` 0-100, `*Qty`/`*Count` and `SortOrder` from 0, and any other integer the limits of its SQL type. The name list is deliberately narrow (a `BirthYear` is not limited to the year range). `CS_Validation` writes the same limits as `[Range]` for the named kinds. In the WinUI3 ViewModels such a field is a `double` (`NaN` = blank) that is checked for being whole and in range before it is converted back.

**Currency inputs.** A `money`/`smallmoney` column, or a `decimal` column whose name says money (`Amount`, `Amt`, `Price`, `Cost`, `Fee`, `Charge`, `Salary`, `Wage`, `Payment`, `Balance`, or `Total...` unless it names another unit such as `TotalHours`), is edited in a WinUI3 `NumberBox` with a `CurrencyFormatter` set in the dialog's code-behind, using the project's `CurrencyCode` (default `USD`) and the column's own scale for the decimals (two for `money`). The ViewModel holds it as a `double` and the save converts it to a `decimal` rounded to that scale. A `Quantity` stays a plain text box. The list screens show money in the Windows regional currency format; the Angular and React grids use the currency pipe and `toLocaleString` with the project's code.

In the desktop app the *Project* button (Alt+P) edits these files and picks the one to generate with.

**Desktop app:** run `CodeGenNew.App`, choose *Connect*, expand the database, right-click a table and pick a template. *Output Location* sets the output folder; *Templates* adds, renames, edits and deletes templates.

The first run creates `Templates\`, `Output\` and `SpecialLogicColumns.config` next to the exe from copies embedded in it, so **copying the exe folder is all the deployment there is**.

### Templates that write several files

`TS_Component` writes four files and the `TS_`/`TSX_` templates write into sub-folders. A template does that by starting each file with a marker line, `@@@FILE relative/path@@@` (see specs.md §8.2). For these templates point the output folder at the **root of the target project** — for Angular, its `src\app` folder; for React, its `src` folder — and the files land in the right places:

```
codegen -S MYSERVER -d MyDatabase -t Holiday -T TS_Component.tt -E -o C:\Work\MyApp\src\app
```

## Using the templates in your own project

The `SP`/`API`/`CS`/`TS`/`WinUI3` templates were written against the TimeEntry sample project (`TimeEntry.Common`, `TimeEntry.ApiService`, an Angular `TimeEntryUI`); the `TSX` family was written against a real React project instead, `CriticalViewer` (a small movie-review app), the same "does this actually match a real app's conventions" role `TimeEntryUI` plays for Angular. **The names that belong to a project are a block at the very top of each template**, under a banner:

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
| `apiNamespace`, `entityNamespace`, `enumNamespace`, `repositoryNamespace` | `API_Crud`, `API_Search`, `CS_Entity`, `CS_Validation`, `CS_Enum`, `CS_Repo` | the `namespace` the generated file declares |
| `usings` | `API_Crud`, `API_Search`, `CS_Entity`, `CS_Validation`, `CS_Repo` | extra `using` lines to write (leave empty if your project uses global usings) |
| `contextType` | `API_Crud`, `API_Search`, `CS_Repo` | your `DbContext` class |
| `baseEntity`, `baseNameActiveEntity` | `CS_Entity` | the base classes your entities derive from |
| `modelsFolder`, `servicesFolder`, `componentsFolder`, `apiPrefix` | `TS_*` | where the Angular files go and the API URL prefix |
| `pagesFolder`, `apiFolder`, `componentsFolder`, `apiPrefix` | `TSX_*` | where the React files go and the API URL prefix |
| `noNavigationTables`, `noRepositoryTables`, `noApiTables`, `noLookupParents` | several | tables that are enums, lookups or have no entity, and so get no navigation property, repository, API or screen |

The templates also assume the shape of the sample project's plumbing: a generic repository with `GetAll`, `GetByIdAsync`, `ExistsAsync` and `DeleteAsync`, a `BaseApi<T>` that registers routes, Angular's own `HttpClient`, and — for `TSX_*` only, since React has no framework-supplied HTTP client — a shared `request<T>(path, init)`/`ApiError` pair the target project must already have (`import { request } from './client'`; `CriticalViewer`'s real `src/api/client.ts` is exactly this shape). Each template's header comment says what it assumes and what it deliberately does **not** write (collection navigations, hand-written business rules, detail grids), so you know what stays yours.

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

The suite needs **no database**. It covers the file-writing engine (`@@@FILE` splitting, unsafe paths, output naming), template discovery and versioning, the seeder (created / refreshed / kept-customized, and that **every file in `Templates\` is actually shipped**), the literal and display-column helpers, and it runs every shipped template against hand-built tables to check what it writes — the API, entity, validation, enum, repository, model, service, component and React-page rules, each template's refusals (composite keys, enum tables), and that changing a project setting changes the output. It takes about a minute, mostly compiling templates.

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

## Functionality Status

Only MS SQL Server is supported; only tables (not views).

## License

[MIT](LICENSE)
