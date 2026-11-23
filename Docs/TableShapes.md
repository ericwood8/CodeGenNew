# Table shapes: what the generator detects, and what it is told

A table has a shape: a many-to-many junction, a table with a Name and an IsActive column, a log, a work queue and so on. This page says which shapes the desktop app's right-click menu detects in a table to decide which templates it offers, and which shapes only a project setting can name. The shape work is item 143 of the specification (`spec.md` for what is open, `spec_completed.md` for what is built).

## Two kinds of shape

| Kind | Who decides | Effect |
|---|---|---|
| Detected from the table | The generator, from the columns, keys and foreign keys the database reports | Decides which templates the right-click menu offers for the table, and which a run refuses for it |
| Named by a project setting | The person, in the project settings (for example `LogTables`, `QueueTables`, `SoftDeleteTables`) | Changes what the templates write for the table (routes, screens, checks). Never changes the menu |

Making a table read-only, a queue or a tenant-owned table on a guess would be wrong, so nearly every shape of item 143 is the second kind: the generator checks the table against the shape the setting names and reports a problem when it does not fit. The first kind is a short, fixed list.

## What the right-click menu detects

The menu builds one `TableSummary` for every table in a single catalog query (`CodeGenNew.SchemaIntrospection`), without building a full `TableModel`. Each fact has a bulk-computed form and a `TableModel` form that follow the same rule, so the menu and a generate run cannot disagree. `TemplateConfig.Check` is the one place the facts are compared with a template's restrictions, and the menu (`TemplateInfo.AppliesTo`), the command line and a whole-project run all use it.

| Fact | Rule | Template restriction (`Requires=` in the `.tt.config`) |
|---|---|---|
| Has a primary key | The database declares one, or (when it declares none) the project's `KeyColumns` entry or the unique index with the fewest columns, all NOT NULL, supplies one (`KeyChooser`; the run report says which) | `PrimaryKey`. This is the default: a table template without a `Requires=` key requires it, and `Requires=` (empty) requires nothing |
| Key shape | One column that is an integer (`SingleInt`), a uniqueidentifier (`SingleUniqueIdentifier`) or text (`SingleText`); more than one column is `Composite`; one column of another type, such as a date or a decimal, is `SingleOther`; none is `None` | `SingleKey`: the table must be one of the three single-column shapes the key types describe (`KeyType`), so a screen or a client can write the key's C#, TypeScript, Python and route form |
| Junction table | Leave out computed columns, audit columns and an identity primary key column. Exactly two columns remain, and each has its own single-column foreign key (to one parent or two; a self-reference counts). One foreign key spanning both columns does not count. A natural composite key and an identity key plus two foreign keys both qualify. No name guessing | `JunctionTable` (the junction editor, the junction routines and the junction pages) |
| Has child tables | At least one other table has a foreign key pointing back at this one | `ChildTables` (the master-detail screens and pages) |
| Name and active table | A required text column named exactly `Name` and a required yes/no column named exactly `IsActive` | `NotNameActive` refuses it: its repository is a name and active repository, so templates that assume a plain list API are not offered |
| Audit table | A column whose name starts with `Create` (creation) and a column that holds `Modif` (but is not `IsBeingModified`), holds `Change` or starts with `Update` (a later change) | `AuditTable` (the audit trail script, `SP_AuditTable`) |
| Database | The connected server's kind: SQL Server, PostgreSQL, MySQL or SQLite | `Dialects=` in the config; a template for another database is hidden |

The menu also shows a header line that is not selectable. It lists what is wrong with the table's own icon or name: no unique key, no primary key, a name that is a SQL or C# reserved word.

Scope decides where a template is offered at all: a table template (the default) appears on a table, a `Scope=Database` template appears on the database name and on the toolbar's database menu, and a `Scope=None` template appears on neither (a command-line or essentials template).

## A shape detected only to give a hint

Three shapes have a detection that does not change what is generated. The generator notes them in the run report so the person can name them with a setting.

| Shape | Looks like | The hint says |
|---|---|---|
| Log table | An integer key, a date-and-time column (not a modified or last-changed stamp), no modified date or user column, and either a name with a log word (Log, Audit, Session, Event, History, Trace, Activity, Error) or a level (or severity) column together with a message column. The timestamp is the date-and-time column whose name says when something happened first (Logged, Occurred, Timestamp, Created, Started and so on) | List it in `LogTables` to get a list that opens newest first with a date range and no Add, Edit or Delete |
| Single-row table | One row at the moment, a key, four or more ordinary columns, not already listed as a single-row or enum table | List it in `SingleRowTables` to get a form instead of a list |
| Name and active table | See above | It gets a screen only when the `Screens` setting lists it |

