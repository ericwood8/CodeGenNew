# SP_PartitionJob_v1 and the PartitionTables setting

`PartitionTables=WebhookEvent,PageView:ViewedOn` names the tables a script splits into one partition for each month of a date column: a table and, after a colon, the column (found as for `ArchiveTables` when left out: the received-on stamp of an inbound table, the occurred-on of a usage ledger, the stamp of a log table, else the one date and time column). The rule is `PartitionPlan` in Core, the text is `PartitionScript`.

`SP_PartitionJob` writes `Sql/PartitionJobs.sql`. The generator never applies it and the application never runs it: review it, run it and schedule the procedure.

## What the script holds, per table

- The conversion, which can run twice (a table that is partitioned already is left alone). SQL Server and MySQL change the table in place; PostgreSQL cannot, so it renames the table to `<Table>_unpartitioned`, creates a partitioned table with the same columns, defaults and identity, copies the rows, hands the sequences over and leaves the old table for you to check and drop.
- One partition for each month from the oldest row to `PartitionMonthsAhead` months ahead (default 3). PostgreSQL also gets a `<Table>_default` partition and MySQL a `pmax` partition, so a row of a month nobody prepared is still stored.
- A procedure `Partition<Table>(months ahead, months to keep)` that adds the coming months and, when `PartitionKeepMonths` is above 0, drops the months that end before the cut-off. SQL Server empties the oldest partition with `TRUNCATE ... WITH (PARTITIONS (2))` and merges the boundary away, PostgreSQL drops the month's table, MySQL drops the partition. A dropped month is gone for good: archive first if you need the rows (`ArchiveTables`).
- The scheduling text, commented out, for the first of each month (Agent job, `pg_cron`, MySQL event).

## What changes in the table

The date column joins the primary key, since no database enforces a key across partitions that lacks the partition column: the key `(Id)` becomes `(Id, ReceivedOn)`. Every other index is recreated on the partitions with its key columns only (a column an index `INCLUDE`s is not kept). A unique index that lacks the date becomes a plain index and the header of the script says so: an inbound table's `(Source, EventId)` index no longer stops two requests that arrive at the same moment from storing the same event (the route still answers a repeated id with `duplicate`, since it looks the id up first).

A table with foreign keys (to it or from it), filtered indexes, no primary key, or a date column that allows NULL is refused with a sentence. SQLite has no partitions.

The generator reads one key column, so generate from the database before the script has run. A table with the composite key already (the one the script makes) is told so.

| Database | Partition | Add a month | Drop a month |
|---|---|---|---|
| SQL Server | partition function (`RANGE RIGHT`) and scheme on `[PRIMARY]`, clustered key on the scheme | `SPLIT RANGE` | `TRUNCATE ... WITH (PARTITIONS (2))`, `MERGE RANGE` |
| PostgreSQL | `PARTITION BY RANGE`, one table for each month named `<Table>_YYYY_MM` | `CREATE TABLE ... PARTITION OF` | `DROP TABLE` |
| MySQL | `PARTITION BY RANGE COLUMNS`, partitions named `pYYYYMM` | `REORGANIZE PARTITION pmax` | `DROP PARTITION` |

## Not covered

No foreign keys, no filtered indexes, no `INCLUDE` columns. The months are the server's calendar months, with no time zone. A PostgreSQL month cannot be added while the default partition holds rows of it; keep the procedure scheduled ahead of the months. The schedule is not applied. Partitioning helps the purge and the oldest-first scans; it does not speed a query that does not filter on the date.

The generated inbound tests (ASP.NET and Python on all three servers, Rust on PostgreSQL and MySQL) also pass over tables the script partitioned, and the add and drop procedure was called on each server (`Docs/Verification/Tools/live_inbound_run.py --partition`).

Checked on SQL Server (a copy of the table in `InvoiceSystem_test`), PostgreSQL (an identity key and a serial key; the old table dropped, the next insert took the next number) and MySQL: the rows landed in the right months, the indexes followed, running the script again changed nothing, and the procedure added months and dropped the old ones with the newest rows kept.
