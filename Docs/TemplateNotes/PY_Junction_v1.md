# PY_Junction_v1

`app/routes/<table>_junction.py` for each many-to-many junction table (`TableModel.IsJunctionTable`) of the Python stack: the routes `API_Junction` writes, so the junction screens of WinUI 3, Uno, React, Angular and Blazor work against the Python API unchanged.

| Route | Answer |
|---|---|
| `GET /api/<plural>/junction/{anchorId}` | every row of the target table as `{ <display columns>, targetId, isSelected }`, ordered by the first display column |
| `POST /api/<plural>/junction/link` | body `{ anchorId, targetId }`; adds the link unless it exists (200 either way) |
| `POST /api/<plural>/junction/unlink` | the same body; removes the link, 200 when there is none |

The statements are SQLAlchemy Core over the database's own table and column names (`table()` and `column()`, with the schema where SQL Server or PostgreSQL has one), so the file runs on every database the stack reads. The list is two reads (the linked keys, then the target rows), so nothing depends on a boolean expression in a select list. A create-date column is set to `datetime.now()` and a create-user column to an empty string, as `JunctionEf` does. A link the database refuses (a missing row on either side, where the database enforces foreign keys) is a 400.

`PY_Mod` lists the router. `ReadRoles` and `WriteRoles` rules of the junction table apply to the list and to link and unlink. Rows are not scoped by `OwnedTables`, as in `API_Junction`.

Settings: none of its own. `Requires=PrimaryKey,JunctionTable`, `NeedsReferencedDisplayColumns=true`, `Stacks=Python`, `PlanTables=Junction`.
