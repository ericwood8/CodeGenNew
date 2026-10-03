# SP_Load_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
SP_Load_v1.tt   (the "seed data load")
Generates: <TableName>_Load.sql

Reads the table's CURRENT rows at generation time (SP_Load.tt.config: NeedsRowData=true) and writes a
procedure that puts those same rows into another database - typically the small reference / lookup
tables (status codes, enum tables, a chart of accounts) that a fresh database needs. Read-only against the
source database, like every template; the generated procedure is for you to review and run.

Design notes (differences from the original hand-written seed-data load procedure, on purpose):
  - Re-runnable. Each row is INSERTed only IF NOT EXISTS for its primary key, so running the procedure
    twice, or against a database that already holds some of the rows, does not duplicate or fail.
    Existing rows are never updated: a seed load must not overwrite data someone has since customized.
  - Primary key values are kept, not regenerated. The original left an identity column out of the INSERT,
    so every row got a new id - which silently breaks anything that refers to a row by id (an enum id
    such as BalanceTypeEnumID = 82, or a self-reference like ContraAccount_AccountID). An identity column is
    therefore loaded with SET IDENTITY_INSERT ON (the identity counter moves past the highest id by itself).
    Cost: SQL Server allows IDENTITY_INSERT on one table at a time per session, and needs ALTER on the table.
  - One transaction (joins the caller's if there is one): all rows or none. XACT_ABORT ON, errors re-raised
    with THROW, IDENTITY_INSERT switched back OFF on every path (it is a session setting; a rollback does
    not undo it).
  - Rows are emitted in primary key order, so a row that refers to an earlier row of the SAME table (parent
    id lower than child id) loads fine. Rows referring to OTHER tables need those tables loaded first -
    run the generated procedures in foreign-key order.
  - Values are written by CodeGenNew.Core.SqlLiteral: doubled quotes, N'...' for Unicode, ISO dates that
    read the same under any language/DATEFORMAT, invariant-culture numbers, 0x binary. Nothing is trimmed:
    this is a copy of what is in the table.
  - Copied as they are, including the active/inactive flag and the admin flag (a seed load reproduces the
    source rows; it is not new-record entry like SP_Insert.tt/SP_Save.tt). The exceptions are audit dates
    (SpecialLogicColumns.config): CreateDateColumn and LastChangedDateColumn become GETDATE() - when the row
    was loaded, not when it was created in the source database - and a ModifiedDateColumn is left out (NULL).
  - Computed and rowversion/timestamp columns are skipped (cannot be assigned).
  - Loading refuses tables over SqlServerSchemaProvider.MaxRowDataRows rows: this is for small tables.

Requires a primary key (that is how "already there" is decided) and a table, not a view.
```
