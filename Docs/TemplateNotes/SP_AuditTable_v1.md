# SP_AuditTable_v1 and SP_TemporalTable_v1

Two ways to keep the history of a table. Both write a script for you to review and run; codegen never applies it. A table can use only one (each keeps a `<Table>_History` table).

## SP_AuditTable: a history table and a trigger (all four databases)

- **Which tables:** a table with audit columns, `TableModel.IsAuditTable`: a column that records its creation (a name starting `Create`: CreateDate, CreateUser, CreatedBy) and a column that records a later change (a name with `Modif` or `Change`, or starting `Update`: ModifiedDate, ModifiedBy, UpdatedOn, LastChangedDate). Only such a table gets the template in its right-click menu, and any other table is refused with the reason. No project setting is involved. A whole-project run includes it, for those tables only, when `PlanAlso` names `SP_AuditTable`. A table also named in `TemporalTables` is refused.
- **The history table** `<Table>_History` is created when missing: its own key `AuditId` (bigint, numbered by the database), `AuditAction` (`U` update, `D` delete), `AuditDate` (UTC) and `AuditUser` (the login or user), then the table's columns with their types and nullability and no key, default or foreign key. An insert is not recorded: the live row is the record of it. SQLite has no user, so `AuditUser` is NULL there.
- **The trigger** copies the old row after an update or delete. SQL Server: one `AFTER UPDATE, DELETE` trigger (`CREATE OR ALTER`). PostgreSQL: a function `<Table>_Audit` and an `AFTER UPDATE OR DELETE` row trigger (`CREATE OR REPLACE`, PostgreSQL 14 or later). MySQL and SQLite: one trigger for update (`..._Audit_U`) and one for delete (`..._Audit_D`), dropped and created again.
- **Run it again safely:** the table is created only when missing and the triggers are replaced. A column added to the table later is not added to the history table: add it to both and run the script again.
- **Left out:** computed columns and, on SQL Server, `text`, `ntext`, `image` and `timestamp` columns (a trigger cannot read them). A table with a column named `AuditId`, `AuditAction`, `AuditDate` or `AuditUser` is refused.
- **Risk:** a trigger runs inside the statement that fired it. A history table that cannot be written rolls back the change. Try the script on a copy first. With MySQL's binary log on, creating a trigger needs the `SUPER` privilege or `log_bin_trust_function_creators=1`; without it the server answers error 1419.
- **Checked:** the scripts were run against SQL Server, PostgreSQL and SQLite on a scratch table with an update and two deletes (the three history rows were right, a second run changed nothing, `nvarchar(MAX)` and `text` columns came through). The MySQL script was not run: the development login has no `SUPER` and the binary log is on, so the first `CREATE TRIGGER` was refused (error 1419) before it could be tried.

## SP_TemporalTable: system-versioned tables (SQL Server only)

- **Project setting:** `TemporalTables=Customer,Item` (opt in, table by table).
- Adds the hidden period columns `SysStartTime` and `SysEndTime` and turns `SYSTEM_VERSIONING` on with `<Table>_History`, which the database creates and fills. Read it with `FOR SYSTEM_TIME AS OF '<time>'`. No trigger, no user (use SP_AuditTable if you need to know who).
- Needs a primary key; `text`, `ntext` and `image` columns are refused. A table that is already temporal is left alone. The existing rows start their history when the script runs.
- **Checked** on SQL Server: the script ran twice, and an insert, update and delete left the old row in the history table.
