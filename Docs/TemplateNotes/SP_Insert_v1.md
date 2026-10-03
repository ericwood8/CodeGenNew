# SP_Insert_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>_Insert.sql

Design notes (deliberately different from SP_Update.tt):
  - Uses ordinary, properly-typed SQL parameters (each column's real SqlTypeDeclaration) and a
    plain parameterized INSERT - NOT the VarChar-for-everything/dynamic-SQL-string convention
    SP_Update.tt uses. Insert doesn't need Update's "NULL means don't touch this column"
    semantics (every insert supplies a real row), so there's no reason to give up real typing
    and real (injection-safe-by-construction) parameters here.
  - Computed columns are excluded (cannot be assigned to).
  - A single IDENTITY primary key column is excluded from parameters (the database assigns it);
    the generated procedure returns it via SCOPE_IDENTITY(). A composite or non-identity primary
    key (e.g. a caller-supplied GUID) is instead a required parameter - there is nothing to
    auto-generate, so the caller must supply it.
  - Audit date columns (SpecialLogicColumns.config): a CreateDateColumn or LastChangedDateColumn
    match is set to GETDATE() automatically and is not a parameter. A ModifiedDateColumn or
    ModifiedUserColumn match (e.g. ModifiedBy, UpdatedBy) is excluded from the INSERT entirely
    (nothing to modify yet on a brand-new row) and is left at whatever the column's own database
    default is (typically NULL).
  - Active/Inactive (HasActiveInactivePair): a newly inserted row should never be born inactive.
    The active-flag column is excluded from parameters and hardcoded instead - 1 for an
    "IsActive"/"Active"-style column, 0 for an "IsInactive"-style column (opposite polarity,
    name contains "Inactive"), so either way the row is active by default. The companion
    InactiveDate column, and any InactiveReasonColumn match (e.g. InactiveReasonNoteText), are
    excluded from the INSERT entirely and left NULL - there's nothing to explain yet.
  - String parameters are trimmed (LTRIM/RTRIM) on the way in, per the project's "trim strings"
    rule; no manual quote-escaping is needed since these are real bound parameters, not
    concatenated SQL text.
  - String-family parameters (char/nchar/varchar/nvarchar/text/ntext) default to '' rather than
    NULL when omitted - a caller who wants a true NULL can still pass it explicitly (a SQL
    Server parameter default only applies when the argument is omitted entirely, not when it's
    explicitly passed as NULL), so no expressiveness is lost. Money columns default to 0 for the
    same reason. Everything else still defaults to NULL.
  - AdminFlagColumn match (e.g. IsAdmin): a newly inserted row should never be born an admin -
    excluded from parameters and hardcoded to "not admin" (0, or 'False' for a string-typed
    column) regardless of caller input.
  - StartEndDate pair (e.g. StartDate/EndDate, DateOpened/DateClosed) and the exact-named
    DateIn/TimeIn/DateOut/TimeOut quartet (e.g. the CMS table): when both sides are supplied,
    rejects the insert with RAISERROR if the range is backwards.

This template does not require a primary key (SP_Insert.tt.config: RequiresPrimaryKey=false) -
a table can be inserted into without one - but only applies to tables, not views (TableOnly=true).
```