A table that looks like a log is the one place the menu acts on a guess. The log templates (`TSX_LogPage` and `SP_LogIndex`) run from the menu for such a table as if it were listed in `LogTables`, for that one run only; the project file is not changed. Every other template treats the table as ordinary until it is listed.

## Shapes named by a project setting

These are the shapes of item 143. Each setting names the table and, where it matters, the columns. A column left out is found by its usual name. If the table does not fit, the run reports a problem (for example `LogTables: Event has no date and time column to list newest first`) instead of writing something wrong. Some of these also have other settings, listed in `Docs/Reference.md`.

Every one of these needs a single whole-number key unless the row says otherwise (the three that read through EF also need `AccessMode=Ef`). Column names are not fixed: where the setting names no column, the planner looks for one by a list of usual names or name stems, and uses it when exactly one column fits.

| Shape | Setting | What the planner checks |
|---|---|---|
| Log table (read only, newest first) | `LogTables` | The table gets an API; `AccessMode=Ef`; the `Columns` list protocol; a date-and-time column (named, or the one whose name says when something happened first) |
| Work queue | `QueueTables` | Status column (default `Status`): text of at least 7 characters. Retry column (default `RetryCount`): whole number. A created stamp (the log timestamp guess). A date-and-time column starting `Claimed`, else `Locked`, else `Started`, that allows NULL. A date-and-time column starting `Processed`, `Completed`, `Finished` or `Done` that allows NULL. Optional text columns for the worker (`ClaimedBy`, `LockedBy`, `WorkerName`, `Worker`) and the error (`LastError`, `ErrorMessage`, `Error`, `Failure`) |
| Archive or purge script | `ArchiveTables` | The days to keep, a primary key, and a date-and-time column to measure age by; `delete` instead of moving rows to `<Table>Archive` is optional |
| Partition script | `PartitionTables` | A date column (named, or guessed as for a log); not SQLite |
| Inbound event landing table | `InboundTables` | A text body (named, or one of a list of usual names), a received-on stamp (a date-and-time column starting `Received`, else the log timestamp guess), and a `Processed` yes/no column or a `Processed...` date-and-time column that allows NULL. Every other column must allow NULL or have a default, since the receiving route cannot fill it |
| Session and token table | `TokenTables` | `ApiAuth` and `AccessMode=Ef`; a text column with `Hash` in its name that holds 64 or more characters; a NOT NULL date-and-time expiry column (by name stem); optionally a revoked yes/no column or a nullable end stamp, and a family column that is not a foreign key |
| Usage ledger | `UsageTables` | `ApiAuth`, `AccessMode=Ef`, tenant-owned; a NOT NULL text kind column; optionally a NOT NULL whole-number or decimal quantity column; a NOT NULL `DateTime` occurred-on column |
| Tenant owned table | `TenantTable` (with `ApiAuth`) | A NOT NULL `int` column named by `TenantColumn` (default `TenantId`) with a single-column foreign key to the tenant table. `NoTenantTables` opts a table out |
| Subscription, plan and limits | `SubscriptionTable`, `PlanTable`, `PlanLimitTable` | The columns the setting names or the usual names, with a tenant table. See `Docs/Reference.md` |
| Roles and permissions in tables | `PermissionTables` | Four tables with the foreign keys described in `Docs/Reference.md` |
| Feature flags | `FeatureTables` | A flag table with a NOT NULL yes/no default column (`DefaultEnabled`, `DefaultOn`, `IsEnabled`, `Enabled` and others); an override table with a NOT NULL yes/no column (`IsEnabled`, `Enabled`, `IsOn`, `Value`, `State`) |
| Self-referencing tree | `TreeTables` | A single-column foreign key from the table to its own key. The parent column is named, or found when the table has exactly one such key |
| Soft delete | `SoftDeleteTables` | A NOT NULL yes/no column (default `IsDeleted`). Never for a log table or a single-row table |
| Effective dated rows | `EffectiveDatedTables` | A NOT NULL first-day date column and a last-day date column of the same type. The group columns default to every single-column foreign key |
| Owned by a person | `OwnedTables` | A column that holds the person, named by the setting or found by the owner rules (`OwnedTable.OwnerColumn`) |
| Content with a slug | `ContentTables` | A text column to make the slug from (named, else `Title`, `Name` or `Headline`), a NOT NULL text `Slug` of room for the slug, a NOT NULL text status wide enough for `published`, and optionally a nullable date-and-time published-on |
| Stored file metadata | `AssetTables` | A NOT NULL text storage key, a NOT NULL text file name, a text file type and a whole-number size; every other column must be fillable by the upload route |
| One default row per group | `DefaultRowTables` | A NOT NULL yes/no column and the group columns. Refused for a table owned by a person or a tenant |
| Party and its identifiers | `PartyTables` | A party table and an identifier table, each with a single whole-number key; exactly one single-column foreign key from the identifier table to the party; NOT NULL text columns for the scheme and the value (default `Scheme` and `Value`). Refused for a table owned by a person or a tenant |
| Exchange rates | `RateTables` | A single whole-number key, NOT NULL text columns for the two currencies, a NOT NULL number column for the rate and a NOT NULL date column for the day (default `FromCurrency`, `ToCurrency`, `Rate`, `RateDate`). Refused for a table owned by a person or a tenant |
| Price lists with quantity breaks | `PriceListTables` | A price table with a single whole-number key, NOT NULL whole-number list and item columns (the list of the same type as the key of the list table), a NOT NULL number minimum quantity and a NOT NULL decimal, double or float price; a list table with a single whole-number key, a NOT NULL text currency, a NOT NULL date first day and a date last day that allows NULL (dates without a time), and optionally a text customer group. May be limited to an owner or a tenant (then its list table, if limited, must be too). A unique index on the list, the item and the minimum quantity is advised. |
| Bank accounts with a checked IBAN | `BankAccountTables` | A table with a single whole-number key and a text IBAN column (`Iban`), or text account number and routing number columns (`AccountNumber`, `RoutingNumber`); a column that looks like a card number, security code or expiry is refused, since a card is never stored |
| Installment schedules | `InstallmentTables` | A table with a single whole-number key, a NOT NULL whole-number document column (of the type of the key of the document table), a NOT NULL whole-number sequence number (`SequenceNo`), a NOT NULL date due day (`DueDate`) and a NOT NULL decimal, double or float amount (`Amount`); the document table has a single whole-number key and a NOT NULL total of the same type as the amount. A table limited to an owner or a tenant is refused for now |
| Due date from payment terms | `PaymentTermsTables` | A table with a single whole-number key, a NOT NULL whole-number document column, a whole-number terms column (`PaymentTermsId`) of the type of the key of the terms table (`PaymentTerms`, with a NOT NULL whole-number `DaysToPay`) and a date due day (`DueDate`) that allows NULL; the document table has a date column (a document with no date gets no due date worked out) |
| Payment transaction ledger | `LedgerTables` | A table with a single whole-number key, a NOT NULL whole-number document column of the type of the key of the document table (which has a NOT NULL decimal, double or float `Total`), NOT NULL text `Kind`, `Status`, `GatewayId` and `IdempotencyKey` columns, a NOT NULL decimal, double or float `Amount` of the type of the total, and a date and time column (the log timestamp); not limited to an owner or a tenant; also a log table |
| Snapshot at checkout | `SnapshotColumns` | Exactly one single-column foreign key to each parent copied from, and copy columns of the source's type and length |
| Polymorphic link | `PolymorphicLinks` | A text type column, a whole-number id column with no foreign key, and at least two tables the type may name. The key must be a single whole number |
| Single-row table | `SingleRowTables` | Nothing beyond what the setting says: the screen is one form |
| Tax lines | `TaxLines` | A line table with a category key, a rate, a taxable amount and a tax amount column, and the category table with its rate (and, optionally, its validity dates) |
| Allowances and charges | `AllowanceCharges` | A table with a NOT NULL yes/no column (charge or allowance), a base amount and a percentage (decimal, double or float) and a NOT NULL amount column, optionally with the column of the document it sits on and the column of the line; a table whose name says allowance, charge or discount is suggested |
| Document totals | `DocumentTotals` | A header table with a whole-number key, a line table whose foreign-key column points at it and a decimal, double or float amount on the line, and header columns (decimal, double or float) for the totals to keep; needs `AllowanceCharges` for the allowance and charge totals and `TaxLines` for the tax |
| Exchange rate on a document | `RateDocuments` | A currency text column, a rate, an amount and a converted amount column on the document, and `RateTables` and `BaseCurrency` in the project |
| Row approvals | `RowApprovals` | `ApiAuth`, a `UserTable` whose `UserRoleColumn` is a yes/no column, and a `<Table>Approval` side table with a foreign key back to the row |
| System-versioned table | `TemporalTables` | SQL Server only; the table is not also an audit table |
| Key sequence | `KeySequenceTables` | SQL Server only; the key comes from the key-sequence table instead of IDENTITY |
| One-to-one extension | none | Detected from the key: the one-column primary key is also a foreign key to another table's primary key. The generator finds it with `ExtensionPlan.For`, with no setting, and the menu shows it as a label |

