# PY_Csv_v1

`app/routes/csv_files.py` for a project with `ApiCsv=true`: the CSV routes of `API_Csv` for the Python API, with no package. `GET /api/<table>/export.csv` writes every row (a header line of the column names, RFC 4180 quoting, UTF-8, `Content-Disposition: attachment`) and `POST /api/<table>/import` reads a CSV request body and adds its rows. It is written for every table that has an API of its own.

- **Columns:** the header is the column name (the same text `API_Csv` writes), read in any order and any case. Identity, computed and row-version columns are not read; every other column that cannot be NULL must be in the header. The user table's password hash is never exported.
- **Import is all or nothing.** A cell that does not convert (a whole number, a decimal, `true`/`false`/`yes`/`no`/`1`/`0`, a date, a GUID), a missing required value, a text longer than its column, or a row the table's `validate` refuses (`ApiValidation=true`) is reported as `Row n: <column> <problem>.`, and then nothing is saved. A 400 answers `{ imported: 0, errors: [...] }`; a good import answers `{ imported: n }`. A row the database refuses (a missing parent, a duplicate) rolls the whole import back. At most 10000 rows. An empty file is a 400.
- **Spreadsheet safety:** a text cell that starts with `=`, `+`, `-` or `@` is exported with a leading apostrophe, and the apostrophe is dropped again on import.
- **ApiAuth:** the routes of a table in `OwnedTables` are for the administrator role only, and so is the import into a table with a `ChangeGates` or `GuardedColumns` rule (an import saves rows without the routes' checks).
- **Route order:** `PY_Mod` puts this router first, because a table's `/{id}` route would otherwise read `export.csv` as a key.

Limits: the body is read as text (send `Content-Type: text/csv`); a multipart form upload is not read (that would need the `python-multipart` package). Written once per run; `InPlan=false`, so it is generated only for `ApiCsv=true` or when named in `PlanAlso`.

A table in `LogTables` has an export that takes `from` and `to` on its timestamp, lists newest first and stops at the first 10000 rows, and no import route.
