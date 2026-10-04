# CS_DataContractDto_v1

A `[DataContract]` class for one table.

- A `[DataMember]` per column (`IsRequired = true` when the column cannot be NULL).
- Four constructors: empty, from a `DataRow`, from an `IDataRecord`, copy. A NULL in the row or reader becomes null for a nullable column.
- For a service that still uses `DataContractSerializer`; new code does better with `CS_Dto` and JSON.
- Checked: compiled for the PostgreSQL sample's tables, and a row round-tripped through `DataContractSerializer`.