`TableShapes.For(table, project)` gathers the shapes one table has, as shape objects (`QueueShape`, `LogShape`, `TreeShape`, `TenantOwnedShape` and the others). A table may have several at once. Each shape is worked out the first time it is asked for. The planners that own a rule (for example `LogTablePlan`, `SnapshotPlan`, `DefaultRowPlan`) are the place to read the exact checks and the exact problem messages.

## Suggestions from the table

`TableShapeDetector.Of(table, project)` finds the shapes below from a table's columns and keys, with the usual column names, and never changes what is generated. Each answer is a suggestion: **fits** when the shape's own planner accepts the table listed with the suggested item (so a suggestion and a run cannot disagree), or **looks like** with the words of what is missing. A table the project already lists is not suggested again.

| Shape | Setting item suggested | Signal |
|---|---|---|
| One-to-one extension | none | The one-column key is also a foreign key to another table |
| Tree | `TreeTables` | Exactly one nullable single-column foreign key from the table to its own key; two or more are listed as ambiguous with each column named |
| Soft delete | `SoftDeleteTables` | A yes/no column named `IsDeleted`, `Deleted`, `IsRemoved`, `Removed`, `IsVoided`, `Voided` or `IsArchived`; a nullable `DeletedOn` with no flag is a note only. Never for a log or single-row table |
| Default row | `DefaultRowTables` | A yes/no column named `IsDefault`, `Default`, `IsPrimary`, `Primary`, `IsMain`, `IsPreferred` or `IsCurrent` and one single-column foreign key (with a column ending in `Type` or `Kind` added to the group); several foreign keys ask for the group; the note says when a unique filtered index on the flag already allows one row per group |
| Effective dated rows | `EffectiveDatedTables` | A date pair named `ValidFrom` and `ValidTo`, `EffectiveFrom` and `EffectiveTo`, `StartDate` and `EndDate`, `FromDate` and `ToDate`, `StartsOn` and `EndsOn` and at least one foreign key, or any two date columns a check constraint puts in order (`Opens <= Closes`); the note says when a check constraint backs the pair |
| Work queue | `QueueTables` | A text `Status` or `State`, a whole-number `RetryCount`, `Attempts`, `Tries` or `AttemptCount`, a claim stamp (`Claimed`, `Locked` or `Started`) and an end stamp (`Processed`, `Completed`, `Finished` or `Done`); the table's own spelling goes into the item (`Mail:State:Attempts`). A table that fits as a queue, or is suggested as an inbound event table, is not also noted as a log |
| Content with a slug | `ContentTables` | A text `Slug`, `UrlName`, `Permalink` or `Handle` beside a text `Title`, `Name` or `Headline` (and a text `Status`); without a unique index on the slug it only looks like |
| Stored file metadata | `AssetTables` | A storage key (`StorageKey`, `StoragePath`, `BlobKey`, `FilePath`, `Path`, `Key`), a file name, a type (`MimeType`, `ContentType`, `MediaType`) and a size; three of the four only look like, a binary column holding the file itself is named in the note as the opposite, and the note names what is missing |
| Inbound event table | `InboundTables` | A text `Body`, `Payload`, `RawBody`, `Json` or `Message`, a `ReceivedOn` or `ReceivedAt` stamp, a processed flag or stamp, and no claim stamp (a claim stamp makes it a queue) |
| Session and token table | `TokenTables` | A text column with `Hash` in its name (not `Password`) and a date-and-time column with `Expires` or `Expiry`; the planner then wants `ApiAuth`, `AccessMode=Ef` and a hash of 64 or more characters |
| Usage ledger | `UsageTables` | A required text `Kind`, `EventType`, `Metric`, `Meter` or `Feature`, a required number `Quantity`, `Units`, `Amount` or `Count`, and a required `OccurredOn`, `RecordedAt` or `UsedOn` |

