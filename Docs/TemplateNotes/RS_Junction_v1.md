# RS_Junction_v1

`src/routes/<table>_junction.rs` for each many-to-many junction table of the Rust stack: the same three routes as `PY_Junction` and `API_Junction` (`GET /api/<plural>/junction/{anchorId}`, `POST .../junction/link` and `.../unlink` with `{ anchorId, targetId }`), so the junction screens work against the Rust API unchanged.

The statements are fixed text written for the database that was read (quotes, placeholders, the display columns cast to text with `CAST(... AS TEXT)`, or `AS CHAR` on MySQL). The list reads the linked keys and the target rows and marks the linked ones in Rust. A link first counts the existing rows and inserts only when there is none, so repeating a move is a 200; a create-date column gets `CURRENT_TIMESTAMP` and a create-user column `''`. A foreign key the database refuses is a 400 through `ApiError`. `RS_Mod` merges the router. `ReadRoles` and `WriteRoles` apply as in `RS_Routes`. PostgreSQL, MySQL and SQLite only (SQL Server is not supported by the Rust stack).

Settings: none of its own. `Requires=PrimaryKey,JunctionTable`, `NeedsReferencedDisplayColumns=true`, `Stacks=Rust`, `PlanTables=Junction`.


**Tenants.** With `TenantTable` and `ApiAuth` the routes need the signed-in caller: the list answers 404 unless the anchor row is one the caller can see and shows only the target rows of the caller's tenant, and a link or unlink answers 404 unless both rows are visible. A table counts as tenant-owned by `owns` in `tenancy.rs`; the statements with the tenant condition are built from constants and the tenant column name.
