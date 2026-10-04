# MD_DataDictionary_v1

One Markdown page per table (`<Table>.md`), for people.

- **Columns:** number, name, type, whether it can be NULL, the default, the key role (`PK`, `FK -> [Parent](Parent.md)`, `unique`), the limits (a text length, a CHECK range, a CHECK list) and notes (identity, computed with its expression, the database name when the naming style changed it).
- **Foreign keys** out of the table ("Refers to") and the tables that point at it ("Referred to by"); run it for every table and the pages link to each other.
- A `|` in a default is escaped so the table survives.
- The schema has no column descriptions to read: add a sentence by hand and keep the page out of the regenerated ones (or wait for the merge mode of spec item 62).
- Not in a plan unless the project names it in `PlanAlso`; written under `docs/tables` by a plan.
- **Descriptions:** when the database holds a description for the table or a column (SQL Server `MS_Description`, PostgreSQL `COMMENT ON`, MySQL `COMMENT`) the page shows the table's description under the title and gains a Description column; a table with no comments is written as before. `ProjectDocs=true` adds this template and `MD_Erd` to a plan.
