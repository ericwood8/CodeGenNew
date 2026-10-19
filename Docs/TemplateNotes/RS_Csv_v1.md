# RS_Csv_v1

`src/routes/csv_files.rs` for a project with `ApiCsv=true`: the same routes and rules as `PY_Csv` and `API_Csv`, for the Rust API, with no new crate. A row is exported by serialising the model to JSON and reading the columns by their JSON names, and imported by building the JSON object of the row from the cells and deserialising it into the model, so the model's own serde rules judge dates, GUIDs and numbers (their error text is the message of the row). The table's `validate` runs on each row with `ApiValidation=true`.

- **All or nothing:** the rows are inserted by `repo::insert_all`, one transaction, which `RS_Repo` writes for a project with `ApiCsv=true`. A database refusal answers a 400 and rolls everything back.
- **Authorisation:** with `ApiAuth` the routes of an owned table, and the import into a gated table, need the administrator role (403 `role-not-allowed` otherwise).
- **Upload size:** the router raises the body limit to 32 MB (axum's default of 2 MB is too small for 10000 rows); the row limit is 10000.
- **Limits:** a computed column that is not an identity, not nullable and not read has no value to deserialise, so such a table cannot be imported; the body is read as text. PostgreSQL, MySQL and SQLite only.
