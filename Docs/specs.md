# CodeGenNew — Specification (v1)

This document is the single go-forward specification for CodeGenNew, replacing the original generator specification, the special-logic column notes, and the notes from six rounds of design Q&A. It reflects six rounds of Q&A plus review of the prior hard-coded generator (`Avatar.CodeGen`) at `C:\EricWork\Avatar Code\AvatarCodeGenerator\`.

## 1. Purpose & Goals

A C# WinUI 3 Windows desktop application, for a developer's own box, that connects to a database-first SQL Server database and generates code files (SQL stored procedures first, then C# APIs, POCOs, JS grids, etc. over time) via a right-click menu on a table in a TreeView. Replaces the author's prior manual "write lines to a text file with substitutions and smart loops" code generators with a T4-based templating engine, while keeping the tool itself simple, portable, and easy to extend with new template types over time.

Non-goals for v1: this is not a general ORM, not a database migration tool, and not trying to replicate the full feature set of the (much larger, company-specific) prior `Avatar.CodeGen` system.

### 1.1 Non-Negotiable Safety Principle: Read-Only Against Everything External

**CodeGenNew never creates, alters, executes, or otherwise changes anything in the target database, and never modifies files belonging to any other application/project.** Its entire job is: read schema metadata from the database (introspection only — `SELECT`s against system catalog views), and write generated files into the local output directory. Nothing more.

This is permanent and absolute, not a per-feature judgment call:
- No `CREATE`/`ALTER`/`DROP`/`INSERT`/`UPDATE`/`DELETE`/`EXEC` is ever issued against the target database by this application, under any flag, mode, or future feature.
- The tool never deploys, runs, or smoke-tests the code it generates against a live database — that responsibility belongs entirely to the developer, who decides for themselves whether to use the generated output, after reviewing it.
- It also never writes into another project's files directly (this is also why the "Shared Base Code" auto-copy idea was dropped, §13 — same principle, generalized).
- The only database traffic this app ever produces is read-only schema/metadata queries (introspection, §6) and a connection test (§9.1) to confirm credentials work.

An earlier draft of this spec included an opt-in CLI `--verify` mode that deployed generated SQL (`CREATE OR ALTER PROCEDURE`) and executed it against the target database for smoke-testing. **That feature was built, tested against a real database, and then deliberately removed** once it became clear it violated this principle — even opt-in, even non-destructive-by-intent, deploying/executing in the target database is not something this tool does. If a future need for automated testing arises, it must run against a disposable/local test database the developer controls, never the database the tool was pointed at for generation.

**Sanctioned manual testing protocol (human/developer-driven, never the app itself):** the author has explicitly authorized ad hoc, human-driven testing of generated output against a real database, on the condition that it never touches the real object name and always cleans up afterward:
1. Take the generated `.sql` file and rename the procedure (e.g. `NameBase_Update` → `NameBase_Update_Test`) so the real, possibly-already-deployed object of that name is never touched.
2. Deploy and exercise the renamed test procedure directly via `sqlcmd` (or similar) — not through the application.
3. `DROP` the test procedure (and clean up any test rows it created) immediately after, restoring the database to its prior state.

This is a one-off verification technique for a person to use while developing templates, not a capability of the shipped application.

## 2. Technology Decisions & Research Summary

Research was done (per the original specification's "Research Before Coding" section) on four options:

| Option | Verdict |
|---|---|
| T4 / `Mono.TextTemplating` (`dotnet-t4`) | **Adopted.** Modern, actively maintained reimplementation of T4 that runs outside Visual Studio, targets .NET Core/.NET 5+, and is exactly what EF Core itself uses internally for `dotnet ef dbcontext scaffold` (as of EF Core 7, via `DbContext.t4`/`EntityType.t4`). |
| Roslyn Source Generators | **Rejected.** Compile-time only, steep Roslyn-symbol learning curve, re-executes on trivial changes, and is explicitly called out in Roslyn's own docs as less suitable than T4 for cases like this. |
| `TextTransformCore.exe` | **Rejected as the invocation mechanism.** Real tool (VS 2022 17.6+, `.NET 6`-based rewrite of `TextTransform.exe`), but it lives inside a Visual Studio install — not appropriate for a self-contained deployed app running on a machine that may not have VS. `Mono.TextTemplating`'s library/NuGet form is used instead. |
| EF Core scaffolding internals | Confirmed EF Core's own reverse-engineering is T4-based (`dotnet new ef-templates` exposes the real templates) — validates the overall approach. |

**Decision:** Use `Mono.TextTemplating` **in-process** (its library API, not shelling out to the `t4` CLI). Every time a developer clicks a right-click menu item, the engine loads the corresponding `.tt` file fresh from disk, transforms it directly to final output text in one step (no intermediate `.cs.generated` file, no post-processing pass), passing a fully-populated `TableModel` in as a T4 parameter. This is a human-triggered, on-demand action — not a hot path — so there is no need to precompile/cache templates; editing a `.tt` file takes effect immediately on the next click.

All "special logic" (soft delete, active/inactive, start/end date, etc.) is **data**, not code outside T4: it is pre-computed onto `TableModel`/`ColumnModel` by the schema-introspection layer, and templates branch on it with ordinary `<# if (...) { #>` blocks.

