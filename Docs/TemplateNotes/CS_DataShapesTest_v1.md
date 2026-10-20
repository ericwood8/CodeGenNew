# CS_DataShapesTest_v1

An MSTest class per table (`<Table>DataShapesTests.cs`) for the two ADO.NET shapes: `CS_DataContractDto` and `CS_TypedDataRow`. It needs no database.

- **Contract serializer:** `<Table>Contract` goes through the `DataContractSerializer` in memory; every column is compared, once more with every optional column NULL.
- **Contract constructors:** a `DataTable` made from the sample row feeds the `DataRow` constructor and, through `CreateDataReader()`, the `IDataRecord` constructor; the copy constructor is checked too. Each must give every column back, with and without NULLs.
- **Typed row:** every typed property of `<Table>Row` reads back what it was given.
- **NULL:** for each optional column, `Set<Column>Null()` makes `Is<Column>Null()` true and reading the property throws `StrongTypingException`, as the template says.
- **Clone:** a cloned `<Table>DataTable` makes rows that work (its typed columns are bound to its own columns).
- **Sample values:** the same fixed values as the other round-trip tests (`TestSampleValues` in Core). A table with a column of an unsupported type is refused.

Run it by hand or from the menu beside `CS_DataContractDto` and `CS_TypedDataRow` for the same table, pointing the output folder at the test project (`OutputApiTests`). None of the three is in a whole-project plan, and the classes it tests must exist, so run those two first.

Does not test a `DataSet`, a relation or an adapter (none is written).
