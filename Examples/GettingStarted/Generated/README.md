# What the generator wrote for the Getting started shop

`Shop.Api` is the output of the [getting started](../../../Docs/GettingStarted.md) steps for the SQLite shop (`../shop.sqlite.sql`), so a reviewer can judge the generated code without running anything. `Shop.Api.Tests` holds the generated check of the essentials.

Start with `Shop.Api/Entities/Customer.cs` (the table as a class), `Shop.Api/Apis/CustomerApi.cs` (a few lines on top of `CrudApi.cs`), `Shop.Api/Repositories/CustomerSearchQuery.cs` (the search and the sort) and `Shop.Api/Program.cs`.

It was written with:

```
sqlite3 shop.db < Examples/GettingStarted/shop.sqlite.sql
codegen generate --provider Sqlite -d shop.db --project Shop -o . --essentials --build
```

using the project file in step 3 of the guide with `DatabaseProvider=Sqlite` and `DatabaseName=shop.db`. It builds with 0 errors. The folder is a snapshot: after a template change, regenerate it with the same commands rather than editing it by hand.

SQL Server, PostgreSQL and MySQL projects also get stored routines for search, save, clone and delete (`AccessMode=Routines`); SQLite has none, so this example reaches the database through EF Core.
