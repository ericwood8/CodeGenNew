# SP_BulkUpdate_v1

One procedure that rewrites the same column in every table that has it.

- **Project settings:** `BulkUpdateColumns=Fnd,Acct` and `BulkUpdateExpression=UPPER({column})` (the default; `{column}` is the column).
- The catalog is read when the script is written, so the procedure is plain `UPDATE` statements, one per table and column, in one transaction (`XACT_ABORT ON`), each followed by a `SELECT` of the rows changed. Nothing is built from text at run time.
- Views, computed, identity, key and row-version columns are skipped; a NULL stays NULL.
- A generic "build a cursor from a SQL string" helper, which a hand-written tool of this kind uses, is not needed: the generator already knows the schema.
- Rewrites every row: run it on a copy first (checked on a scratch copy: 51 rows of one column rewritten).

Value rules (NonNegativeColumns, NotFutureDateColumns): after the UPDATE of a rewritten column the script checks that no row now breaks the project's rule for that column (`IF EXISTS ... THROW 50000`, "(after the rewrite)"); with `SET XACT_ABORT ON` the error undoes the whole run.
