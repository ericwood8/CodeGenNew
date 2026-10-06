# PY_Routes_v1

The Python stack: a **FastAPI** API over the database, with **SQLAlchemy 2.0** models and **Pydantic 2** schemas, serving the contract every stack of the project keeps, so the React, Angular and Blazor front ends call it unchanged. `Stacks=Python` (`OutputPython`, default `<ProjectName>.Python`; `BuildPython`, default `python -m compileall -q app`; `TestPython`, no default). It reads SQL Server (pyodbc), PostgreSQL (psycopg 3), MySQL (PyMySQL) and SQLite.

| Template | Writes |
|---|---|
| `PY_Model` | `app/models/<table>.py`: the SQLAlchemy class: the database's own table and column names, snake_case attributes, the column types, nullability, the key (`autoincrement` when it is an identity), server-generated columns |
| `PY_Schema` | `app/schemas/<table>.py`: the Pydantic class a body and a response are: camelCase JSON names, a decimal as a JSON number, defaults for nullable and database-written columns, `columns()` (what an insert writes) and `changes()` (what an update sets) |
| `PY_Routes` | `app/routes/<table>.py`: the FastAPI router |
| `PY_Validate` | `app/validation/<table>.py`: the schema's rules (`ApiValidation=true`; also `PlanAlso`) |
| `PY_Mod` | `app/models/__init__.py`, `app/schemas/__init__.py`, `app/routes/__init__.py` (`routers`) and, with validation, `app/validation/__init__.py` |

**Routes (per table, as the other stacks):** `GET /api/<plural>` (all rows, by key), `GET /{id}`, `POST` (201, a `Location` header and the row), `PUT /{id}` (400 when the id in the body differs, 404 for no such row), `DELETE /{id}` (200, 404, or 400 when the row is in use), `POST /{id}/clone` (a cloneable table: dates reset, audit and soft-delete columns cleared, a unique text value made free by `suggest_free`) and `GET /search` (a table with a search endpoint: a "contains" filter per searchable text column, `pageNumber`, `pageSize` up to 1000, `sortBy` over the sortable columns and `sortDir`, the best display column and then the key as the default order). The answer of a search is `{ items, page, pageSize, totalCount, totalPages }`.

**Errors:** a type that does not fit, and the rules of `PY_Validate`, are a 400 in the ASP.NET validation-problem form (`{ type, title, status, errors: { Property: [messages] } }`), not FastAPI's 422; a 400 or 404 from a route is `{ type, title, status }`.

**Essentials** (`codegen essentials --stack python`, or Essentials > Python essentials; written once and kept): `App` (`app/db.py`: `Base`, the engine and the session, from `DatabaseProvider`, `DatabaseServer`, `DatabaseName`, `DatabaseUser` or `DATABASE_URL`; `app/support.py`; `app/main.py`: the app, CORS for the front ends' dev servers or `CORS_ORIGINS`) and `Project` (`requirements.txt` by version range with the driver of the database, `.env.example`, `.gitignore`, `README.md`). **No password is ever written:** it is read from `PGPASSWORD` (PostgreSQL), `MYSQL_PWD` (MySQL) or `DB_PASSWORD` (SQL Server, with a `DB_USER`; no user means Windows authentication).

Run it: `pip install -r requirements.txt`, then `uvicorn app.main:app --port <ApiPort>`; the interactive documentation is at `/docs`.

## Limits

- Tables with an API of their own and a single int key only (the tables the other stacks serve); a name/active table, a composite key, a GUID key and an enum table get nothing.
- A binary column, a PostgreSQL `money` column and a type CodeGenNew cannot map are left out of the model and the schema (a comment in the model lists them).
- Sorting by a foreign key sorts by the key, not by the name the grid shows.
- Not generated: tests, migrations (Alembic), authentication, a mirror of `ApiDocs`'s `openapi.yaml` (FastAPI serves its own at `/openapi.json`), navigation properties (relationships) and the dashboard, CSV and CQRS pieces of the .NET API.
- SQLite stores a decimal as a float, so SQLAlchemy warns about a `Numeric` column there.

## Checked

Generated for the PostgreSQL InvoiceSystem sample (FastAPI 0.142, SQLAlchemy 2.0.54, Pydantic 2.13, psycopg 3.3, uvicorn 0.54 in a fresh virtual environment, about 72 MB on disk) and run: get all, search with a filter, sort and paging, create (201 and `Location`), get, update (a bare `2026-10-05` for a date-time field is accepted), the id mismatch (400) and missing rows (404), clone, delete, delete of a customer that has invoices (400, nothing deleted), the 400 forms for a text that is too long and for values of the wrong type. The Blazor app generated for the same database was then run against it unchanged: the grid, search, sort, the customer drop-down of an invoice, and add, edit, clone, validation message and delete of a customer through the form. No 500 and no warning in the server log. The rows added were deleted again.
