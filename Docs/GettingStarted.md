# Getting started

From an empty folder to a running web API generated from a database, in about ten minutes. The same steps work for SQL Server (`--provider SqlServer`, the default), MySQL (`--provider MySql`) and SQLite (`--provider Sqlite`); this walk-through uses PostgreSQL.

## What you need

- The [.NET 10 SDK](https://dotnet.microsoft.com/download) (the templates are compiled when they run, so the runtime alone is not enough).
- A SQL Server, PostgreSQL or MySQL server you can create a database on (or no server at all for SQLite, which is a file). CodeGenNew only ever *reads* the database.

## 1. Install the tool

```
dotnet tool install -g CodeGenNew.Cli
codegen --help
```

(Or build it from a checkout: `dotnet build CodeGenNew.Cli`; the program is `CodeGenNew.Cli/bin/Debug/net10.0/codegen.exe`.)

An installed tool keeps its templates, project files and output in `%APPDATA%\CodeGenNew`; set `CODEGENNEW_HOME` to use another folder. A build or a plain copy keeps them beside the program instead. The first run copies the default templates into `Templates` there; they are yours to edit, and are never overwritten (delete the `Templates` folder to get the new defaults after an update).

## 2. Make a database to read

[Examples/GettingStarted](../Examples/GettingStarted) holds a small shop (customers, products, orders) for SQL Server and PostgreSQL, with a few `CHECK` constraints: the generator turns those into limits on the forms.

```
psql -U <login> -d postgres -c "CREATE DATABASE shop"
psql -U <login> -d shop -f Examples/GettingStarted/shop.postgresql.sql
```

**SQLite** needs no server: `sqlite3 shop.db < Examples/GettingStarted/shop.sqlite.sql` makes the same shop in a file. Then use these lines in step 3 instead of the PostgreSQL ones, and leave out `-S` and `-U` in steps 4 and 5 (the database is the file: `--provider Sqlite -d shop.db`):

```
DatabaseProvider=Sqlite
DatabaseName=shop.db
```

SQLite has no stored routines, so the project reaches the database through EF Core (`AccessMode=Ef`, which SQLite always uses): step 5 writes no SQL, and `Shop.Api/Repositories/CustomerSearchQuery.cs` holds the search. Run the API from the folder that holds `shop.db` (the connection string is `Data Source=shop.db`).

## 3. Describe the project

Everything that belongs to *your* project and not to the templates (names, namespaces, folders, how the database is reached) goes in one small file, `Projects/Shop.config` in the home folder from step 1. One `key=value` per line:

```
ProjectName=Shop
NamingStyle=Pascal
DatabaseProvider=PostgreSql
DatabaseName=shop
DatabaseUser=<login>
EnumTables=none
Stacks=Api
OutputApi=Shop.Api
EntityNamespace=Shop.Api.Entities
RepoNamespace=Shop.Api.Repositories
ContextNamespace=Shop.Api.Data
ApiNamespace=Shop.Api.Apis
```

`NamingStyle=Pascal` turns `sales_order` into `SalesOrder` in the code. `EnumTables=none` keeps a small table such as `product` an ordinary table with an entity (a small lookup table is otherwise turned into a C# enum). The full list of settings is in the [README](../README.md) and in the app's Project dialog.

## 4. Look at one file first

```
codegen --provider PostgreSql -S localhost:5432 -d shop -U <login> -t customer -T CS_Validation.tt --project Shop -o ./preview
```

The password is asked for when `-P` is left out (a terminal is needed for that). `./preview/CustomerValidation.cs` is the validation class: the `credit_limit >= 0` check shows as `[Range(0, double.MaxValue)]`; on the screens the same check becomes the number box's minimum, and the `status IN (...)` check becomes a drop-down of the allowed values.

## 5. Generate the whole API

```
mkdir shop && cd shop
codegen generate --provider PostgreSql -S localhost:5432 -d shop -U <login> --project Shop -o . --essentials --build
```

One run reads the schema once and writes, for every table: the entity, the repository, the minimal-API endpoints, the search endpoints and the stored routines, plus the *essentials* (the `.csproj`, `Program.cs`, `appsettings.json`, base classes, `.editorconfig`, `.gitignore`) that no table drives. `--build` builds it afterwards. Running it again changes nothing that did not change; add `--dry-run` to see what would be written and `--diff` to see how a file differs.

## 6. Run it

```
cd Shop.Api
dotnet run
```

Open `http://localhost:5080/api/customers` (the port is in `appsettings.json`). The connection string in `appsettings.json` has no password: PostgreSQL's driver reads `PGPASSWORD`, MySQL's `MYSQL_PWD`; for SQL Server use Windows authentication or add the password through user secrets. Never commit one.

## Next steps

- **A front end.** Add `--stack api,react` (or `angular`, `winui3`) and the stack's folder (`OutputReact=shop-react`) to the project file; the essentials write the app's `package.json` / `.csproj` too. `Screens=Customer,Product,SalesOrder` is the menu order.
- **The desktop app** (`CodeGenNew.App`, Windows) does the same from a window: connect, pick a table, pick a template, or *Generate all*.
- **Your own templates.** A template is a `.tt` file in the `Templates` folder with a `.tt.config` beside it; the [README](../README.md) and [Docs/TemplateNotes](TemplateNotes) describe the model the templates read.
- **Checking a generated app in a running copy**: [Docs/Verification](Verification).
