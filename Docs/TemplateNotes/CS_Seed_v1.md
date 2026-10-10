# CS_Seed_v1 and CS_SeedData_v1

Seed data for the tables that are not enums. A lookup table of a few rows (a region, a currency, a status) that the application needs before anyone uses it is written as a class that adds its rows to an empty table, so a new database starts with them. `CS_Enum` covers the tables whose rows become a C# enum; this covers the ones that stay data.

The project setting `SeedTables=Region,Currency` names the tables, **a parent before its children** (that is the order they are seeded in), and adds both templates to the plan. The rows are read when you generate, so load them first.

## What they write

| File | What it holds |
|---|---|
| `Seeding/<Table>Seed.cs` | `<Table>Seed.NewRows()` (new objects on every call, one `new Region { ... }` per row, in key order) and `<Table>Seed.SeedAsync(context)`, which adds the rows when the table has none, so running it again changes nothing. |
| `Seeding/SeedData.cs` | `SeedData.SeedAsync(context)`, which runs each table's seed in the setting's order, and `app.SeedGeneratedDataAsync()` for `Program.cs`, which opens a scope and runs it. Call it once the database exists (after the migrations or `EnsureCreated`). |

Both are in the context's namespace (`ContextNamespace`) and use the entity classes. Nothing in the generated API calls them: Program.cs is yours to add `await app.SeedGeneratedDataAsync();` to.

## Rules

- A column's value is written in the C# type of its property (`CSharpLiteral`): a SQLite integer read as a long becomes an `int` literal for an `int` column, a bit becomes `true` or `false`, a date `new DateTime(y, m, d)`, a decimal `1.5m`, a Guid `Guid.Parse("...")`. A null value, binary data and a computed column are left out, so the property keeps its default.
- **An identity key is left out and numbered by the database**, in the order of the rows (SQL Server refuses an explicit value for an identity column unless `IDENTITY_INSERT` is on). A table that other seeded tables point at has to hold the keys 1, 2, 3 ... when it is read; when it does not, the summary comment of the class says which keys the rows had and that they are numbered again. A key that is not an identity (a text code) is written as it is, and that is the safe shape for a table others point at.
- An empty table, a table with more than 1000 rows and a table in `SeedTables` that has no entity are errors with the reason: seed data is a handful of rows, not a data load.
- The rows are only added to an empty table, never updated: a changed row in the source is a changed seed class, and a database that has rows is left as it is.

## Not done

Updating a row that already exists (an upsert by key), seeding a table of another project's database, and a seed for a table with a composite key (it needs an entity with a single key here).
