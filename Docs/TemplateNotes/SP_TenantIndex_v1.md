# SP_TenantIndex_v1

For a multi-tenant project (`TenantTable`): `TenantIndexes.sql` in the `Sql` folder, a script that makes the unique indexes of the tenant-owned tables per tenant. The script is reviewed and run by you; codegen never applies it.

A unique index that leaves the tenant column out, such as `UNIQUE (Name)`, refuses the second tenant's equal value, although the tenants share nothing. For each such index of a tenant-owned table the script drops it and creates it again with the tenant column first, `(TenantId, Name)`. It is the same rule the duplicate-name check of a Name and IsActive table already applies in code.

- **Which indexes:** the unique ones of the tables `TenantPlan.OwnedTables` lists, except the primary key and an index that already holds the tenant column. The tenant table itself and a table in `NoTenantTables` are not touched. A filtered or partial unique index is not looked at. `TenantIndexPlan` (Core) finds them and writes the script.
- **The new name** is `UX_<Table>_<TenantColumn>_<columns>`, cut at 60 characters.
- **The drop** follows the database. SQL Server asks `sys.key_constraints` and `sys.indexes` and drops a constraint with `ALTER TABLE ... DROP CONSTRAINT` or an index with `DROP INDEX`, whichever it is. PostgreSQL runs `DROP CONSTRAINT IF EXISTS` and then `DROP INDEX IF EXISTS`. MySQL runs `DROP INDEX ... ON`. SQLite runs `DROP INDEX IF EXISTS`; an index made by a `UNIQUE` constraint in the table definition (`sqlite_autoindex_*`) cannot be dropped, and the script says that the table has to be rebuilt without it.
- **Before you run it:** two rows of one tenant that share a value stop the new index, after the old one has gone. Try it on a copy.
- **Written** when `TenantTable` is set and some index needs the change; the file is empty (and not written) otherwise. It is part of the whole-project plan (`ImpliedPlanTemplates`) and can be run on its own for the database.

Checked on a SQLite copy of `Schemas/tenants_bypass.sql`: the script ran, and the index of the `Tag` table became `(TenantId, Name)`. The SQL Server, PostgreSQL and MySQL texts are covered by the generation tests only.
