# API_Dashboard_v1

`DashboardApi.cs`: one read-only endpoint for the dashboard, written only for a project with `Dashboard=true`.

- `GET /api/dashboard` returns every widget of the dashboard page in one call: `{ "widgets": [ { "id", "kind", "title", "table", "route", "points": [ { "label", "value" } ], "rows": [ { "label", "detail" } ] } ] }`. `kind` is `count`, `childCount`, `money`, `topBy`, `breakdown`, `trend`, `ratio`, `status` or `recent`; a `recent` widget fills `rows`, every other one `points`. `route` is the route of the table's screen (empty when it has none), where a click on the widget goes.
- `GET /api/dashboard?table=Customer` returns the cards of that table's strip (`DashboardStrip=true`); a table with none gets an empty list.
- The data comes from `DashboardData` (`CS_Dashboard`) and is kept for a minute. `API_Registration` registers the endpoint (`DashboardApi.Register(app)`) when the project says `Dashboard=true`.
- The aggregates are those of every row. A project with row-level rules must put this endpoint behind the same policy as its tables.
- **Not done:** the endpoint is not in the document `API_OpenApi` writes.
- **Checked:** the generated API of all four InvoiceSystem copies (SQL Server, PostgreSQL, MySQL, SQLite) was run and the endpoint called over HTTP.
