# CS_TypedDataRow_v1

The typed-DataSet shape of one table.

- `<Table>DataTable : DataTable`: the columns (type, `AllowDBNull`, `AutoIncrement` for identity, `MaxLength` for text), the primary key, `GetRowType`, `NewRowFromBuilder`, `CreateInstance`, a typed `NewRow` / `Add<Table>Row`, and a `Clone` that rebinds the column properties.
- `<Table>Row : DataRow`: one typed property per column. A column that can be NULL reads through a guard that throws `StrongTypingException` for DBNull, and has an `Is<Column>Null` / `Set<Column>Null` pair.
- No DataSet, relations or adapter.
