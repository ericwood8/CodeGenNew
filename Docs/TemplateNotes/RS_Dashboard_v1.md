# RS_Dashboard_v1

`src/routes/dashboard.rs`: the dashboard's endpoint for the Rust stack, written only for a project with `Dashboard=true` (the default).

- `GET /api/dashboard` answers every widget of the dashboard page in one call, with the JSON the other stacks answer (`DashboardWire`): `{ "widgets": [ { "id", "kind", "title", "table", "route", "isMoney", "points": [ { "label", "value" } ], "rows": [ { "label", "detail" } ] } ] }`. `GET /api/dashboard?table=Customer` answers the cards of that table's strip (`DashboardStrip=true`); a table with none gets an empty list.
- The widgets and their statements are the choice `DashboardPlan` makes in Core, the one `CS_Dashboard` writes into `DashboardData.cs`, so every stack shows the same numbers. Each statement is one fixed text without a parameter (written with `DialectInfo` for PostgreSQL, MySQL or SQLite) that returns `Label` and `Value`, or for a recent-rows widget `Label` and `Detail`. The `table` in the query string is only compared with the fixed list, and a table that has no strip is answered without touching the cache.
- With `ApiAuth` and `ReadRoles` or `TableReadRoles`, the handler takes the signed-in `CurrentUser` and drops the widgets of tables the role may not read from its copy of the answer (the cache is shared by every caller).
- The answer is kept for a minute per table. `RS_Mod` merges `dashboard::router()` with the tables' routers.
- The aggregates are those of every row; a project with row-level rules must put this route behind the same policy as its tables.
- **Checked:** the InvoiceSystemRust sample was run against its PostgreSQL copy: 11 widgets (the totals, counts and top lists agree with the tables), the strip answer for a table with none is empty, and the React dashboard page draws 4 cards and 7 panels.


**Tenants.** With `TenantTable` and `ApiAuth` the statements and the cache work per tenant, as in `CS_Dashboard_v1.md`: `load(pool, table, tenant)` replaces `{tenant}` in the statement with the caller's tenant key (`NULL` for a caller in no tenant); the statement is built from a constant and a whole number, so it is marked safe for sqlx.
