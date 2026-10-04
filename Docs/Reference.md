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

One per template, same base name, plain `key=value` lines, `#` comments. A missing key takes its documented default; an unknown key is ignored, so the format can grow.

**Restrictions** (decide whether the menu, the CLI and a plan offer the template for a table):

| Key | Default | Meaning |
|---|---|---|
| `RequiresPrimaryKey` | true | hidden for a table without a primary key |
| `TableOnly` | true | hidden for a view (the tree lists tables only today) |
| `RequiredPrimaryKeyShape` | unset | `SingleColumn` (any one key column: a TypeScript interface needs one "key" property), `SingleIntOrGuid` (routes take `{id:int}` or `{id:guid}`), `SingleInt` (the generic repository takes an `int`). The model's own `PrimaryKeyShape` is the finer `None`, `Composite`, `SingleInt`, `SingleUniqueIdentifier`, `SingleOther`; the three values are the tiers the templates need. Junction and child-grid templates set nothing: they go by their association columns, and a junction table's key may be composite. |
| `RequiresJunctionTable` | false | only for a many-to-many table: excluding computed, audit and surrogate identity-key columns, exactly two columns remain and each is covered by its own single-column foreign key (a natural composite key and an identity key plus two foreign keys both count; one composite foreign key over both columns does not) |
| `RequiresChildTables` | false | only when another table has a foreign key back at this one (master-detail screens) |
| `RequiresNotNameActiveTable` | false | hidden for a "name/active" table (a NOT NULL text column `Name` and a NOT NULL bit column `IsActive`). Its repository is a `NameActiveRepo` with duplicate-name checks and trimming a template cannot supply, so `API_Crud` refuses it and every screen that assumes a plain list / get / create / update / delete backend sets this. `CS_Entity` and `CS_Repo` support both shapes. |
| `Dialects` | all | the databases the template is for (`SqlServer`, `PostgreSql`, `MySql`, `Sqlite`); a plan skips it silently for another one, the menu does not offer it, and a single run is refused with the reason |
| `AccessMode` | both | `Routines` or `Ef`: the template writes routines the generated code calls, or the LINQ that replaces them; a plan skips it when the project's access mode (the `AccessMode` setting; `Ef` for SQLite) is the other one, unless `PlanAlso` names it |

**Extra input the template asks for** (each costs read-only catalog queries, so the default is off):

| Key | Meaning |
|---|---|
| `NeedsRowData` | read the table's rows into `TableModel.Rows` (ordered by key; computed columns are `null`); refused above 5000 rows, since it is meant for small reference tables |
| `NeedsReferencedDisplayColumns` | look up the display columns of each foreign-keyed table into `ForeignKeyModel.ReferencedDisplayColumns` |

**What the template is given:**

| Key | Meaning |
|---|---|
| `DatabaseOnly` | one file for the whole database; receives `Database` (every table), run without `-t` |
| `NoDatabase` | needs neither table nor database; receives only `Project`, run without `-S`, `-d` or `-t` |

**Output:**

| Key | Meaning |
|---|---|
| `OutputName` | file-name pattern replacing `<Table>_<Suffix>.<ext>`; `{Table}` is the table name; file name only, never a path |
| `OutputFolder`, `OutputFolder.<Stack>` | the folder under the root that the template's relative paths land in |
| `OutputRoot` | `Stack` (the stack's project folder, default) or `Sql` (the project's SQL folder) |

**Whole-project generation:**