With a project chosen the right-click menu reads the whole database once (kept until the project's file changes or the tables are refreshed) and offers the table's shapes and the shapes of the next section that point at it; with no project it reads only the table's own model. A shape that fits is an item "Use as ... and generate" that adds the item to the chosen project's file (every other line of the file stays as it is; nothing is added twice) and then regenerates the project's `Stacks` with the shape on. The run is for that table and the templates that cover the whole database (where a shape's routes and checks are written), not every other table's own files; regenerate the project to refresh those. A file that did not change is not touched and a file a person edited is kept. A project with no `Stacks` setting only gets the line; a shape that only looks like is a label that cannot be clicked. With no project chosen, every suggestion is a label. A whole-database run lists each suggestion in the run report.

The detector finds four more from the table alone:

| Shape | Setting item suggested | Signal |
|---|---|---|
| Log table | `LogTables` | An integer key, a date-and-time column, no modified stamp or user, and a log word in the name or a level beside a message. A claim stamp or a received stamp makes it a queue or an inbound table instead. It fits only with `AccessMode=Ef` and the Columns list protocol |
| Single-row table | `SingleRowTables` | One row at the moment, a key and four or more ordinary columns, not an enum table |
| System-versioned table | `TemporalTables` | SQL Server reports the table as versioned by the database itself (`temporal_type` 2) |
| Owned by a person | `OwnedTables` | `ApiAuth` is on and the table has a NOT NULL whole-number column named like the project's `OwnerColumn` (`EmployeeId` by default); a child of an owned table is scoped through its parent and is not suggested |

