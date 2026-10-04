# FS_Entity_v1

An immutable F# record per table, with validation that answers with a `Result`.

- **Record:** one field per column; a nullable column is an `option` (no nulls). The namespace is `FSharpNamespace` (default `<ProjectName>.Domain`). A column named like an F# keyword is written in double backticks.
- **Types:** `int`, `int64`, `int16`, `byte`, `bool`, `decimal`, `double`, `float32`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `string`, `byte[]`; anything else is `obj`.
- **`validate`:** text columns that cannot be NULL must not be empty; text length limits; whole-number limits that the column's name or a CHECK range gives; decimal limits from a CHECK range (strict bounds stay strict). Every problem is reported.
- **`ofStrings`:** builds a record from a `Map<string, string>` keyed by column name. Missing required fields and unreadable values are collected with the `<*>` applicative from `Rop`; then the record is validated. Identity and computed columns default when missing.
- Needs `Rop.fs` (FS_Rop) first in the project's compile order. Checked: the records of the PostgreSQL sample compile with `dotnet build`, and `ofStrings` reports a bad number.
- **Not done:** a data-access layer, relations between records, typed errors (the errors are text).
