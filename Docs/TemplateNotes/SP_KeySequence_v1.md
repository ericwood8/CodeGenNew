# SP_KeySequence_v1

A surrogate-key generator kept in a table, as an alternative to `IDENTITY`.

- **Project settings:** `KeySequenceTables=Customer,Item` lists the tables that take their key from the sequence (each must have a single whole-number key without `IDENTITY`); `KeySequenceTable=AutoInc` names the table that holds the counters.
- **Script:** creates the sequence table if missing (one row per table and key column: start, end, step, current), starts each listed table's counter from `MAX(key)` so a table with rows carries on where it was, and writes `GetNextID(@TableName, @FieldName, @NextId OUTPUT)`. Past the end value the sequence starts over.
- **SP_Insert:** for a listed table the key is no parameter; the procedure calls `GetNextID`, inserts with the key it got and returns it.
- **Cost:** every call takes `TABLOCKX` on the sequence table for its transaction, so all inserts of all listed tables queue behind one another. This is for a modest write load, or for keys that must be known before the insert; it is not a faster `IDENTITY`.
- **Not done:** `SP_Save` still takes the key as a parameter for such a table (a caller that wants a new key asks `GetNextID` first); other databases (PostgreSQL has sequences, MySQL `AUTO_INCREMENT`).
- codegen never applies the script. Checked on a scratch copy of a SQL Server database: two inserts returned keys 1 and 2 and `GetNextID` refused an unknown table.