A whole-database run (`DatabaseShapeDetector`) also looks at more than one table and lists these, of which the menu offers those that name a table (every row below is offered on one table), and the run report lists them all. A choice for the whole project (the tenant table, the user table, roles and permissions, feature flags, the subscription table) is offered on the table that holds it, the tenant table, the user table, the role table, the flag table or the subscription table, and the run report names that table:

| Shape | Setting | Signal |
|---|---|---|
| Polymorphic link | `PolymorphicLinks` | A text column ending in `Type`, `Table` or `Kind` and a whole-number column of the same stem ending in `Id` with no foreign key; the tables come from a check constraint that names them, with no constraint it only looks like |
| Snapshot at checkout | `SnapshotColumns` | A table with a `Quantity` and columns that copy a parent's column (same name, the parent's name before it, or `UnitPrice` for `Price`) of the same type and length. Always only looks like; the note carries the item to check |
| Party identifiers | `PartyTables` | A NOT NULL text `Scheme`, `SchemeCode`, `IdentifierType` or `IdentifierScheme` and a NOT NULL text `Value`, `Identifier`, `IdentifierValue` or `Number` under a foreign key to one other table; suggested on the identifier table, and two foreign keys to that table only look like it |
| Exchange rates | `RateTables` | NOT NULL text columns for two currencies (`FromCurrency` or `BaseCurrency` and `ToCurrency` or `QuoteCurrency`, with the `Code` forms), a NOT NULL number column (`Rate`, `ExchangeRate`, `FxRate`) and a NOT NULL date column (`RateDate`, `EffectiveDate`, `AsOfDate`, `ValidFrom`) |
| Price lists with quantity breaks | `PriceListTables` | A NOT NULL whole-number `ItemId` (or `ProductId`), a NOT NULL number `MinQuantity` (or `MinQty`, `MinimumQuantity`, `FromQuantity`) and a NOT NULL decimal, double or float `Price` (or `UnitPrice`) on a table that has a foreign key to a list table with a NOT NULL text `CurrencyCode`, a NOT NULL date `ValidFrom` and a date `ValidTo` that allows NULL (an optional `CustomerGroup` is named in the entry). |
| Bank accounts with a checked IBAN | `BankAccountTables` | A text column named `Iban` (or `IbanNumber`, `IbanCode`) on a table with a single whole-number key |
| Installment schedules | `InstallmentTables` | A NOT NULL whole-number `SequenceNo` (or `InstallmentNo`), a NOT NULL date `DueDate` and a NOT NULL decimal, double or float `Amount` on a table that has a foreign key to a document table with a NOT NULL decimal, double or float `Total` (or `TotalAmount`, `GrandTotal`, `PayableAmount`) |
| Due date from payment terms | `PaymentTermsTables` | A date `DueDate` that allows NULL on a table that has a foreign key to a terms table with a NOT NULL whole-number `DaysToPay` and a foreign key to a document table with a date (`OrderedOn`, `PlacedOn`, `IssueDate`, `InvoiceDate`) |
| Payment transaction ledger | `LedgerTables` | NOT NULL text `Kind`, `Status`, `GatewayId` and `IdempotencyKey` columns, a NOT NULL `Amount` and a foreign key to a document table with a NOT NULL `Total` |
| Archive | `ArchiveTables` | Another table named like it plus `Archive`, `_Archive`, `History` or `_History` that has all its columns. Only looks like: the days to keep are the person's choice |
| Tenant table | `TenantTable` | A required whole-number column named `TenantId`, `OrganizationId`, `AccountId`, `CompanyId` or `OrgId`, a foreign key to the same table in most of the others; fits with `ApiAuth` |
| User table | `UserTable` | Exactly one table with a login name and a password hash by the names the user table already reads |
| Roles and permissions | `PermissionTables` | `Role`, `Permission`, a junction of the two and a junction of `Role` and another table |
| Feature flags | `FeatureTables` | A flag table with an integer key, a `Name` and a required yes/no default, and a table with one foreign key to it and a required `IsEnabled` |
| Subscription table | `SubscriptionTable` | The one table with `Subscription` in its name, a NOT NULL `PlanId`, a NOT NULL status and a NOT NULL tenant column; it fits only when the project already names `PlanTable` |
| Tax lines | `TaxLines` | A line table with a NOT NULL tax rate, taxable amount and tax amount and a foreign key to a table with Tax, Vat or Levy in its name that has a rate column |
| Row approvals | `RowApprovals` | A `<Table>Approval` side table with the status, requester, decider and revision columns of a request cycle and a key back to the table; it fits with `ApiAuth` and a `UserTable` that has a role flag |
| Exchange rate on a document | `RateDocuments` | A currency text column, a rate (`ExchangeRate`, `FxRate`), an amount (`Total`, `Amount`) and a converted amount (`BaseTotal`, `BaseAmount`) that cannot be NULL; it fits when `RateTables` names a rate table and `BaseCurrency` is set |

