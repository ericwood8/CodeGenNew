# Architecture: a guide for reviewers

This is for someone reading the code for the first time: where things are, how a run flows, what is deliberate, and where a second opinion helps most. It is a map, not a reference; the XML comments on the types and the notes in [TemplateNotes](TemplateNotes) hold the detail, and [specs.md](specs.md) says what the product promises.

## 1. Reading order (about 30 minutes)

1. [specs.md](specs.md), sections 2 and 6: the rules and the decisions.
2. This page, sections 2 to 5: the shape of a run.
3. `CodeGenNew.Core/TableModel.cs` and `ColumnModel.cs`: the one data structure every template reads.
4. `Templates/SP_Insert_v1.tt`, `Templates/CS_Entity_v1.tt` and its `.tt.config`: what a template looks like.
5. `CodeGenNew.TemplateEngine/TemplateRunner.cs`, then `CodeGenNew.Generation/ProjectGenerator.cs`: how a template is run, and a whole project.
6. `Examples/GettingStarted` and `Docs/GettingStarted.md`: a schema you can generate from in five minutes.

## 2. The shape of a run

```
 database (SQL Server | PostgreSQL | MySQL)
        |  read-only catalog queries
        v
 SchemaIntrospection:  ISchemaProvider  ->  TableModel / DatabaseModel      (one provider per database, four of them;
        |                                                                      the rest is shared)
        |   + ProjectSettings (<name>.config) + SpecialLogicColumns.config
        v
 TemplateEngine:  TemplateRunner / TemplateCache  (Mono.TextTemplating)
        |   template gets  Model | Database  and  Project
        v
 generated text  --split at @@@FILE markers-->  files
        v
 OutputWriter: write only if the text changed; dry-run and diff; manifest of what was written
```

`Generation` repeats the middle of that picture for every template and table a project's plan names; the CLI and the desktop app are two front ends over the same libraries.

## 3. Projects

Dependencies point downwards; nothing references `App` or `Cli`.

