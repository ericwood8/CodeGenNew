# SP_ReplicationTriggers_v1

Three triggers on one table that copy each change to other databases.

- **Project setting:** `ReplicationTargets=server1.Sales,server2.Sales` (linked server, then database; both parts required).
- **Insert:** a cursor over `inserted`, one `EXEC <target>.<schema>.<Table>_Replicate_Insert` per row and target, passing every column in table order.
- **Update:** a change of the primary key is refused (`RAISERROR` and `ROLLBACK`); nothing is sent unless a non-key column was assigned (`IF UPDATE(col) OR ...`); then `_Replicate_Update` per row and target.
- **Delete:** the key columns of each `deleted` row go to `_Replicate_Delete`.
- **The routines on the targets** (`<Table>_Replicate_Insert`, `_Update`, `_Delete`) are not written here. Each takes one parameter per column (insert and update) or per key column (delete), in the table's column order: the call is positional.
- text, ntext, image, timestamp and computed columns are left out (a trigger cannot read them from `inserted`).
- **Risk:** a trigger runs inside the statement that fired it. A target that is down, or a routine that fails, rolls back every write to the table (checked on a scratch copy: with a linked server that does not exist, an update of the table was refused). Review the script, try it on a copy, run it yourself.
- **Not done:** writing the target-side routines; batching instead of a row cursor.
