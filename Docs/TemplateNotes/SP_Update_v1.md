# SP_Update_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>_Update.sql

Behavior (see Docs/Reference.md sections 5 and 7):
  - Every column becomes a VarChar parameter, regardless of its real SQL type (this is a
    deliberate, established convention from the author's prior generators - NOT a data-type
    mismatch bug). Passing NULL for a parameter means "do not update this column."
  - Computed columns are skipped entirely (cannot be assigned to).
  - The primary key column(s) are still declared as parameters (needed for the WHERE clause)
    but are excluded from the dynamically-built SET clause.
  - String/VarChar-typed columns get LTRIM/RTRIM applied and embedded single quotes doubled
    (standard T-SQL escaping) before being concatenated into the dynamic SQL text, per the
    "trim strings" and "strip bad characters" rules in the original spec.
  - Composite primary keys are supported: the WHERE clause AND-joins every PK column.
  - The SQL Server statement is built as text, so a text or uniqueidentifier key is written into the WHERE as a quoted literal with its quotes doubled; a whole-number key is written as it is.
  - Deviation from the original single-PK worked example: this generalized version returns
    1 on success / 0 on failure, rather than echoing back the (single) ID parameter, since a
    composite key has no single value to echo.
  - Uses CREATE OR ALTER PROCEDURE (not ALTER PROCEDURE) so it deploys whether or not the
    procedure already exists - required for the CLI's verify parameter (Docs/ARCHITECTURE.md section 7).
  - Audit date columns (SpecialLogicColumns.config categories CreateDateColumn/ModifiedDateColumn/
    LastChangedDateColumn, e.g. CreateDate/ModifiedDate/LastDateChanged) are excluded from the
    parameter list entirely - they are never caller-supplied. ModifiedDateColumn and
    LastChangedDateColumn matches are both set to GETDATE() unconditionally on every update
    (LastChangedDateColumn is a blend: the Insert template, when built, will also set it
    alongside CreateDate). A CreateDateColumn match is never touched by Update logic at all.
  - CreateUserColumn match (e.g. CreateUser) is likewise excluded from the parameter list: who created
    a row never changes, so Update must not be able to overwrite it (Insert and Save still take it).
  - ModifiedUserColumn match (e.g. ModifiedBy, UpdatedBy) is the mirror image and needs no special
    handling here: it is a normal caller-supplied parameter, same as any other column (the caller says
    who is editing). It is Insert/Save's insert branch/Load/Clone that exclude it, not Update.
  - Active/Inactive (HasActiveInactivePair): if the caller supplies a value for InactiveDate
    and/or any InactiveReasonColumn match (e.g. InactiveReasonNoteText), the active-flag column
    is forced to its "inactive" value (0 for IsActive/Active-style, 1 for IsInactive-style) -
    regardless of whatever value, if any, was explicitly passed for the flag itself. The active
    flag is therefore excluded from the normal per-column loop and handled by a single
    IF/ELSE IF instead: SQL Server rejects assigning the same column twice in one UPDATE's SET
    clause (error 264) - confirmed by testing - so there can only ever be one assignment to it,
    never two. The flag can still be set independently (e.g. to reactivate a record) via the
    ELSE IF branch when neither inactivation trigger is supplied.
  - StartEndDate pair (e.g. StartDate/EndDate, DateOpened/DateClosed) and the exact-named
    DateIn/TimeIn/DateOut/TimeOut quartet (e.g. the CMS table): when the caller supplies BOTH
    sides of a pair in the same call, rejects the update with RAISERROR if the range is backwards. Only checked when both sides are supplied together - since NULL
    means "don't touch this column" here, validating a partial update (only one side supplied)
    against whatever's already in the row would need an extra SELECT this template doesn't do;
    review generated output for that case if it matters for a given table.

This template requires a primary key (see SP_Update.tt.config: RequiresPrimaryKey=true) and
only applies to tables, not views (TableOnly=true).
```

Guard clauses (project lists): a column in `NonNegativeColumns` or `NotFutureDateColumns` that the database states no rule for (a column whose CHECK already has a minimum needs none) gets an `IF` before the write that raises `[Column] must not be negative.` or `[Column] must not be later than today.` (`RAISERROR` and `RETURN` 0 on SQL Server; `RAISE EXCEPTION` on PostgreSQL; `SIGNAL SQLSTATE '45000'` on MySQL). The tests are inline, not shared procedures, so a database needs no extra script. A NULL parameter never trips one ("leave the column as it is"); this procedure's parameters are all text, so the value is converted with `TRY_CAST` first and a value that is no number or date is left to the statement that stores it.

Now: a stamp column of type datetime2 is set with SYSDATETIME() and a datetimeoffset with SYSDATETIMEOFFSET() (ColumnModel.SqlServerNow), so it keeps its own precision; any other date uses GETDATE(). A uniqueidentifier key is a 36-character parameter in the update procedure.

A guard on an existing row (SP_Update, SP_Save) skips a value equal to the one stored: `AND NOT EXISTS (SELECT 1 FROM table WHERE key = parameter AND column = parameter)`. A stored value that breaks the rule therefore does not stop an edit of another column, while a changed value is refused. SP_Insert has no stored row and always checks.