| Key | Meaning |
|---|---|
| `Stacks` | the stacks that include the template (`Api`, `WinUI3`, `React`, `Angular`); empty means run by hand only |
| `PlanTables` | which tables a plan runs it for (entity tables, `Context`: the entity tables and the composite-key junction tables, tables with an API, tables with a screen ...); ignored for database-level and no-database templates |
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
| `ViewsFolder` | blank = the template's own folder |
| `ViewModelsFolder` | blank = the template's own folder |
| `CurrencyCode` | ISO currency code for money fields; blank = USD |
| `Usings` | comma-separated namespaces every generated file should use |
| `DetailMasterTables` | comma-separated table names whose Add/Edit dialog is a Detail-Master dialog (with child grids) |
| `EnumTables` | comma-separated enum tables (no entity, repository or API); blank = decided from each table's shape |
| `EnumMaxRows` | a lookup table with more rows than this is not an enum; blank = 25 |
| `EnumNameSuffixes` | comma-separated name endings that mark an enum table; blank = Type, Types, Code, Codes, Status, Kind |
| `HiddenParents` | comma-separated tables whose foreign key columns the TypeScript forms hide |
| `ModelFileOverrides` | Table=file pairs for model files not named after the table, e.g. DepartmentTeam=department,ProjectTask=project |
| `ChildGridTitles` | Parent.Child=Title pairs, comma-separated, for a child grid whose table name does not say what the rows mean (Customer.CustomerItem=Item Purchase History) |
| `BaseEntity` | base class of generated entities; blank = BaseEntity |
| `BaseNameActiveEntity` | base class for Name + IsActive tables; blank = BaseNameActiveEntity |
| `NoLookupParents` | overrides EnumTables for this one question: tables whose foreign key is a number box, not a drop-down |
| `NoRepositoryTables` | overrides EnumTables for this one question: tables that get no repository |
| `NoApiTables` | overrides EnumTables for this one question: tables that get no API or TypeScript model/screen |
| `NoNavigationTables` | overrides EnumTables for this one question: tables referenced without a navigation property |
| `NamingStyle` | AsIs (the database's names, the default) or Pascal (customer_item becomes CustomerItem; the SQL keeps the real names) |
| `Acronyms` | comma-separated words the Pascal style keeps upper-case whole (PO,UPC,MSRP) |
| `Screens` | comma-separated tables that get a screen, in menu order; blank = every table that has an API and a search, alphabetically |
| `NoCloneTables` | comma-separated tables that get no Clone button although they could (Customer,SalesInvoice) |
| `NonNegativeColumns` | comma-separated money columns that can never be negative (CreditLimit, or Item.Cost for one table): their number box gets a minimum of 0 (a CHECK range in the database does this without the list) |
| `ValidatorNamespace` | the namespace of the FluentValidation validators CS_Validator writes (default &lt;ProjectName&gt;.App.Validators) |
| `FakerNamespace` | the namespace of the Bogus fakers CS_Faker writes (default &lt;ProjectName&gt;.App.Fakers) |
| `ErdTables` | comma-separated tables MD_Erd draws (Customer,SalesInvoice); empty: every table |
| `ApiDocs` | true: the plan also writes openapi.yaml (API_OpenApi) and the API serves it with a Swagger UI page at /docs |
| `ApiHttp` | true: the plan also writes a .http request file per table (API_Http) |
| `ApiFakers` | true: the plan also writes a Bogus fake-data generator per table (CS_Faker) and the generated project references Bogus |
| `ProjectDocs` | true: the plan also writes a data dictionary page per table and the ER diagram (MD_DataDictionary, MD_Erd) |
| `ApiValidation` | true: the plan also writes a FluentValidation validator per table (CS_Validator) and the create and update endpoints run them (400 with the messages) |
| `AccessMode` | Routines or Ef: how search, sort, paging, clone and the junction editors reach the database. Ef uses LINQ over the context and needs no routine in the database; SQLite always uses it; blank = Routines |
| `DtoNamespace` | the namespace of the data-transfer classes and mappers (CS_Dto, CS_Mapper, CS_DataContractDto, CS_TypedDataRow, CS_SerializationDtos; default &lt;ProjectName&gt;.App.Dtos) |
| `FSharpNamespace` | the namespace of the F# records FS_Entity and the Rop module FS_Rop write (default &lt;ProjectName&gt;.Domain) |
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
| `IgnoredColumns` | comma-separated columns to leave out (Tags, or Place.Location): a type CodeGenNew cannot map, such as an array or geometry |
| `ListingName` | WinUI3_DirectoryListing: the class stem, e.g. Document (DocumentListPage); blank = Document |
| `ListingFolder` | WinUI3_DirectoryListing: the folder whose files are listed (for example %LocalAppData%/Project/Name); blank = under LocalAppData |
| `ListingPattern` | WinUI3_DirectoryListing: which files are listed; blank = *.* |
| `Stacks` | stacks to generate: Api, WinUI3, React, Angular (comma-separated), e.g. Api,React |
| `PlanAlso` | templates to run in a whole-project generate although their config leaves them out, e.g. SP_Insert,SP_Update |
| `OutputApi` | folder of the API project under the output folder; blank = &lt;ProjectName&gt;.Api |
| `OutputWinUI3` | folder of the WinUI3 app under the output folder; blank = &lt;ProjectName&gt;.App |
| `OutputReact` | folder of the React app; blank = frontend |
| `OutputAngular` | folder of the Angular app; blank = frontend |
| `OutputSql` | folder of the generated SQL; blank = sql |
| `AppNamespace` | root namespace of the WinUI3 app; blank = &lt;ProjectName&gt;.App |
| `DatabaseProvider` | SqlServer, PostgreSql, MySql or Sqlite (appsettings.json and the package reference); blank = SqlServer |
| `DatabaseServer` | server for appsettings.json; blank = localhost |
| `DatabaseName` | database for appsettings.json (for SQLite the path of the database file); blank = the project name |
| `DatabaseUser` | login for appsettings.json (never the password); blank = Windows authentication |
| `ApiPort` | port the API listens on; blank = 5080 |
| `DevPort` | port of the front end's dev server; blank = 5173 (React) or 4200 (Angular) |
| `ProjectTitle` | the web app's title; blank = the project name in words |
| `BuildApi` | command that builds the API after a generate; blank = dotnet build -v q (none skips it) |
| `BuildWinUI3` | command that builds the WinUI3 app after a generate; blank = dotnet build -v q |
| `BuildReact` | command that builds the React app; blank = npm run build |
| `BuildAngular` | command that builds the Angular app; blank = npm run build |
| `TestApi` | command that tests the API; blank = no tests |
| `TestWinUI3` | command that tests the WinUI3 app; blank = no tests |
| `TestReact` | command that tests the React app; blank = npm test |
| `TestAngular` | command that tests the Angular app; blank = npm test -- --watch=false |
