# Reference: configuration files and column rules

The rules that source comments and template configs point at. The product's promises are in [specs.md](specs.md); the layout of the code and the flow of a run are in [ARCHITECTURE.md](ARCHITECTURE.md). The project settings keys (`<name>.config`) are in section 8.

## 1. `Settings.json`

Plain JSON with the app-level settings and the last connection (never a password). It lives beside the program, or in `%APPDATA%\CodeGenNew` for the installed tool (`CODEGENNEW_HOME` overrides both).

```json
{
  "OutputDirectory": "Output",
  "TemplatesDirectory": "Templates",
  "SpecialLogicColumnsConfigPath": "SpecialLogicColumns.config",
  "SpCanDeleteVerificationConfigPath": "SpCanDeleteVerification.config",
  "LastConnection": { "AuthMode": "SqlLogin", "ServerName": "", "DatabaseName": "", "UserName": "" }
}
```

- `AuthMode` is `SqlLogin` or `WindowsAuth`. The connection dialog always asks for the password again.
- If `OutputDirectory` does not exist, the app asks whether to create it.
- **Saves merge, they do not overwrite.** `AppSettingsService.Update` re-reads the file as it is on disk, changes the one setting and writes it back, so a second (or stale) running instance cannot replace the saved connection with its own old copy. A failed write is shown in the status bar. The last connection is also remembered when a test succeeded and the dialog was then cancelled.

## 2. `SpecialLogicColumns.config`

Pipe-delimited text, hand-editable, `#` starts a comment. One row per rule:

```
category|flagcolumn(s)|companioncolumn(s)|special
```

- A name pattern: `*` at the end means starts with, `*` at the start means ends with, `*` in the middle means contains, no `*` means exact. Matching is case-sensitive unless `special` contains `IgnoreCase`. Several patterns for one slot are comma-separated; any one matching counts.
- **Companion blank:** a per-column classification (the flag pattern alone makes a per-column property true).
- **Companion filled:** a table-level pair rule: the table needs a column matching the flag pattern **and** one matching the companion pattern (for example `HasActiveInactivePair`).
- A pattern list is an ordered priority where order matters (`DisplayColumn`). Categories must not claim the same column: the config has no shared priority between categories, so the pattern lists themselves are kept disjoint (a test guards `ModifiedUserColumn` against `ModifiedDateColumn`).
- Patterns are real spellings seen across many databases, not broad catch-alls: `*Updated` or `*Created` would also match a count or a flag and generate a date assignment into it.

## 3. `<TemplateName>.tt.config`

One per template, same base name, plain `key=value` lines, `#` comments. A missing key takes its documented default. A key the reader does not know (a typo such as `Requries`, or an item such as `Requires=SingleKye`) is reported: the CLI prints a warning, a whole-project run lists it among its warnings, and a test fails for a shipped template, because the restriction it was meant to state is not applied.

**Restrictions** (decide whether the menu, the CLI and a plan offer the template for a table):

