# SP_Clone_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>_Clone.sql

Behind a "Clone" button on a grid row: copies the chosen row into a NEW row so the person does not have to
retype a table's worth of identical values, and hands back the new row's key. (The idea and the parameter
names @CopyFrom<Key> / @New<Key> come from the original clone procedure.)

How it copies - a single  INSERT INTO t (explicit columns) SELECT ... FROM t WHERE <key>  - differs from the
original (a #TMP table filled with SELECT *, ALTER TABLE ... DROP COLUMN ID, then a positional
INSERT ... SELECT *) on purpose:
  - Columns are named, so computed and rowversion/timestamp columns (which cannot be inserted) are simply
    left out, and a column added or reordered later cannot silently shift values into the wrong column.
  - No temp table. (The original's IF EXISTS(SELECT * FROM #TMP) raised "invalid object name" when #TMP did not
    exist yet, and "SET @@NewID" is not valid T-SQL. New ids come from SCOPE_IDENTITY(), not @@IDENTITY,
    which a trigger can hijack.)
  - Every column the row is not supposed to carry over is handled deliberately (below), instead of a blind copy.

What the copy is: an exact copy of the source row's values EXCEPT -
  - The primary key. It is what makes the row a different row:
      * an IDENTITY key is assigned by the database and returned in @New<Key> OUTPUT;
      * a single uniqueidentifier key gets NEWID(), also returned in @New<Key> OUTPUT;
      * any other key (a composite key, or a natural key such as a code) has nothing to generate: the caller
        passes the new value(s) in required @New<Key> parameters.
  - Audit columns (SpecialLogicColumns.config), as for a brand-new row: CreateDateColumn and
    LastChangedDateColumn become GETDATE() (the clone is not created when the source was); ModifiedDateColumn and
    ModifiedUserColumn (e.g. ModifiedBy, UpdatedBy) are left NULL - a fresh clone has no prior editor either;
    CreateUserColumn is a parameter (who is cloning it - the source's creator is not the clone's),
    defaulting to '' like SP_Insert.
  - Active/Inactive: cloning an inactive row makes an ACTIVE row; InactiveDate and any InactiveReason columns are
    left NULL.  Admin flag: the clone is never an admin, whatever the source is (same reasoning as SP_Insert).
    Soft delete: the clone is not deleted; its deleted-date is left NULL.
  - Columns in a UNIQUE index/constraint (an account number, say): a copy that kept the source's value could
    never be saved. Each gets an optional override parameter (same name as in SP_Insert/SP_Update, = NULL):
    pass a value to give the clone its own, or omit it to copy the source's - which is right when the unique
    index is composite and something else in it already differs, and otherwise fails with SQL Server's own
    duplicate-key error (the whole clone rolls back). An override cannot be NULL (NULL means "copy").

Failure handling: joins the caller's transaction or owns its own (XACT_ABORT ON, errors re-raised with THROW);
a source key that matches no row raises error 55509 (next to the Lookup's 55508). Returns 0 on success.
Nothing here depends on spLogPath/spLogError.

Requires a primary key (that is how the source row is named); tables only.
```

- **Access mode (EF Core instead of routines):** with `AccessMode=Ef` (always for SQLite) this template writes LINQ over the context instead of the routine; the plan leaves it out. See CS_SearchQuery_v1.md and Docs/Reference.md.