| Project | Responsibility | Depends on | Start with |
|---|---|---|---|
| `CodeGenNew.Core` | The model (`TableModel`, `ColumnModel`, `ForeignKeyModel`, `DatabaseModel`), project settings, and the **shared helpers templates call**: `ColumnTypes` (C#, F#, TypeScript, JSON Schema types), `Labels`, `JsonNames`, `Pluralizer`, `NameConverter`, literal formatting per database, CHECK parsing, call shapes for search / clone / junction routines. No I/O except reading config files. | nothing | `TableModel.cs`, `ProjectSettings.cs`, `ColumnTypes.cs` |
| `CodeGenNew.Connections` | Builds a connection for SQL Server (SQL login or Windows authentication), PostgreSQL and MySQL; a connection test. | Core | `ConnectionRequest.cs` |
| `CodeGenNew.SchemaIntrospection` | Reads a database's catalog and builds the model. `SchemaProviderBase` holds everything database-independent (display columns, lookup shape, the special-logic rules, child tables, naming); `SqlServerSchemaProvider`, `PostgresSchemaProvider` and `MySqlSchemaProvider` hold the catalog queries. | Core, Connections | `SchemaProviderBase.cs` |
| `CodeGenNew.TemplateEngine` | Finds templates and their configs (`TemplateCatalog`, `TemplateConfig`), runs one (`TemplateRunner`) or many with one compile each (`TemplateCache`), splits multi-file output (`GeneratedFiles`), writes files (`OutputWriter`), seeds the shipped templates (`DefaultAssetSeeder`), lists the essentials groups. | Core | `TemplateRunner.cs`, `TemplateConfig.cs` |
| `CodeGenNew.Generation` | Whole-project generation: `ProjectPlan` decides which templates run for which tables, `ProjectGenerator` runs them, `GenerationManifest` records what was written, `ProjectBuilder` builds and tests the result. | Core, Connections, SchemaIntrospection, TemplateEngine | `ProjectPlan.cs`, `ProjectGenerator.cs` |
| `CodeGenNew.Cli` | The `codegen` command (packed as a .NET tool): hand-rolled argument parsing, the one-template form, `generate` and `essentials`. | the above | `Program.cs`, `GenerateCommand.cs` |
| `CodeGenNew.App` | The WinUI 3 desktop app, MVVM with CommunityToolkit.Mvvm: a table tree with a right-click template menu, and dialogs for connection, output location, template management, project settings, essentials and Generate All. | the above | `ViewModels/MainViewModel.cs` |
| `CodeGenNew.Tests` | MSTest suite; see section 8. | the above | `TestSupport.cs` |
| `Templates/` | The T4 templates and their `.tt.config` files; embedded into the CLI and the app so a fresh install is seeded. | | section 5 |
| `Docs/` | `specs.md`, `GettingStarted.md`, one note per template (`TemplateNotes`), the verification scripts (`Verification`). | | |

## 4. The schema model

**One vocabulary.** Every provider reads its own catalog (`sys.*`, `information_schema` plus `pg_catalog`, `information_schema`) and reports a column as a `RawColumn` whose type is a **SQL Server type name** whatever the source database is. Classification (integer, money, string, date, boolean) and the C#, F# and TypeScript mappings are therefore written once. A database whose spelling of the declaration differs supplies an override for the SQL text only. A type with no mapping (an array, a geometry) is reported as a warning, and the `IgnoredColumns` setting leaves it out.

**What the model holds.** Columns with type, length, precision, nullability, identity, computed definition, default, CHECK range or list, description; primary key and its shape (`None`, `Composite`, `SingleInt`, `SingleUniqueIdentifier`, `SingleOther`); foreign keys in both directions; indexes (key columns, for the unindexed-foreign-key script); display columns; whether the table is a many-to-many junction, a lookup (enum-like) table, or a name/active table; the dialect. Rows are read only when a template's config asks (`NeedsRowData`, capped at 5000 rows).

**Names.** `NamingStyle=Pascal` converts `snake_case` to `PascalCase` (with an `Acronyms` list); the model carries both the generated name and the database name (`DbTableName`, `DbName`), and SQL text always uses the second.

**Special-logic columns** (`SpecialLogicColumns.config`) are name-pattern rules: `category|flag columns|companion columns|special`, with `*` as a wildcard and an `IgnoreCase` option. A pair rule (an active flag and its inactive date) sets table-level properties; a single-column rule classifies a column (audit date, create user, display column, file path). The rules are applied once, when the model is built, and templates only read the resulting properties.

**Descriptions** (SQL Server `MS_Description`, PostgreSQL `COMMENT ON`, MySQL `COMMENT`) are read into `Description` on columns and tables and appear in the data dictionary and the OpenAPI document.

**How the generated code reaches the database.** Two choices, both in the project settings. `AccessMode=Routines` (the default where routines exist) calls the procedures and functions the `SP_` templates write; `AccessMode=Ef` writes LINQ over the EF Core context instead (`CS_SearchQuery`, the clone and junction code in `CS_Repo`, `API_Junction` and `WinUI3_JunctionEditor`) and leaves the routine templates out of a plan. SQLite has no routines, so it is always `Ef`. A template says which mode it belongs to with `AccessMode=` in its config and which databases it is for with `Dialects=`. `DialectInfo` (Core) answers what differs between the four databases when a template writes SQL text itself (quoting, paging, placeholders, a case-insensitive contains, grouping a date into a month, LIKE escaping), and `SearchPlan` states what a table's search does independent of how it is carried out; a stack that does not use EF (a Rust repository, a dashboard query) reads those two instead of copying the rules.

## 5. Templates

**Files.** `Templates/<Group>_<Name>_v1.tt` plus `<same name>.tt.config`. The group (`SP`, `CS`, `API`, `TS`, `TSX`, `WinUI3`, `FS`, `MD`) names the menu and decides the default file extension. A trailing `_vN` is the version; the newest version is the one offered, and a script that says `-T SP_Save.tt` follows it. The first lines of every template summarise what it writes, what it refuses and what it needs.

**How one runs.** `TemplateRunner` creates a `Mono.TextTemplating` generator, adds a reference to `CodeGenNew.Core`, puts the model in the session as `Model` (a table) or `Database` (every table) and the settings as `Project`, and processes the file. A template compiles on each run; `TemplateCache` compiles once and reuses the compiled class across tables, which is what makes a whole-project run quick. Compiling shells out to the .NET SDK's compiler, so an SDK must be installed. A template reports a problem with `Error(...)`, which fails that run without writing a file; `Refuse` in the config is the earlier, cheaper check.

**Conventions a reviewer will see everywhere.**
- Output is built so that lines end in `\n`; the repository stores templates as LF.
- A template never contains a double hyphen (T4 text becomes XML comments and XAML; a test enforces it).
- Anything that belongs to the project (namespaces, folders, table lists) comes from `Project`, with a built-in neutral default when no project is chosen.
- Anything that two templates would both compute (a C# type, a caption, a JSON name, whether a table has an API) is a helper in `Core`, covered by tests over every column type.
- A switch that is off changes nothing: optional extras are guarded by a project flag, and a test checks both states.

**`.tt.config` keys** (plain `key=value`, `#` comments; the full rules are in [Reference.md](Reference.md)). Unknown keys are ignored; a missing file means the conservative defaults.

| Key | Meaning |
|---|---|
| `RequiresPrimaryKey`, `TableOnly` | hide the template for a table without a key, or for a view (both default to true) |
| `RequiredPrimaryKeyShape` | `SingleColumn`, `SingleIntOrGuid` or `SingleInt`: the key shape the generated routes need |
| `RequiresJunctionTable`, `RequiresChildTables`, `RequiresAuditTable`, `RequiresNotNameActiveTable` | shape restrictions for the specialised templates |
| `NeedsRowData`, `NeedsReferencedDisplayColumns` | ask the schema reader for more (rows; the display columns of parent tables) |
| `DatabaseOnly`, `NoDatabase` | one file for the whole database, or no database at all |
| `Dialects` | the databases the template is for; a plan skips it silently for another |
| `Stacks`, `PlanTables`, `InPlan`, `OutputRoot`, `OutputFolder` | a place in a whole-project run (see section 6) |
| `OutputName` | a file-name pattern with `{Table}` |
| `EssentialsGroup`, `Description`, `EssentialsDefault`, `Needs` | the template is one file group of a stack's essentials |

**Seeding.** The templates and `SpecialLogicColumns.config` are embedded in the CLI and the app. On start-up `DefaultAssetSeeder` creates what is missing, refreshes a file the user never touched when the shipped copy changed, and writes the shipped copy beside a file the user edited as `<name>.new`. It compares SHA-256 hashes against a record of what it last wrote (`SeededAssets.config`); it cannot overwrite a user's edit. Adding a template means adding it to the seeder's list, and tests fail if the list and the folder disagree.

## 6. Whole-project generation

1. `ProjectGenerator` reads the schema **once** into a `DatabaseModel`.
2. `ProjectPlan` turns the templates' own configs and the project's settings into steps: which template, for which stacks, for which tables (`Entity` tables, tables with an API, tables with a screen, ...). Templates whose config says `InPlan=false` join only through the project's `PlanAlso` or an optional flag (`ApiDocs`, `ApiFakers`, ...).
3. Each step runs through one `TemplateCache`; a template that refuses a table is reported, not failed.
4. `OutputWriter` writes a file only when its text differs (line endings ignored), or reports what would change (`--dry-run`, `--diff`).
5. The essentials groups of the stacks are written, by default only the files that do not exist yet.
6. `GenerationManifest` (`.codegen-manifest.json` in the output folder) lists each file with the hash of the generated text. The next run reports files it no longer produces (stale); `--delete-stale` removes only those whose hash still matches, so an edited file is never deleted.
7. `ProjectBuilder` optionally builds and tests each stack (`dotnet build`, `npm run build`, ...), with per-project command overrides.

**Known gap:** a normal run overwrites a generated file the user edited. The manifest already holds what is needed to refuse that; it is not wired in yet.

## 7. The two front ends

**CLI.** `Program.cs` parses arguments (no library: there are few flags), seeds assets, resolves a project, builds a schema provider through `SchemaProviderFactory`, then either runs one template (`TemplateRunner`) or calls `ProjectGenerator` (`generate`, `essentials`). Schema reads are wrapped in a retry. Exit code 0 means success, anything else is a failure with the message on stderr, so it is safe in a script. The `AppHome` rule decides where `Templates`, `Projects`, `Output` and `Settings.json` live: beside the program, or in `%APPDATA%\CodeGenNew` for the installed tool (a tool's own folder is replaced on update); `CODEGENNEW_HOME` overrides both.

**Desktop app.** MVVM: views bind to view models with `x:Bind`; view models hold no WinUI types except where a control forces it (a `PasswordBox` cannot bind its value, so the password is pushed in from code-behind and never stored). `MainViewModel` holds the connection and table list and builds the right-click menu from `TemplateCatalog` filtered by what each template's config accepts, using only cheap per-table summaries; the full `TableModel` is built only for the table a template is run on. `Settings.json` saves by re-reading the file, changing one value and writing it back, so a second running instance cannot overwrite the first one's saved connection.

## 8. Tests and verification

`CodeGenNew.Tests` (MSTest, about 820 tests, about a minute without a database):

| Kind | Examples | Needs a database |
|---|---|---|
| Model and helpers | `CoreTests`, `ColumnTypesTests`, `CheckConstraintTests`, `ProjectSettingsTests` | no |
| Template rendering over hand-built tables | `TemplateRenderingTests`, `ScreenTemplateTests`, `DtoTemplateTests`, `ApiExtrasTemplateTests`, `FSharpTemplateTests` | no |
| Engine and plan | `TemplateEngineTests` (seeder, versions, group counts), `GenerateAllTests`, `GeneratedFilesTests` | no |
| Generated documents checked by real parsers | `ApiExtrasTemplateTests` (the OpenAPI document, with Microsoft.OpenApi), `DirectoryListingTests` (XAML as XML); the Mermaid diagram is checked only as text | no |
| Live schema and routine tests | `SchemaDescriptionTests`, `PostgresIntegrationTests`, `MySqlTests`, SQL Server metadata tests | yes: set the `CODEGENNEW_*` variables, otherwise inconclusive |
| Hygiene | `LeftoverNamesTests` (no private names), a test that forbids double hyphens in templates | no |

Two checks are not unit tests and are the ones to trust most:
- **Samples unchanged.** Seven sample applications (SQL Server with WinUI 3, Angular and React; PostgreSQL with an API and WinUI 3; MySQL with an API and WinUI 3) are regenerated with `codegen generate --dry-run`; a refactor is correct only if every one reports 0 created and 0 updated. The samples live outside this repository.
- **Run it.** [Verification](Verification) has scripts that build a generated project, start it against a scratch copy of the database and use it: an API create/read/update/delete walk, an OpenAPI route check, a UI Automation click-through of the desktop app, and recipes for the Angular and React pages. A build cannot see a blank drop-down or a dialog that never closes.

CI (`.github/workflows`) builds and tests on Windows, packs the tool, and builds the desktop app.

## 9. Deliberately hand-written

A generated project still needs a person for: collection navigation properties and reverse navigations, enum-typed properties, hand-picked sort columns, tables whose API is not plain CRUD (a name/active table with duplicate-name rules, a composite-key table), screens with unusual shapes, authentication and CORS policy, and the registration lines of a hand-written host. Each template's header says what it assumes. The line is intentional: generating these would need guesses that are wrong often enough to cost more than writing them.

## 10. Where a second opinion helps most

These are the owner's open questions; the list is a draft to be edited by the owner before the review starts.

1. **Generated SQL and user text.** Search boxes, sort columns and paging reach generated routines. Values are bound parameters and a sort column is picked from a fixed list inside the routine, but `LIKE` wildcards in a value are not escaped, and no written threat model or hostile-input test exists yet. Look at `SP_Search_v1.tt` and the PostgreSQL and MySQL variants.
2. **The template contract.** Is `Model` / `Database` / `Project` plus a `.tt.config` the right surface? Are the config keys the right size, or has restriction logic (`RequiredPrimaryKeyShape`, `RequiresNotNameActiveTable`) outgrown a flat file?
3. **Regeneration safety.** Whole-project runs overwrite edited generated files; the manifest could prevent that. Is refuse-and-report the right default, or a side file?
4. **Four screen families from one model.** WinUI 3, Angular and React are written in parallel and must behave the same. Is there a better seam than copying behaviour into each template, such as a neutral screen description rendered three ways?
5. **Provider growth.** The schema reader normalises to SQL Server's type vocabulary. Does that hold for a fourth database (SQLite has no routines, so search, sort and paging would move into LINQ)?

## 11. Limits worth knowing

- Tables only: no views, no keyless tables, no routine metadata.
- The CLI targets plain `net10.0` but CI runs on Windows only; Linux and macOS are untested. The desktop app is Windows only.
- Template compilation needs the .NET SDK on the machine.
- The package is version 0.1.0; a breaking template change will be a `_v2` file beside the old one.
- Git history still holds earlier private names; the working tree has been scrubbed (see `LeftoverNamesTests`).
