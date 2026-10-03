# SP_Save_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>_Save.sql

A Save is a blend of Insert and Update ("AddUpdate"): if a row with the supplied primary key exists it
is updated, otherwise a new row is inserted. Upside: one procedure to call, and any special business
logic lives in ONE place instead of being duplicated across _Insert and _Update. Downside: it cannot
be optimized per operation the way separate Insert/Update procedures can (e.g. Update's "NULL means
don't touch" trick), so the caller must ALWAYS pass the entire record - see "no defaults" below.

Design notes:
  - Insert-or-update strategy: an EXISTS lookup by primary key (single OR composite), NOT "attempt the
    UPDATE and fail over to INSERT on error". Reasons: (1) no error- or @@ROWCOUNT-driven control flow
    (an INSTEAD OF trigger can make @@ROWCOUNT lie; an error inside a transaction can doom it);
    (2) a composite key is just more AND terms; (3) intent is obvious when reading the output.
    The lookup uses WITH (UPDLOCK, HOLDLOCK) inside the transaction, so two concurrent Saves of the
    same not-yet-existing key serialize instead of both deciding "insert" and one dying on a
    primary key violation.
  - Ordinary, properly-typed parameters (like SP_Insert.tt, unlike SP_Update.tt). NO parameter has a
    default value, deliberately: on the update path an omitted parameter would silently overwrite the
    column with NULL. Forgetting a parameter is therefore a loud "expects parameter" error, not
    silent data loss. The one exception is an identity primary key: it is `= NULL OUTPUT` - pass NULL
    (or omit it) to insert; the new id comes back in the same parameter.
  - Identity primary key: if the supplied id is NULL OR no such row exists, a new row is inserted and
    the new id (SCOPE_IDENTITY(), not @@IDENTITY, which a trigger can hijack) is returned in the
    OUTPUT parameter. A composite or non-identity primary key is a required input; nothing is
    generated for it.
  - Strings: trimmed ONCE at the top of the procedure (SET @p = LTRIM(RTRIM(@p))), so the UPDATE and
    INSERT branches both just use the parameter. A NOT NULL string column has NULL turned into ''
    (so an "accidentally NOT NULL" column cannot fail on a NULL); a nullable one keeps its NULL.
    text/ntext parameters are left alone (LTRIM/RTRIM do not accept them).
    Quotes are deliberately NOT doubled and LIKE wildcards are NOT escaped: these are real bound
    parameters in static SQL, so escaping would store O''Brien / 50[%] in the table. That is also why
    dbo.uf_FixString (a helper in the original save procedure) is not used - it is meant for dynamic SQL / LIKE
    patterns only, and it also converts NVARCHAR to VARCHAR (lossy) and adds a scalar-UDF call per
    string parameter. SP_Update.tt does its own quote-doubling where it really builds dynamic SQL.
  - Computed and rowversion/timestamp columns are skipped (cannot be assigned). An identity column
    that is not part of the primary key is skipped too.
  - Audit date columns (SpecialLogicColumns.config), never caller-supplied: CreateDateColumn is set to
    GETDATE() on insert only and never touched on update; ModifiedDateColumn is set to GETDATE() on
    update only; LastChangedDateColumn is set to GETDATE() on both.
  - CreateUserColumn (e.g. CreateUser): a normal parameter, written on INSERT only. It is left out of the
    UPDATE's SET list so an existing row's creator is never overwritten by a later Save.
  - ModifiedUserColumn (e.g. ModifiedBy, UpdatedBy): the mirror image - a normal parameter, but left out
    of the INSERT column list (nothing to modify yet on a brand-new row) and written on UPDATE only.
  - Active/Inactive (HasActiveInactivePair): on INSERT the row is always born active (flag hardcoded,
    InactiveDate and InactiveReason columns left NULL, whatever the caller passed). On UPDATE the whole
    record is passed, so InactiveDate/reason/flag are honored - except that a non-empty InactiveDate or
    reason forces the flag to its inactive value. That is one CASE expression, i.e. exactly one
    assignment to the flag: SQL Server rejects assigning a column twice in one SET (error 264).
  - AdminFlagColumn (e.g. IsAdmin): on INSERT hardcoded to "not admin" regardless of the parameter, like
    SP_Insert.tt. On UPDATE it is set from the parameter, like SP_Update.tt.
  - StartEndDate pair and the DateIn/TimeIn/DateOut/TimeOut quartet: validated once, up front, for
    both paths (THROW when the range is backwards), only when both sides are supplied.
  - Transaction: joins the caller's transaction if there is one, otherwise starts and owns its own.
    SET XACT_ABORT ON + TRY/CATCH; errors are re-raised with THROW (the procedure does not swallow
    them). Nothing here depends on spLogError/spLogPath - add an EXEC in the CATCH if you want one.
  - Returns 0 on success (errors are raised, not returned). Row count is reported (NOCOUNT OFF).

Requires a primary key (SP_Save.tt.config: RequiresPrimaryKey=true); tables only (TableOnly=true).
```
