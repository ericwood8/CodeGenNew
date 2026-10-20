# CS_SerializationTest_v1

An MSTest class per table (`<Table>SerializationTests.cs`) for the three classes `CS_SerializationDtos` writes. It needs no database.

- **Basic:** `<Table>Basic`, the plain `[Serializable]` class, goes through the `DataContractSerializer` in memory (the `BinaryFormatter` no longer exists in current .NET) and every column is compared.
- **Serializable:** `<Table>Serializable` has a custom `ISerializable` contract. The `DataContractSerializer` calls its `GetObjectData` and its protected constructor, so a column missing from either side, or written under a name the other side does not read, fails the test with the column named.
- **Xml:** `<Table>Xml` goes through the `XmlSerializer`. The contract ignores a computed column, so that column is not compared.
- **NULL:** each class is also run with every optional column NULL, so a null stays a null.
- **Sample values:** the same fixed values as `CS_RoundTripTest` (`TestSampleValues` in Core). A table with a column of an unsupported type is refused.

Run it by hand or from the menu beside `CS_SerializationDtos` for the same table, pointing the output folder at the test project (`OutputApiTests`). Neither is in a whole-project plan, and the classes it tests must exist, so run `CS_SerializationDtos` first.

Does not test a version number, the file format of a partner, or the code that reads or writes the files.