| Key | Default | Meaning |
|---|---|---|
| `Requires` | `PrimaryKey` | the table restrictions of a table template, as one comma-separated list of: `PrimaryKey` (hidden for a table without a primary key), `SingleKey`, `NotNameActive`, `JunctionTable`, `ChildTables`, `AuditTable` (each described below). Without the key a table template requires a primary key and nothing else; `Requires=` (empty) requires nothing. |
| `Requires=SingleKey` | | the table must have one key column that is an int, a uniqueidentifier or text (the keys `KeyType` describes: the template reads the C#, TypeScript, Python and route form of the key from Core). A composite key, a key of another type (a decimal or a date) or none is refused up front. The model's own `PrimaryKeyShape` is the finer `None`, `Composite`, `SingleInt`, `SingleUniqueIdentifier`, `SingleText`, `SingleOther`. Junction and child-grid templates set nothing: they go by their association columns, and a junction table's key may be composite. |
| `Requires=JunctionTable` | | only for a many-to-many table: excluding computed, audit and surrogate identity-key columns, exactly two columns remain and each is covered by its own single-column foreign key (a natural composite key and an identity key plus two foreign keys both count; one composite foreign key over both columns does not) |
| `Requires=ChildTables` | | only when another table has a foreign key back at this one (master-detail screens) |
| `Requires=AuditTable` | | only for a table with audit columns: one that records its creation (a name starting `Create`, like CreateDate or CreateUser) and one that records a later change (a name with `Modif` or `Change`, or starting `Update`, like ModifiedDate or UpdatedBy) |
| `Requires=NotNameActive` | | hidden for a "name/active" table (a NOT NULL text column `Name` and a NOT NULL bit column `IsActive`). Its repository is a `NameActiveRepo` with duplicate-name checks and trimming a template cannot supply, so the templates that assume a plain list / get / create / update / delete backend (the single-row screens, the Blazor and Uno OData pages, Rust, Python) set this. `API_Crud`, the Angular, React, Blazor, Uno and WinUI3 screens and their clients write a variant for it and do not set it. `CS_Entity` and `CS_Repo` support both shapes. |
| `Dialects` | all | the databases the template is for (`SqlServer`, `PostgreSql`, `MySql`, `Sqlite`); a plan skips it silently for another one, the menu does not offer it, and a single run is refused with the reason |
| `Dashboard` | on unless false: the plan also writes the dashboard: its queries (CS_Dashboard, SP_Dashboard), the GET /api/dashboard endpoint (API_Dashboard), a page and menu entry per front end (TSX_Dashboard, TS_Dashboard, WinUI3_DashboardPage, BLZ_Dashboard, RS_Dashboard, PY_Dashboard) and a Markdown page listing the widgets (MD_Dashboard) |
| `DashboardStrip` | true (with Dashboard): each table screen shows that table's two or three cards above its grid |
| `DashboardMeasures` | comma-separated widgets you name: Table.Column:sum|avg|max|min|count, or Table.Column:aggregate:DateColumn:day|month|year for a trend (SalesInvoice.TotalAmount:sum:InvoiceDate:month) |
| `NoDashboardTables` | comma-separated tables the dashboard leaves out |
| `AccessMode` | both | `Routines` or `Ef`: the template writes routines the generated code calls, or the LINQ that replaces them; a plan skips it when the project's access mode (the `AccessMode` setting; `Ef` for SQLite) is the other one, unless `PlanAlso` names it |

**Extra input the template asks for** (each costs read-only catalog queries, so the default is off):

| Key | Meaning |
|---|---|
| `NeedsRowData` | read the table's rows into `TableModel.Rows` (ordered by key; computed columns are `null`); refused above 5000 rows, since it is meant for small reference tables |
| `NeedsReferencedDisplayColumns` | look up the display columns of each foreign-keyed table into `ForeignKeyModel.ReferencedDisplayColumns` |

**What the template is given:**

| Key | Meaning |
|---|---|
| `Scope` | `Table` (the default: once for a table, receives `Model`), `Database` (one file for the whole database; receives `Database`, every table; run without `-t`) or `None` (needs neither table nor database; receives only `Project`; run without `-S`, `-d` or `-t`) |

**Output:**

| Key | Meaning |
|---|---|
| `OutputName` | file-name pattern replacing `<Table>_<Suffix>.<ext>`; `{Table}` is the table name; file name only, never a path |
| `Base` | the file of the template whose text this one runs (same folder), for a stub such as `TS_ODataComponent` that is `TS_Component` in its OData form; the stub's own config says where the output goes |
| `Variant` | with `Base`: the value of the base template's `Variant` parameter (for example `OData`); the base writes its second form when it sees one |
| `OutputFolder`, `OutputFolder.<Stack>` | the folder under the root that the template's relative paths land in |
| `OutputRoot` | `Stack` (the stack's project folder, default) or `Sql` (the project's SQL folder) |

**Whole-project generation:**

| Key | Meaning |
|---|---|
| `Stacks` | the stacks that include the template (`Api`, `WinUI3`, `React`, `Angular`); empty means run by hand only |
| `PlanTables` | which tables a plan runs it for (entity tables, `Client`: the entity tables the project gives an API to (a front end's model, service and api module), `Context`: the entity tables and the composite-key junction tables, tables with an API, `AspNetApi`: those, the name/active tables and the guid and text key tables, `AspNetSearch`: the tables with a search endpoint, guid and text key tables included, tables with a screen (a guid or text key table is listed for the stacks in `KeyType.StackHandlesEveryKey`: Api, Angular, React, Blazor and WinUI3 so far) ...); ignored for database-level and no-database templates |
| `InPlan` | default true; `false` joins a plan only through the project's `PlanAlso` or a flag that implies it |
| `EssentialsGroup`, `Description`, `EssentialsDefault` | the template is one file group of a stack's essentials: its menu name, one line on what it writes, and whether it is ticked the first time |
| `Needs` | files (relative to the stack folder, `{Context}` for the context name) that the group assumes another run wrote; a run warns when one is missing |

The default extension comes from the group prefix: `SP` is `.sql`, `API`, `CS` and `WinUI3` code is `.cs`, `TS`/`TSX` are `.ts`/`.tsx`, `FS` is `.fs`, `MD` is `.md`, anything else `.txt`.

## 4. `SpCanDeleteVerification.config`

Pipe-delimited, append-only: `ServerName|DatabaseName|Status|CheckedAtUtc`, with `Status` `Verified` or `NotFoundOrWrongSignature`. `SpCanDeleteVerifier` writes one line the first time a database is connected to: a read-only catalog check that a procedure `spCanDelete` exists with the signature `@deleteFromTable varchar, @deleteId int`. Later connections use the line. It is informational only and changes nothing a template writes; deleting a line forces a new check.

## 5. What the special-logic rules do

The rules of section 2 are read once, when the model is built, and become properties on the table and its columns. Templates only read them.

1. **Active / inactive pair** (`HasActiveInactivePair`). The flag pattern covers both polarities: `IsActive` / `Active` (1 is active) and `IsInactive` (1 is inactive).
   - *Insert:* a new row is never born inactive: the flag is left out of the parameters and set to its active value (`1`, or `0` for an `IsInactive` column). The inactive-date column and any `InactiveReasonColumn` match are left out and stay `NULL`.
   - *Update:* if the caller supplies an inactive date or a reason, the flag is **forced** to its inactive value; otherwise a supplied flag is applied, so a row can be reactivated. This is a single `IF ... SET flag = <inactive> ELSE IF ... SET flag = <caller's value>`: SQL Server rejects assigning one column twice in one `UPDATE` (error 264), so the flag is excluded from the ordinary per-column loop.
2. **Start / end date pair** (`HasStartEndDatePair`). Generated logic checks start before end.
3. **Soft delete pair** (`HasSoftDelete`): an `IsDeleted` flag and a deleted date.
4. **Dependency check on delete** is not a column rule. The delete routine relies on the database's own foreign key enforcement (section 6).
5. **Audit columns**, per column, never taken as caller parameters except where noted:
   - `CreateDateColumn`: set by insert only, never touched by update.
   - `CreateUserColumn` (`CreateUser*`): a parameter of insert and save, left out of update so the creator is never overwritten.
   - `ModifiedDateColumn`: set to the current time by every update, not by insert.
   - `LastChangedDateColumn`: touched by every write, set with the create date on insert and with the modified date on update.
   - `ModifiedUserColumn` (`ModifiedBy*`, `UpdatedBy*`): the update-side mirror of the create user: a parameter of update, left out of insert, load and clone.
6. **`DisplayColumn`** (a per-column rule where pattern order is a priority): the columns a lookup shows so a person can recognise a row (`ShortDescr`, `Name`, `AccountNumber` ...). Every match is shown in table order, and the earliest-listed pattern that matches picks the lookup's `ORDER BY`. Key and foreign-key columns are never display columns; a table with none falls back to its first ordinary text column; large text, `xml` and binary columns never count.
7. **`InactiveReasonColumn`** and **`FilePathColumn`**: classified; the first is left out of insert, the second is read by the directory listing's settings only.

## 6. The generated delete routine

The routine attempts the delete and reads the error: success returns `0`, a foreign key or CHECK violation (SQL Server error 547) returns `-1` (blocked: a dependent row exists), anything else returns `-2`. The error number is captured into a variable straight after the statement, because it describes only the immediately preceding statement. There is no pre-check, no temporary table and no call to `spCanDelete`: those cost time on every delete, by every user, to repeat a check the database already makes for free, and the routine behaves the same for every key shape. That is the SQL Server form; the PostgreSQL function catches `foreign_key_violation` and the MySQL procedure handles error 1451, and both return the same three results.

## 7. Style of generated SQL

- Every T-SQL statement ends with a semicolon (`DECLARE`, `SET`, `SELECT`, `INSERT`, `DELETE`, `EXEC`, `RETURN`, `CREATE`/`DROP TABLE`); `IF`, `BEGIN`, `END` and `WHILE` are not statements and are not terminated.
- **Parameter names** on SQL Server are `@p` + a type code + the PascalCase column name, so a reader sees the underlying type: `Description` (varchar) is `@pstrDescription`, `ID` (int) is `@plngID`, `StatusDate` (datetime) is `@pdteStatusDate`.

| Column type family | Prefix |
|---|---|
| int, bigint, smallint, tinyint | `lng` |
| varchar, nvarchar, char, nchar | `str` |
| datetime, date, datetime2, smalldatetime | `dte` |
| bit | `bln` |
| decimal, numeric | `dec` |
| float, real | `flt` |
| money, smallmoney | `cur` |
| uniqueidentifier | `guid` |
| binary, varbinary | `bin` |
| anything else | `var` |

- **Update** declares every parameter as `VarChar` and builds its statement dynamically, so a `NULL` argument means "leave this column alone", with trimming and doubled single quotes for text. **Insert** and **save** use properly typed parameters and a static statement (bound values need no escaping). Save takes the whole record with no defaults, so an omitted parameter cannot silently null a column.

## 8. Project settings keys

One `<name>.config` file per project: `key=value` lines, `#` comments, lists comma-separated. Every key is optional; a blank one takes the default described here (a name derived from `ProjectName`, or the template's own value). The settings screen shows the same text beside each box.

| Key | Meaning |
|---|---|
| `ProjectName` | the project's name (the file name of its .config); every blank namespace and folder is derived from it |
| `ViewNamespace` | blank = &lt;ProjectName&gt;.App.Views |
| `ViewModelNamespace` | blank = &lt;ProjectName&gt;.App.ViewModels |
| `ContextName` | blank = &lt;ProjectName&gt;Context |
| `ContextNamespace` | blank = &lt;ProjectName&gt;.App.Data |
| `ApiNamespace` | blank = &lt;ProjectName&gt;.ApiService.Apis |
| `EnumNamespace` | blank = &lt;ProjectName&gt;.App.Enums |
| `RepoNamespace` | blank = &lt;ProjectName&gt;.App.Repositories |
| `EntityNamespace` | blank = &lt;ProjectName&gt;.App.Entities |
| `MinYear` | blank = 2000 |
| `MaxYear` | blank = 2100 |
| `MaxGridColumns` | the most columns a generated grid shows (long text is never shown); blank = 18 |
| `ViewsFolder` | blank = the template's own folder |
| `ViewModelsFolder` | blank = the template's own folder |
| `CurrencyCode` | ISO currency code for money fields; blank = USD |
| `Usings` | comma-separated namespaces every generated file should use |
| `RequiredMarks` | a red `*` after the caption of every required field on the generated forms of every stack (a NOT NULL field the person fills in; not a yes/no box, not a system stamp); false: no marks; blank: true. Regenerate the screens to apply it |
| `DetailMasterTables` | comma-separated table names whose Add/Edit dialog is a Detail-Master dialog (with child grids) |
| `EnumTables` | comma-separated enum tables (no entity, repository or API); blank = decided from each table's shape |
| `EnumMaxRows` | a lookup table with more rows than this is not an enum; blank = 25 |
| `EnumNameSuffixes` | comma-separated name endings that mark an enum table; blank = Type, Types, Code, Codes, Status, Kind |
| `HiddenParents` | comma-separated tables whose foreign key columns the TypeScript forms hide |
| `ModelFileOverrides` | Table=file pairs for model files not named after the table, e.g. DepartmentTeam=department,ProjectTask=project |
| `EditableChildren` | comma-separated child tables whose rows are added, changed and removed inside the parent's form and saved with it in one request: the child (ProjectTask), or Parent.Child (Department.DepartmentTeam) for a child that also points at another table. The child needs an identity whole-number key and one NOT NULL link to the parent; Angular and React (the master-detail page), Blazor (the form of the page), WinUI3 and Uno (the master-detail dialog); blank: every child grid is read-only. Refresh the `CrudApi` group with `--replace` |
| `SeedTables` | comma-separated tables whose current rows are written as seed data, a parent before its children (Region,Currency): `CS_Seed` writes a class per table (`Seeding/<Table>Seed.cs`) that adds its rows when the table is empty, and `CS_SeedData` the class that runs them all and `app.SeedAsync()`. The rows are read when you generate; an identity key is numbered again from 1. Blank: nothing is seeded |
| `ChildGridTitles` | Parent.Child=Title pairs, comma-separated, for a child grid whose table name does not say what the rows mean (Customer.CustomerItem=Item Purchase History) |
| `BaseEntity` | base class of generated entities; blank = BaseEntity |
| `BaseNameActiveEntity` | base class for Name + IsActive tables; blank = BaseNameActiveEntity |
| `TypeAheadRows` | a parent with more rows than this is picked by typing (a type-ahead that asks the API for matches) instead of a drop-down of every row; 0 = never; blank = 1000 |
| `TypeAheadParents` | tables that are always picked by typing, whatever their row count |
| `NoLookupParents` | overrides EnumTables for this one question: tables whose foreign key is a number box, not a drop-down |
| `NoRepositoryTables` | overrides EnumTables for this one question: tables that get no repository |
| `NoApiTables` | overrides EnumTables for this one question: tables that get no API or TypeScript model/screen |
| `NoNavigationTables` | overrides EnumTables for this one question: tables referenced without a navigation property |
| `NamingStyle` | AsIs (the database's names, the default) or Pascal (customer_item becomes CustomerItem; the SQL keeps the real names) |
| `Acronyms` | comma-separated words the Pascal style keeps upper-case whole (PO,UPC,MSRP) |
| `Screens` | comma-separated tables that get a screen, in menu order; blank = every table that has an API and a search, alphabetically |
| `NoCloneTables` | comma-separated tables that get no Clone button although they could (Customer,SalesInvoice) |
| `NonNegativeColumns` | comma-separated money columns that can never be negative (CreditLimit, or Item.Cost for one table): their number box gets a minimum of 0, the validators refuse a negative value and the insert, update and save routines raise an error for one (a CHECK range in the database does the first two without the list, and then needs no guard in the routines). An update judges only the values it changes, in the routines (the bulk update too), the ASP.NET, Python and Rust APIs and the React, Angular, Blazor (with `ApiAnnotations`) and WinUI3 forms, so a stored value that already breaks the rule does not stop an edit of another column |
| `ApiAnnotations` | true: the plan also writes the DataAnnotations attributes per table (CS_Validation, a `<Table>Metadata` class the entity need not be partial for) and the create and update endpoints run them with `Validator.TryValidateObject` (400 with the messages); works with or without `ApiValidation`; the Dtos input types and the Blazor models carry the same attributes and the Blazor forms run them |
| `DatePairs` | comma-separated pairs of date columns where the second must not be earlier than the first (`FirstInvoice>LastInvoice`, or `Project.Opened>Closed` for one table), added to the pairs the schema names: a start and end column found by name (`StartDate` and `EndDate`, the pair the insert and update routines already refuse) and a CHECK constraint that puts one date column before another (`CHECK (check_out >= check_in)`, also `end IS NULL OR start <= end`). The ASP.NET, Python and Rust validators, the zod schema and the Angular group validators refuse an end before its start, a form's end date box starts at the start (React, Angular, Blazor, WinUI3, Uno) and the form refuses a changed one; a date may equal the other and a blank one is never judged |
| `NoDatePairs` | comma-separated tables, end columns or `Table.End` columns that are not held to a start date even though the names or a CHECK constraint say so (`Sabbatical`, `Closed`, `Project.Closed`) |
| `NotFutureDateColumns` | comma-separated date columns that can never be later than today (BirthDate, or Employee.HireDate for one table): the insert, update and save routines raise an error for a later date, and so does the FluentValidation validator |
| `PlainTextColumns` | comma-separated text columns whose name says email, phone or URL but that hold something else (EmailTemplate, or Site.PhoneNotes for one table): they get no `[EmailAddress]` / `[Phone]` / `[Url]`, no validator rule and an ordinary text box |
| `EmailColumns` | comma-separated text columns that hold an email address whatever their name (Reach, or Contact.Backup for one table): they get `[EmailAddress]`, the email checks of every stack and an email box. A name with Email in it, or that is Mail or ends in Mail with a capital (OwnerMail), needs no entry |
| `UrlColumns` | comma-separated text columns that hold a web address whatever their name (Where, or Site.Address for one table). A name with Url, Website, Link, Homepage, WebPage or WebAddress in it, or that is Web or ends in Web with a capital (CompanyWeb), needs no entry |
| `MoneyColumns` | comma-separated decimal or floating-point columns that hold money whatever their name (Surplus, or Invoice.Surplus for one table): a form shows them as currency. MySQL has no money type, so there a decimal(19,4) or decimal(10,4) (the size of SQL Server's money and smallmoney) is currency without an entry, unless its name says it counts or measures something (a percentage, a quantity, a weight) |
| `NotMoneyColumns` | comma-separated columns that are not money though their name or their MySQL size says so (Rate, or Item.Cost for one table): shown as plain numbers. Beats MoneyColumns and the name |
| `ValidatorNamespace` | the namespace of the FluentValidation validators CS_Validator writes (default &lt;ProjectName&gt;.App.Validators) |
| `ApplicationNamespace` | the namespace of the commands, queries and handlers CS_CqrsHandlers writes (default &lt;ProjectName&gt;.App.Application) |
| `FakerNamespace` | the namespace of the Bogus fakers CS_Faker writes (default &lt;ProjectName&gt;.App.Fakers) |
| `ErdTables` | comma-separated tables MD_Erd draws (Customer,SalesInvoice); empty: every table |
| `ApiDocs` | true: the plan also writes openapi.yaml (API_OpenApi) and the API serves it with a Swagger UI page at /docs |
| `ApiHttp` | true: the plan also writes a .http request file per table (API_Http) |
| `ApiFakers` | true: the plan also writes a Bogus fake-data generator per table (CS_Faker) and the generated project references Bogus |
| `ProjectDocs` | true: the plan also writes a data dictionary page per table and the ER diagram (MD_DataDictionary, MD_Erd) |
| `ApiProduction` | true: the API project also gets a production profile (ProductionProfile.cs: Problem Details, health checks, rate limiting, response compression, security headers) and a Dockerfile; no package is added |
| `ApiTelemetry` | true: the API project also gets OpenTelemetry traces, metrics and logs (Telemetry.cs) and references the OpenTelemetry packages; they are exported over OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` names a collector |
| `ApiTestContainers` | true: with `ApiTests` and a PostgreSQL, MySQL or SQL Server database, the test project starts a throwaway container of that database (Testcontainers; the package for the provider is added to the test project, Docker must be running) when `<PROJECT>_TEST_CONNECTION` is not set, fills it from `ApiTestSchema`, tests over it and removes it; without Docker the tests report themselves as inconclusive. SQLite needs no container |
| `ApiTestSchema` | comma-separated SQL scripts a test container is filled from, relative to the API project's folder, applied in the order listed (a folder means every `.sql` file in it by name): the create script of the database, then the routines the plan wrote when `AccessMode` is not `Ef`. Blank (the default) = the plan writes `sql/CreateSchema.sql` from the catalog (`SP_CreateSchema`) and the fixture applies it, then the SQL folder |
| `BlazorTests` | true: the plan also writes a bUnit test class per Blazor page (BLZ_Test) and the test project (Tests essentials group of the Blazor stack) beside the Blazor app: the heading, a row the server returns, the Edit dialog, a Save that sends the row and `?edit=<key>`; the API is a stand-in, so no database is needed; folder `OutputBlazorTests`, default `<ProjectName>.Blazor.Tests` |
| `DesktopTests` | true: the plan also writes a test class per WinUI3 and Uno dialog (WinUI3_Test, UNO_Test) and a plain net10.0 test project beside each app (the Tests essentials group of the stack) that compiles the app's own view models: a new row with nothing filled in is refused, a row opened and saved unchanged keeps every value, and each rule of the form (a shape, a date later than today, an end before its start, a number that is not whole) refuses a spoiled value. WinUI3 runs over an in-memory SQLite database made from the generated context, Uno over a stand-in for the API; folders `OutputWinUI3Tests` and `OutputUnoTests` |
| `PythonTests` | true: the plan also writes pytest tests into the Python API, in `tests` (`PY_TestApprovals`, for a project with `RowApprovals` and `ApiAuth`): `conftest.py` opens the application over a scratch database (a copy of the SQLite file, or the URL in `<PROJECT>_TEST_CONNECTION` for PostgreSQL and MySQL; SQL Server is skipped) and `test_approvals.py` plays the approval cycle as the people it adds with SQL and removes again. `requirements.txt` then lists pytest and httpx. Run `pytest` from the API's folder |
| `RustTests` | true: the plan also writes integration tests into the Rust crate, in `tests/api` (RS_Test, RS_TestBodies, RS_TestJunction, and with a dashboard, `ApiCsv` or a gate or guard rule RS_TestDashboard, RS_TestCsv and RS_TestGates) and the helper they share (Tests essentials group of the Rust stack): add, read, change and remove a row, a 404, the search, the sorts, a copy, a unique value, the validation limits, with `ApiAuth` the 401 and 403 answers and the owner rule of `OwnedTables`, and the junction, dashboard, CSV and gate routes. They run the router in process, so `cargo test` needs no server; SQLite tests run over a copy of the file, PostgreSQL and MySQL need `<PROJECT>_TEST_CONNECTION` (a URL of a scratch copy) |
| `ApiTests` | true: the plan also writes an integration test class per table (API_Test) and the test project (Tests essentials group) beside the API, and with `Dtos=true` also a database-free round-trip test class per table (CS_RoundTripTest); folder `OutputApiTests`, default `<ProjectName>.Api.Tests` |
| `ApiAuth` | true: the API signs people in. JWT bearer wiring, `POST /api/auth/login` and a fallback policy that makes every other endpoint need a token (the `Jwt` essentials group and the package `Microsoft.AspNetCore.Authentication.JwtBearer`). The signing key is `Jwt:Key` (an environment variable, never a file); the first administrator is `Bootstrap:AdminUserName` and `Bootstrap:AdminPassword`; register your own `IUserDirectory` for real users. Regenerate the project and refresh the `CrudApi` and `Program` groups with `--replace`. The Python stack gets `app/auth.py` and the Rust stack `src/auth.rs` (the `Jwt` group of each: the standard library, or the crates `hmac`, `sha2`, `pbkdf2` and `base64`; the key is `Jwt__Key` there too, the first administrator `Bootstrap__AdminUserName` and `Bootstrap__AdminPassword`) |
| `OwnedTables` | with `ApiAuth`: comma-separated tables whose rows belong to a person (TimeSheet,Expense); `Table:Column` names another owner column (`DonateLeave:DonateFrom_EmployeeId`). A caller reads and writes only the rows the `IOwnerScope` allows (their own, or everyone's for the administrator role); blank: no table is scoped |
| `OwnerColumn` | with `OwnedTables`: the NOT NULL whole-number column that holds a row's owner; blank: EmployeeId |
| `OwnedChildren` | with `OwnedTables`: a table with a NOT NULL foreign key to an owned table belongs to the owner of that parent row (the detail rows of a time sheet); false: such tables are not scoped; blank: true. A refresh of the `CrudApi` group with `--replace` is needed |
| `AuthRoleClaim` | with `ApiAuth`: the token claim that carries the person's role; blank: role |
| `AuthOwnerClaim` | with `ApiAuth`: the token claim that carries the owner id; blank: employeeId |
| `AuthAdminRole` | with `ApiAuth`: the role that sees and changes every row of an owned table; blank: Administrator |
| `UserTable` | with `ApiAuth`: the table whose rows are the people who sign in (`Employee`). `API_UserDirectory` writes `Apis/TableUserDirectory.cs`, the sign-in's `IUserDirectory` for it, and the `set-password` command (`PY_UserDirectory` writes `app/users.py` for the Python stack and `RS_UserDirectory` `src/users.rs` for the Rust stack, with the same command); blank: only the bootstrap administrator |
| `UserNameColumn` | with `UserTable`: the text column that holds the sign-in name (`Login`); rows where it is empty cannot sign in; blank: the one column called `Login`, `LoginName`, `UserName`, `User_Name`, `UserLogin`, `Logon` or `LogonName` |
| `PasswordHashColumn` | with `UserTable`: the text column that holds the password hash, at least 90 characters wide (blank: the one column called `PasswordHash`, `Password_Hash`, `PwdHash` or `Pword`); a password is never stored, set one with `dotnet run -- set-password <name>` and the environment variable `NEW_PASSWORD` |
| `UserOwnerColumn` | with `UserTable`: the int column that goes into the token as the owner id (`EmployeeId`); blank: no owner id |
| `UserRoleColumn` | with `UserTable`: a yes/no column (`IsManager`) whose two values give the two roles below, or a text column that holds the role; blank: no role |
| `UserAdminColumn` | with `UserTable`: a yes/no column (`IsAdministrator`); true makes the person an administrator (the `AuthAdminRole`) whatever `UserRoleColumn` says; blank: the one yes/no column called `IsAdministrator`, `IsAdmin`, `Administrator` or `Admin`, and nobody is an administrator when there is none |
| `UserRoleWhenTrue` | with a yes/no `UserRoleColumn`: the role when it is true; blank: Manager |
| `UserRoleWhenFalse` | with a yes/no `UserRoleColumn`: the role when it is false; blank: Employee |
| `ChangeGates` | with `ApiAuth`: rules that hold a change, separated by `\|`: `Table.Column; to 2,3,4,5,6; when Parent.Column=2; unless Parent.Column=1; start 1; message "words"`. `to` takes values or a comparison, `when` and `unless` compare a column of the row or of a parent one foreign key away (`=`, `<>`, `<`, `>`, `<=`, `>=`, `in`, joined by `and` / `or`). The API answers 409 `change-not-allowed` (API_Gates); blank: none |
| `ApproverTable` | with `RowApprovals`: who may approve is a table of rows, `Approver:EmployeeId,TableName` (the table, the column holding the person as the user table's key, optionally a text column naming the `RowApprovals` table the person may decide; blank there means every table), instead of the user table's role flag. An administrator always may. The table needs an entry in `TableWriteRoles` (or `WriteRoles`) so only trusted people change it. Blank: the role flag decides |
| `LogTables` | comma-separated insert-only tables the application only reads (`ErrorLog,UserSession:StartedOn`: a table and optionally its timestamp column; without one the plan picks the date-and-time column whose name says when something happened). The API registers no write route for them (a POST, PUT or DELETE answers 405), lists newest first and takes `from` and `to` (the first and last day) on the timestamp; the CSV export takes the same range and has no import; every UI stack writes a list with a From and a To box and no Add New, Edit, Clone, Delete or form; `SP_LogIndex` writes an index script. Needs `AccessMode=Ef` and the Columns list protocol; not for a table served as OData. A run note suggests a table that looks like a log. Regenerate the API and every front end, and refresh `Apis/CrudApi.cs` with the essentials replace option. See `Docs/TemplateNotes/TSX_LogPage_v1.md`. |
| `QueueTables` | comma-separated work-queue tables (`JobQueue,Mail:State:Attempts`: a table and optionally its status and retry columns, default `Status` and `RetryCount`). The table needs a pending, claimed, done or failed status, a created stamp, `ClaimedOn` and `ProcessedOn` (both allowing NULL), optionally `ClaimedBy` and `LastError`. The API (ASP.NET, Python and Rust) gets `stats`, `failed`, `claim`, `{id}/renew`, `{id}/complete`, `{id}/fail` and `{id}/retry`, and the ASP.NET API also `POST` on the table to put a row in the queue; the ASP.NET table is otherwise a read-only list like a log table, with the counts by status over the grid and a Retry button on a failed row (React, Angular, Blazor). Needs `AccessMode=Ef`. Regenerate the API and the web front ends. See `Docs/TemplateNotes/API_Queue_v1.md`. |
| `QueueMaxRetries` | how many times a queue row may fail before it stays failed; blank = 3. Written into the routes, so regenerate after a change. |
| `QueueClaimMinutes` | the minutes after which a claim nobody completed goes back to pending; blank = 15. Written into the routes, so regenerate after a change. |
| `ArchiveTables` | comma-separated tables whose old rows a script moves to `<Table>Archive` or deletes (`ErrorLog:90,JobQueue:30:delete`: the days to keep, then `delete`). `SP_ArchiveJob` writes `Sql/ArchiveJobs.sql` for you to review, run and schedule; the application never runs it. An archive table the database has is shown as a read-only list. See `Docs/TemplateNotes/SP_ArchiveJob_v1.md`. |
| `ArchiveBatchSize` | the rows one batch of the archive script moves or deletes; blank = 1000. Written into the script, so regenerate after a change. |
| `ArchiveRunAt` | the time of day, `HH:mm` in 24 hours, that the commented scheduling text of the archive script runs it at; blank = `02:00`. Not applied by the generator. |
| `TreeTables` | comma-separated self-referencing tables (`Category,Comment:ParentId:4`: a table, optionally its parent column and the deepest level, a root is level 1). The write routes refuse a parent that is the row itself or one of its descendants and a level past the limit; a row with children is not deleted (`POST {id}/promote-children` hands them to the row's own parent first); `GET tree` answers the rows depth first with their level. ASP.NET API, `AccessMode=Ef`. Regenerate the API and refresh the CrudApi essentials. See `Docs/TemplateNotes/API_RowShapes_v1.md`. |
| `SoftDeleteTables` | comma-separated tables whose rows are flagged instead of removed (`Customer,Order:IsRemoved`: a yes/no column that cannot be NULL, default `IsDeleted`; `DeletedOn` and `DeletedBy` are filled when the table has them). The delete route sets the flag, the context filters flagged rows out of every read, `GET deleted` lists them and `POST {id}/restore` brings one back. ASP.NET API, `AccessMode=Ef`. Regenerate the API and the context and refresh the CrudApi essentials. |
| `EffectiveDatedTables` | comma-separated tables whose rows are valid for a period (`Price:ValidFrom:ValidTo:ItemId+CurrencyId`: the first and last day columns of one date type, then the columns that say what the row is a price of, default every single-column foreign key). A save that runs backwards or overlaps another row of its group is refused; `GET current?on=` (and the group columns) answers the rows in effect. The last day is inclusive and may be empty (open). ASP.NET API, `AccessMode=Ef`. |
| `RowApprovals` | with `ApiAuth` and `UserTable` (`UserRoleColumn` a yes/no column): comma-separated tables whose rows each need their own decision (`TimeSheet,Expense`); each needs a `<Table>Approval` side table (the `SP_ApprovalTable` script creates it). The API gets request, approve, reject and withdraw routes and `GET <route>/toapprove`; a `ChangeGates` rule may add the clause `approval`. See `API_Approvals_v1.md` |
| `GuardedColumns` | with `ApiAuth` and `UserTable`: columns only some people may change, separated by `\|`: `Table.Column; by Employee.IsManager; stampBy Column; stampOn Column`. An administrator may too; anyone else may save the row while the value is unchanged (403 `column-not-yours` otherwise); the stamp columns are set to the person and the time. `GET <route>/guards` says which of them the caller may write (API_Gates); blank: none |
| `ReadRoles` | with `ApiAuth`: the roles that may read rows, separated by commas (`Administrator,Hr`); the `AuthAdminRole` always may. Another role gets 403 `role-not-allowed` on the list, one row, search and find-by-name routes of REST (ASP.NET, Python and Rust) and OData; the Angular, React and Blazor menu and route hide the screen. The `lookup` route (key and name for drop-downs) stays open; blank: any signed-in person may read |
| `TableReadRoles` | with `ApiAuth`: read roles of single tables instead of `ReadRoles`, tables separated by `;` (`Salary:Administrator,Hr`); `Table:` alone lets any signed-in person read it; blank: `ReadRoles` for every table |
| `WriteRoles` | with `ApiAuth`: the roles that may create, change and delete rows, separated by commas (`Administrator,Manager`); the `AuthAdminRole` always may. Another role gets 403 `role-not-allowed` on the REST (ASP.NET, Python and Rust) and OData writes and the Angular screens hide Add, Edit, Delete and Clone. With `Auth=true` the React and Blazor pages hide the same buttons, as Angular does. Reading stays open to any signed-in person; blank: any signed-in person may write |
| `TableWriteRoles` | with `ApiAuth`: write roles of single tables instead of `WriteRoles`, tables separated by `;` (`Holiday:Administrator;LeaveType:Administrator,Manager`); `Table:` alone lets any signed-in person write it; blank: `WriteRoles` for every table |
| `ApiValidation` | true: the plan also writes a FluentValidation validator per table (CS_Validator) and the create and update endpoints run them (400 with the messages) |
| `ApiCsv` | true: the plan also writes CSV export and import endpoints for every table (API_Csv) and Program.cs maps them: GET /api/&lt;table&gt;/export.csv and POST /api/&lt;table&gt;/import. Regenerate after ticking |
| `OData` | true: the API also serves the selected tables as OData v4 under `/odata`, next to the REST routes (which stay); the generated API project needs the `Microsoft.AspNetCore.OData` package. With `ApiAuth` the routes scope owned tables to the caller and run the change gates and guarded columns, as the REST routes do (`Docs/TemplateNotes/API_OData_v1.md`); the user table, a project with `Dtos`, a table listed in `OwnedTables` that cannot be scoped and a table with editable child lists are refused, with the reason written into the generated file. The Angular, React and Blazor stacks write OData screens for the served tables (`ODataScreens`). Regenerate after ticking |
| `ODataTables` | the tables served as OData, separated by commas; blank or `*` = every table that has a CRUD API and is not refused |
| `ODataMaxTop` | the most rows one OData request may ask for with `$top`; blank = 100 |
| `ODataPageSize` | the rows of one OData page; a longer answer carries `@odata.nextLink`; never above `ODataMaxTop`; blank = 25 |
| `ODataScreens` | with `OData`: `Add` (the default) puts the Angular, React and Blazor OData screens of a served table after the REST screens as a second entry (`<table>-odata`, "<Table> (OData)"); `Replace` puts them in the REST screen's place with the same route and menu text, so the REST screen is no longer routed (a table hosted with dashboard cards keeps it) |
| `ODataExpandDepth` | how many levels deep `$expand` may go; blank = 2 |
| `Dtos` | true: a table's CRUD API takes an input type (`<Table>Input`: only the columns a caller may set, not the identity key, computed or audit columns) and answers a transfer class (`<Table>Dto`: every column, no navigation). The plan also writes CS_Dto, CS_DtoInput and CS_Mapper; needs the Api stack. Regenerate after ticking |
| `EfConfigurations` | true: the plan also writes an IEntityTypeConfiguration per table (CS_EfConfiguration) and the context applies them from the assembly; composite keys move into them. Regenerate after ticking |
| `AccessMode` | Routines or Ef: how search, sort, paging, clone and the junction editors reach the database. Ef uses LINQ over the context and needs no routine in the database; SQLite always uses it; blank = Routines |
| `DtoNamespace` | the namespace of the data-transfer classes and mappers (CS_Dto, CS_Mapper, CS_DataContractDto, CS_TypedDataRow, CS_SerializationDtos; default &lt;ProjectName&gt;.App.Dtos) |
| `FSharpNamespace` | the namespace of the F# records FS_Entity and the Rop module FS_Rop write (default &lt;ProjectName&gt;.Domain) |
| `TemporalTables` | SQL Server only: comma-separated tables the database versions itself (SP_TemporalTable: SYSTEM_VERSIONING and a `<Table>_History` table); SP_AuditTable refuses such a table |
| `ReplicationTargets` | SQL Server only: linked server and database each change is copied to, comma-separated (server1.Sales,server2.Sales); SP_ReplicationTriggers writes the triggers |
| `KeySequenceTables` | SQL Server only: tables whose key comes from the key-sequence table instead of IDENTITY (Customer,Item); their insert routine calls GetNextID |
| `KeySequenceTable` | the table the key sequence is kept in (default AutoInc) |
| `BulkUpdateColumns` | SQL Server only: columns SP_BulkUpdate rewrites in every table that has one (Fnd,Acct) |
| `BulkUpdateExpression` | what those columns are set to, {column} standing for the column (default UPPER({column})) |
| `ApiFolder` | React / Angular folder for the api modules, relative to the source folder; blank = api |
| `ModelsFolder` | React / Angular folder for the TypeScript models; blank = models |
| `ServicesFolder` | Angular folder for the services; blank = services |
| `ComponentsFolder` | React / Angular folder for the shared components and Angular screens; blank = components |
| `PagesFolder` | React folder for the pages; blank = pages |
| `DbSetNames` | Plural = Customers, blank = the table name (Customer) |
| `AngularVersion` | major version of Angular, e.g. 22; blank = output that every version from 18 accepts |
| `DockerUpstreamVariable` | the environment variable the Docker group's nginx reads the API's address from; it holds a whole address (`https://host:8443`), the way .NET Aspire names a service (`services__shopapi__https__0`), and nginx passes to it as it is; blank: `API_UPSTREAM`, which holds `host:port` behind an `upstream` block with keepalive |
| `Auth` | Angular, React and Blazor: the app signs in. Writes the Auth group (Angular: auth service, token interceptor, route guard, login page; React: `api/auth.ts`, `pages/LoginPage.tsx`, `components/RequireAuth.tsx`; Blazor: `Services/AuthSession.cs`, `Pages/Login.razor`, `Components/RedirectToStart.razor`; the token lives in sessionStorage), sends the token with every call, guards every generated route, hides the screens `ReadRoles` does not give the role and the buttons `WriteRoles` does not give it, and shows the user name with Sign out in the shell. Needs POST api/auth/login answering { token, expiresUtc, user: { name, role } }. Regenerate the project; essentials already written are kept (use --replace for the shell, config and the React Support group; for Blazor the Project and Layout groups). |
| `Toasts` | Angular: ngx-toastr shows the API's refusals as toasts (the package is added to package.json and provideToastr to app.config.ts); blank = the browser's alert and confirm dialogs, no package |
| `ListProtocol` | Columns (what API_Search writes: GET <route>/search with a parameter per column) or Search (GET <route>?pageIndex&pageSize&sort=name:desc&search=text, one search box, answered as { data, count }): the generated API, Angular and React all follow it; the API needs essentials --groups crudapi --replace; blank = Columns |
| `PageParameter` | ListProtocol=Search: the query parameter that holds the page; blank = pageIndex (pageNumber with PageBase 1) |
| `PageSizeParameter` | ListProtocol=Search: the query parameter that holds the page size; blank = pageSize |
| `SortParameter` | ListProtocol=Search: the query parameter that holds the sort (column:asc or column:desc); blank = sort |
| `SearchParameter` | ListProtocol=Search: the query parameter that holds the search text; blank = search |
| `PageBase` | ListProtocol=Search: 0 when the first page is 0, 1 when it is 1; blank = 0 |
| `ItemsMember` | ListProtocol=Search: the member of the answer that holds the rows of the page; blank = data |
| `TotalMember` | ListProtocol=Search: the member of the answer that holds the number of rows in all; blank = count |
| `SearchColumns` | ListProtocol=Search: Table=Column or Table=Parent.Column pairs, comma-separated (TimeSheet=Notes,TimeSheet=Employee.Name): what the one search box matches; blank = the table's searchable text columns and the display column of its parents |
| `DefaultSorts` | Table=Column:asc\|desc pairs, comma-separated (TimeSheet=WhenEntered:desc,Holiday=Name): the sort an Angular grid starts with until the person sorts it |
| `IgnoredColumns` | comma-separated columns to leave out (Visits, or Place.Location): a type CodeGenNew cannot map, such as PostGIS geometry or a range of timestamps (a PostgreSQL array, interval, range of numbers or dates and the built-in shapes are mapped as text) |
| `KeyColumns` | comma-separated `Table=Column` or `Table=ColumnA+ColumnB`: the columns that identify a row of a table the database gives no primary key. Without it a table with no primary key takes its unique index (the one with the fewest columns, all NOT NULL), else its identity column (when there is exactly one); a table with none of these is skipped, and the run note says whether an `Id` column could be its key. Every column must be NOT NULL; you answer for uniqueness |
| `SingleRowTables` | comma-separated tables the application keeps one row of (a company's `Settings`, a `GeneralLedgerPostingAccount`): on every UI stack (WinUI3, Uno, React, Angular, Blazor) the table's screen is a form for the one row (Save or Submit and Reload, no list, no paging, no Delete, no child grids; an empty table shows a blank form and the first Save adds the row) instead of a list page. A table served as OData keeps its OData pages. A run note suggests a table that holds one row and has four or more ordinary columns |
| `ListingName` | WinUI3_DirectoryListing: the class stem, e.g. Document (DocumentListPage); blank = Document |
| `ListingFolder` | WinUI3_DirectoryListing: the folder whose files are listed (for example %LocalAppData%/Project/Name); blank = under LocalAppData |
| `ListingPattern` | WinUI3_DirectoryListing: which files are listed; blank = *.* |
| `Stacks` | stacks to generate: Api, WinUI3, React, Angular, Blazor, Uno, Rust, Python (comma-separated), e.g. Api,React |
| `PlanAlso` | templates to run in a whole-project generate although their config leaves them out, e.g. SP_Insert,SP_Update |
| `OutputApi` | folder of the API project under the output folder; blank = &lt;ProjectName&gt;.Api |
| `OutputWinUI3` | folder of the WinUI3 app under the output folder; blank = &lt;ProjectName&gt;.App |
| `OutputReact` | folder of the React app; blank = frontend |
| `OutputRust` | folder of the Rust API; blank = &lt;ProjectName&gt;.Rust |
| `OutputBlazorTests` | folder of the Blazor test project (`BlazorTests=true`); blank = &lt;ProjectName&gt;.Blazor.Tests |
| `OutputWinUI3Tests` | folder of the WinUI3 view-model test project (`DesktopTests=true`); blank = &lt;ProjectName&gt;.WinUI3.Tests |
| `OutputUnoTests` | folder of the Uno view-model test project (`DesktopTests=true`); blank = &lt;ProjectName&gt;.Uno.Tests |
| `OutputApiTests` | folder of the API test project (`ApiTests=true`); blank = &lt;ProjectName&gt;.Api.Tests |
| `RustCrateName` | the crate name of the Rust API; blank = the project name in snake_case |
| `RustPort` | the port the Rust API listens on; blank = ApiPort (5080) |
| `OutputAngular` | folder of the Angular app; blank = frontend |
| `OutputBlazor` | folder of the Blazor WebAssembly app; blank = &lt;ProjectName&gt;.Blazor |
| `OutputUno` | folder of the Uno Platform app; blank = &lt;ProjectName&gt;.Uno |
| `OutputPython` | folder of the Python API; blank = &lt;ProjectName&gt;.Python |
| `OutputSql` | folder of the generated SQL; blank = <ProjectName>.App/sql |
| `AppNamespace` | root namespace of the WinUI3 app; blank = &lt;ProjectName&gt;.App |
| `DatabaseProvider` | SqlServer, PostgreSql, MySql or Sqlite (appsettings.json and the package reference); blank = SqlServer |
| `DatabaseServer` | server for appsettings.json; blank = localhost |
| `DatabaseName` | database for appsettings.json (for SQLite the path of the database file); blank = the project name |
| `DatabaseUser` | login for appsettings.json (never the password); blank = Windows authentication |
| `ApiPort` | port the API listens on; blank = 5080 |
| `DevPort` | port of the front end's dev server; blank = 5173 (React), 4200 (Angular), 5190 (Blazor) or 5195 (the Uno app's browser head) |
| `ProjectTitle` | the web app's title; blank = the project name in words |
| `BuildApi` | command that builds the API after a generate; blank = dotnet build -v q (none skips it) |
| `BuildWinUI3` | command that builds the WinUI3 app after a generate; blank = dotnet build -v q |
| `BuildReact` | command that builds the React app; blank = npm run build |
| `BuildAngular` | command that builds the Angular app; blank = npm run build |
| `BuildBlazor` | command that builds the Blazor app; blank = dotnet build -v q |
| `BuildUno` | command that builds the Uno app; blank = dotnet build -v q -f net10.0-desktop |
| `BuildPython` | command that checks the Python API after a generate; blank = python -m compileall -q app |
| `BuildRust` | command that checks the Rust API after a generate; blank = cargo check (on Windows, a script that sets up the C++ build tools first, then runs cargo) |
| `TestApi` | command that tests the API; blank = no tests |
| `TestWinUI3` | command that tests the WinUI3 app; blank = no tests |
| `TestReact` | command that tests the React app; blank = npm test |
| `TestAngular` | command that tests the Angular app; blank = npm test -- --watch=false |
| `TestBlazor` | command that tests the Blazor app; blank = no tests |
| `TestUno` | command that tests the Uno app; blank = no tests |
| `TestPython` | command that tests the Python API; blank = no tests |
| `TestRust` | command that tests the Rust API; blank = cargo test |

**Earlier format.** A file that still uses `RequiresPrimaryKey=`, `RequiresSingleKey=`, `RequiresNotNameActiveTable=`, `RequiresJunctionTable=`, `RequiresChildTables=`, `RequiresAuditTable=`, `DatabaseOnly=true` or `NoDatabase=true` is read as before, so a template someone wrote keeps working. `TableOnly` is read and ignored (views are not read) and `SqlServerOnly=true` means `Dialects=SqlServer`. The shipped templates use none of them (a test checks).
