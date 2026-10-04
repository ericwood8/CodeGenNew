# CS_Dto_v1

A plain transfer class filled from a data reader.

- One property per column (a nullable column is `T?`; a text column that cannot be NULL starts as `""`).
- `Populate(IDataRecord)` finds each column by name: the required columns, then the optional ones (`record.IsDBNull` gives null). It works with any ADO.NET provider.
- `IsIdentical(other)` compares the columns whose change matters: not the key, an identity, a computed column, or an audit, create or modify column (the project's special-logic categories). A table with no such column compares its key.
- `ToString()` shows the table's display column.
- Namespace: `DtoNamespace` (default `<ProjectName>.App.Dtos`).