The log and single-row notes stay as described above, and the run report no longer repeats them as a second suggestion.

## Shapes with no detection

Some shapes are a choice the table cannot show, so only the setting names them (`TableShapes` still gives the shape once the project lists the table, and every shape of it is asked from `TableShapes.For`):

- Partition script (`PartitionTables`): any large table with a date could be split; the months to keep are the person's choice.
- Archive days (`ArchiveTables`): a table with an archive twin is suggested, but the days to keep are the person's choice, so the suggestion only looks like.
- Key sequence (`KeySequenceTables`): the table's key looks like any other whole-number key.
- Party table and the tables of a party (`PartyTables` children `names`, `contacts`, `taxes`, `roles`): the identifier table is suggested and the children follow from it.
- Plan limits and the usage roll-up: they follow from the subscription and usage tables.

## How a setting-named shape reaches the files

The templates that exist for one of these shapes (the queue routes, the token routes, the usage ledger) are database templates: `Scope=Database`, `InPlan=false`. The menu offers them on the database name, not on a table. A whole-project run writes one only for a project whose setting names a table, and its `.tt.config` says so (for example `written only for a project with QueueTables`). The per-table templates (the pages, the models, the tests) offer themselves by the detected facts above and write the shape's routes, screens and checks when the setting names the table. So a click on an ordinary table never changes what it gets, and the menu and a whole-project run give the same files.

## Adding a detected shape

A new detected shape touches four places, and they must give the same answer:

1. The rule, in Core, as a static function of the columns (like `AuditTableShape`), so a table summary and a full table model call the same code.
2. The bulk form in each schema provider's table summary listing, and the matching property on `TableModel`.
3. A field on `TableFacts` and a `Requires=` word in `TemplateConfig` (a new entry in the list of names, the check and the explanation text the menu and the command line print).
4. A test that the summary and the model agree for a table of that shape and for one that is just short of it.

A shape named only by a setting needs none of this: it needs a plan in Core with its problems, and the setting registered in `ProjectSettings`, the settings tabs and the hints.
