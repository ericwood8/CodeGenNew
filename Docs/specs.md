# CodeGenNew: specification

What CodeGenNew is for, the rules it keeps, and what it does today. How the code is organised is in [ARCHITECTURE.md](ARCHITECTURE.md); how to use it is in [GettingStarted.md](GettingStarted.md) and the [README](../README.md); the configuration files and column rules are in [Reference.md](Reference.md); the reasoning behind each template is in [TemplateNotes](TemplateNotes).

## 1. Purpose

A developer points CodeGenNew at a database that already exists (database-first), picks a table or a whole project, and gets source files back: SQL routines, C# entities, repositories and APIs, and the screens of a front end. The files are written from **T4 templates** that are plain text beside the program, so changing what is generated, or adding a kind of file, means editing or adding a `.tt` file and not changing the program.

It runs as a WinUI 3 desktop app (right-click a table), as a command-line tool (`codegen`, installable as a .NET tool) and as a library.

**Not goals.** It is not an ORM, not a migration tool, not a runtime framework and not a designer. It does not try to generate an application nobody needs to touch: it writes the repetitive part and says clearly which parts stay hand-written.

## 2. Rules that do not bend

1. **Read-only against everything outside its own output folder.** CodeGenNew only reads schema metadata (and the rows of a small table when a template asks for them) and writes files to a folder. It never creates, alters or runs anything in the database, never deploys what it generated, and never edits another project's files. Generated SQL is a deliverable for a person to review and apply.
2. **No secrets on disk.** A database password is typed, passed on the command line or read from the environment for that run. It is never saved by the app, the CLI or a template.
3. **Rules are data, not code.** What a template does for a soft-delete pair, an audit date or a lookup table is decided when the schema is read and stored on the model; a template branches on a property and never re-scans names itself.
4. **Templates are the extension point.** Behaviour that belongs to a kind of file lives in its template; behaviour that belongs to a project lives in the project settings; behaviour shared by many templates lives in one helper in `CodeGenNew.Core`.
5. **Output is reviewable and reproducible.** The same schema, settings and templates give the same files, byte for byte. A run can be previewed (`--dry-run`, `--diff`), a project's files are tracked so stale ones are found, and a file is only rewritten when its text changed.
6. **A template never leaves a shipped sample different without saying so.** Seven sample applications are regenerated from the templates; a template change that alters them is a deliberate, reviewed change.

## 3. What it reads

SQL Server, PostgreSQL, MySQL and SQLite, through one schema model. Whatever the source, a table arrives as a `TableModel` (columns, keys, foreign keys in both directions, defaults, CHECK ranges and lists, column and table comments) and a database as a `DatabaseModel`. Names can be converted (`NamingStyle=Pascal` turns `customer_item` into `CustomerItem`) while the SQL keeps the real names. A set of name-pattern rules, kept in `SpecialLogicColumns.config`, classifies columns that mean something (an active flag, a start and end date, audit dates and users, a soft delete, display columns); they are plain text a user can edit.

Tables only. Views, keyless tables and routine metadata are not read.

## 4. What it writes

About seventy templates in groups named by the text before the first underscore, which also gives the file extension:

| Group | Writes |
|---|---|
| `SP` | stored procedures (SQL Server, MySQL) and functions (PostgreSQL): insert, update, save, delete, load, lookup, search with sort and paging, clone, junction, plus SQL Server scripts for key sequences, bulk update and replication triggers |
| `CS` | entity, enum, repository, `DbContext`, validation attributes, FluentValidation validators, transfer classes and mappers, Bogus fakers |
| `API` | minimal-API CRUD, search and junction endpoints, the endpoint registration, an OpenAPI document, `.http` request files |
| `TS` / `TSX` | the Angular and React model, service, grid-and-form screen, master-detail screen, junction screen, sorting helpers, the list of screens |
| `WinUI3` | the desktop screen family: master list, detail dialog, master-detail dialog, junction editor, directory listing, the list of screens |
| `FS` | F# records with validation |
| `MD` | a data dictionary and an entity-relationship diagram |
| `Essential*` | the files every app of a stack needs and no table drives: project files, shell, styles, base classes, `Program.cs` |

A template may write several files (`@@@FILE` markers), one file for a table, or one file for the whole database. A template can refuse a table with a reason (a composite key where a single key is needed) and the menu and the plan then leave it out.

**The three screen families behave the same** (WinUI 3, Angular, React): paged grid with server-side search and sort, add/edit/delete, foreign-key drop-downs, master-detail dialogs with child grids, many-to-many editors, clone, and validation limits read from the schema.

**Optional extras are switches** in the project settings (`ApiDocs`, `ApiHttp`, `ApiFakers`, `ProjectDocs`, `ApiValidation`, `ApiProduction`) and are off by default, so a default run is unchanged.