Target framework: **.NET 10 LTS** (to be verified against Windows App SDK 2.4.0 compatibility as an early implementation task; fall back to .NET 9 only if that combination doesn't work).

## 3. Deployment Model

- **Unpackaged EXE, no MSIX** (`WindowsPackageType=None`). No package identity, so no `ApplicationData.Current` / Credential Locker.
- The Windows App SDK's own native/XAML runtime is self-contained (`<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`), but .NET's own deployment is **framework-dependent** (`<SelfContained>false</SelfContained>`) — these are independent knobs. Framework-dependent is required: Mono.TextTemplating compiles a generated template by shelling out to a "dotnet"-hosted csc, located by walking a fixed number of parent directories up from the current runtime's own directory. That's correct for a framework-dependent app (runtime directory = the shared framework under `Program Files\dotnet\shared\...`) but wrong for a self-contained one (runtime directory = the app's own output folder), producing a nonsensical path and failing with "The system cannot find the file specified." (found running the real app, not just in testing). Fine for "a developer's own box" — that box already has the .NET SDK installed, or this project wouldn't build.
- Templates and the SpecialLogicColumns/SpCanDeleteVerification config files are **plain files next to the EXE** — chosen for portability (the whole folder can be copied to another machine, e.g. a work computer). `Settings.json` is the one exception: it lives under `%LocalAppData%\CodeGenNew\Settings.json` instead, since the exe's own folder is a build output directory that gets overwritten by `CopyToOutputDirectory` on every rebuild (and can differ between how the app was last built/launched), which was silently wiping the saved last-used connection between runs.
- **No encryption of connection secrets.** The developer is prompted for the database password every time the Connection screen is used; nothing sensitive is persisted to disk. This was chosen deliberately over passphrase-based portable encryption, to keep things simple.
- Supports both **SQL Server Login** (username/password) and **Windows Integrated Authentication**.

## 4. Solution / Project Structure

```
CodeGenNew.sln
├── CodeGenNew.App                 (WinUI 3 UI: TreeView, Connection screen, Template Management screen)
├── CodeGenNew.Connections          (connect + test SQL Server connections; SQL Login & Windows Auth; no schema introspection)
├── CodeGenNew.SchemaIntrospection  (interface + SQL Server implementation: reads tables/columns/PK/FK, builds TableModel)
├── CodeGenNew.TemplateEngine       (Mono.TextTemplating wrapper: discovers .tt files, reads .tt.config, runs a template against a TableModel)
├── CodeGenNew.Core                 (shared model classes: TableModel, ColumnModel, ForeignKeyModel, Settings POCOs — referenced by all of the above)
└── CodeGenNew.Cli                  (console app; see section 10 — scriptable/CI-friendly entry point, bypasses the WinUI3 App entirely)
```

Rationale: `Connections` and `SchemaIntrospection` are deliberately separate (per round-3/round-4 answers) since introspection is the part most likely to need a second implementation when MySQL support is added later; the connection-handling concern (auth, connection string building/testing) is expected to be more reusable across databases.

Git is **not** initialized yet — deferred until there's a working v1, per the author's preference to stay local as a solo developer until something is proven out.

## 5. Configuration Files

All files below live next to the EXE (i.e., at the solution/output root during development).

### 5.1 `Settings.json`

Plain JSON, holds app-level settings and the single last-used connection (no password). Example (see the actual `Settings.json` shipped alongside this spec):

```json
{
  "OutputDirectory": "Output",
  "TemplatesDirectory": "Templates",
  "SpecialLogicColumnsConfigPath": "SpecialLogicColumns.config",
  "SpCanDeleteVerificationConfigPath": "SpCanDeleteVerification.config",
  "LastConnection": {
    "AuthMode": "SqlLogin",
    "ServerName": "",
    "DatabaseName": "",
    "UserName": ""
  }
}
```

- `AuthMode` is `"SqlLogin"` or `"WindowsAuth"`.
- On save, if `OutputDirectory` doesn't exist, the app asks to create it (per the original Location screen spec).
- The Connection screen always re-prompts for password regardless of what's saved here.
- **Saves merge, they don't overwrite.** The app changes a setting through `AppSettingsService.Update`, which re-reads `Settings.json` as it is on disk at that moment, applies just that one change, and writes it back. Writing the whole in-memory copy let a second (or stale) running instance that saved anything — even only the output folder — silently replace the saved connection with its own outdated one (reproduced, then fixed). A failed write is reported in the status bar. The last connection is also remembered when a Test succeeded and the dialog was then cancelled.

### 5.2 `SpecialLogicColumns.config`

Pipe-delimited, plain text, hand-editable, comment lines start with `#`. One row per table-level or column-level special-logic rule:

```
category|flagcolumn(s)|companioncolumn(s)|special
```

- **Column matching per name-pattern token**: `*` at the end = `StartsWith`; `*` at the start = `EndsWith`; `*` in the middle = `Contains`; no `*` = exact match. Case-sensitive unless `special` contains `IgnoreCase`.
- Multiple patterns for one slot are comma-separated; matching any one counts as a match.
- `special` is a comma-separated slot for flags — currently only `IgnoreCase` is defined, more may be added later.
- **When `companioncolumn(s)` is blank**, the rule is a **per-column classification** (the flag column pattern alone makes a per-column boolean true, e.g. a future `IsEmailColumn` rule).
- **When `companioncolumn(s)` is populated**, the rule is a **table-level paired-column** rule: the table must have a column matching the flag pattern *and* a column matching the companion pattern for the table-level flag to be true (e.g. `HasActiveInactivePair`).

v1 ships with exactly the three column-pair rules that are actually wired into generation logic (see §7); `IsEmailColumn`/`IsUrlColumn`-style single-column rules were discussed only to illustrate the wildcard syntax and are **not** shipped or wired in v1.

### 5.3 `<TemplateName>.tt.config`

One per `.tt` file in `Templates\`, same base name. Plain key/value text, one setting per line:

```
RequiresPrimaryKey=true
TableOnly=true
```

Lines starting with `#` are comments (consistent with `SpecialLogicColumns.config`, §5.2).

- **Both restriction checkboxes default to `true`** when a `.tt.config` doesn't exist yet (e.g. a `.tt` file dropped into the folder by hand, never opened in the Template Management screen). This is the conservative default — a developer explicitly unchecks a restriction that doesn't apply to their template (e.g., unchecking `RequiresPrimaryKey` for a future Insert SP template).
- `RequiresPrimaryKey` — if true, the template is hidden/disabled in the right-click menu for tables with no primary key.
- `TableOnly` — if true, the template is hidden for views (irrelevant until v2 adds views, but modeled now).
- `NeedsRowData` — if true, the schema provider also reads the table's rows into `TableModel.Rows` (and sets `HasRowData`) before the template runs. **Defaults to `false`** (unlike the two restrictions above): only a template that generates from actual data (`SP_Load.tt`) asks for it, so every other template pays nothing. Read-only `SELECT`, ordered by primary key, refused beyond `SqlServerSchemaProvider.MaxRowDataRows` (5000) rows since it's meant for small reference/seed tables; computed columns are not read (their slot is `null`).
- `NeedsReferencedDisplayColumns` — if true, the schema provider also looks up each foreign-keyed table's display columns into `ForeignKeyModel.ReferencedDisplayColumns` (and sets `TableModel.HasReferencedDisplayColumns`). **Defaults to `false`**; only `SP_Lookup.tt` asks. Costs a few read-only catalog queries per distinct referenced table.
- `OutputName` — optional file-name pattern for the generated file, replacing the default `<Table>_<Suffix>.<ext>`; the token `{Table}` becomes the table name (`{Table}Api.cs` → `E_DonateLeaveApi.cs`). Only the file-name part is used, never a path. Absent = default naming. The default extension comes from the submenu group: `SP`→`.sql`, `API`/`CS`→`.cs`, `JS`→`.js`, anything else `.txt`.
- More restriction types can be added to this format later without breaking existing files (unknown/missing keys default to their documented default).

### 5.4 `SpCanDeleteVerification.config`

Pipe-delimited, plain text, append-only (comment header written once, then one line per verified database). Format:

```
ServerName|DatabaseName|Status|CheckedAtUtc
```

- `Status` is `Verified` or `NotFoundOrWrongSignature`.
- Written by `SpCanDeleteVerifier` (`CodeGenNew.SchemaIntrospection`) the first time `CodeGenNew` connects to a given `(server, database)` — a one-time, read-only metadata check (`sys.procedures`/`sys.parameters`) confirming `spCanDelete` exists with the exact `@deleteFromTable varchar, @deleteId int` signature. On every later connection to that same database, the cached line is used and no live check runs.
- Purely informational (§7.1) — has no effect on what any template generates. Deleting a database's line forces `CodeGenNew` to re-check it next time.

## 6. Database Introspection: `TableModel` / `ColumnModel` / `ForeignKeyModel`

Built fresh each time a table is selected in the TreeView (not cached across selections). Field list is intentionally lean for v1 — Note from the author: *"we will just expand as we need or [are] forced to"* rather than capturing every possible piece of schema metadata up front. Explicitly **excluded, permanently**: SQL Server extended properties, permissions, and change tracking — considered too custom/company-specific.

```csharp
namespace CodeGenNew.Core;

public class TableModel
{
    public string SchemaName { get; init; }        // e.g. "dbo"
    public string TableName { get; init; }          // raw name, e.g. "ProductionUnitMaster"
    public string QuotedName { get; init; }          // "[dbo].[ProductionUnitMaster]"
    public bool IsReservedWordName { get; init; }    // SQL Server reserved word collision
    public bool IsCSharpReservedWordName { get; init; }

    public List<ColumnModel> Columns { get; init; }
    public List<ColumnModel> PrimaryKeyColumns { get; init; }   // ordered; supports composite & GUID PKs
    public bool HasPrimaryKey => PrimaryKeyColumns.Count > 0;

    // Only populated when the template's .tt.config says NeedsRowData=true (section 5.3); Rows is otherwise empty
    // because it was never read, not because the table is empty.
    public bool HasRowData { get; init; }
    public List<object?[]> Rows { get; init; }   // ordered by PK; one value per Columns entry, same order; format with SqlLiteral.Format

    public List<ColumnModel> DisplayColumns { get; init; }        // this table's own display columns (DisplayColumnSelector)
    public bool HasReferencedDisplayColumns { get; init; }

    public List<ForeignKeyModel> ForeignKeys { get; init; }
    public bool HasAtLeastOneForeignKey => ForeignKeys.Count > 0;
    public bool IsSelfReferencing(ForeignKeyModel fk) => fk.ReferencedTable == TableName;
    public bool IsForeignKeyMulti(ForeignKeyModel fk) =>
        ForeignKeys.Count(f => f.ReferencedTable == fk.ReferencedTable) > 1;

    // Special-logic, table-level (see section 7)
    public bool HasActiveInactivePair { get; init; }
    public ColumnModel ActiveColumn { get; init; }
    public ColumnModel InactiveDateColumn { get; init; }

    public bool HasStartEndDatePair { get; init; }
    public ColumnModel StartDateColumn { get; init; }
    public ColumnModel EndDateColumn { get; init; }

    public bool HasSoftDelete { get; init; }
    public ColumnModel IsDeletedColumn { get; init; }
    public ColumnModel DeletedDateColumn { get; init; }
}

public class ColumnModel
{
    public string Name { get; init; }
    public string QuotedName { get; init; }
    public bool IsReservedWordName { get; init; }
    public bool IsCSharpReservedWordName { get; init; }

    public System.Data.SqlDbType SqlType { get; init; }
    public string SqlTypeDeclaration { get; init; }  // e.g. "varchar(30)", "int", "decimal(18,2)"
    public int? MaxLength { get; init; }
    public int? Precision { get; init; }
    public int? Scale { get; init; }
    public bool IsNullable { get; init; }
    public int OrdinalPosition { get; init; }

    public bool IsIdentity { get; init; }
    public int? IdentitySeed { get; init; }
    public int? IdentityIncrement { get; init; }

    public bool IsPrimaryKey { get; init; }

    public bool IsComputed { get; init; }            // from sys.computed_columns
    public string ComputedDefinition { get; init; }  // excluded from INSERT/UPDATE parameter lists

    public string DatabaseDefaultSql { get; init; }              // raw DEFAULT constraint text, if any
    public string SuggestedCSharpDefaultValueLiteral { get; init; } // translated from DatabaseDefaultSql when recognizable
                                                                      // (numeric/string literal, getdate()->DateTime.Now,
                                                                      // getutcdate()->DateTime.UtcNow, newid()->Guid.NewGuid());
                                                                      // else the ported heuristic (SetColumnDefault); null if neither applies.
                                                                      // This is a rarely-used, best-effort value — not guaranteed.

    // Type classification (ported/adapted from Avatar.CodeGen.SqlServer.DataLayer.Column, retargeted from
    // the old custom SqlDataType enum onto System.Data.SqlDbType)
    public bool IsIntegerColumn { get; init; }
    public bool IsNumericColumn { get; init; }   // decimal/float/real, non-money
    public bool IsMoneyColumn { get; init; }
    public bool IsStringColumn { get; init; }
    public bool IsDateColumn { get; init; }
    public bool IsBooleanColumn { get; init; }
    public bool IsAuditColumn { get; init; }  // ported from ColumnTools.IsAuditColumn (name-pattern match, not config-driven)
    public bool IsCreateDateColumn { get; init; }   // SpecialLogicColumns.config category "CreateDateColumn"; never touched by generated Update logic
    public bool IsInUniqueIndex { get; init; }      // in a UNIQUE index/constraint other than the PK; SP_Clone gives such a column an override parameter
    public bool IsCreateUserColumn { get; init; }   // category "CreateUserColumn" (e.g. CreateUser); a parameter on Insert/Save, excluded from Update
    public int? DisplayRank { get; init; }          // category "DisplayColumn" (e.g. ShortDescr, Name); index of the first matching pattern, lower = better; null = not a display column
    public bool IsDisplayColumn => DisplayRank.HasValue;
    public bool IsModifiedDateColumn { get; init; } // SpecialLogicColumns.config category "ModifiedDateColumn"; set to GETDATE() unconditionally by generated Update logic
    public bool IsLastChangedDateColumn { get; init; } // category "LastChangedDateColumn" (e.g. LastDateChanged); blend of Create+Modify -- see section 7
    public bool IsInactiveReasonColumn { get; init; }   // category "InactiveReasonColumn" (e.g. InactiveReasonNoteText); excluded from Insert, left NULL -- see section 7

    // Hungarian-prefixed SQL parameter name, matching the author's established convention
    // (see Appendix A) — e.g. "@pstrDescription", "@plngID", "@pdteStatusDate"
    public string ParameterName { get; init; }
}

public class ForeignKeyModel
{
    public string ConstraintName { get; init; }
    public List<string> ReferencingColumns { get; init; }
    public string ReferencedSchema { get; init; }
    public string ReferencedTable { get; init; }
    public List<string> ReferencedDisplayColumns { get; init; }   // only when the template sets NeedsReferencedDisplayColumns=true (section 5.3)
    public List<string> ReferencedColumns { get; init; }
}
```

Primary keys are found via index metadata (`sys.indexes` / `sys.key_constraints`), not just `INFORMATION_SCHEMA.COLUMNS` — SQL Server PKs are backed by a unique index (clustered or nonclustered), as seen in the `ProductionUnitMaster` example (`PRIMARY KEY NONCLUSTERED`).

Indexes and foreign keys are **not** shown as separate child nodes under a table in the TreeView (the old system did this via a right-click-on-index/FK context menu that the author found was "never really used"). The data is still captured internally — it's just not rendered as its own tree nodes.

## 7. Special-Logic Column Detection

Only **four** behaviors are actually wired into v1/v2 generation logic. Everything else in the original special-logic column notes (optimistic concurrency, state machines, sync tracking, bi-temporal validity, hierarchy, visibility/publishing, validation/quality, transactional totals, system overrides, and general record auditing) is kept **purely as a future-roadmap reference** — not modeled, not wired, not mentioned again until actually needed.

1. **Active/Inactive pair** — `HasActiveInactivePair` on `TableModel`. The flag-column pattern covers both polarities: `IsActive`/`Active` (1 = active) and `IsInactive` (1 = inactive — the opposite sense). **On Insert** (`SP_Insert.tt`), a newly inserted row should never be born inactive: the active-flag column is excluded from parameters and hardcoded instead — `1` for `IsActive`/`Active`-style columns, `0` for `IsInactive`-style columns (detected by checking whether the matched column's own name contains "Inactive") — so either polarity ends up meaning "active" by default. The companion `InactiveDate`/`InactivatedAt` column, and any `InactiveReasonColumn` match (e.g. `InactiveReasonNoteText`), are excluded from Insert entirely and left `NULL` — there's nothing to explain yet on a brand-new row.

   **On Update** (`SP_Update.tt`): if the caller supplies a value for `InactiveDate` and/or any `InactiveReasonColumn` match, the active-flag column is **forced** to its inactive value regardless of whatever (if anything) was explicitly passed for the flag itself — supplying an inactivation reason/date implies inactivation. Getting this right took two tries: the first attempt appended the forced assignment *after* the flag's own normal optional assignment, assuming "last `SET` wins" — but SQL Server actually **rejects** assigning the same column twice in one `UPDATE`'s `SET` clause (error 264), confirmed by testing (the whole update failed). Fixed by excluding the active-flag column from the normal per-column loop entirely and handling it with a single `IF (<InactiveDate or reason> IS NOT NULL) SET flag = <inactive value> ELSE IF (<flag param> IS NOT NULL) SET flag = <caller's value>` — so there is only ever one assignment, and the flag can still be set independently (e.g. to reactivate a record) when neither inactivation trigger is supplied. All three cases (contradiction, independent reactivation, date-only trigger) retested against real data.
2. **Start/End date pair** — `HasStartEndDatePair`. When present, generated logic should validate `StartDate < EndDate`.
3. **Soft delete pair** — `HasSoftDelete`. Added as a 4th behavior per author's note that soft deletes are extremely common.
4. **Dependency check on delete** — *not* a column-name-pattern rule (so it does not live in `SpecialLogicColumns.config`). **Revised**: the generated `Delete` procedure no longer calls `spCanDelete` itself (§7.1) — it relies on SQL Server's own FOREIGN KEY constraint enforcement, which is what actually prevents an orphaned delete. `spCanDelete`'s existence/signature is instead verified once per database by `CodeGenNew` directly, independent of what gets generated — see §7.1.

All four are read from `SpecialLogicColumns.config` (except #4) at schema-introspection time and pre-computed onto `TableModel`/`ColumnModel` — templates never re-scan column names themselves.

**A fifth, narrower behavior** was added once real data-generation testing surfaced the need: per-column classification for **Create/Modified/LastChanged audit-date columns** (`CreateDateColumn`/`ModifiedDateColumn`/`LastChangedDateColumn` categories, e.g. `CreateDate`/`ModifiedDate`/`LastDateChanged`). This is *not* the full "Record Auditing & Provenance" category in the original special-logic column notes (that remains future-roadmap, e.g. `CreatedBy`/`UpdatedBy` user-tracking columns are still out of scope) — it's specifically the date columns needed to generate correct Insert/Update SPs, all excluded from generated parameters since these values should never be caller-supplied:
- `CreateDateColumn` match: set only by Insert logic; never touched by Update logic.
- `CreateUserColumn` match (`CreateUser*`, e.g. `CreateUser`): a normal caller-supplied parameter on `SP_Insert.tt` and `SP_Save.tt` (the caller says who is creating the row), but excluded from `SP_Update.tt`'s parameters and from `SP_Save.tt`'s UPDATE branch so the original creator is never overwritten. Only `CreateUser*` on purpose — `CreatedBy`-style names already match `CreateDateColumn`'s `Created*` pattern.
- `DisplayColumn` (per-column, but pattern **order is a priority**): the columns a Lookup shows so a person can recognize a row — `ShortDescr`, `Name`, `AccountNumber`, … Every matching column is shown (table order); the earliest-listed pattern that matches also picks the Lookup's `ORDER BY`. Primary-key and foreign-key columns are never display columns (they are the IDs). A table matching none falls back to its first ordinary string column. Never applied to text/ntext/xml/binary columns. Tunable in `SpecialLogicColumns.config` to match your naming.
- `ModifiedDateColumn` match: set to `GETDATE()` unconditionally by Update logic; not touched by Insert logic (there's nothing to modify yet).
- `LastChangedDateColumn` match: a **blend** — set alongside `CreateDate` by Insert logic, and set alongside `ModifiedDate` by Update logic. In other words it's touched on every write, regardless of which operation.

### 7.1 `spCanDelete` and the Generated Delete Procedure (design history)

`spCanDelete` (full source already known — see the original generator specification's text, ported verbatim) is itself just another generated output file for the developer to run themselves, same as any other template output — CodeGenNew does not insert it into a database directly (§1.1).

**v1 of `SP_Delete.tt`** had the generated procedure call `spCanDelete` itself before every delete: capture its result set into a local temp table (`CREATE TABLE #NameBaseUsage` / `INSERT INTO #NameBaseUsage EXEC spCanDelete ...`), check `EXISTS`, and refuse to delete (`-3`) if the `spCanDelete` call itself failed (e.g. a signature mismatch — this path was confirmed for real during testing, §15.4/§15.5).

**That was deliberately removed**, for two reasons the author gave directly: *performance* and *"too much junk for little safety."* These generated procedures run **often** in production (every delete, by every user, indefinitely) — paying for a temp table plus `spCanDelete`'s own dynamic-SQL loop across every FK relationship on **every single call** is real, compounding overhead for a check that SQL Server already performs natively, for free, as part of enforcing the FK constraint itself. The current, much leaner design:

```sql
DELETE FROM [Schema].[Table] WHERE (<primary key columns>);

DECLARE @lngDeleteError INT = @@ERROR;
IF @lngDeleteError = 547        -- FOREIGN KEY / CHECK constraint violation
    SELECT @lngReturn = -1;     -- blocked: a dependent row exists elsewhere
ELSE IF @lngDeleteError <> 0
    SELECT @lngReturn = -2;     -- some other database-level failure
ELSE
    SELECT @lngReturn = 0;      -- deleted successfully
```

Just attempt the delete and read `@@ERROR` (captured into a variable immediately, since `@@ERROR` only reflects the *immediately preceding* statement — referencing it a second time without capturing it first is a classic, fragile T-SQL mistake). SQL Server error `547` is specifically a FOREIGN KEY/CHECK constraint violation, so it cleanly distinguishes "blocked by a real dependency" from any other unexpected failure, with zero pre-checking overhead. This also eliminated the old `canCheckDependencies` branch (single-integer-PK vs. composite/GUID) entirely — every table now gets the exact same lean logic regardless of primary key shape, since the FK constraint check is native and doesn't care what the key looks like.

**Whether `spCanDelete` itself exists and is correctly shaped is now a completely separate concern from what `SP_Delete.tt` generates.** `CodeGenNew` verifies it directly — once per `(server, database)`, not per delete call, not per generation — via `SpCanDeleteVerifier` (`CodeGenNew.SchemaIntrospection`), which is purely a read-only metadata check (`sys.procedures`/`sys.parameters`, confirming the exact `@deleteFromTable varchar, @deleteId int` signature). The result is cached in `SpCanDeleteVerification.config` (§5.4) so the live check only ever runs once per database; the CLI reports the result only the first time (a fresh, uncached check), staying silent on subsequent runs against an already-recorded database. This check is purely informational for the developer — it has no effect on what `SP_Delete.tt` generates, and deleting a line from the config forces a re-check the next time that database is connected to.

## 8. Template Engine (T4 / Mono.TextTemplating)

- Templates live in **`.\Templates\`**, a fixed folder next to the EXE. The app creates this folder if missing when a template is first saved. v1 ships with default templates already in place (see §11).
- A template's **file name (minus `.tt`) is its menu display name**. E.g. `SP_Update.tt` → menu item "Update" nested under a submenu "SP". (A trailing `_vN` version suffix is stripped first — see §8.0.)
- **Submenu grouping**: the substring before the **first underscore** in the filename becomes the submenu name (`SP_Update.tt` → submenu `SP`, item `Update`). A filename with no underscore (e.g. `Refresh.tt`) appears as a flat top-level item. Menu items within a submenu, and top-level items, are ordered alphabetically.
- The menu is **rebuilt dynamically** from whatever `.tt` files are found in `Templates\` at the time a table is right-clicked — adding a new template later requires no code change.
- Each `.tt` file may have a same-named `.tt.config` (§5.3) controlling menu visibility restrictions (`RequiresPrimaryKey`, `TableOnly`).
- **No shared-base-code tracking.** A generated file that references a shared base class (e.g. `GenericRepo<T>`) simply assumes that class already exists in the target project — the developer's responsibility, surfaced as an ordinary compile error if missing. (Originally proposed as a "Shared Base Code" grid feature in the Template Screen; explicitly dropped in favor of simplicity.)

### 8.0 Template Versioning and Keeping Shipped Files Current

**Versioned file names.** Shipped templates carry their version in the file name: `SP_Save_v1.tt` + `SP_Save_v1.tt.config`. The suffix is `_v` plus digits at the end of the name (before `.tt`). Everywhere the name is *displayed or reused* the suffix is stripped — the menu item and submenu (`SP` → `Save`) and the generated file (`Acct_Save.sql`) are unchanged by versioning. A file with no suffix (an older install's copy, or a template the developer wrote) counts as **version 0**.

**The menu offers only the newest version of each template.** If `Templates\` holds `SP_Save.tt` (v0), `SP_Save_v1.tt` and `SP_Save_v2.tt`, only v2 appears in the right-click menu; the older files stay on disk (they may be customized) but are hidden. The CLI follows the same rule: `-T SP_Save.tt` (no version) means the **latest**, so scripts keep working as templates are revised, while `-T SP_Save_v1.tt` pins that exact file. The Template Management screen still lists every file, marks superseded ones "(old)", and lets you delete them. (A hand-made template that reuses a shipped name without a suffix, e.g. your own `SP_Save.tt`, is version 0 and is therefore hidden by the shipped `_v1` — give it a different name.)

**Shipped files never clobber yours — `DefaultAssetSeeder`.** On startup the App and CLI create missing folders and files from the copies embedded in the EXE (silently). For a file that already exists, the seeder compares SHA-256 hashes of the file on disk, the embedded copy, and what it last wrote there (recorded in `Templates\SeededAssets.config`):

| On disk is… | Result |
|---|---|
| missing | created (silent) |
| identical to the shipped copy | nothing to do (hash remembered) |
| identical to what the seeder last wrote, but the shipped copy has since changed | you never touched it → **refreshed in place** (reported by the CLI) |
| anything else — edited by you, **or no record of it** (an install from before `SeededAssets.config` existed, or the file was deleted) | **left alone**; the shipped copy is written beside it as `<name>.new` to compare/merge (reported once by the CLI) |

A new template *version* arrives as a new file (`_v2`), so this table matters mainly for files that keep their name: `.tt.config` files, `SpecialLogicColumns.config` (its `.new` lands next to it), and any in-place fix. Deleting `SeededAssets.config` just makes every existing file look "possibly customized" again — it never causes an overwrite. `.new` files aren't picked up as templates.

### 8.1 Generation Flow

1. Developer right-clicks a table/view node, sees a menu built from `Templates\*.tt` filtered by that node's applicable restrictions.
2. Developer picks a menu item → engine resolves it to `Templates\<Name>.tt`.
3. Schema introspection builds a fresh `TableModel` for the selected table.
4. `Mono.TextTemplating` transforms the `.tt` file in-process, with `TableModel` passed in as a T4 parameter, producing the final output text directly (no intermediate file, no post-processing pass).
5. Output is written to `<OutputDirectory>\<derived file name>`. The file name is `<TableName>_<suffix>.<ext>`, where `<suffix>` is the template name with its submenu-group prefix removed (e.g. `SP_Update.tt` → suffix `Update`), and `<ext>` is inferred from the submenu-group prefix (`SP`→`sql`, `CS`→`cs`, `JS`→`js`; unrecognized prefixes fall back to `.txt`). Example: `SP_Update.tt` + table `ProductionUnitMaster` → `ProductionUnitMaster_Update.sql`.
6. User is told the process is finished. **Nothing is ever deployed, executed, or otherwise applied to the target database by CodeGenNew itself (§1.1)** — the generated file in the output directory is the entire deliverable; the developer reviews it and decides what to do with it.

### 8.2 Templates That Write Several Files (`@@@FILE`)

Most templates produce one file, named by `OutputFileNaming` (or by the `OutputName` key of §5.3). A template that must write **several** files — or a file inside sub-folders, like an Angular component's four files under `components\<name>\` — says so itself by writing a marker line before each file:

```
@@@FILE components/holiday/holiday.component.ts@@@
...that file's text...
@@@FILE components/holiday/holiday.component.html@@@
...
```

`CodeGenNew.TemplateEngine.GeneratedFiles` (used by both the CLI and the app) splits the output at the markers and writes each file under the output folder, creating the sub-folders. A file may be empty (a marker followed straight by the next marker). The path must be relative, may use `/` or `\`, and can never be rooted, contain a drive, or climb out with `..` — an unsafe path fails the generation instead of writing anything. Output with no marker is the ordinary single file. The CLI prints one `Wrote …` line per file; the app's "Done" box lists the files (first twelve) and its button opens the first one. Point the output folder at the target project's root for the templates' relative paths (for the `TS_` templates, the Angular app's `src\app` folder).

### 8.3 Project Settings at the Top of a Template, and the Test Suite

**Project settings.** Everything in a template that belongs to a *project* rather than to the table — the namespaces the generated file declares (`apiNamespace`, `entityNamespace`, `enumNamespace`, `repositoryNamespace`), extra `using` lines (`usings`), the `DbContext` class (`contextType`), the base classes, the Angular folder names and API prefix, and the lists of tables that are enums / lookups / have no entity (`noNavigationTables`, `noRepositoryTables`, `noApiTables`, `noLookupParents`) — is a block of plain C# variables at the **very top** of the `.tt` file, under a `PROJECT SETTINGS` banner. Using a template in another project means editing that block; nothing needs rebuilding, because templates are loaded fresh from disk on every generation. `usings` defaults to empty (the sample project uses global usings) and, when filled in, adds one `using` line per entry to the generated file. The SP templates have no such block: a stored procedure has no project namespace.

**Tests.** `CodeGenNew.Tests` (MSTest, `dotnet test CodeGenNew.Tests\CodeGenNew.Tests.csproj`) needs no database. It exercises `GeneratedFiles` (marker splitting, empty files, line endings, unsafe paths refused, sub-folders created), `OutputFileNaming`, `TemplateConfig`, `TemplateCatalog` (versions, pinning, groups), `DefaultAssetSeeder` (created / untouched-older refreshed / customized kept with a `.new`, and that every `Templates\*.tt*` file is in the seeder's list and embedded), `SqlLiteral` and `DisplayColumnSelector`; and it renders every shipped template through `TemplateRunner` against hand-built `TableModel`s to check what it writes, what it refuses (composite keys, enum tables, name/active tables for `API_Crud`), and that editing a project setting (`usings`, a namespace, the Angular folder names) changes the output.

## 9. Screens — `CodeGenNew.App` (WinUI 3, MVVM)

Built as a plain unpackaged WinUI 3 app (`net10.0-windows10.0.19041.0`, `WindowsPackageType=None`, self-contained — resolving the §15.1 risk: this combination builds and launches cleanly), following MVVM via **CommunityToolkit.Mvvm** (the standard, WPF/WinUI-agnostic toolkit — `ObservableObject`/`[ObservableProperty]`/`[RelayCommand]`). Views (`MainWindow`, and `ContentDialog`-based `ConnectionDialog`/`LocationDialog`/`TemplateManagementDialog`) bind to ViewModels via `x:Bind`; ViewModels never reference WinUI types except where a dialog genuinely needs to own View-only state (e.g. a `PasswordBox` doesn't support two-way binding to its `Password` property by design, so the View's code-behind pushes it into the ViewModel on `PasswordChanged`).

One implementation note for whoever touches this next: `[ObservableProperty]` on a `partial` **property** (the newer, WinRT-marshalling-friendly syntax CommunityToolkit.Mvvm recommends, vs. the classic backing-field style) produced `CS9248`/`CS8050` compile errors in this project — the generator didn't emit a matching implementation part, even with `LangVersion` forced to `latest`. Root cause wasn't tracked down further; reverted to the classic `[ObservableProperty] private T _field;` style throughout, which builds and runs correctly, with `MVVMTK0045` suppressed project-wide (see the comment in `CodeGenNew.App.csproj`) since the AOT-marshalling concern that warning exists for doesn't apply to a self-contained-but-not-trimmed app.

### 9.1 Connection Screen (`ConnectionDialog`)

- Fields: server, database, auth mode (SQL Login / Windows Auth via `RadioButtons`), and — only for SQL Login — username/password.
- Password is **always** re-entered; never persisted (`ConnectionDialogViewModel.Password` is a plain in-memory property, not an `[ObservableProperty]`, set directly from the `PasswordBox`).
- Two buttons beyond Cancel: **Test** (validates the connection, keeps the dialog open, shows the result) and **Save** (re-validates via the same test, and only closes/commits if it succeeds — implemented via `ContentDialogButtonClickEventArgs`'s deferral + `args.Cancel`).
- On successful Save, everything except password is written to `Settings.json`'s `LastConnection` and pre-filled next time; `MainViewModel.ConnectAsync` then loads the table list and runs the one-time `SpCanDeleteVerifier` check (§7.1) for that database.

### 9.2 Location Screen (`LocationDialog`)

- Text field plus a **Browse...** button using WinUI 3's `FolderPicker` (initialized with the owner window's HWND via `WinRT.Interop.InitializeWithWindow`, required for an unpackaged app).
- On Save, validates the path; if it doesn't exist, shows a nested confirmation `ContentDialog` ("Create it?") before creating it — matches the original Location screen spec exactly.

### 9.3 Template Management Screen (`TemplateManagementDialog`)

Simplified from the original "rich text field" design (per round 2) into a **file-management screen**, since templates are now real `.tt` files a developer can edit in any external editor (VS Code, Notepad++, etc.):

- `ListView` over `TemplateCatalog.Discover(...)`-backed rows, each showing name, the two `.tt.config` checkboxes (`RequiresPrimaryKey`, `TableOnly` — edited inline, saved immediately on change via `TemplateRowViewModel`), and Open/Rename/Delete buttons.
- **New**/**Rename** use a small reusable "prompt for a name" `ContentDialog` (a `TextBox` in its `Content`) built once in code-behind rather than a dedicated XAML file, since it's the same two-field shape both times.
- **Open** launches the OS default handler for the file (`Process.Start` with `UseShellExecute=true`) — no embedded editor.
- **Delete** confirms first (a second `ContentDialog`) before removing both the `.tt` and its `.tt.config`.

### 9.4 Code Generation Screen (`MainWindow`)

- **TreeView reality check**: WinUI 3's `TreeView` does not support a literal `<TreeViewItem>` as direct XAML content, nor `TreeViewItem.ItemTemplate` — the original plan (a single expandable "database" root node containing table children, all via one data-bound hierarchy) doesn't fit that API. Built instead as a bold header (icon + `Server \ Database` label, updated via `MainViewModel.DatabaseLabel`) directly above a **flat, `ItemsSource`-bound `TreeView`** listing tables (`TreeView.ItemTemplate` keyed to `TableNodeViewModel`, using ordinary `x:Bind`). Same visual/functional outcome the spec called for (a labeled, iconified, single-selection list of tables under a clearly-shown database) without fighting the control's real hierarchical-template model for a tree that only ever has one root anyway.
- Root label icon = `database.png`; single-selection; no reordering.
- Children = tables (views excluded until v2), from `SqlServerSchemaProvider.ListTablesAsync()` — a lightweight, read-only query (table name + whether it has a PK + whether it has any unique index) kept deliberately cheap since it runs for every table up front, unlike the full `TableModel` build which only happens for the one table actually selected for generation.
- System tables filtered via `SystemTableFilter.IsSystemTable` (ported/broadened from `Avatar.CodeGen.SqlServer.DataLayer.TableTools.IsSystemTable`, §14).
- Table names colliding with a SQL Server or C# reserved word render in red (`TableNodeViewModel.TextBrush`, resolved once against `Application.Current.Resources["TextFillColorPrimaryBrush"]` so it still respects the current theme for the non-colliding case).
- **Icon selection is a 3-tier priority** matching the three shipped table icons (looked at each PNG rather than guessing from its filename — see `IconProvider`'s doc comment): has a primary key → `table.png`; no PK but some other unique index exists → `table _no_pk.png`; no PK and no unique index at all → `table_no_unique.png`. Never disabled regardless of tier, since not every template requires a PK (`SP_Insert.tt` doesn't).
- No child nodes for columns, foreign keys, or indexes under a table (deliberately dropped from the prior system's UI — the equivalent right-click-for-more feature there went unused).
- **Right-click menu** (`RightTapped` on each row) is built fresh every time from `MainViewModel.GetApplicableTemplates(table)` — `TemplateCatalog.Discover(...)` filtered by `TemplateInfo.AppliesTo(hasPrimaryKey, isView: false)` — grouped into `MenuFlyoutSubItem`s by `SubmenuGroup` exactly as §8 describes, so adding a new `.tt` file changes the menu with no code change, GUI included.
- Picking a menu item calls `MainViewModel.RunTemplateAsync`, which builds the full `TableModel` for just that table, runs the template, writes the output file, and shows a completion `ContentDialog` naming the file and restating the never-touches-the-database guarantee (§1.1) — matching the CLI's own messaging.
- **Toolbar** (`CommandBar`): Connect (`DataSource.png`), Output Location (a `FontIcon` glyph — no shipped image fit "folder"), Manage Templates (`UIs.png`, closest available fit), Refresh (`Refresh.png`). `New database.png` and `network-server-database.png` have no matching v1 feature yet and are unused.

## 10. Command-Line Interface

A sixth project, **`CodeGenNew.Cli`** (console app), references `Core`, `Connections`, `SchemaIntrospection`, and `TemplateEngine` directly — it does not go through the WinUI3 `App` project at all. This exists so generation can be scripted/automated (CI, quick manual testing) without driving the GUI, and it's a direct payoff of keeping those four libraries independent of the UI (§4).

Flags (short forms loosely follow `sqlcmd` conventions where they overlap, e.g. `-S`/`-U`/`-P`/`-d`/`-E`):

| Flag | Long form | Meaning | Required? |
|---|---|---|---|
| `-S` | `--server` | SQL Server instance name | Yes |
| `-d` | `--database` | Database name | Yes |
| `-s` | `--schema` | Schema name | No — defaults to `dbo` |
| `-t` | `--table` | Table name | Yes |
| `-T` | `--template` | Template file name (e.g. `SP_Update.tt`), resolved against `TemplatesDirectory` | Yes |
| `-o` | `--output` | Output directory | No — falls back to `Settings.json`'s `OutputDirectory` |
| `-U` | `--user` | SQL Login username | Only when not using `-E` |
| `-P` | `--password` | SQL Login password | No — if omitted (and not using `-E`), prompts interactively with masked input; never persisted |
| `-E` | `--trusted` | Use Windows Integrated Authentication instead of SQL Login | No |
| | `--provider` | Database provider: `SqlServer` (default) or `MySql` | No — `MySql` is accepted but rejected with a clear "not implemented yet" error until a MySQL `SchemaIntrospection`/`Connections` implementation exists (§12) |

Notes:
- `-T` (template) and `-t` (table) are intentionally case-differentiated, matching the Windows CLI-tool convention `sqlcmd` itself uses (`-S`/`-s`, etc. are not actually both used by `sqlcmd`, but the differentiate-by-case pattern is the same idea).
- `-o` here means *output directory*, unlike `sqlcmd`'s own `-o` which is an output *file* for query results — not a real conflict since this isn't `sqlcmd`, but worth knowing so nobody expects file semantics.
- The CLI always exits with code `0` on success and non-zero on any failure, and writes errors to stderr — this is the main point of having a CLI at all (scriptability).
- No password is ever written to `Settings.json` or any other file, matching the GUI's connection-secret handling (§3, §9.1).
- **The CLI never writes to the target database.** It reads schema metadata (read-only) and writes one generated file to the output directory — nothing else, ever (§1.1). The read-only schema lookup is wrapped in a retry-then-halt-and-beep helper (`RetryRunner`): up to 5 attempts, then `Console.Beep()` and a halt, on the theory that repeated failure to even *read* schema likely means the database is unreachable — not something worth retrying forever silently.

Example (SQL Login, explicit password prompt avoided by piping or `-E` for Windows Auth):
```
codegen.exe -S SERVER_NAME -d databaseName -U username -P password -s dbo -t tableName -T SP_Update.tt -o C:\Github\CodeGenNew\Output
```
```
codegen.exe -S SERVER_NAME -E -d databaseName -t tableName -T SP_Update.tt
```

## 11. V1 Scope

- SQL Server only.
- Tables only (no views).
- Fourteen shipped templates (seven stored procedure templates, one API template, three C# templates and three Angular TypeScript templates; all in `Templates\`; on disk they are `SP_Insert_v1.tt` etc. — see §8.0 — while this document uses the unversioned names), each tested against a real database using the manual protocol above:
  - **`SP_Update.tt`** — the Update stored procedure pattern from the original generator specification, generalized to any table via `TableModel`, supporting composite/GUID primary keys. Uses the original worked example's convention of VarChar-for-every-parameter plus dynamic SQL string-building (so a NULL parameter means "don't touch this column"), with trimming and single-quote escaping added for string columns.
  - **`SP_Insert.tt`** — deliberately uses ordinary, properly-typed parameters and a plain parameterized `INSERT` instead of Update's VarChar/dynamic-SQL convention, since Insert has no "don't touch this column" requirement. A single identity primary key is excluded from parameters and returned via `SCOPE_IDENTITY()`; a composite or non-identity key is a required parameter instead. The active-flag column (either polarity) is hardcoded to "active by default" rather than taken as a parameter; `InactiveDate` and any `InactiveReasonColumn` match are omitted entirely and left `NULL` (§7). String-family parameters default to `''` rather than `NULL` when omitted — a caller who wants a true `NULL` can still pass it explicitly, since a SQL Server parameter default only applies when the argument is omitted entirely.
  - **`SP_Delete.tt`** — a plain `DELETE` guarded only by SQL Server's own FOREIGN KEY constraint enforcement (no `spCanDelete` call, no temp table — removed for performance and simplicity, §7.1). Works uniformly regardless of primary key shape. Returns `0` (deleted), `-1` (blocked — SQL error 547, a real FK/CHECK constraint conflict), or `-2` (some other database-level failure).
  - **`SP_Save.tt`** — insert-or-update in one procedure ("AddUpdate"): looks the row up by primary key (single or composite) with `EXISTS (... WITH (UPDLOCK, HOLDLOCK))` inside a transaction, then `UPDATE`s or `INSERT`s. Chosen over "attempt the UPDATE and fail over to INSERT" because it has no error- or `@@ROWCOUNT`-driven control flow and handles composite keys naturally. Properly-typed parameters with **no defaults** (the caller must pass the entire record — an omitted parameter would otherwise silently overwrite a column with `NULL` on update); the exception is an identity key, which is `= NULL OUTPUT` and returns the new id via `SCOPE_IDENTITY()`. Strings are trimmed once at the top (`ISNULL(..., '')` only for NOT NULL columns; `text`/`ntext` skipped); quotes are deliberately **not** doubled and `[`/`%` not escaped, because these are bound parameters in static SQL — which is also why `uf_FixString` (the helper in the original save procedure) is not used (it would store `O''Brien`/`50[%]`, and it downcasts NVARCHAR to VARCHAR). All the special logic from Insert and Update lives here once: audit dates (Create on insert only, Modified on update only, LastChanged on both), born-active/no-admin on insert, inactivation-forcing via a single `CASE` on update, and start/end and in/out range checks up front (`THROW`). Joins a caller's transaction or owns its own; errors are re-raised, return value is `0`. Tested against a disposable LocalDB database (identity, composite, key-only and Unicode cases; nested-transaction and error paths), then dropped.
  - **`API_Crud.tt`** — the first template that is not a stored procedure: a minimal-API class `<Table>Api.cs` (`OutputName={Table}Api.cs`) in the style of TimeEntryServer's `TimeEntry.ApiServiceApis_DonateLeaveApi.cs` — `Register()` maps GET-all, GET-by-id, POST, PUT and DELETE, each a static handler over the table's own repository (`<Entity>Repo`, written by `CS_Repo`); route names come from `BaseApi.BreakIntoStrings`, so the template never spells one. Assumes the entity class is named like the table and its key property like the key column. GET-by-id returns 404 for a missing row, PUT returns 400 when the URL id and body id disagree and 404 for a missing row, DELETE maps the repo's -1/-2 results to 404/400. `GetAll` orders newest-first by the first NOT NULL date column that is not an audit column (`GenericRepo.GetAllOrderByDescending` only takes a non-nullable `DateTime`), or is a plain `GetAll()` when the table has none. Requires a **single `int` primary key** — anything else stops with a template error rather than generating routes that cannot work. A table with no repository (the enum/`*Type` tables and tables with no entity class) also stops with an error, and so does a **name/active table** (NOT NULL text `Name` plus NOT NULL bit `IsActive`): its repository is a `NameActiveRepo`, which has no `GetAll`, and its API needs duplicate-name checks and trimming, so those APIs stay hand-maintained. Project-specific names (`apiNamespace`, `contextType`) are two variables at the top of the template. Writes no business rules, so it suits only plain tables; tables whose hand-kept API class has validation, custom lists or non-standard routes stay hand-maintained. Generated `E_DonateLeave`, `E_TimeSheet` and `Response` from the `TimeEntry` database matched the existing files apart from whitespace and the `ProducesProblem(422)` on POST, and the whole `TimeEntry.sln` compiled with them. The entity, its `DbSet` and the `ApiRegisterExtension` registration line are not generated.
  - **`CS_Entity.tt`** — an Entity Framework entity class `<Table>.cs` (`OutputName={Table}.cs`) in the style of TimeEntryServer's `TimeEntry.Common\Entities\E_DonateLeave.cs`: a `#region Omitted` holding the `[Key]` column and the foreign-key columns (`[Display(Order = -1, AutoGenerateField = false)]`, `[ForeignKey(nameof(<role>))]`), one nullable **navigation property per single-column foreign key** named for its role (the FK column without its trailing `Id`: `DonateFrom_EmployeeId` → `DonateFrom_Employee`; `ManagerId` → `Manager`), then one property per remaining column — `required` when NOT NULL, `Display(Name/Description)` from the column name split into words, date/decimal/money/`StringLength`/`MultilineText`/`PhoneNumber` attributes chosen from the SQL type, a bit column's database default as its initializer. A table with a NOT NULL text `Name` and NOT NULL bit `IsActive` derives from `BaseNameActiveEntity` (which already declares those two) instead of `BaseEntity`. Requires a single-column primary key (a composite key needs Fluent API). Foreign keys to a table in the template's `noNavigationTables` list (the enum tables and tables with no entity) stay plain columns. It deliberately does **not** write collection navigation properties, reverse navigations the parent already lists, enum-typed properties, `[Range]`/`[RegularExpression]` validation, or a property the class makes optional although the column is NOT NULL — an entity needing those stays hand-maintained. Verified against the `TimeEntry` database by generating every entity, then comparing before/after with a throwaway harness that dumps each type's public surface and EF Core's own model (`ToDebugString`): the five kept generated (`E_DonateLeave`, `E_RequestExpenseDetail`, `Employee`, `Holiday`, `RestrictLeave`) differ from the hand-written ones only in navigation properties becoming nullable instead of `required` and `RestrictLeave` losing its `ToString()`; the other entities were rejected for the reasons above and the solution compiled with the generated ones in place.
  - **`CS_Enum.tt`** — a C# enum `<Table>.cs` whose members are the **rows** of a small lookup table (`NeedsRowData=true`, §5.3): the value is the primary key (a single int/smallint/tinyint/bigint, which also picks the enum's underlying type), the name is the row's `Name` column, else `<Table>Name` (`Company` -> `CompanyName`; an `E_`/`SY_` prefix on the table is ignored), else the best-ranked display column, else any text column ending in `Name` with spaces and punctuation removed and each word capitalized (`Paid time off` → `PaidTimeOff`); names that would be illegal or repeat are made unique (`_2Fast`, `Name_3`). An empty table is refused ("the table is empty ... load the data first"), since an enum with no members is useless. Regenerating over a hand-tuned enum renames abbreviations the developers chose (`OvertimeType.RegularOT` comes out `RegularOvertime`), so those enums stay hand-maintained. Verified on the `TimeEntry` lookup tables: six of the eight enums regenerated identically; `LeaveType` and `OvertimeType` differ only in hand-abbreviated member names and were left as they were.
  - **`CS_Repo.tt`** — the thin per-table repository class `<Table>Repo.cs` (`OutputName={Table}Repo.cs`) in the style of TimeEntryServer's `TimeEntry.Common\Repositories\HolidayRepo.cs`: a constructor over `TimeEntryContext` on top of `NameActiveRepo<Entity>` (a table with a NOT NULL text `Name` and NOT NULL bit `IsActive`, the same test `CS_Entity` uses) or `GenericRepo<Entity>`. The shared `GenericRepo`/`NameActiveRepo` and their interfaces are hand-written once and not generated. Two extra queries are written only where the columns alone decide them: `GetAllOf<Parent>(int id)` (active rows for the parent, by name) on a name/active table with exactly one NOT NULL int foreign key to an entity table, and `GetByName(string name)` on a non-name/active table with a NOT NULL text `Name`. Queries that `Include` navigation properties or use a hand-picked order stay hand-maintained. **No repository is written for the enum tables, the other `*Type` lookup tables or tables with no entity class** — the template's `noRepositoryTables` list makes them stop with an error. Verified on the `TimeEntry` database: `DepartmentTeamRepo`, `ProjectTaskRepo` and `HolidayRepo` regenerate identically apart from one comment; six repositories that carry `Include` queries or an unusual base (`Department`, `Employee`, `Project`, `E_RequestExpenseSheet`, `E_TimeSheetDetail`, `TimeEntryUser`) were left hand-maintained; six tables with no repository yet got a plain one, and the solution compiled.
  - **`TS_Model.tt`**, **`TS_Service.tt`**, **`TS_Component.tt`** — the Angular TypeScript layer of `TimeEntryUI` (`src\app\models`, `services`, `components`), written with the multi-file marker of §8.2 so each lands in its own folder (`models/holiday.ts`, `services/holiday.service.ts`, `components/holiday/holiday.component.{css,html,spec.ts,ts}`). All three name things from the table without its `E_`/`SY_` prefix (`E_TimeSheet` → `TimeSheet`, files `timesheet…`), use the same service URL the API registers (`api/<lower name>s`), and refuse tables with no API (enum/`*Type`/entity-less tables) or (for `TS_Service`/`TS_Component`) without a single `int` or `uniqueidentifier` key — a GUID key is a `string` in the model and service, the route is expected to take `{id:guid}`, and a new row is sent with the empty GUID so the API assigns the key (`TS_Model` accepts any single-column key). `TS_Component` loads all rows with no paging, and shows a table's long-text columns in the grid only when it has no other columns.
    - **`TS_Model`** — an interface per table: property names are what ASP.NET Core's JSON camel-casing really sends (`SY_IsoCountry_Alpha3Code` → `sY_IsoCountry_Alpha3Code`, `E_TimeSheetId` → `e_TimeSheetId`); numbers, booleans and strings, with **dates typed `string`** (JSON has no date type; the API sends `"2025-12-25T00:00:00"`); the key and every nullable column optional, NOT NULL columns required; one optional navigation property per foreign key to a table that is not an enum/lookup table (imports gathered per model file, with an override for interfaces that share another table's file).
    - **`TS_Service`** — `getAll`, `getById`, `create`, `update`, `delete`, plus `findByName` when the table has a text `Name` column; identical method names on every service.
    - **`TS_Component`** (`NeedsReferencedDisplayColumns=true`) — a standalone screen: grid, "Add New", a search box (only with a `Name`), and an add/edit form under the grid, `alert()`-based errors (400 bad value, 404 gone, delete's 400 "in use"). Controls follow the column type (text/textarea from 100 chars, number, checkbox for bit, date box, and a drop-down for a foreign key fed by the parent's service and showing its display column — the grid shows the same name). `edit()` trims a timestamp to `yyyy-MM-dd` for the date box; `add()` starts a row at today / 0 / "" / the bit's database default. The spec includes the `HttpClient` testing providers the stock CLI spec lacks. No detail grids, dependent drop-downs or routing (the route, sidebar entry and `app.config` line remain by-hand checklist steps).
    - Verified against the `TimeEntry` database and `TimeEntryUI`: every table with an API generated with no template errors; the generated screens for `DonateLeave`, `DepartmentTeam`, `ProjectTask`, `RequestExpenseDetail`, `RequestExpenseSheet`, `Response`, `RestrictLeave` and `TimeEntryUser` were routed in a scratch copy and compiled with `ng build` (templates type-checked); `Holiday`'s component, model and service, `Employee`'s model and service, and the `Project`, `TimeSheet` and `Request` services replaced the hand-written files and all 12 unit tests pass in headless Chrome (`ng test`). Screens with detail grids or dependent drop-downs (`Department`, `Employee`, `Project`, `Request`, `TimeSheet`, `TimeSheetDetail`) and the `Request`/`TimeSheet`/`TimeSheetDetail`/`Department`/`Project` models stay hand-maintained.
  - **`SP_Load.tt`** — the "seed data load": reads the table's *current rows* at generation time (`NeedsRowData=true`, §5.3) and writes `<Table>_Load`, a procedure that puts the same rows into another database — for small reference/lookup tables. Improves on the original hand-written seed-data load procedure in three deliberate ways: (1) **re-runnable** — each row is inserted only `IF NOT EXISTS` for its primary key, and existing rows are never updated; (2) **primary key values are kept** (identity columns loaded under `SET IDENTITY_INSERT ON`) instead of being regenerated, because ids are referenced elsewhere (enum ids, self-references) — the original silently broke those; (3) **one transaction**, joining the caller's, with `IDENTITY_INSERT` switched back off on every path. Values are written by `CodeGenNew.Core.SqlLiteral` (doubled quotes, `N'…'` for Unicode, language-independent ISO dates, invariant numbers, `0x` binary). Rows are a faithful copy — including the active and admin flags — except that `CreateDateColumn`/`LastChangedDateColumn` become `GETDATE()` and a `ModifiedDateColumn` is omitted. Rows are emitted in primary key order (so a same-table parent with a lower key loads first); other tables it refers to must be loaded first. Tested by generating from a populated disposable LocalDB database (every SQL type incl. edge values, NULLs, composite and self-referencing keys, empty table), loading an identical empty database twice, and comparing both directions with `EXCEPT` — zero differences; failure path rolls back and resets `IDENTITY_INSERT`.
  - **`SP_Lookup.tt`** — returns, for every row, its ID and display columns plus, per foreign key, the referenced row's ID and display columns (`NeedsReferencedDisplayColumns=true`, §5.3), so a list/drop-down needs no joins of its own. Built to avoid the defects visible in the original lookup procedure: **unique output names** (an fk's id is the base table's own fk column, e.g. `accounttype1099enumid`; a foreign row's display value is named for its **role**); the **role** comes from the fk column name — trailing `enumid`/`id` dropped, then a `_<referencedtable>` tail (`accounttype1099enumid` → `accounttype1099`, `contraaccount_accountid` → `contraaccount`) — and doubles as the table alias, so the same table joined many times (`syenum`) and a **self-reference** (`contraaccount` → `account`) each get a distinct alias, never the base table's own name; **`left join` when the fk column is nullable** (the original's `inner join`s dropped every row with an optional fk left null — 0 of 3 rows survived in testing), `inner` only when not null, composite fks join on all columns. `order by` the best-ranked display column then the key. a table with an active/inactive flag returns only active rows unless ` = 1` (referenced rows are never filtered). kept from the original: no filter parameters, and error 55508 when nothing is returned. tested against a disposable localdb database (many enum fks into one table, a self-referencing fk, a composite fk, a two-display-column table, a table with no display columns, an active flag, an empty table), then dropped.
  - **`SP_Clone.tt`** — behind a grid row's "Clone" button: copies a row into a new one and returns the new key (`<Key>` in, `<Key>` out — names from the original clone procedure). Replaces the original's `#TMP` + `SELECT *` + positional `INSERT … SELECT *` (which broke on computed/`timestamp` columns and column reordering, had an invalid `SET @@NewID`, and used `@@IDENTITY`) with one `INSERT INTO t (named columns) SELECT … FROM t WHERE <key>`. The **new key**: an identity key is assigned and returned via `SCOPE_IDENTITY()`; a single `uniqueidentifier` key gets `NEWID()` and is returned; any other key (composite/natural) is a required `@New<Key>` input. Otherwise an exact copy, **except** by rule: `CreateDate`/`LastChanged` → `GETDATE()`, `ModifiedDate` NULL, `CreateUser` is a parameter (who is cloning), an inactive source clones **active** (InactiveDate/reason NULL), the **admin flag is never copied**, a soft-deleted source clones not-deleted. Columns in a **unique index** (`ColumnModel.IsInUniqueIndex`, read from `sys.indexes`) get an optional override parameter (`= NULL` means copy the source's value); without one, a copy that keeps a unique value fails with SQL Server's own duplicate-key error and rolls back. Joins the caller's transaction or owns its own; a source key matching no row raises **55509**. Tested against a disposable LocalDB database (identity, unique-constrained, GUID, composite, natural, key-only and trigger-bearing tables; failed-clone rollback; caller-rolled-back transaction), then dropped.
- The plugin/menu/template-discovery architecture supports adding more templates (e.g. `API_Crud.tt` was added this way) with no code change.
- Special-logic detection (Active/Inactive, StartDate/EndDate, Soft Delete, and the Create/Modified/LastChanged audit-date trio) is live in `TableModel` and actively used by these three templates.

## 12. V2+ Roadmap (not built yet)

- Add views to the TreeView; refactor to treat tables/views uniformly where possible.
- Further API templates beyond `API_Crud.tt` (a name/active-flag variant with duplicate-name checks, a list-by-parent variant) (like the hand-written `E_DonateLeaveApi`), assuming a `GenericRepo<T>`/`BaseApi<T>`/`TimeEntryContext` style base already exists in the target project.
- Additional SP templates: Insert, Delete (with `spCanDelete` check wired in), Select.
- MySQL support — the reason `CodeGenNew.Connections` and `CodeGenNew.SchemaIntrospection` are separate projects/interfaces now.

## 13. Explicitly Out of Scope (decided, not just deferred)

- SQL Server extended properties, permissions, and change-tracking metadata — never captured.
- The prior system's grid/single-view metadata-table-driven UI customization (`GridVisibleColumns`, `SingleViewHiddenColumns`, `Tabs`, `Groups`, `Caption` via a config table) — considered too custom to this application.
- Parent-table-name-prefix stripping (`NameBaseXXX` → `XXX`) — company-specific convention, doesn't apply to the databases this tool targets.
- Shared Base Code file tracking/copying in the Template Screen — dropped for simplicity; developer's responsibility.
- Encrypted/portable-passphrase connection secrets — dropped in favor of always prompting for password.
- Showing columns/FKs/indexes as their own TreeView nodes.

## 14. Reused / Ported Reference Code

Source: `C:\EricWork\Avatar Code\AvatarCodeGenerator\AvatarCodeGenerator\Avatar.CodeGen\` and `C:\EricWork\Avatar Code\Avatar.Common.Extension\...\ExtensionMethods\`. Per the author's instruction, **specific methods/properties are ported, not whole files**, regrouped into whatever `CodeGenNew` files make sense:

| Source | What's reused |
|---|---|
| `Avatar.CodeGen.SqlServer.DataLayer\Table.cs` | Concept/shape of a rich, cached "table model" object (`HasActiveState`-style pattern); `IsColumnForeignKeyOnTable`; `IsSelfReferencing`; `IsForeignKeyMulti`. Grid/single-view/Tabs/Groups/Caption members explicitly **not** ported. |
| `Avatar.CodeGen.SqlServer.DataLayer\Column.cs` | Type-classification pattern (`IsIntegerColumn`, `IsMoneyColumn`, `IsStringColumn`, `IsDateColumn`, etc.), retargeted from the old custom `SqlDataType` enum onto `System.Data.SqlDbType`; `IsStartDate`/`IsEndDate` prefix-matching approach (generalized into the wildcard config format, §5.2). |
| `Avatar.CodeGen.SqlServer.DataLayer\TableTools.cs` | `IsSystemTable` (ported/broadened, see §9.4). Rest of file is company-specific and not used. |
| `Avatar.CodeGen.SqlServer.DataLayer\ColumnTools.cs` | `SetColumnDefault` (heuristic fallback default, used only when there's no real DB default constraint) and `IsAuditColumn`. |
| `Avatar.Common.Extension\...\ExtensionMethods.String.cs` | `IsSqlReservedWord`, `ToPlural`, `ToSingular`, `StripTablePrefixes`, `ToUserFriendly`, and any other individual methods found necessary during implementation. Not the whole 1700+ line file. |
| `Avatar.Common.Extension\...\ExtensionMethods.DataColumn.cs` | Reviewed; nothing beyond what's already covered elsewhere was needed. |

`ChildTable.cs` and `DataRow`/`DataTable` extension methods were explicitly excluded per the author (not useful here / company-specific).

## 15. Open Risks / To-Verify (first implementation tasks)

1. ~~.NET 10 + Windows App SDK 2.4.0 compatibility~~ — resolved: `net10.0-windows10.0.19041.0`, unpackaged, self-contained, `Microsoft.WindowsAppSDK` 2.4.0 builds clean and launches without error. No need for the .NET 9 fallback.
2. SQL default-value translation (`SuggestedCSharpDefaultValueLiteral`) is explicitly best-effort/low-priority per the author ("don't worry about edge cases... rarely used item") — unparseable defaults simply produce no suggested value, never an error.
3. ~~`Mono.TextTemplating`'s exact in-process API surface~~ — resolved: `TemplateGenerator.GetOrCreateSession()` returns an `ITextTemplatingSession` (an `IDictionary<string, object>`); setting `session["Model"] = tableModel` before `ProcessTemplateAsync()` is how a `<#@ parameter name="Model" type="..." #>` directive receives a real object, not just a string. `generator.Refs.Add(...)` makes a custom assembly (e.g. `CodeGenNew.Core.dll`) resolvable to the compiled template class. Confirmed working end-to-end against a real database.
4. ~~`spCanDelete` version drift~~ — resolved: the test database briefly had a drifted, 5-parameter version of `spCanDelete` deployed (extra `@SkipBridgeTables`/`@StopAfterFirstUsage`/`@TotalTimesUsed OUTPUT` params); the author has since reverted it back to the 2-parameter signature documented in the original generator specification, which is what `SP_Delete.tt` targets. Confirmed via testing that `SP_Delete.tt` correctly returns `-1` (blocked, row untouched) when a dependency exists and `0` (deleted) when none exists, against the reverted signature.
5. ~~`spCanDelete` reserved-word bug~~ — fully resolved by the author: both `@UsedInTable` and `@UsedInColumn` are now bracket-quoted in the dynamic SQL (`FROM [' + @UsedInTable + '] WHERE [' + @UsedInTable + '].[' + @UsedInColumn + ']`). Retested against `NameBase`/`ID=1` twice (once after the table-name fix, once after the column-name fix) — no syntax errors either time, correctly returns `-1` (blocked).

## Appendix A0: SQL Statement Style

Every generated T-SQL statement ends with a semicolon (`;`), across all templates — modern ANSI-standard style, not the older SQL Server convention of treating it as optional. `IF`/`BEGIN`/`END`/`WHILE` themselves aren't terminated (they're control-flow keywords, not statements), but every `DECLARE`, `SET`, `SELECT`, `INSERT`, `DELETE`, `EXEC`, `CREATE TABLE`/`DROP TABLE`, and `RETURN` inside a generated procedure body is. Retested all three shipped templates end-to-end after this change (Update/Insert/Delete against real `NameBase` data via the manual test protocol, §1.1) — no regressions.

## Appendix A: Hungarian Parameter Prefix Convention

Matches the convention already used in the author's worked example (`ProductionUnitMaster_Update`). `ColumnModel.ParameterName` = `"@p" + typeCode + PascalCase(ColumnName)`:

| SQL type family | Prefix |
|---|---|
| int / bigint / smallint / tinyint | `lng` |
| varchar / nvarchar / char / nchar | `str` |
| datetime / date / datetime2 / smalldatetime | `dte` |
| bit | `bln` |
| decimal / numeric | `dec` |
| float / real | `flt` |
| money / smallmoney | `cur` |
| uniqueidentifier | `guid` |
| binary / varbinary | `bin` |
| (anything else) | `var` |

Example: `Description` (varchar) → `@pstrDescription`; `ID` (int) → `@plngID`; `StatusDate` (datetime) → `@pdteStatusDate`.

Per the shipped `SP_Update.tt`, **every** generated parameter is declared as `VarChar` regardless of the column's real SQL type (matching the author's original worked example exactly) — the Hungarian prefix still reflects the *underlying* column type for readability, even though the parameter's declared SQL type is always `VarChar`.
