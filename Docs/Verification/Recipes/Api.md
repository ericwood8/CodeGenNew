# Verifying a generated API

1. Copy the database: `ScratchDatabase.ps1 -Provider <SqlServer|PostgreSql|MySql> -Source <database> ...` (SQL Server: backup and restore, PostgreSQL: `TEMPLATE`, MySQL: table copies; passwords come from `PGPASSWORD` / `MYSQL_PWD` in your own shell).
2. Point the API at the copy without editing a file: `ConnectionStrings__DbConnectionString` (a connection string with no password), a spare port (`--urls http://localhost:5199`).
3. `Test-ApiCrud.ps1` does the rest for one route: POST, GET by id, PUT, GET all, DELETE (404 afterwards). Run it for a table with a foreign key and for one with a decimal column.
4. Look at what the database refused: a 400 for a row in use, a rejected value (a CHECK range), a missing required column.
5. Drop the copy (`-Drop`).

The routes carry the `/api` prefix (`/api/customers`); a Swagger page, when the project has one, lists them.
