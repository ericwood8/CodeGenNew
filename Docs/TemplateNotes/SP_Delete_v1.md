# SP_Delete_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>_Delete.sql

Design notes (revised - see Docs/Reference.md section 6 for the full history):
  - The first version of this template pre-checked for dependent rows by calling spCanDelete
    into a temp table before every delete. That was removed deliberately: these procedures run
    OFTEN in production, and paying for a temp table + a dynamic-SQL loop across every FK
    relationship on every single delete call is real, avoidable overhead for very little extra
    safety - SQL Server's own FOREIGN KEY constraint enforcement already refuses the DELETE
    if a dependent row exists elsewhere, natively and fast, with no extra steps.
  - So this procedure just attempts the DELETE and inspects the result: SQL Server error 547 is
    specifically a FOREIGN KEY (or CHECK) constraint violation, so it's used to distinguish
    "blocked by a real dependency" (-1) from any other unexpected database-level failure (-2).
  - Whether a *separate*, general-purpose dependency-lookup procedure like spCanDelete exists and
    is correctly shaped on the target database is verified once per (server, database) - not
    per delete call, and not by this generated procedure - by CodeGenNew itself at connection
    time, cached in SpCanDeleteVerification.config (Docs/Reference.md sections 6 and 4). That
    check is informational for the developer; it has no runtime relationship to this procedure.
  - Return convention: 0 = deleted successfully, -1 = blocked by a foreign key elsewhere,
    -2 = the DELETE failed for some other database-level reason.

This template requires a primary key (SP_Delete.tt.config: RequiresPrimaryKey=true) and only
applies to tables, not views (TableOnly=true). Works uniformly for single, composite, and
non-integer (e.g. GUID) primary keys - there's no longer a reason to special-case any of them.
```
