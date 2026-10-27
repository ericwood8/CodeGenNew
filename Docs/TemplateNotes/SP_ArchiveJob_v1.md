# SP_ArchiveJob_v1 and the ArchiveTables setting

`ArchiveTables=ErrorLog:90,JobQueue:30:delete` names the tables whose old rows a script moves out: a table, the days of rows to keep and, after a second colon, `delete` to remove the rows instead of moving them to `<Table>Archive`. The rule is `ArchivePlan` in Core, the text is `ArchiveScript`.

`SP_ArchiveJob` writes `Sql/ArchiveJobs.sql`. The generator never applies it and the application never runs it: review it, run it and schedule it.

## What the script holds, per table

- The archive table, unless the database has it: the table's columns with their own types and NULL rules, no identity and no default, the same primary key and an `ArchivedOn` stamp. An archive table the database already has is used as it is: it needs every column of the source (an identity key on SQL Server is refused, because the rows keep their key) and uses `ArchivedOn` when it has one.
- A procedure `Archive<Table>(days, batch)` with the days of the setting and `ArchiveBatchSize` rows per batch (default 1000), so the log never holds a lock for long.
- The age is measured on the table's timestamp (`LogTables`), or for a work queue on `ProcessedOn`; a queue gives up its `done` rows only (failed rows wait for a Retry).
- The scheduling text, commented out, at `ArchiveRunAt` (`HH:mm`, default `02:00`).
- A row that another table's foreign key refers to is left where it is (`AND NOT EXISTS (SELECT 1 FROM <child> ...)` for each such key), so the batch is never refused; the row moves in a later run once the rows that refer to it have gone.

| Database | Moves a batch with | Schedule |
|---|---|---|
| SQL Server | `DELETE TOP (n) ... OUTPUT deleted.* INTO archive` (one statement, so a row is never in both or neither) | an Agent job (`sp_add_job` and friends, in a comment) |
| PostgreSQL | a procedure with a `DELETE ... RETURNING` feeding the `INSERT`, rows picked by `ctid` with `FOR UPDATE SKIP LOCKED`, `COMMIT` after each batch | `pg_cron` (in a comment) |
| MySQL | a procedure that inserts a batch and deletes the same ordered batch in one transaction | an event (in a comment; the server needs `event_scheduler=ON`) |
| SQLite | a plain script: insert, delete, one transaction | the operating system's scheduler |

## The archive as a list

An archive table the database has is a read-only list (`LogTablePlan`): newest first by its event time, a From and a To box, no write route, in every stack. `SP_LogIndex` writes its index. A table that is deleted from (`:delete`) has no archive to show.

## Not covered

No archive of a table with no primary key. A table that refers to itself keeps the old behavior (the delete is refused while a row refers to a row in the same batch). The foreign key columns are matched by the names the database reader gave, so a project that renames columns with `NamingStyle` may need the script checked. The scheduling text is not applied, so no job exists until you create it. The Python and Rust stacks have no list of an archive.

Checked against the `ErrorLog`, `ErrorLogArchive` and `JobQueue` tables on SQL Server, PostgreSQL and MySQL (old rows moved in batches smaller than their number, the recent row stayed, only the finished queue rows were deleted), on a SQLite copy, and by applying the script that creates a missing archive table on all three servers. With a child table whose foreign key refers to `ErrorLog` and a batch of 2, five old rows left four moved and the referenced one in place on all three servers, and the fifth moved once the child row was deleted.