## 5. How a run is driven

- **One template, one table**: the desktop right-click menu, or `codegen -T <template> -t <table>`.
- **A whole project**: `codegen generate` (or the Generate All dialog) reads the schema once and runs every template the project's **stacks** (`Api`, `WinUI3`, `React`, `Angular`) call for, over the tables the project selects, writes the essentials of those stacks, then can build and test them (`--build`, `--test`). It reports what it created, updated and left alone, which templates refused which tables, and which files an earlier run wrote that this one does not (stale; only unedited ones are deleted, and only with `--delete-stale`).
- **A project** is one `<name>.config` file of key=value lines: names and namespaces, the database, the output folders, which tables get screens, which are enums, currency, naming, switches. Nothing in it is needed to generate a single file.

## 6. Decisions worth knowing

- **T4 through `Mono.TextTemplating`, in process.** It runs outside Visual Studio and is what EF Core's own scaffolding uses. Source generators were rejected (compile-time only, wrong fit), and the Visual Studio `TextTransformCore.exe` was rejected (not present on every machine). A template is loaded from disk on use, so an edit takes effect on the next run; a whole-project run compiles each template once.
- **A .NET SDK is required at run time**, because compiling a template shells out to the SDK's compiler. The desktop app is therefore framework-dependent (its Windows App SDK runtime is bundled).
- **Everything a user may edit sits in plain files next to the program** (or in the user's application-data folder for the installed tool): `Templates`, `Projects`, `SpecialLogicColumns.config`, `Settings.json`. The program seeds them from copies embedded in itself and **never overwrites a file the user changed**; a newer shipped copy is written beside it as `<name>.new`.
- **Templates are versioned by file name** (`SP_Save_v1.tt`). The menu and `-T SP_Save.tt` use the newest version; a breaking change is a new `_v2` file beside the old one.
- **Type mapping and naming are shared code**, not copied per template (`ColumnTypes`, `Labels`, `JsonNames`, `TableModel.HasCrudApi` and `HasSearchApi`), each tested over every column type.
- **Routines are the default data-access path for search, sort, paging, clone and junctions, and EF Core LINQ is the alternative** (`AccessMode=Routines` or `Ef`; SQLite, which has no routines, always uses `Ef`). The generated SQL treats every value as a bound parameter and a sort column is chosen from a fixed list, in a routine or in a `switch` in the generated query class; the two paths are meant to behave the same, and one `SearchPlan` in Core states what a search does.

- **A generator cannot know some things**, and says so in each template's header: collection navigation names, hand-picked sort columns, screens with unusual shapes, and the registration lines of a hand-written host. These stay hand-written.

## 7. Quality bar

- An automated suite of more than 800 tests: unit tests of the model and helpers, rendering tests of every template against hand-built tables, tests that parse generated documents with real parsers (the OpenAPI document, generated XAML), and live tests against SQL Server, PostgreSQL and MySQL that run only when their environment variables are set and otherwise report themselves as inconclusive.
- The seven sample applications are regenerated and must come out unchanged; generated projects are built and run.
- `Docs/Verification` holds the scripts that check a running app (a scratch copy of the database, a UI Automation module for the desktop app, an API walk, an OpenAPI route check, recipes for the web front ends).
- Continuous integration builds, tests and packs on Windows for every push.

## 8. Decided out of scope

- Writing to, deploying to or testing against the database it was pointed at.
- Database permissions and change-tracking metadata. (Column and table comments are read and used for documentation.)
- Metadata tables that customise a grid or a form per column; a project does that through its settings and its templates.
- Company-specific naming rules such as stripping a parent-table prefix.
- Tracking or copying shared base code into a project: a generated file assumes the base classes exist, or the essentials write them.
- Encrypted, portable connection secrets.
- A DevExtreme (or any commercial component library) screen pair, until a real project needs one.

## 9. Direction

Themes, in rough order; each is a design question before it is a task.

1. **Trust before reach**: a written account of what the generated SQL does with user text, and regeneration that does not overwrite a file the user edited.
2. **Validation that is enforced**: the attributes, validators and form limits agree, and something at run time actually applies them.
3. **Generated tests that test something**: an integration test per table, and screens whose tests check what is shown, not only that they render.
4. **A fourth database, SQLite**, which needs a way to do search, sort, paging and clone without routines.
5. **More small templates and stacks** (Blazor, routine wrappers, a production profile for the API), each optional and each off by default.
6. **New kinds of output**, each optional: a Rust stack (an Axum API, and a Tauri shell around the generated web front end), a dashboard page derived from the schema (KPI cards, breakdowns, trends), timed reminders that pop up a notification when a row falls due, and themes written from exported Figma design tokens.
