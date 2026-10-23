# Standing test databases (SQL Server)

`InvoiceSystem_test` is a copy of the real InvoiceSystem database that a test may write to, so a click-through does not need a `_scratch` copy each time. The real databases are only read.

| File | What it is |
|---|---|
| `Regenerate-InvoiceSystemTest.ps1` | Rebuilds the test database from its source. `-Server` is the instance (default `localhost`, Windows authentication); `-SeedOnly` runs just the fixture rows again. |
| `Rebuild-TestDatabase.ps1` | What the regenerate script calls, for any SQL Server database (`-Source`, `-Project`, and `-ConfigFile` and `-SeedFile` when those files live elsewhere): backup and restore over `<Source>_test` (schema and every row), the users made limited, the routines that write generated and applied, the seed run. |
| `InvoiceSystemTest.config` | Project settings for the write routines (`SP_Insert`, `SP_Update`, `SP_Save`, `SP_Delete`, `SP_Lookup`, and `SP_BulkUpdate` for InvoiceSystem) with the value rules on (`NonNegativeColumns`, `NotFutureDateColumns`). |
| `InvoiceSystemTest.seed.sql` | The fixture rows. Every one is tagged `ZZ` and the seed removes the earlier ones first, so running it again is safe. It refuses to run in a database whose name does not end in `_test`. |
| `InvoiceSystemJunction.sql`, `InvoiceSystemJunction.seed.sql` | Added to the real InvoiceSystem database (not to the test copy only): the `EmployeeCustomer` junction table and 49 links for it. The seed leaves the first and last employee and customer unlinked, because the generated junction test unlinks the pair it takes from the start of each list. Rebuilding `InvoiceSystem_test` brings both in. |
| `InvoiceSystemEmptyTest.sql` | Adds `EmptyTest`, an ordinary table that is kept EMPTY on purpose (no seed script writes to it). `EmptyTableTests` in the test project reads it from the live database (`CODEGENNEW_SQLSERVER_HOST` and `CODEGENNEW_SQLSERVER_DATABASE`, Windows authentication unless `CODEGENNEW_SQLSERVER_USER` is set), generates every stack for it with `Tables` set to `EmptyTest`, and fails if the table holds a row afterwards. A fourth test runs every shipped template on it: the ones that decline an ordinary table (the junction, detail-master, OData and opt-in templates) are listed in the test, each with a plain reason, and any other template that fails fails the test. |

Rebuild a database after a change to a template that writes routines, to put it back to a known state after a test that wrote rows, or after the real database changed. The script replaces everything in `<Source>_test`.

## The users

After the restore every user of the database is a member of `db_datareader` and `db_datawriter` only (a `db_owner` membership is dropped, so `jdoe` is limited here), with `EXECUTE` on `dbo`. They cannot create or alter a table. The server accepts SQL logins (mixed mode), so `akim` and `jdoe` can connect and the routines can be run as them; pass the password through `SQLCMDPASSWORD` or an environment variable, never a file. If a login is recreated, grant `EXECUTE ON SCHEMA::dbo` again (the rebuild does it).

## InvoiceSystem_test fixtures

| Rows (tag `ZZ-`) | For |
|---|---|
| `ZZ-BADEMAIL`, `ZZ-BADPHONE`, `ZZ-BADZIP` | a stored value that breaks a shape rule; an edit of another field must save |
| `ZZ-NEGCREDIT` (-5), `ZZ-FUTURECLOSED` (closed in the future), `ZZ-ITEM-NEGPRICE` (-5) | a stored value that breaks a number or date rule |
| `ZZ-OKCREDIT` (250) | a normal row beside `ZZ-NEGCREDIT`, for `BulkUpdateColumns` (adds 1 to a credit limit and leaves a negative one: -5 stays, 250 becomes 251) |
| `ZZ-FREE1..3`, `ZZ-ITEM-FREE1..2` | create, edit, clone and delete freely |
| `ZZ-INUSE`, `ZZ-ITEM-INUSE` | delete is refused: an invoice with two lines, a monthly summary and a purchase (the child-row editor) |
| `ZZ-HOSTILE` | quotes, `%`, `_`, brackets, a backslash, markup and accents in a name |
| `ZZ-FILL*`, `ZZ-ITEM-FILL*` | fillers that make the Customer and Item counts end in 1: the last page of a 10-row grid holds one row |
