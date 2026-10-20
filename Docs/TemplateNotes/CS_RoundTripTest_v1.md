# CS_RoundTripTest_v1

An MSTest class per table (`<Table>RoundTripTests.cs`) that needs no database and no running API. It goes to the test project (`OutputApiTests`), beside the `API_Test` classes.

- **Plan:** a whole-project run writes it for a project with `Dtos=true` and the API tests on (they are on unless `ApiTests=false`). By hand it runs for any table.
- **JSON round trip:** the transfer class (`CS_Dto`) is filled with a sample value in every column, written with `System.Text.Json`, read back, and every column is compared (`byte[]` with `CollectionAssert`). A second test does the same with every optional column NULL, so a null stays a null.
- **Reader constructor:** a `DataTable` is made with the table's columns and the sample row (`DBNull` for a NULL), `CreateDataReader()` gives a real reader, and `new <Table>Dto(reader)` must read every column back (once with the optional columns NULL).
- **`IsIdentical`:** a copy is identical to its source, `null` is not, and changing the first column that matters (not the key, an identity, a computed, audit or stamp column) makes it differ.
- **Mapper:** for a table with a primary key, an entity (`CS_Entity`) is copied to the transfer class and back with `CS_Mapper` and every column is compared; a null in gives a null out, and no list gives an empty list.
- **The password hash:** the user table's hash column is not in the JSON (`CS_Dto` marks it `JsonIgnore`), so it is left out of the comparison.
- **Sample values:** two fixed values per type (`1` and `2`, `"Sample"` and `"Other"`, two dates, two byte arrays), so a copy can be told from a changed one. A table with a column of an unsupported type is refused with a sentence.

Does not test any rule of the database or the SQL the reader came from, or the view models of the desktop screens (they work on the `DbContext` directly and have no repository to replace).

The project file comes from the Tests essentials group (`API_EssentialTests`); the entity, transfer class and mapper come from `CS_Entity`, `CS_Dto` and `CS_Mapper`.
