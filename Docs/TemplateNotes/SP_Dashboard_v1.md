# SP_Dashboard_v1

`DashboardQueries.sql`: the dashboard's statements for the whole database in one script, written only for a project with `Dashboard=true` (into the SQL folder, `OutputSql`, like the other `SP_` scripts).

- One statement per widget, each under a comment with the widget's title and the reason it exists, ending in `;`. They are the same text the web API and the desktop page run, in the syntax of the database that was read.
- Nothing applies it: run it by hand (`sqlcmd -i`, `psql -f`, `mysql <`, `sqlite3 <`) to check a widget against the data. It is a script of `SELECT`s, so it changes nothing.
- It is written for SQLite too, although SQLite has no routines: the name follows the group (`SP_` = SQL), not a stored procedure.
- **Checked:** run against the SQL Server, PostgreSQL and MySQL InvoiceSystem databases and the SQLite file; every statement returned rows with matching numbers.
