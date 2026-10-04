# SP_ForeignKeyIndexes_v1

`ForeignKeyIndexes.sql` for the whole database: one `CREATE INDEX` for each foreign key whose columns no index starts with (any index counts, the primary key's and a unique one's too, and the foreign key's columns may be in any order). An unindexed foreign key makes every join to it, and every delete of a parent row, scan the child table.

- The index list comes from `TableModel.Indexes`, read by each provider (`sys.indexes`, `pg_index`, `information_schema.STATISTICS`). A filtered, partial or expression index is left out, because it does not cover every row; so are included-only columns.
- **Syntax by database:** SQL Server guards with `IF NOT EXISTS (... sys.indexes ...)`, PostgreSQL uses `CREATE INDEX IF NOT EXISTS`, MySQL has no such clause (run its script once; InnoDB already indexes every foreign key, so it usually reports nothing to do).
- The name is `IX_<table>_<columns>`; a name over 63 characters (PostgreSQL's limit, the shortest of the three) is cut and ends with a hash of the whole name, so two long names stay different and the same input always gives the same name.
- Advice only: codegen never applies it, and every index costs a little on each write. Not in a plan unless the project names it in `PlanAlso`.
- **Checked** on the SQL Server sample (11 indexes, applied inside a rolled-back transaction), on a scratch copy of the PostgreSQL sample (applied twice: the second run skips them), and on the MySQL sample (no output, as expected).
