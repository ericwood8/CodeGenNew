# SP_Search_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>_Search.sql - two procedures in one file, the same "several CREATE PROCEDURE...GO blocks in one output file" convention
SP_Junction.tt already uses for its List/Link/Unlink trio.

<TableName>_Search: a list/search screen that builds a dynamic filter from whichever of several
optional text boxes the user actually filled in: one @Column parameter per searchable column, defaulting to NULL (not filtered on);
every supplied parameter contributes an AND-ed "column LIKE '%value%'" clause, so a caller can search
by any combination of columns, or none (returns every row). Paginated:
@PageNumber (1-based) and @PageSize, applied via OFFSET/FETCH NEXT after the ORDER BY every paginated
query needs to be deterministic.

<TableName>_SearchCount: the identical WHERE clause (same filter parameters, same rule for what counts
as searchable), but SELECT COUNT(*) - no columns, no ORDER BY, no paging. A caller needs this to know
how many pages exist. Kept as a SEPARATE procedure/round-trip rather than a COUNT(*) OVER() column on
every row of <TableName>_Search: that column would stop <TableName>_Search's result shape from matching
the entity's own mapped columns exactly, which breaks the Set<T>().FromSqlRaw(...) call every consumer
of this procedure already uses the same way an SpCanDeleteAsync call does -
avoided rather than asking every target project to register a second per-table DTO type just to read
one extra column.

Searchable columns are every string column (ColumnModel.IsStringColumn) that isn't audit-classified
(IsAuditColumn - Create*/Modif*/Change*/Delete*/Update*/Activ*/Inactiv*, per AuditColumnClassifier):
nobody types into a search box for "who created this row". A table with none (all-numeric/date/bit
columns, e.g. a monthly-summary table keyed only by ids/numbers) still gets both procedures, just with
no filter parameters and no WHERE clause at all - pagination and searchability are separate concerns,
found live needing pagination on exactly such a table (2026-09-28): a table only grows over time, with
or without anything to type into a search box.

Parameters are LTRIM/RTRIM'd before the LIKE, matching SP_Insert.tt's "trim strings" rule - a caller
who fat-fingers a leading/trailing space still gets the match they meant.

No index is assumed for any searched column: LIKE '%...%' is a leading-wildcard scan, O(n) per
filtered column on a large table. That's a real performance cost on a big table with many searchable
columns - add an index (or trim which columns this searches, by hand, in the generated file) if it
gets slow, the same "documented limit, not silently generated as if free" choice TS_Component.tt's
own no-paging note makes.

<TableName>_Search returns every column (not just the display columns SP_Lookup.tt returns), since a
search results grid usually wants to show more than just the row's identity - this is a plain
filtered SELECT, not a summary. ORDER BY is the same "best display column, then primary key"
convention SP_Lookup.tt uses.

Requires a primary key (the result set needs something stable for a caller to act on) and a table,
not a view. Does not filter out inactive/soft-deleted rows - that's a different, orthogonal concern
from "which columns can I type into a search box" and is left to the caller (or SP_Lookup.tt, which
already handles it) rather than folded in here silently.
```

- **Access mode (EF Core instead of routines):** with `AccessMode=Ef` (always for SQLite) this template writes LINQ over the context instead of the routine; the plan leaves it out. See CS_SearchQuery_v1.md and Docs/Reference.md.
